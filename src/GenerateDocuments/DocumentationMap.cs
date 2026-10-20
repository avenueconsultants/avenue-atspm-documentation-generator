using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AtspmDocsGenerator;

public sealed class DocumentationMap
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public int SchemaVersion { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public IReadOnlyList<string> SourcePaths { get; init; } = [];

    public IReadOnlyList<ConfigurationTypeRegistration> ConfigurationTypes { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ExcludedProperties { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PropertyDescriptions { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    public IReadOnlyList<string> ExcludedSections { get; init; } = [];

    public LogMessageMap LogMessages { get; init; } = new() { Enabled = false };

    public SiteMap Site { get; init; } = new();

    public ExampleValueMap Examples { get; init; } = new();

    public IReadOnlyList<ContainerDefinition> Containers { get; init; } = [];
}

public sealed class LogMessageMap
{
    public bool Enabled { get; init; } = true;
    public bool IncludeOrdinaryCalls { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public IReadOnlyList<string> SourcePaths { get; init; } = [];

    public IEnumerable<string> GetSourcePaths() =>
        string.IsNullOrWhiteSpace(SourcePath) ? SourcePaths : [SourcePath];

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<int> AllowedDuplicateEventIds { get; init; } = [];
}

public sealed class ConfigurationTypeRegistration
{
    public string TypeName { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class SiteMap
{
    public string SiteName { get; init; } = string.Empty;
    public string SiteDescription { get; init; } = string.Empty;
    public string SiteUrl { get; init; } = string.Empty;
    public string Copyright { get; init; } = string.Empty;
    public string ExportFilePrefix { get; init; } = string.Empty;
    public string LogReferenceTitle { get; init; } = string.Empty;
}

public sealed class ExampleValueMap
{
    public IReadOnlyDictionary<string, JsonElement> PropertyValues { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, JsonElement> TypeValues { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed class ContainerDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public IReadOnlyList<string> Sections { get; init; } = [];

    public bool AutoDiscover { get; init; }
}

public static class DocumentationMapLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static DocumentationMap Load(string path)
    {
        try
        {
            var map = JsonSerializer.Deserialize<DocumentationMap>(
                File.ReadAllText(path),
                SerializerOptions)
                ?? throw new InvalidDataException("The configuration map must be an object, not null.");

            Validate(map);
            return map;
        }
        catch (JsonException exception)
        {
            throw new JsonException($"Invalid configuration file '{path}': {exception.Message}", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"Invalid configuration file '{path}': {exception.Message}", exception);
        }
    }

    public static void Validate(DocumentationMap map)
    {
        if (map.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                $"Unsupported configuration map schema version '{map.SchemaVersion}'. Expected version 2.");
        }

        if (string.IsNullOrWhiteSpace(map.ProductName))
        {
            throw new InvalidDataException("A non-empty product name is required.");
        }

        if (map.SourcePaths is null || map.SourcePaths.Count == 0 || map.SourcePaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("At least one non-empty source path is required.");
        }

        EnsureUnique(map.SourcePaths, "source path");

        if (map.ConfigurationTypes is null || map.ConfigurationTypes.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.TypeName) || string.IsNullOrWhiteSpace(item.SectionName)))
            throw new InvalidDataException("configurationTypes requires objects with typeName and sectionName.");
        EnsureUnique(map.ConfigurationTypes.Select(item => item.TypeName), "registered type");
        EnsureUnique(map.ConfigurationTypes.Select(item => item.SectionName), "registered section");
        if (map.ExcludedSections is null || map.ExcludedSections.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("excludedSections must contain non-empty section names.");
        EnsureUnique(map.ExcludedSections, "excluded section");
        if (map.ExcludedProperties is null || map.ExcludedProperties.Any(item =>
                string.IsNullOrWhiteSpace(item.Key) || item.Value is null || item.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("excludedProperties must map section names to arrays of property names.");
        foreach (var exclusion in map.ExcludedProperties)
            EnsureUnique(exclusion.Value, $"excluded property in '{exclusion.Key}'");
        if (map.PropertyDescriptions is null || map.PropertyDescriptions.Any(section =>
                string.IsNullOrWhiteSpace(section.Key) || section.Value is null ||
                section.Value.Any(property => string.IsNullOrWhiteSpace(property.Key)
                    || string.IsNullOrWhiteSpace(property.Value))))
            throw new InvalidDataException("propertyDescriptions must map section and property names to non-empty descriptions.");

        if (map.LogMessages is null)
        {
            throw new InvalidDataException("logMessages must be an object, not null.");
        }

        if (map.LogMessages.SourcePaths is null || map.LogMessages.SourcePath is null)
        {
            throw new InvalidDataException("logMessages sourcePath/sourcePaths cannot be null.");
        }

        if (!string.IsNullOrWhiteSpace(map.LogMessages.SourcePath) && map.LogMessages.SourcePaths.Count > 0)
        {
            throw new InvalidDataException("Use logMessages.sourcePath or sourcePaths, not both.");
        }

        if (map.LogMessages.SourcePaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("logMessages.sourcePaths must contain non-empty paths.");
        }
        EnsureUnique(map.LogMessages.SourcePaths, "log source path");

        if (map.LogMessages.Enabled && !map.LogMessages.GetSourcePaths().Any())
        {
            throw new InvalidDataException("Enabled logMessages requires sourcePath or non-empty sourcePaths.");
        }

        if (map.LogMessages.Enabled && string.IsNullOrWhiteSpace(map.LogMessages.Description))
        {
            throw new InvalidDataException("A non-empty log message description is required.");
        }

        if (map.LogMessages.AllowedDuplicateEventIds is null)
        {
            throw new InvalidDataException("logMessages.allowedDuplicateEventIds must be an array, not null.");
        }

        if (map.Site is null || new[] { map.Site.SiteName, map.Site.SiteDescription,
                map.Site.SiteUrl, map.Site.Copyright, map.Site.ExportFilePrefix,
                map.Site.LogReferenceTitle }.Any(value => value is null))
        {
            throw new InvalidDataException("site must be an object with non-null string values.");
        }
        if (map.Site.ExportFilePrefix.Length > 0
            && !Regex.IsMatch(map.Site.ExportFilePrefix, "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$"))
        {
            throw new InvalidDataException("site.exportFilePrefix must contain letters/numbers separated by single hyphens.");
        }
        if (map.Site.SiteUrl.Length > 0
            && (!Uri.TryCreate(map.Site.SiteUrl, UriKind.Absolute, out var siteUri)
                || siteUri.Scheme is not ("https" or "http")))
        {
            throw new InvalidDataException("site.siteUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (map.LogMessages.AllowedDuplicateEventIds.Distinct().Count()
            != map.LogMessages.AllowedDuplicateEventIds.Count)
        {
            throw new InvalidDataException("Allowed duplicate event IDs must be unique.");
        }

        if (map.Examples is null || map.Examples.PropertyValues is null || map.Examples.TypeValues is null)
        {
            throw new InvalidDataException("examples, examples.propertyValues and examples.typeValues must be objects, not null.");
        }

        if (map.Containers is null || map.Containers.Count == 0)
        {
            throw new InvalidDataException("At least one container definition is required.");
        }

        if (map.Containers.Any(container => container is null))
        {
            throw new InvalidDataException("containers must contain objects, not null entries.");
        }

        EnsureUnique(
            map.Containers.Select(container => container.Name),
            "container name");
        EnsureUnique(
            map.Containers.Select(container => container.Slug?.ToLowerInvariant() ?? string.Empty),
            "container slug");

        foreach (var container in map.Containers)
        {
            if (string.IsNullOrWhiteSpace(container.Name))
            {
                throw new InvalidDataException("Container names cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(container.Slug)
                || !Regex.IsMatch(container.Slug, "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException(
                    $"Container slug '{container.Slug}' must contain ASCII letters or numbers separated by single hyphens.");
            }

            if (container.Slug.Equals("index", StringComparison.OrdinalIgnoreCase)
                || container.Slug.Equals("log-messages", StringComparison.OrdinalIgnoreCase)
                || container.Slug.Equals("configuration-changes", StringComparison.OrdinalIgnoreCase)
                || container.Slug.Equals("configuration-templates", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Container slug '{container.Slug}' is reserved for a generated reference page.");
            }

            if (container.Sections is null || (!container.AutoDiscover && container.Sections.Count == 0)
                || container.Sections.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException(
                    $"Container '{container.Name}' must define at least one configuration section or set autoDiscover to true.");
            }

            EnsureUnique(container.Sections, $"section in container '{container.Name}'");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string valueDescription)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Duplicate {valueDescription} '{duplicate.Key}' was found.");
        }
    }
}
