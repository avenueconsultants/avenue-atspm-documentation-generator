namespace AtspmDocsGenerator.Tests;

public sealed class ProgramTests
{
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
            "map.json",
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
