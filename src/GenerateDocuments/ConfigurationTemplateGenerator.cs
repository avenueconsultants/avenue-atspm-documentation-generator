using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AtspmDocsGenerator;

internal static partial class ConfigurationTemplateGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ExampleValueMap EmptyExamples = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static void Generate(DocumentationMap map,
        IReadOnlyDictionary<string, ConfigurationSection> available, string outputRoot)
    {
        OutputLayout.CreateSupportDirectory(outputRoot);
        foreach (var container in map.Containers)
        {
            var sections = ConfigurationDiscovery.GetSections(container, available)
                .Select(name => ConfigurationDiscovery.ResolveSection(name, available))
                .ToArray();
            var json = new JsonObject();
            var environment = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var section in sections)
            {
                var current = ConfigurationExampleTree.Section(json, section.SectionName);
                foreach (var property in section.Properties)
                {
                    var value = SafeValue(property);
                    current[property.Name] = value?.DeepClone();
                    foreach (var suffix in property.EnvironmentVariableSuffixes is { Count: > 0 }
                        ? property.EnvironmentVariableSuffixes : [property.Name])
                    {
                        var key = $"{MappedSectionName.ToEnvironmentVariablePrefix(section.SectionName)}__{suffix}";
                        if (!EnvironmentKeyRegex().IsMatch(key))
                            throw new InvalidDataException($"Invalid environment template key '{key}'.");
                        var text = suffix == property.Name ? FormatEnvironmentValue(value) : "REPLACE_ME";
                        if (environment.TryGetValue(key, out var existing) && existing != text)
                            throw new InvalidDataException($"Conflicting environment template key '{key}'.");
                        environment[key] = text;
                    }
                }
            }
            File.WriteAllText(Path.Combine(outputRoot, OutputLayout.SupportFile($"{container.Slug}.example.json")),
                json.ToJsonString(JsonOptions) + "\n", Utf8WithoutBom);
            File.WriteAllText(Path.Combine(outputRoot, OutputLayout.SupportFile($"{container.Slug}.env.example")),
                string.Join("\n", environment.Select(item => $"{item.Key}={item.Value}")) + "\n",
                Utf8WithoutBom);
        }
    }

    private static JsonNode? SafeValue(ConfigurationProperty property)
    {
        // Downloadable files must never inherit string defaults or map overrides,
        // which can contain repository-specific credentials or connection details.
        if (SensitiveNameRegex().IsMatch(property.Name))
            return JsonValue.Create("REPLACE_WITH_SECRET");
        if (property.TypeName.TrimEnd('?') == "string")
            return JsonValue.Create(property.Name.Contains("Connection", StringComparison.OrdinalIgnoreCase)
                ? "REPLACE_WITH_CONNECTION_STRING" : "REPLACE_ME");
        return MarkdownDocumentationGenerator.CreateExampleValue(property, EmptyExamples);
    }

    private static string FormatEnvironmentValue(JsonNode? value)
    {
        if (value is null) return "REPLACE_ME";
        if (value is JsonObject or JsonArray) return "REPLACE_ME";
        var element = JsonSerializer.SerializeToElement(value);
        var text = element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
        return SimpleValueRegex().IsMatch(text) ? text : JsonSerializer.Serialize(text);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentKeyRegex();

    [GeneratedRegex("^[A-Za-z0-9_./:-]+$")]
    private static partial Regex SimpleValueRegex();

    [GeneratedRegex("password|secret|token|credential|privatekey|apikey|connectionstring|^key$", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNameRegex();
}
