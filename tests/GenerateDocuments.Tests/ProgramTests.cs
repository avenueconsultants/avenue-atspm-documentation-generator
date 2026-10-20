namespace AtspmDocsGenerator.Tests;

[Collection("Console commands")]
public sealed class ProgramTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingOrOmittingLogsRemovesPreviousManagedLogPage(bool omit)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("Config/Options.cs", """
            [ConfigurationSection("Utility")]
            public class Options { public int Port { get; set; } = 123; }
            """);
        directory.WriteFile("Logs/Events.cs", "// no log events required");
        var map = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "schemaVersion": 2, "productName": "Utility", "sourcePaths": ["Config"],
              "logMessages": { "sourcePaths": ["Logs"], "description": "Events" },
              "containers": [{ "name": "Utility", "slug": "utility", "sections": ["Utility"] }]
            }
            """)!;
        var path = directory.WriteFile("documentation.json", map.ToJsonString());
        var output = System.IO.Path.Combine(directory.Path, "output");
        string[] args = ["--workspace-root", directory.Path, "--source-root", directory.Path,
            "--output-root", output, "--map", path, "--repository-url", "https://github.com/example/utility",
            "--repository-ref", "abc123", "--generated-at", "2026-09-29T00:00:00Z"];
        Assert.Equal(0, Program.Main(args));
        Assert.True(File.Exists(System.IO.Path.Combine(output, "log-messages.md")));
        if (omit)
            map.AsObject().Remove("logMessages");
        else
            map["logMessages"] = System.Text.Json.Nodes.JsonNode.Parse("""{"enabled":false,"sourcePath":"does-not-exist"}""");
        File.WriteAllText(path, map.ToJsonString());
        Assert.Equal(0, Program.Main(args));
        Assert.False(File.Exists(System.IO.Path.Combine(output, "log-messages.md")));
        Assert.DoesNotContain("log-messages", File.ReadAllText(System.IO.Path.Combine(output, "index.md")));
        Assert.DoesNotContain("log-messages", File.ReadAllText(System.IO.Path.Combine(output, GeneratedOutputPolicy.ManifestFileName)));
    }

    [Fact]
    public void MainReturnsExpectedHelpAndParseErrorCodes()
    {
        Assert.Equal(0, Program.Main(["--help"]));
        Assert.Equal(2, Program.Main(["--repository-ref", "main"]));
    }

    [Fact]
    public void MainReportsGenerationFailuresWithoutAStackTrace()
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.WriteFile("invalid-map.json", "{}");
        var originalError = Console.Error;
        using var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var exitCode = Program.Main(
            [
                "--workspace-root", directory.Path,
                "--source-root", directory.Path,
                "--output-root", System.IO.Path.Combine(directory.Path, "output"),
                "--map", mapPath,
                "--repository-url", "https://github.com/example/source",
                "--repository-ref", "abc123",
                "--generated-at", "2026-08-18T19:15:00Z"
            ]);

            Assert.Equal(1, exitCode);
            Assert.StartsWith("Generation failed:", error.ToString());
            Assert.DoesNotContain(" at ", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void MainProducesIdenticalOutputForIdenticalInputs()
    {
        using var directory = new TemporaryDirectory();
        var source = System.IO.Path.Combine(directory.Path, "source");
        Directory.CreateDirectory(source);
        directory.WriteFile(
            "source/Config/Options.cs",
            """
            [ConfigurationSection("Example")]
            public class ExampleOptions
            {
                public string Host { get; set; } = "localhost";
            }
            """);
        directory.WriteFile(
            "source/Logs/Messages.cs",
            """
            public partial class Messages
            {
                [LoggerMessage(EventId = 1, EventName = "Started", Level = LogLevel.Information)]
                public partial void Started();
            }
            """);
        var mapPath = directory.WriteFile(
            "source/documentation.json",
            """
            {
              "schemaVersion": 2,
              "productName": "Test product",
              "sourcePaths": ["Config"],
              "logMessages": {
                "sourcePath": "Logs",
                "description": "Test logger messages, sorted by event ID.",
                "allowedDuplicateEventIds": []
              },
              "examples": { "propertyValues": {}, "typeValues": {} },
              "containers": [
                { "name": "Example", "slug": "example", "sections": ["Example"] }
              ]
            }
            """);
        var output = System.IO.Path.Combine(directory.Path, "generated");
        var args = new[]
        {
            "--workspace-root", directory.Path,
            "--source-root", source,
            "--output-root", output,
            "--map", mapPath,
            "--repository-url", "https://github.com/example/source",
            "--repository-ref", "abc123",
            "--generated-at", "2026-08-18T19:15:00Z"
        };

        Assert.Equal(0, Program.Main(args));
        var first = ReadOutput(output);
        Assert.Equal(0, Program.Main(args));
        var second = ReadOutput(output);

        Assert.Equal(first.Keys, second.Keys);
        foreach (var file in first.Keys)
        {
            Assert.Equal(first[file], second[file]);
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ReadOutput(string output) =>
        Directory.EnumerateFiles(output)
            .ToDictionary(
                path => System.IO.Path.GetFileName(path)!,
                File.ReadAllBytes,
                StringComparer.Ordinal);
}
