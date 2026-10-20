namespace AtspmDocsGenerator.Tests;

public sealed class DocumentationMapLoaderTests
{
    private const string ConsumerMap = """
        {
          "schemaVersion": 2,
          "productName": "Example utility",
          "sourcePaths": ["src/Configuration"],
          "logMessages": { "sourcePath": "src/Logging", "description": "Utility events." },
          "containers": [{ "name": "Utility", "slug": "utility", "sections": ["UtilityOptions"] }]
        }
        """;

    [Theory]
    [InlineData("{\"enabled\":true}")]
    [InlineData("{\"sourcePaths\":[],\"description\":\"Events\"}")]
    [InlineData("{\"sourcePath\":\"Logs\",\"sourcePaths\":[\"Other\"],\"description\":\"Events\"}")]
    [InlineData("{\"sourcePaths\":[\"Logs\",\"Logs\"],\"description\":\"Events\"}")]
    public void RejectsInvalidLogFolderConfiguration(string logs)
    {
        using var directory = new TemporaryDirectory();
        var json = System.Text.Json.Nodes.JsonNode.Parse(ConsumerMap)!;
        json["logMessages"] = System.Text.Json.Nodes.JsonNode.Parse(logs);
        var path = directory.WriteFile("documentation.json", json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => DocumentationMapLoader.Load(path));
    }

    [Theory]
    [InlineData("{\"exportFilePrefix\":\"../secret\"}")]
    [InlineData("{\"siteUrl\":\"javascript:alert(1)\"}")]
    [InlineData("{\"siteName\":null}")]
    [InlineData("null")]
    public void RejectsInvalidSiteConfiguration(string site)
    {
        using var directory = new TemporaryDirectory();
        var json = System.Text.Json.Nodes.JsonNode.Parse(ConsumerMap)!;
        json["site"] = System.Text.Json.Nodes.JsonNode.Parse(site);
        var path = directory.WriteFile("documentation.json", json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => DocumentationMapLoader.Load(path));
    }

    [Fact]
    public void LoadConsumerFileAllowsLegacyOptionalDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteFile("documentation.json", ConsumerMap);
        var map = DocumentationMapLoader.Load(path);
        Assert.Equal("Example utility", map.ProductName);
        Assert.Empty(map.Examples.PropertyValues);
        Assert.Empty(map.Examples.TypeValues);
        Assert.Empty(map.LogMessages.AllowedDuplicateEventIds);
    }

    [Theory]
    [InlineData("sourcePaths")]
    [InlineData("logMessages")]
    [InlineData("examples")]
    [InlineData("containers")]
    [InlineData("logMessages.allowedDuplicateEventIds")]
    [InlineData("examples.propertyValues")]
    [InlineData("examples.typeValues")]
    [InlineData("containers.0")]
    [InlineData("containers.0.sections")]
    public void LoadRejectsNullStructuresWithFileContext(string field)
    {
        using var directory = new TemporaryDirectory();
        var json = System.Text.Json.Nodes.JsonNode.Parse(ConsumerMap)!;
        json["examples"] = new System.Text.Json.Nodes.JsonObject();
        var parts = field.Split('.');
        var parent = json;
        foreach (var part in parts[..^1])
        {
            parent = int.TryParse(part, out var index) ? parent[index]! : parent[part]!;
        }
        if (int.TryParse(parts[^1], out var finalIndex))
            parent[finalIndex] = null;
        else
            parent[parts[^1]] = null;
        var path = directory.WriteFile("documentation.json", json.ToJsonString());
        var exception = Assert.Throws<InvalidDataException>(() => DocumentationMapLoader.Load(path));
        Assert.Contains(path, exception.Message);
    }

    [Theory]
    [InlineData("index")]
    [InlineData("INDEX")]
    [InlineData("log-messages")]
    [InlineData("configuration-changes")]
    [InlineData("configuration-templates")]
    public void LoadRejectsReservedOutputNames(string slug)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteFile("documentation.json", ConsumerMap.Replace("\"slug\": \"utility\"", $"\"slug\": \"{slug}\""));
        Assert.Contains("reserved", Assert.Throws<InvalidDataException>(() => DocumentationMapLoader.Load(path)).Message);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"sourcePaths\": 42}")]
    [InlineData("{\"typo\": true}")]
    public void LoadReportsMalformedJsonWithFileContext(string contents)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteFile("documentation.json", contents);
        Assert.Contains(path, Assert.Throws<System.Text.Json.JsonException>(
            () => DocumentationMapLoader.Load(path)).Message);
    }

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
