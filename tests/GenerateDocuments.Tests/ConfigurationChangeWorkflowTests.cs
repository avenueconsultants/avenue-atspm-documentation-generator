using System.Text.Json.Nodes;

namespace AtspmDocsGenerator.Tests;

[Collection("Console commands")]
public sealed class ConfigurationChangeWorkflowTests
{
    private static string WriteMap(TemporaryDirectory directory) => directory.WriteFile("map.json", """
        {
          "schemaVersion": 2, "productName": "Utility", "sourcePaths": ["Config"],
          "containers": [{ "name": "Worker", "slug": "worker", "autoDiscover": true }]
        }
        """);

    private static void WriteSource(TemporaryDirectory directory, int port) => directory.WriteFile("Config/Options.cs", $$"""
        [ConfigurationSection("Options")]
        public class Options { public int Port { get; set; } = {{port}}; }
        """);

    private static int Generate(TemporaryDirectory directory, string revision) => Program.Main([
        "--workspace-root", directory.Path, "--source-root", directory.Path,
        "--output-root", System.IO.Path.Combine(directory.Path, "output"),
        "--map", System.IO.Path.Combine(directory.Path, "map.json"),
        "--repository-url", "https://github.com/example/utility", "--repository-ref", revision,
        "--generated-at", "2026-10-01T00:00:00Z"]);

    [Fact]
    public void ReportKeepsLatestComparisonAcrossRerunsAndUnrelatedSourceRevisions()
    {
        using var directory = new TemporaryDirectory();
        WriteMap(directory);
        WriteSource(directory, 123);
        Assert.Equal(0, Generate(directory, "first"));
        var output = System.IO.Path.Combine(directory.Path, "output");
        WriteSource(directory, 456);
        Assert.Equal(0, Generate(directory, "second"));
        var snapshot = ConfigurationChangeGenerator.LoadPrevious(output)!;
        Assert.Equal("first", snapshot.Report.BaselineRevision);
        Assert.Equal("second", snapshot.SourceRevision);
        Assert.Equal("Default changed", Assert.Single(snapshot.Report.Changes).Kind);
        var report = File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.ReportFileName));
        var data = File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.SnapshotFileName));
        Assert.Equal(0, Generate(directory, "second"));
        Assert.Equal(0, Generate(directory, "third-unrelated"));
        Assert.Equal(data, File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.SnapshotFileName)));
        Assert.Equal(report, File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.ReportFileName)));
        // Reverting a setting is a real change, compared with the latest recorded state.
        WriteSource(directory, 123);
        Assert.Equal(0, Generate(directory, "fourth-revert"));
        snapshot = ConfigurationChangeGenerator.LoadPrevious(output)!;
        Assert.Equal("second", snapshot.Report.BaselineRevision);
        Assert.Equal("456", Assert.Single(snapshot.Report.Changes).Before);
        Assert.Equal("123", snapshot.Report.Changes[0].After);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unsupported-version")]
    [InlineData("missing")]
    [InlineData("missing-required-status")]
    public void InvalidPreviousSnapshotFailsWithoutReplacingGeneratedOutput(string problem)
    {
        using var directory = new TemporaryDirectory();
        WriteMap(directory);
        WriteSource(directory, 123);
        Assert.Equal(0, Generate(directory, "first"));
        var output = System.IO.Path.Combine(directory.Path, "output");
        var path = System.IO.Path.Combine(output, ConfigurationChangeGenerator.SnapshotFileName);
        if (problem == "missing") File.Delete(path);
        else if (problem == "malformed") File.WriteAllText(path, "broken");
        else
        {
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            if (problem == "unsupported-version") json["schemaVersion"] = 99;
            else json["sections"]![0]!["properties"]![0]!.AsObject().Remove("required");
            File.WriteAllText(path, json.ToJsonString());
        }
        var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(file => System.IO.Path.GetRelativePath(output, file), File.ReadAllBytes);
        WriteSource(directory, 456);
        Assert.Equal(1, Generate(directory, "second"));
        var after = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Assert.Equal(before.Count, after.Length);
        foreach (var file in after) Assert.Equal(before[System.IO.Path.GetRelativePath(output, file)], File.ReadAllBytes(file));
    }

    [Fact]
    public void FlatOutputMigratesToSupportWithoutLosingBaseline()
    {
        using var directory = new TemporaryDirectory();
        WriteMap(directory);
        WriteSource(directory, 123);
        Assert.Equal(0, Generate(directory, "first"));
        var output = System.IO.Path.Combine(directory.Path, "output");
        var support = System.IO.Path.Combine(output, "support");
        var snapshot = File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.SnapshotFileName));
        var report = File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.ReportFileName));
        var manifestPath = System.IO.Path.Combine(output, GeneratedOutputPolicy.ManifestFileName);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["ManagedFiles"] = new JsonArray(manifest["ManagedFiles"]!.AsArray()
            .Select(item => JsonValue.Create(item!.GetValue<string>().Replace("support/", ""))).ToArray());
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        foreach (var file in Directory.GetFiles(support))
            File.Move(file, System.IO.Path.Combine(output, System.IO.Path.GetFileName(file)));
        Directory.Delete(support);

        Assert.Equal(0, Generate(directory, "second"));
        Assert.Equal(new[] { "index.md", "worker.md" }, Directory.GetFiles(output)
            .Select(System.IO.Path.GetFileName).Order().ToArray());
        Assert.Equal(snapshot, File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.SnapshotFileName)));
        Assert.Equal(report, File.ReadAllBytes(System.IO.Path.Combine(output, ConfigurationChangeGenerator.ReportFileName)));
        WriteSource(directory, 456);
        Assert.Equal(0, Generate(directory, "third"));
        Assert.Equal("first", ConfigurationChangeGenerator.LoadPrevious(output)!.Report.BaselineRevision);
    }

    [Fact]
    public void LegacyOutputUpgradesWithoutInventingChanges()
    {
        using var directory = new TemporaryDirectory();
        WriteMap(directory);
        WriteSource(directory, 123);
        var output = System.IO.Path.Combine(directory.Path, "output");
        directory.WriteFile("output/worker.md", "Previous reference");
        directory.WriteFile("output/index.md", "Previous home page");
        Assert.Equal(0, Generate(directory, "first"));
        var snapshot = ConfigurationChangeGenerator.LoadPrevious(output)!;
        Assert.Null(snapshot.Report.BaselineRevision);
        Assert.Empty(snapshot.Report.Changes);
        Assert.Contains("configuration-changes.md", File.ReadAllText(System.IO.Path.Combine(output, "index.md")));
    }
}
