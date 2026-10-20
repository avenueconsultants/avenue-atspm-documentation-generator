using System.Text.Json.Serialization;

namespace AtspmDocsGenerator;

public sealed class ConfigurationSnapshot
{
    public required int SchemaVersion { get; init; }
    public required string ProductName { get; init; }
    public required string RepositoryUrl { get; init; }
    public required string SourceRevision { get; init; }
    public required DateTimeOffset SourceTimestamp { get; init; }
    public required IReadOnlyList<SnapshotSection> Sections { get; init; }
    public required ConfigurationChangeReport Report { get; init; }
}

public sealed record SnapshotService(string Name, string Slug);

public sealed record SnapshotProperty(
    string Name,
    string Type,
    string Default,
    [property: JsonRequired] bool Required,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> EnvironmentVariables);

public sealed record SnapshotSection(
    string Name,
    string SourcePath,
    int SourceLine,
    IReadOnlyList<SnapshotService> Services,
    IReadOnlyList<SnapshotProperty> Properties);

public sealed class ConfigurationChangeReport
{
    public required string? BaselineRepositoryUrl { get; init; }
    public required string? BaselineRevision { get; init; }
    public required IReadOnlyList<ConfigurationChange> Changes { get; init; }
}

public sealed record ConfigurationChange(
    string Kind,
    string Section,
    string? Property,
    string? Before,
    string? After,
    IReadOnlyList<string> AffectedServices,
    IReadOnlyList<string> EnvironmentVariables);
