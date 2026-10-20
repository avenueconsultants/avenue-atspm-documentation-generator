namespace AtspmDocsGenerator.Tests;

public sealed class ConfigurationChangeGeneratorTests
{
    private static SnapshotProperty Property(string name = "Port") =>
        new(name, "int", "123", false, ["Old"], [$"Options__{name}"]);

    private static SnapshotSection Section(string name = "Options", params SnapshotProperty[] properties) =>
        new(name, "Config/Options.cs", 1, [new("Worker", "worker")], properties);

    [Fact]
    public void CompareReportsEverySupportedFieldAndBothServiceAssignments()
    {
        var oldProperty = Property();
        var newProperty = oldProperty with
        {
            Type = "long", Default = "456", Required = true, Options = ["New"],
            EnvironmentVariables = ["Options__Port__0"]
        };
        var before = Section("Options", oldProperty);
        var after = Section("Options", newProperty) with { Services = [new("API", "api")] };
        var changes = ConfigurationChangeGenerator.Compare([before], [after]);
        Assert.Equal(new[] { "Service mapping changed", "Type changed", "Default changed",
            "Required status changed", "Enum options changed", "Environment variables changed" }, changes.Select(change => change.Kind));
        Assert.All(changes, change => Assert.Equal(new[] { "API (api)", "Worker (worker)" }, change.AffectedServices));
        var defaults = changes.Single(change => change.Kind == "Default changed");
        Assert.Equal("123", defaults.Before);
        Assert.Equal("456", defaults.After);
        Assert.Equal(new[] { "Options__Port", "Options__Port__0" }, defaults.EnvironmentVariables);
    }

    [Fact]
    public void CompareReportsSectionAndSettingAdditionsAndRemovalsWithoutGuessingRenames()
    {
        var before = Section("Old", Property("OldName"));
        var after = Section("New", Property("NewName") with { Required = true, Default = "Not set" });
        var changes = ConfigurationChangeGenerator.Compare([before], [after]);
        Assert.Contains(changes, change => change.Kind == "Section added" && change.Section == "New");
        Assert.Contains(changes, change => change.Kind == "Section removed" && change.Section == "Old");
        Assert.Contains(changes, change => change.Kind == "Setting removed" && change.Property == "OldName");
        Assert.Contains(changes, change => change.Kind == "Required setting added" && change.After!.Contains("Not set"));
        Assert.DoesNotContain(changes, change => change.Kind.Contains("rename", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompareIgnoresOrderingAndSourceLocationChanges()
    {
        var first = Property() with { Options = ["One", "Two"], EnvironmentVariables = ["Options__A", "Options__B"] };
        var before = Section("Options", first, Property("Other"));
        var after = before with
        {
            SourcePath = "Moved.cs", SourceLine = 50,
            Properties = [Property("Other"), first with { Options = ["Two", "One"], EnvironmentVariables = ["Options__B", "Options__A"] }]
        };
        Assert.Empty(ConfigurationChangeGenerator.Compare([before], [after]));
    }

    [Fact]
    public void CaptureIncludesNamedBindingsAndUnmappedSectionsAndRetainsBaselineOnUnchangedCommit()
    {
        var map = new DocumentationMap
        {
            ProductName = "Utility",
            Containers = [new() { Name = "Worker", Slug = "worker", Sections = ["Database:Main"] },
                          new() { Name = "API", Slug = "api", Sections = ["Database:Main"] }]
        };
        var available = new Dictionary<string, ConfigurationSection>
        {
            ["Database"] = new("Database", null, null, "Options.cs", 1,
                [new("Host", "string", "Not set", true, "A host.")]),
            ["NewSection"] = new("NewSection", null, null, "New.cs", 1, [])
        };
        var options = new CliOptions(".", ".", ".", "map.json", "https://github.com/example/utility", "first", DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var first = ConfigurationChangeGenerator.Capture(map, available, options, null);
        Assert.Null(first.Report.BaselineRevision);
        Assert.Empty(first.Report.Changes);
        var database = first.Sections.Single(section => section.Name == "Database:Main");
        Assert.Equal(2, database.Services.Count);
        Assert.Equal("Database__Main__Host", Assert.Single(database.Properties[0].EnvironmentVariables));
        Assert.Empty(first.Sections.Single(section => section.Name == "NewSection").Services);
        Assert.Same(first, ConfigurationChangeGenerator.Capture(map, available, options with { RepositoryRef = "second" }, first));
        Assert.Contains("Initial baseline established", ConfigurationChangeGenerator.Render(first));
        Assert.Contains("Unmapped discovered sections", ConfigurationChangeGenerator.Render(first));
    }

    [Fact]
    public void RenderEscapesTableContentAndRoundTripsTheSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var snapshot = new ConfigurationSnapshot
        {
            SchemaVersion = 1, ProductName = "Utility", RepositoryUrl = "https://github.com/example/utility",
            SourceRevision = "new", SourceTimestamp = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            Sections = [Section("Options", Property())],
            Report = new ConfigurationChangeReport
            {
                BaselineRepositoryUrl = "https://github.com/example/old-repo", BaselineRevision = "old",
                Changes = [new("Default changed", "Options", "Port", "a|b", "`x`\n<script>", ["Worker (worker)"], ["Options__Port"])]
            }
        };
        ConfigurationChangeGenerator.Write(directory.Path, snapshot);
        var loaded = ConfigurationChangeGenerator.LoadPrevious(directory.Path)!;
        var report = ConfigurationChangeGenerator.Render(loaded);
        Assert.Contains("a\\|b", report);
        Assert.Contains("https://github.com/example/old-repo/commit/old", report);
        Assert.DoesNotContain("`x`\n<script>", report);
        Assert.Equal("old", loaded.Report.BaselineRevision);
        Assert.Equal(report, File.ReadAllText(System.IO.Path.Combine(directory.Path, ConfigurationChangeGenerator.ReportFileName)));
    }
}
