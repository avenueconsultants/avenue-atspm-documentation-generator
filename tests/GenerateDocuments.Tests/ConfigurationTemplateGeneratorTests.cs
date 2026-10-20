using System.Text.Json;

namespace AtspmDocsGenerator.Tests;

public sealed class ConfigurationTemplateGeneratorTests
{
    [Fact]
    public void TemplatesUseSafeValuesAndDocumentedNestedEnvironmentKeys()
    {
        using var directory = new TemporaryDirectory();
        var map = new DocumentationMap
        {
            SchemaVersion = 2,
            ProductName = "Example",
            SourcePaths = ["."],
            Containers = [new() { Name = "Worker", Slug = "worker", Sections = ["Worker:Nested"] }]
        };
        var sections = new Dictionary<string, ConfigurationSection>
        {
            ["Worker"] = new("Worker", null, null, "Worker.cs", 1,
            [
                new("Password", "string", "\"unsafe-source-default\"", false, null),
                new("Endpoint", "string", "\"https://private.example\"", false, null),
                new("BatchSize", "int", "50_000", false, null),
                new("Database", "DatabaseOptions", "Not set", false, null,
                    ["Database__Host", "Database__Port"])
            ])
        };
        ConfigurationTemplateGenerator.Generate(map, sections, directory.Path);
        var json = File.ReadAllText(Path.Combine(directory.Path, "support/worker.example.json"));
        var env = File.ReadAllText(Path.Combine(directory.Path, "support/worker.env.example"));
        using var document = JsonDocument.Parse(json);
        var nested = document.RootElement.GetProperty("Worker").GetProperty("Nested");
        Assert.Equal("REPLACE_WITH_SECRET", nested.GetProperty("Password").GetString());
        Assert.Equal("REPLACE_ME", nested.GetProperty("Endpoint").GetString());
        Assert.Equal(50000, nested.GetProperty("BatchSize").GetInt32());
        Assert.Contains("Worker__Nested__Database__Host=REPLACE_ME", env);
        Assert.Contains("Worker__Nested__BatchSize=50000", env);
        Assert.DoesNotContain("unsafe-source-default", json + env);
        Assert.DoesNotContain("private.example", json + env);
    }
}
