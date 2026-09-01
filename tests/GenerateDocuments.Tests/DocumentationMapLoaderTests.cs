namespace AtspmDocsGenerator.Tests;

public sealed class DocumentationMapLoaderTests
{
    [Fact]
    public void ValidateRejectsDuplicateSlugs()
    {
        var map = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Test product",
            SourcePaths = ["src"],
            LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "First",
                    Slug = "shared",
                    Sections = ["FirstOptions"]
                },
                new ContainerDefinition
                {
                    Name = "Second",
                    Slug = "shared",
                    Sections = ["SecondOptions"]
                }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentationMapLoader.Validate(map));

        Assert.Contains("Duplicate container slug", exception.Message);
    }

    [Fact]
    public void ValidateRejectsUnsafeSlugs()
    {
        var map = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Test product",
            SourcePaths = ["src"],
            LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "Example",
                    Slug = "../example",
                    Sections = ["ExampleOptions"]
                }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentationMapLoader.Validate(map));

        Assert.Contains("ASCII letters", exception.Message);
    }

    [Fact]
    public void ValidateRejectsConsecutiveHyphensAndDuplicateSourcePaths()
    {
        var map = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Test product",
            SourcePaths = ["src", "src"],
            LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "Example",
                    Slug = "bad--slug",
                    Sections = ["ExampleOptions"]
                }
            ]
        };

        var sourceException = Assert.Throws<InvalidDataException>(() => DocumentationMapLoader.Validate(map));
        Assert.Contains("Duplicate source path", sourceException.Message);

        var slugMap = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Test product",
            SourcePaths = ["src"],
            LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
            Containers = map.Containers
        };
        var slugException = Assert.Throws<InvalidDataException>(
            () => DocumentationMapLoader.Validate(slugMap));
        Assert.Contains("single hyphens", slugException.Message);
    }

    [Fact]
    public void LoadRejectsUnknownProperties()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteFile(
            "map.json",
            """
            {
              "schemaVersion": 1,
              "sourcePaths": ["src"],
              "containers": [{ "name": "Example", "slug": "example", "sections": ["Options"], "unknown": true }]
            }
            """);

        Assert.Throws<System.Text.Json.JsonException>(() => DocumentationMapLoader.Load(path));
    }

    [Fact]
    public void ValidateAllowsCaseDistinctSectionNames()
    {
        var map = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Test product",
            SourcePaths = ["src"],
            LogMessages = new() { SourcePath = "Logs", Description = "Test log messages." },
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "Example",
                    Slug = "example",
                    Sections = ["Jwt", "JWT"]
                }
            ]
        };

        DocumentationMapLoader.Validate(map);
    }
}
