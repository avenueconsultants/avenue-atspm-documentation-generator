using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtspmDocsGenerator;

using static DocumentationText;

public static class ConfigurationChangeGenerator
{
    public const string SnapshotFileName = "configuration-snapshot.json";
    public const string ReportFileName = "configuration-changes.md";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ConfigurationSnapshot? LoadPrevious(string outputRoot)
    {
        var path = Path.Combine(outputRoot, SnapshotFileName);
        if (!File.Exists(path))
        {
            var manifestPath = Path.Combine(outputRoot, GeneratedOutputPolicy.ManifestFileName);
            var expected = File.Exists(Path.Combine(outputRoot, ReportFileName));
            if (File.Exists(manifestPath))
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                expected |= manifest.RootElement.GetProperty("ManagedFiles").EnumerateArray()
                    .Any(item => item.GetString() == SnapshotFileName);
            }
            if (expected)
                throw new InvalidDataException($"Previous {SnapshotFileName} is missing. Restore it from the documentation baseline before regenerating.");
            return null;
        }
        try
        {
            var snapshot = JsonSerializer.Deserialize<ConfigurationSnapshot>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Snapshot must be an object.");
            Validate(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Invalid previous snapshot '{path}': {exception.Message}", exception);
        }
    }

    public static ConfigurationSnapshot Capture(DocumentationMap map,
        IReadOnlyDictionary<string, ConfigurationSection> available, CliOptions options,
        ConfigurationSnapshot? previous)
    {
        ConfigurationDiscovery.ValidateReferences(map, available);
        var bindings = new Dictionary<string, (ConfigurationSection Section, List<SnapshotService> Services)>(StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in map.Containers)
        {
            foreach (var name in ConfigurationDiscovery.GetSections(container, available))
            {
                var baseName = available.ContainsKey(name) ? name : MappedSectionName.GetBaseName(name);
                consumed.Add(baseName);
                if (!bindings.TryGetValue(name, out var binding))
                    bindings.Add(name, binding = (available[baseName], []));
                binding.Services.Add(new SnapshotService(container.Name, container.Slug));
            }
        }
        // Unmapped discoveries remain visible, rather than hiding a newly introduced setting.
        foreach (var name in available.Keys.Except(consumed))
            bindings.Add(name, (available[name], []));

        var sections = bindings.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
            new SnapshotSection(item.Key, item.Value.Section.SourcePath, item.Value.Section.SourceLine,
                item.Value.Services.OrderBy(service => service.Slug, StringComparer.Ordinal).ToArray(),
                item.Value.Section.Properties.OrderBy(property => property.Name, StringComparer.Ordinal).Select(property =>
                    new SnapshotProperty(property.Name, property.TypeName, property.DefaultExpression, property.IsRequired,
                        Sorted(property.Options ?? []),
                        Sorted((property.EnvironmentVariableSuffixes is { Count: > 0 } ? property.EnvironmentVariableSuffixes : [property.Name])
                            .Select(suffix => $"{MappedSectionName.ToEnvironmentVariablePrefix(item.Key)}__{suffix}"))))
                    .ToArray())).ToArray();
        var changes = previous is null ? [] : Compare(previous.Sections, sections);
        // Keep provenance and the latest meaningful comparison, even at a later source revision.
        if (previous is not null && changes.Count == 0)
            return previous;
        return new ConfigurationSnapshot
        {
            SchemaVersion = 1, ProductName = map.ProductName, RepositoryUrl = options.RepositoryUrl,
            SourceRevision = options.RepositoryRef, SourceTimestamp = options.GeneratedAt, Sections = sections,
            Report = new ConfigurationChangeReport
            {
                BaselineRepositoryUrl = previous?.RepositoryUrl, BaselineRevision = previous?.SourceRevision,
                Changes = changes
            }
        };
    }

    public static IReadOnlyList<ConfigurationChange> Compare(IReadOnlyList<SnapshotSection> before, IReadOnlyList<SnapshotSection> after)
    {
        var changes = new List<ConfigurationChange>();
        var oldSections = before.ToDictionary(section => section.Name, StringComparer.Ordinal);
        var newSections = after.ToDictionary(section => section.Name, StringComparer.Ordinal);
        foreach (var name in Sorted(oldSections.Keys.Concat(newSections.Keys)))
        {
            oldSections.TryGetValue(name, out var oldSection);
            newSections.TryGetValue(name, out var newSection);
            var services = Sorted(ServiceNames(oldSection).Concat(ServiceNames(newSection)));
            void Add(string kind, string? property, string? oldValue, string? newValue, IEnumerable<string> variables) =>
                changes.Add(new ConfigurationChange(kind, name, property, oldValue, newValue, services, Sorted(variables)));
            if (oldSection is null)
                Add("Section added", null, null, name, []);
            else if (newSection is null)
                Add("Section removed", null, name, null, []);
            else if (!ServiceNames(oldSection).SequenceEqual(ServiceNames(newSection)))
                Add("Service mapping changed", null, JoinServices(oldSection), JoinServices(newSection), []);

            var oldProperties = (oldSection?.Properties ?? []).ToDictionary(property => property.Name, StringComparer.Ordinal);
            var newProperties = (newSection?.Properties ?? []).ToDictionary(property => property.Name, StringComparer.Ordinal);
            foreach (var propertyName in Sorted(oldProperties.Keys.Concat(newProperties.Keys)))
            {
                oldProperties.TryGetValue(propertyName, out var oldProperty);
                newProperties.TryGetValue(propertyName, out var newProperty);
                var variables = Sorted((oldProperty?.EnvironmentVariables ?? []).Concat(newProperty?.EnvironmentVariables ?? []));
                if (oldProperty is null)
                    Add(newProperty!.Required ? "Required setting added" : "Setting added", propertyName, null, Describe(newProperty), variables);
                else if (newProperty is null)
                    Add("Setting removed", propertyName, Describe(oldProperty), null, variables);
                else
                {
                    void Field(string kind, string oldValue, string newValue)
                    {
                        if (oldValue != newValue) Add(kind, propertyName, oldValue, newValue, variables);
                    }
                    Field("Type changed", oldProperty.Type, newProperty.Type);
                    Field("Default changed", oldProperty.Default, newProperty.Default);
                    Field("Required status changed", YesNo(oldProperty.Required), YesNo(newProperty.Required));
                    if (!Sorted(oldProperty.Options).SequenceEqual(Sorted(newProperty.Options)))
                        Add("Enum options changed", propertyName, string.Join(", ", oldProperty.Options), string.Join(", ", newProperty.Options), variables);
                    if (!Sorted(oldProperty.EnvironmentVariables).SequenceEqual(Sorted(newProperty.EnvironmentVariables)))
                        Add("Environment variables changed", propertyName, string.Join(", ", oldProperty.EnvironmentVariables), string.Join(", ", newProperty.EnvironmentVariables), variables);
                }
            }
        }
        return changes;
    }

    public static void Write(string outputRoot, ConfigurationSnapshot snapshot)
    {
        File.WriteAllText(Path.Combine(outputRoot, SnapshotFileName), NormalizeLineEndings(JsonSerializer.Serialize(snapshot, JsonOptions)) + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputRoot, ReportFileName), Render(snapshot), new UTF8Encoding(false));
    }

    public static string Render(ConfigurationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Configuration changes");
        builder.AppendLine();
        builder.AppendLine("This page retains the latest recorded configuration comparison. Unchanged reruns keep the same report and snapshot, even when other source files change.");
        builder.AppendLine();
        builder.AppendLine($"**Recorded configuration revision:** {CodeSpan(snapshot.SourceRevision)} ([source commit]({BuildRepositoryUrl(snapshot.RepositoryUrl, "commit", snapshot.SourceRevision)})).");
        builder.AppendLine();
        builder.AppendLine($"**Source commit time:** {CodeSpan(snapshot.SourceTimestamp.ToUniversalTime().ToString("O"))}.");
        builder.AppendLine();
        if (snapshot.Report.BaselineRevision is null)
            builder.AppendLine("**Initial baseline established.** No earlier snapshot was available, so historical changes cannot be determined. Existing settings are not labeled as newly added.");
        else
        {
            builder.AppendLine($"**Compared with:** {CodeSpan(snapshot.Report.BaselineRevision)} ([baseline commit]({BuildRepositoryUrl(snapshot.Report.BaselineRepositoryUrl!, "commit", snapshot.Report.BaselineRevision)})).");
            builder.AppendLine();
            builder.AppendLine($"**{snapshot.Report.Changes.Count} change entries** detected in the documented configuration.");
        }
        builder.AppendLine();
        builder.AppendLine("[Download the configuration snapshot](configuration-snapshot.json)");
        builder.AppendLine();
        builder.AppendLine("Review required settings, defaults, accepted values, environment-variable names and affected services before deployment. Defaults are source expressions, not evaluated runtime values. Changes may come from source edits or documentation mappings/exclusions; they do not prove that deployed configuration changed. Renames appear as removals and additions.");
        builder.AppendLine();
        var unmapped = snapshot.Sections.Where(section => section.Services.Count == 0).Select(section => CodeSpan(section.Name)).ToArray();
        if (unmapped.Length > 0)
        {
            builder.AppendLine($"**Unmapped discovered sections:** {string.Join(", ", unmapped)}. Assign these to the services that use them.");
            builder.AppendLine();
        }
        if (snapshot.Report.Changes.Count > 0)
        {
            builder.AppendLine("| Change | Section | Setting | Before | After | Affected services (before or after) | Environment variables (before or after) |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var change in snapshot.Report.Changes)
                builder.AppendLine($"| {CodeSpan(change.Kind)} | {CodeSpan(change.Section)} | {Cell(change.Property)} | {Cell(change.Before)} | {Cell(change.After)} | {Cell(change.AffectedServices.Count == 0 ? "Unmapped" : string.Join(", ", change.AffectedServices))} | {Cell(string.Join(", ", change.EnvironmentVariables))} |");
        }
        return NormalizeLineEndings(builder.ToString());
    }

    private static string Cell(string? value) => value is null ? "—" : CodeSpan(value.Length == 0 ? "(none)" : value);
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string Describe(SnapshotProperty property) =>
        $"Type: {property.Type}; default: {property.Default}; required: {YesNo(property.Required)}"
        + (property.Options.Count == 0 ? "" : $"; options: {string.Join(", ", property.Options)}");
    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static IReadOnlyList<string> ServiceNames(SnapshotSection? section) =>
        Sorted((section?.Services ?? []).Select(service => $"{service.Name} ({service.Slug})"));
    private static string JoinServices(SnapshotSection section) =>
        section.Services.Count == 0 ? "Unmapped" : string.Join(", ", ServiceNames(section));

    private static void Validate(ConfigurationSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1) throw new InvalidDataException($"Unsupported snapshot schema version {snapshot.SchemaVersion}; expected 1.");
        if (string.IsNullOrWhiteSpace(snapshot.ProductName) || string.IsNullOrWhiteSpace(snapshot.SourceRevision)
            || !IsRepositoryUrl(snapshot.RepositoryUrl) || snapshot.SourceTimestamp == default
            || snapshot.Sections is null || snapshot.Report is null || snapshot.Report.Changes is null)
            throw new InvalidDataException("Snapshot metadata, sections or report are incomplete.");
        if ((snapshot.Report.BaselineRevision is null) != (snapshot.Report.BaselineRepositoryUrl is null)
            || (snapshot.Report.BaselineRevision is not null && (string.IsNullOrWhiteSpace(snapshot.Report.BaselineRevision)
                || !IsRepositoryUrl(snapshot.Report.BaselineRepositoryUrl)))
            || (snapshot.Report.BaselineRevision is null && snapshot.Report.Changes.Count != 0))
            throw new InvalidDataException("Snapshot comparison provenance is invalid.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in snapshot.Sections)
        {
            if (section is null || string.IsNullOrWhiteSpace(section.Name) || !names.Add(section.Name)
                || string.IsNullOrWhiteSpace(section.SourcePath) || section.SourceLine < 1
                || section.Properties is null || section.Services is null)
                throw new InvalidDataException("Snapshot sections are invalid or duplicated.");
            var slugs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var service in section.Services)
                if (service is null || string.IsNullOrWhiteSpace(service.Name) || string.IsNullOrWhiteSpace(service.Slug) || !slugs.Add(service.Slug))
                    throw new InvalidDataException("Snapshot services are invalid or duplicated.");
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in section.Properties)
                if (property is null || string.IsNullOrWhiteSpace(property.Name) || !properties.Add(property.Name)
                    || string.IsNullOrWhiteSpace(property.Type) || property.Default is null
                    || property.Options is null || property.Options.Any(string.IsNullOrWhiteSpace)
                    || property.EnvironmentVariables is null || property.EnvironmentVariables.Count == 0
                    || property.EnvironmentVariables.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Snapshot properties are invalid or duplicated.");
        }
        foreach (var change in snapshot.Report.Changes)
            if (change is null || string.IsNullOrWhiteSpace(change.Kind) || string.IsNullOrWhiteSpace(change.Section)
                || change.AffectedServices is null || change.AffectedServices.Any(string.IsNullOrWhiteSpace)
                || change.EnvironmentVariables is null || change.EnvironmentVariables.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Snapshot change entries are invalid.");
    }

    private static bool IsRepositoryUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
