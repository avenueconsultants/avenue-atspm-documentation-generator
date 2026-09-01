using System.Text.Json;

namespace AtspmDocsGenerator;

internal static class GeneratedOutputPolicy
{
    public const string ManifestFileName = ".atspm-docs-generator.json";
    private const string GeneratorId = "atspm-documentation-generator";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void ValidateExisting(string outputRoot, DocumentationMap map)
    {
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        var actualFiles = Directory.EnumerateFiles(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var directories = Directory.EnumerateDirectories(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();

        if (actualFiles.Length == 0 && directories.Length == 0)
        {
            return;
        }

        if (directories.Length > 0)
        {
            throw new InvalidDataException(
                $"Output root contains directories not owned by the generator: {outputRoot}");
        }

        var manifestPath = Path.Combine(outputRoot, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<GeneratedOutputManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException($"Generated output manifest is empty: {manifestPath}");
            if (!string.Equals(manifest.Generator, GeneratorId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Output root has an unrecognized ownership manifest: {manifestPath}");
            }

            var allowed = manifest.ManagedFiles.Append(ManifestFileName).ToHashSet(StringComparer.Ordinal);
            var foreign = actualFiles.Where(file => !allowed.Contains(file)).ToArray();
            if (foreign.Length > 0)
            {
                throw new InvalidDataException(
                    $"Output root contains files not owned by the generator: {string.Join(", ", foreign)}");
            }

            return;
        }

        var expected = GetManagedFiles(map).Order(StringComparer.Ordinal).ToArray();
        if (!actualFiles.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Existing output root is not empty and has no generator ownership manifest: {outputRoot}");
        }
    }

    public static void WriteManifest(string outputRoot, DocumentationMap map)
    {
        var manifest = new GeneratedOutputManifest(GeneratorId, GetManagedFiles(map));
        var json = JsonSerializer.Serialize(manifest, SerializerOptions) + "\n";
        File.WriteAllText(Path.Combine(outputRoot, ManifestFileName), json);
    }

    private static IReadOnlyList<string> GetManagedFiles(DocumentationMap map) =>
        map.Containers.Select(container => $"{container.Slug}.md")
            .Append("index.md")
            .Append("log-messages.md")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed record GeneratedOutputManifest(string Generator, IReadOnlyList<string> ManagedFiles);
}
