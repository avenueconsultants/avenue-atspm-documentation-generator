using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AtspmDocsGenerator;

using static DocumentationText;

public sealed partial class MarkdownDocumentationGenerator
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions ExampleJsonOptions = new()
    {
        WriteIndented = true
    };

    public GenerationResult Generate(
        DocumentationMap map,
        IReadOnlyDictionary<string, ConfigurationSection> availableSections,
        CliOptions options)
    {
        DocumentationMapLoader.Validate(map);
        var documentedSections = new HashSet<string>(StringComparer.Ordinal);
        var pages = map.Containers
            .Select(container =>
            {
                var sections = ConfigurationDiscovery.GetSections(container, availableSections)
                    .Select(sectionName => ConfigurationDiscovery.ResolveSection(sectionName, availableSections))
                    .ToArray();
                if (container.AutoDiscover && sections.Length == 0)
                    throw new InvalidDataException($"Automatic page '{container.Name}' found no configuration sections. Check sourcePaths and configurationTypes.");
                documentedSections.UnionWith(sections.Select(section => section.SectionName));
                return new
                {
                    FileName = $"{container.Slug}.md",
                    Contents = BuildContainerPage(container, sections, options, map.Examples)
                };
            })
            .Append(new
            {
                FileName = "configuration-templates.md",
                Contents = BuildTemplatePage(map, options)
            })
            .Append(new
            {
                FileName = "index.md",
                Contents = BuildIndexPage(map, options)
            })
            .ToArray();

        Directory.CreateDirectory(options.OutputRoot);
        foreach (var page in pages)
        {
            WriteFile(
                Path.Combine(options.OutputRoot, page.FileName),
                page.Contents);
        }

        return new GenerationResult(map.Containers.Count, documentedSections.Count);
    }

    private static string BuildIndexPage(
        DocumentationMap map,
        CliOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Configuration reference");
        builder.AppendLine();
        builder.AppendLine(
            $"Configuration sections are grouped by the {EscapeText(map.ProductName)} service or utility that consumes them.");
        builder.AppendLine();
        AppendGenerationTimestamp(builder, options);
        builder.AppendLine("## Containers");
        builder.AppendLine();

        foreach (var container in map.Containers)
        {
            builder.AppendLine($"- [{EscapeText(container.Name)}]({container.Slug}.md)");
        }

        builder.AppendLine();
        builder.AppendLine("## References");
        builder.AppendLine();
        if (map.LogMessages.Enabled)
        {
            builder.AppendLine("- [Log messages](log-messages.md)");
        }
        builder.AppendLine("- [Configuration changes](configuration-changes.md)");
        builder.AppendLine("- [Configuration templates](configuration-templates.md)");

        return NormalizeLineEndings(builder.ToString());
    }

    private static string BuildContainerPage(
        ContainerDefinition container,
        IReadOnlyList<ConfigurationSection> sections,
        CliOptions options,
        ExampleValueMap examples)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {EscapeText(container.Name)} configuration");
        builder.AppendLine();
        builder.AppendLine(
            $"Configuration options available to the **{EscapeText(container.Name)}** container.");
        builder.AppendLine();
        AppendGenerationTimestamp(builder, options);
        builder.AppendLine("## Contents");
        builder.AppendLine();

        foreach (var section in sections)
        {
            builder.AppendLine(
                $"- [{EscapeText(section.SectionName)}](#{ToAnchor(section.SectionName)})");
        }

        builder.AppendLine();

        foreach (var section in sections)
        {
            AppendSection(builder, section, options);
        }

        AppendExampleConfiguration(builder, sections, examples);
        builder.AppendLine();
        builder.AppendLine("## Download templates");
        builder.AppendLine();
        builder.AppendLine($"- [JSON example]({container.Slug}.example.json)");
        builder.AppendLine($"- [Environment example]({container.Slug}.env.example)");
        builder.AppendLine();
        builder.AppendLine("These are starting points from the source revision linked above. Replace placeholders before use.");

        return NormalizeLineEndings(builder.ToString());
    }

    private static string BuildTemplatePage(DocumentationMap map, CliOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Configuration templates");
        builder.AppendLine();
        builder.AppendLine("Downloadable examples for the documented configuration sections. They are starting points, not deployment settings.");
        builder.AppendLine();
        AppendGenerationTimestamp(builder, options);
        builder.AppendLine($"Source revision: [{EscapeText(options.RepositoryRef)}]({BuildRepositoryUrl(options.RepositoryUrl, "tree", options.RepositoryRef, string.Empty)}).");
        builder.AppendLine();
        builder.AppendLine("String values and credentials use safe placeholders. Review the configuration reference and replace them before use. The generator never writes deployment configuration.");
        builder.AppendLine();
        foreach (var container in map.Containers)
        {
            builder.AppendLine($"- **{EscapeText(container.Name)}:** [JSON]({container.Slug}.example.json) · [environment]({container.Slug}.env.example)");
        }
        return NormalizeLineEndings(builder.ToString());
    }

    private static void AppendExampleConfiguration(
        StringBuilder builder,
        IEnumerable<ConfigurationSection> sections,
        ExampleValueMap examples)
    {
        var configuration = new JsonObject();

        foreach (var section in sections)
        {
            var current = ConfigurationExampleTree.Section(configuration, section.SectionName);
            foreach (var property in section.Properties)
            {
                current[property.Name] = CreateExampleValue(property, examples);
            }
        }

        builder.AppendLine("## Example JSON configuration");
        builder.AppendLine();
        builder.AppendLine(
            "This example includes every documented setting. Replace placeholder secrets, URLs, paths, and connection details before use.");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(configuration.ToJsonString(ExampleJsonOptions));
        builder.AppendLine("```");
    }

    internal static JsonNode? CreateExampleValue(ConfigurationProperty property, ExampleValueMap examples)
    {
        var name = property.Name;
        var typeName = property.TypeName.TrimEnd('?');
        var defaultExpression = property.DefaultExpression;

        if (name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Key", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("replace-with-a-secret");
        }

        if (examples.PropertyValues.TryGetValue(name, out var propertyValue))
        {
            return JsonNode.Parse(propertyValue.GetRawText());
        }

        if (examples.TypeValues.TryGetValue(typeName, out var typeValue))
        {
            return JsonNode.Parse(typeValue.GetRawText());
        }

        if (property.TypeName.EndsWith("?", StringComparison.Ordinal)
            && defaultExpression == "Not set")
        {
            return null;
        }

        if (IsCollectionType(typeName))
        {
            return new JsonArray();
        }

        if (typeName.Contains("Dictionary<", StringComparison.Ordinal)
            || typeName.Contains("IDictionary<", StringComparison.Ordinal))
        {
            return new JsonObject();
        }

        if (typeName == "string")
        {
            return JsonValue.Create(CreateExampleString(name, defaultExpression));
        }

        if (typeName == "bool")
        {
            return JsonValue.Create(bool.TryParse(defaultExpression, out var value) && value);
        }

        if (typeName is "byte" or "short" or "int" or "long")
        {
            // C# digit separators are legal in source literals but not in JSON numbers.
            var numeric = defaultExpression.TrimEnd('L', 'l').Replace("_", "", StringComparison.Ordinal);
            return JsonValue.Create(
                long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0);
        }

        if (typeName is "float" or "double" or "decimal")
        {
            var numeric = defaultExpression.TrimEnd('F', 'f', 'D', 'd', 'M', 'm')
                .Replace("_", "", StringComparison.Ordinal);
            return JsonValue.Create(
                decimal.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0);
        }

        if (typeName is "DateTime" or "DateTimeOffset")
        {
            return JsonValue.Create("2026-01-01T00:00:00Z");
        }

        if (typeName == "TimeSpan")
        {
            return JsonValue.Create("00:05:00");
        }

        if (typeName == "DirectoryInfo")
        {
            return JsonValue.Create("./data");
        }

        if (defaultExpression.Contains('.', StringComparison.Ordinal)
            && !defaultExpression.Contains('(', StringComparison.Ordinal))
        {
            return JsonValue.Create(defaultExpression.Split('.').Last());
        }

        return new JsonObject();
    }

    private static bool IsCollectionType(string typeName) =>
        typeName.EndsWith("[]", StringComparison.Ordinal)
        || typeName.StartsWith("IEnumerable<", StringComparison.Ordinal)
        || typeName.StartsWith("IReadOnlyCollection<", StringComparison.Ordinal)
        || typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
        || typeName.StartsWith("ICollection<", StringComparison.Ordinal)
        || typeName.StartsWith("IList<", StringComparison.Ordinal)
        || typeName.StartsWith("List<", StringComparison.Ordinal)
        || typeName.StartsWith("HashSet<", StringComparison.Ordinal);

    private static string CreateExampleString(string name, string defaultExpression)
    {
        if (defaultExpression.StartsWith('"') && defaultExpression.EndsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(defaultExpression) ?? string.Empty;
            }
            catch (JsonException)
            {
                // Fall through to a descriptive placeholder.
            }
        }

        return name switch
        {
            "Host" => "localhost",
            "Issuer" => "https://identity.example.com",
            "Authority" => "https://identity-provider.example.com",
            "CallbackPath" => "/signin-oidc",
            "Path" or "BasePath" => "./data",
            "TimeZoneId" => "America/Denver",
            "FileFormat" => "csv",
            "DateTimeFormat" => "yyyy-MM-dd HH:mm:ss",
            _ when name.EndsWith("Url", StringComparison.OrdinalIgnoreCase) => "https://example.com",
            _ => "replace-me"
        };
    }

    private static void AppendGenerationTimestamp(StringBuilder builder, CliOptions options)
    {
        builder.AppendLine(GenerationTimestamp(options.GeneratedAt));
        builder.AppendLine();
    }

    private static void AppendSection(
        StringBuilder builder,
        ConfigurationSection section,
        CliOptions options)
    {
        builder.AppendLine($"## {EscapeText(section.SectionName)}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(section.AttributeDescription))
        {
            builder.AppendLine($"> {EscapeText(section.AttributeDescription)}");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(section.Summary)
            && !string.Equals(
                section.AttributeDescription,
                section.Summary,
                StringComparison.Ordinal))
        {
            builder.AppendLine(EscapeText(section.Summary));
            builder.AppendLine();
        }

        var sourceUrl = BuildRepositoryUrl(
            options.RepositoryUrl,
            "blob",
            options.RepositoryRef,
            section.SourcePath) + $"#L{section.SourceLine}";
        builder.AppendLine($"[View source]({sourceUrl})");
        builder.AppendLine();
        builder.AppendLine("| Setting | Type | Default | Required | Options | Environment variable | Description |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var property in section.Properties)
        {
            var environmentVariables = property.EnvironmentVariableSuffixes is { Count: > 0 }
                ? property.EnvironmentVariableSuffixes
                : [property.Name];
            var environmentVariable = string.Join(
                "<br>",
                environmentVariables.Select(suffix =>
                    CodeSpan($"{MappedSectionName.ToEnvironmentVariablePrefix(section.SectionName)}__{suffix}")));
            var enumOptions = property.Options is { Count: > 0 }
                ? string.Join("<br>", property.Options.Select(CodeSpan))
                : string.Empty;
            builder.AppendLine(
                $"| {CodeSpan(property.Name)} " +
                $"| {CodeSpan(property.TypeName)} " +
                $"| {CodeSpan(property.DefaultExpression)} " +
                $"| {(property.IsRequired ? "Yes" : "No")} " +
                $"| {enumOptions} " +
                $"| {environmentVariable} " +
                $"| {EscapeTableText(property.Summary ?? string.Empty)} |");
        }

        if (section.Properties.Count == 0)
        {
            builder.AppendLine("| _No public configuration properties_ |  |  |  |  |  |  |");
        }

        builder.AppendLine();
    }

    private static string EscapeTableText(string value) => EscapeText(value);

    private static string ToAnchor(string value)
    {
        var anchor = NonAnchorCharacterRegex()
            .Replace(value.Replace(":", "", StringComparison.Ordinal).ToLowerInvariant(), "-")
            .Trim('-');
        return RepeatedHyphenRegex().Replace(anchor, "-");
    }

    private static void WriteFile(string path, string contents) =>
        File.WriteAllText(path, contents, Utf8WithoutBom);

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAnchorCharacterRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex RepeatedHyphenRegex();
}
