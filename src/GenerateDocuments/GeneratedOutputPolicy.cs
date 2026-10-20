using System.Text.Json;

namespace AtspmDocsGenerator;

internal static class GeneratedOutputPolicy
{
    public const string ManifestFileName = "support/.atspm-docs-generator.json";
    private const string GeneratorId = "atspm-documentation-generator";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void ValidateExisting(string outputRoot, DocumentationMap map)
    {
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        var entries = EnumerateEntries(outputRoot).ToArray();
        var actualFiles = entries.Where(path => File.Exists(path))
            .Select(path => Path.GetRelativePath(outputRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();
        var directories = entries.Where(Directory.Exists)
            .Select(path => Path.GetRelativePath(outputRoot, path).Replace('\\', '/')).ToArray();

        if (actualFiles.Length == 0 && directories.Length == 0)
        {
            return;
        }

        var manifestPath = OutputLayout.ExistingPath(outputRoot, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<GeneratedOutputManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException($"Generated output manifest is empty: {manifestPath}");
            if (!string.Equals(manifest.Generator, GeneratorId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Output root has an unrecognized ownership manifest: {manifestPath}");
            }
            if (manifest.ManagedFiles is null || manifest.ManagedFiles.Any(name =>
                string.IsNullOrWhiteSpace(name) || name.Contains('\\') || Path.IsPathRooted(name)
                || name.Split('/').Any(part => part is "" or "." or "..")))
                throw new InvalidDataException($"Output root has an invalid managed file list: {manifestPath}");

            var allowed = manifest.ManagedFiles.Append(Path.GetRelativePath(outputRoot, manifestPath).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            var foreign = actualFiles.Where(file => !allowed.Contains(file)).ToArray();
            var foreignDirectories = directories.Where(directory => !allowed.Any(file => file.StartsWith(directory + "/", StringComparison.Ordinal))).ToArray();
            if (foreign.Length > 0 || foreignDirectories.Length > 0)
            {
                throw new InvalidDataException(
                    $"Output root contains files or directories not owned by the generator: {string.Join(", ", foreign.Concat(foreignDirectories))}");
            }

            return;
        }

        var expected = GetManagedFiles(map).Select(name => Path.GetFileName(name)).Order(StringComparer.Ordinal).ToArray();
        var legacy = map.Containers.Select(container => $"{container.Slug}.md")
            .Append("index.md")
            .Concat(map.LogMessages.Enabled ? new[] { "log-messages.md" } : [])
            .Order(StringComparer.Ordinal).ToArray();
        var previous = legacy.Append(Path.GetFileName(ConfigurationChangeGenerator.ReportFileName))
            .Append(Path.GetFileName(ConfigurationChangeGenerator.SnapshotFileName))
            .Order(StringComparer.Ordinal).ToArray();
        if (directories.Length > 0 || (!actualFiles.SequenceEqual(expected, StringComparer.Ordinal)
            && !actualFiles.SequenceEqual(previous, StringComparer.Ordinal)
            && !actualFiles.SequenceEqual(legacy, StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                $"Existing output root is not empty and has no generator ownership manifest: {outputRoot}");
        }
    }

    public static void WriteManifest(string outputRoot, DocumentationMap map)
    {
        OutputLayout.CreateSupportDirectory(outputRoot);
        var manifest = new GeneratedOutputManifest(GeneratorId, GetManagedFiles(map));
        var json = DocumentationText.NormalizeLineEndings(JsonSerializer.Serialize(manifest, SerializerOptions)) + "\n";
        File.WriteAllText(Path.Combine(outputRoot, ManifestFileName), json);
    }

    private static IReadOnlyList<string> GetManagedFiles(DocumentationMap map) =>
        map.Containers.Select(container => $"{container.Slug}.md")
            .Concat(map.Containers.SelectMany(container => new[]
            {
                OutputLayout.SupportFile($"{container.Slug}.example.json"), OutputLayout.SupportFile($"{container.Slug}.env.example")
            }))
            .Append("index.md")
            .Append(OutputLayout.TemplatePageFileName)
            .Append(ConfigurationChangeGenerator.ReportFileName)
            .Append(ConfigurationChangeGenerator.SnapshotFileName)
            .Concat(map.LogMessages.Enabled ? new[] { OutputLayout.LogFileName } : [])
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed record GeneratedOutputManifest(string Generator, IReadOnlyList<string> ManagedFiles);

    private static IEnumerable<string> EnumerateEntries(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Generated output cannot contain symbolic links: {path}");
            yield return path;
            if (Directory.Exists(path))
                foreach (var child in EnumerateEntries(path)) yield return child;
        }
    }
}
