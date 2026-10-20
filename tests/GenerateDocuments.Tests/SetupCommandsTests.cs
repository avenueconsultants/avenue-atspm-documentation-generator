namespace AtspmDocsGenerator.Tests;

[Collection("Console commands")]
public sealed class SetupCommandsTests
{
    private static string Options(string section, string properties = "public int Port { get; set; } = 123;") =>
        $$"""[ConfigurationSection("{{section}}") ] public class {{section}}Options { {{properties}} }""";

    private static string WriteMap(TemporaryDirectory directory) => directory.WriteFile("documentation.json", """
        {
          "schemaVersion": 2, "productName": "Utility", "sourcePaths": ["src"],
          "site": {"siteName": "My Guide"},
          "examples": {"propertyValues": {"Port": 456}},
          "containers": [
            {"name": "Worker", "slug": "worker", "sections": ["First"]},
            {"name": "API", "slug": "api", "sections": ["First"]}
          ]
        }
        """);

    [Fact]
    public void InitDiscoversSourceAndLogsButNeverOverwritesExistingConfiguration()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/Options.cs", Options("Utility"));
        directory.WriteFile("src/Logs/Events.cs", """
            public partial class Events {
                [LoggerMessage(EventId = 1, EventName = "Start", Level = LogLevel.Information)]
                public partial void Start();
            }
            """);
        foreach (var ignored in new[] { "tests", "node_modules", "_source", "packages", ".git", "obj", "Utility.Tests" })
            directory.WriteFile($"{ignored}/Duplicate.cs", Options("Utility"));
        Assert.Equal(0, Program.Main(["init", "--source-root", directory.Path, "--product-name", "Custom Utility"]));
        var path = System.IO.Path.Combine(directory.Path, "documentation.json");
        var map = DocumentationMapLoader.Load(path);
        Assert.Equal("Custom Utility", map.ProductName);
        Assert.True(map.Containers[0].AutoDiscover);
        Assert.Equal(new[] { "src/Logs" }, map.LogMessages.SourcePaths);
        Assert.Equal(0, Program.Main(["check", "--source-root", directory.Path]));
        var before = File.ReadAllBytes(path);
        Assert.Equal(1, Program.Main(["init", "--source-root", directory.Path]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void SyncRequiresDestinationPreviewsAndPreservesCustomValuesWhenWritten()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/First.cs", Options("First"));
        directory.WriteFile("src/Second.cs", Options("Second"));
        var path = WriteMap(directory);
        var before = File.ReadAllBytes(path);
        Assert.Equal(1, Program.Main(["check", "--source-root", directory.Path]));
        Assert.Equal(1, Program.Main(["sync", "--source-root", directory.Path, "--write"]));
        Assert.Equal(0, Program.Main(["sync", "--source-root", directory.Path, "--container", "worker"]));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(0, Program.Main(["sync", "--source-root", directory.Path, "--container", "worker", "--write"]));
        var map = DocumentationMapLoader.Load(path);
        Assert.Equal(new[] { "First", "Second" }, map.Containers[0].Sections);
        Assert.Equal(new[] { "First" }, map.Containers[1].Sections);
        Assert.Equal("My Guide", map.Site.SiteName);
        Assert.Equal(456, map.Examples.PropertyValues["Port"].GetInt32());
        var synchronized = File.ReadAllBytes(path);
        Assert.Equal(0, Program.Main(["sync", "--source-root", directory.Path, "--container", "worker", "--write"]));
        Assert.Equal(synchronized, File.ReadAllBytes(path));
        Assert.Equal(0, Program.Main(["check", "--source-root", directory.Path]));
    }

    [Fact]
    public void AutomaticGenerationFindsNewSectionsAndPropertiesWithoutRewritingTheMap()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/First.cs", Options("First"));
        Assert.Equal(0, Program.Main(["init", "--source-root", directory.Path, "--source-path", "src"]));
        var path = System.IO.Path.Combine(directory.Path, "documentation.json");
        var before = File.ReadAllBytes(path);
        directory.WriteFile("src/First.cs", Options("First", "public int Port { get; set; } public string Added { get; set; }"));
        directory.WriteFile("src/Second.cs", Options("Second"));
        var output = System.IO.Path.Combine(directory.Path, "docs");
        Assert.Equal(0, Program.Main(["--workspace-root", directory.Path, "--source-root", directory.Path,
            "--output-root", output, "--map", path, "--repository-url", "https://github.com/example/project",
            "--repository-ref", "abc", "--generated-at", "2026-10-01T00:00:00Z"]));
        var page = File.ReadAllText(System.IO.Path.Combine(output, "configuration.md"));
        Assert.Contains("## Second", page);
        Assert.Contains("First__Added", page);
        Assert.Equal(0, Program.Main(["sync", "--source-root", directory.Path, "--write"]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void SyncRefusesStaleMappingsInsteadOfRemovingThem()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/Second.cs", Options("Second"));
        var path = WriteMap(directory);
        var before = File.ReadAllBytes(path);
        Assert.Equal(1, Program.Main(["sync", "--source-root", directory.Path, "--container", "worker", "--write"]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void SetupWritesCannotEscapeSourceRootAndEmptyScaffoldFailsCoverageCheck()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var path = System.IO.Path.Combine(outside.Path, "documentation.json");
        Assert.Equal(1, Program.Main(["init", "--source-root", directory.Path, "--map", path]));
        Assert.False(File.Exists(path));
        Assert.Equal(0, Program.Main(["init", "--source-root", directory.Path]));
        Assert.Equal(1, Program.Main(["check", "--source-root", directory.Path]));
    }

    [Theory]
    [InlineData("init", "--write")]
    [InlineData("check", "--product-name")]
    [InlineData("sync", "--source-path")]
    public void RejectsUnsupportedCommandOptions(string command, string option)
    {
        Assert.Equal(2, Program.Main([command, option]));
        Assert.Equal(0, Program.Main([command, "--help"]));
    }
}
