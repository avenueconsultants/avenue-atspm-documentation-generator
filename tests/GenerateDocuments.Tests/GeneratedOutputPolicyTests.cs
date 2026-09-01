namespace AtspmDocsGenerator.Tests;

public sealed class GeneratedOutputPolicyTests
{
    [Fact]
    public void ValidateExistingRejectsForeignFiles()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "notes.md"), "handwritten");

        var exception = Assert.Throws<InvalidDataException>(
            () => GeneratedOutputPolicy.ValidateExisting(output, CreateMap()));

        Assert.Contains("ownership manifest", exception.Message);
    }

    [Fact]
    public void ValidateExistingAllowsExactLegacyOutputAndOwnedOutput()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        foreach (var file in new[] { "example.md", "index.md", "log-messages.md" })
        {
            File.WriteAllText(System.IO.Path.Combine(output, file), file);
        }

        var map = CreateMap();
        GeneratedOutputPolicy.ValidateExisting(output, map);
        GeneratedOutputPolicy.WriteManifest(output, map);
        GeneratedOutputPolicy.ValidateExisting(output, map);

        File.WriteAllText(System.IO.Path.Combine(output, "foreign.txt"), "foreign");
        Assert.Throws<InvalidDataException>(() => GeneratedOutputPolicy.ValidateExisting(output, map));
    }

    private static DocumentationMap CreateMap() => new()
    {
        SchemaVersion = 2,
        ProductName = "Test product",
        SourcePaths = ["Source"],
        LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
        Containers =
        [
            new ContainerDefinition { Name = "Example", Slug = "example", Sections = ["Example"] }
        ]
    };
}
