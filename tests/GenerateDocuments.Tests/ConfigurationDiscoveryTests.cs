namespace AtspmDocsGenerator.Tests;

public sealed class ConfigurationDiscoveryTests
{
    [Fact]
    public void ExplicitRegistrationResolvesQualifiedPartialTypeAndExcludesInternalProperty()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("One.cs", """
            namespace Utility;
            /// <summary>Migration settings.</summary>
            public partial class MigrationOptions { public string Source { get; set; } }
            """);
        directory.WriteFile("Two.cs", """
            namespace Utility;
            public partial class MigrationOptions { public bool EndIsDateOnly { get; set; } }
            """);
        directory.WriteFile("Other.cs", "namespace Other; public class MigrationOptions { public int Wrong { get; set; } }");
        var map = new DocumentationMap
        {
            SourcePaths = ["."],
            ConfigurationTypes = [new() { TypeName = "Utility.MigrationOptions", SectionName = "Migration", Description = "Explicit description" }],
            ExcludedProperties = new Dictionary<string, IReadOnlyList<string>> { ["Migration"] = ["EndIsDateOnly"] }
        };
        var section = Assert.Single(ConfigurationDiscovery.Analyze(directory.Path, map)).Value;
        Assert.Equal("Source", Assert.Single(section.Properties).Name);
        Assert.Equal("Migration settings.", section.Summary);
        Assert.Equal("Explicit description", section.AttributeDescription);
        Assert.Equal("One.cs", section.SourcePath);
    }

    [Fact]
    public void UnknownTypeAndMisspelledExclusionsAreReported()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("Options.cs", """[ConfigurationSection("Options")] public class Options { public int Port {get;set;} }""");
        Assert.Throws<InvalidDataException>(() => ConfigurationDiscovery.Analyze(directory.Path,
            new DocumentationMap { SourcePaths = ["."], ConfigurationTypes = [new() { TypeName = "Missing.Options", SectionName = "Other" }] }));
        Assert.Throws<InvalidDataException>(() => ConfigurationDiscovery.Analyze(directory.Path,
            new DocumentationMap { SourcePaths = ["."], ExcludedProperties = new Dictionary<string, IReadOnlyList<string>> { ["Options"] = ["Porrrt"] } }));
        Assert.Throws<InvalidDataException>(() => ConfigurationDiscovery.Analyze(directory.Path,
            new DocumentationMap { SourcePaths = ["."], ExcludedSections = ["Missing"] }));
    }

    [Fact]
    public void PropertyDescriptionOverridesApplyAndMisspellingsFail()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("Options.cs", """[ConfigurationSection("Options")] public class Options { public int Port {get;set;} }""");
        var descriptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Options"] = new Dictionary<string, string> { ["Port"] = "Listening port." }
        };
        var map = new DocumentationMap
        {
            SourcePaths = ["."],
            PropertyDescriptions = descriptions
        };
        var section = Assert.Single(ConfigurationDiscovery.Analyze(directory.Path, map)).Value;
        Assert.Equal("Listening port.", Assert.Single(section.Properties).Summary);

        descriptions["Options"] = new Dictionary<string, string> { ["Porrrt"] = "Wrong." };
        Assert.Throws<InvalidDataException>(() => ConfigurationDiscovery.Analyze(directory.Path, map));
    }

    [Fact]
    public void AutoDiscoveryPreservesNamedBindingsAndOmitsExcludedSections()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("Options.cs", """
            [ConfigurationSection("Database")] public class Database { public string Connection {get;set;} }
            [ConfigurationSection("New")] public class New { public int Port {get;set;} }
            [ConfigurationSection("Internal")] public class Internal { public int State {get;set;} }
            """);
        var container = new ContainerDefinition { Name = "Utility", Slug = "utility", AutoDiscover = true, Sections = ["Database:Named"] };
        var map = new DocumentationMap { SourcePaths = ["."], ExcludedSections = ["Internal"], Containers = [container] };
        var sections = ConfigurationDiscovery.Analyze(directory.Path, map);
        Assert.Equal(new[] { "Database:Named", "New" }, ConfigurationDiscovery.GetSections(container, sections));
        Assert.Empty(ConfigurationDiscovery.Unmapped(map, sections));
    }
}
