using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AtspmDocsGenerator;

public static class SetupCommands
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public const string HelpText = """
        Setup commands:
          init --source-root <path> [--map <path>] [--product-name <name>] [--source-path <folder>]...
          check --source-root <path> [--map <path>]
          sync --source-root <path> [--map <path>] [--container <slug>] [--write]

        --map defaults to documentation.json at source-root.
        init scans source-root (or repeated --source-path folders) and never overwrites a file.
        check validates paths, registrations, exclusions, mappings, logs and coverage without writing.
        sync previews additive mapping changes; --container explicitly selects a page for unmapped sections.
        --write applies those additions atomically. It preserves values/order but reformats JSON and removes comments.
        Automatic pages include newly discovered sections during generation without rewriting the map.
        init/sync writes must stay inside source-root. No command edits application code or runs Git.
        Exit codes: 0 success, 1 validation/coverage failure, 2 command usage error.
        """;

    public static int Run(string[] args)
    {
        try
        {
            if (args.Any(value => value is "--help" or "-h"))
            {
                Console.WriteLine(HelpText);
                return 0;
            }
            var command = args[0];
            var allowed = new HashSet<string>(["--source-root", "--map"], StringComparer.Ordinal);
            if (command == "init") allowed.UnionWith(["--source-path", "--product-name"]);
            if (command == "sync") allowed.UnionWith(["--container", "--write"]);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var paths = new List<string>();
            var write = false;
            for (var index = 1; index < args.Length; index++)
            {
                var key = args[index];
                if (!allowed.Contains(key)) throw new ArgumentException($"Unknown option '{key}' for {command}.");
                if (key == "--write")
                {
                    if (write) throw new ArgumentException("--write was provided more than once.");
                    write = true;
                    continue;
                }
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException($"Option '{key}' requires a non-empty value.");
                if (key == "--source-path") paths.Add(args[index]);
                else if (!values.TryAdd(key, args[index])) throw new ArgumentException($"Option '{key}' was provided more than once.");
            }
            if (!values.TryGetValue("--source-root", out var source))
                throw new ArgumentException("--source-root is required.");
            var root = PathSafety.Canonicalize(source);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Source root does not exist: {root}");
            var mapPath = Path.GetFullPath(Path.Combine(root,
                values.GetValueOrDefault("--map", "documentation.json")));
            if (command == "init")
                return Init(root, mapPath, paths.Count == 0 ? ["."] : paths,
                    values.GetValueOrDefault("--product-name", new DirectoryInfo(root).Name));

            var map = DocumentationMapLoader.Load(mapPath);
            var sections = ConfigurationDiscovery.Analyze(root, map);
            ConfigurationDiscovery.ValidateReferences(map, sections);
            if (map.LogMessages.Enabled)
                new LogMessageSourceAnalyzer().Analyze(root, map.LogMessages.GetSourcePaths(),
                map.LogMessages.AllowedDuplicateEventIds.ToHashSet(), map.LogMessages.IncludeOrdinaryCalls);
            var unmapped = ConfigurationDiscovery.Unmapped(map, sections);
            Console.WriteLine($"Discovered {sections.Count} configuration sections; {unmapped.Count} unmapped.");
            foreach (var name in unmapped) Console.WriteLine($"Unmapped section: {name}");
            foreach (var section in sections.Values)
            {
                if (string.IsNullOrWhiteSpace(section.Summary) && string.IsNullOrWhiteSpace(section.AttributeDescription))
                    Console.WriteLine($"Warning: missing description for section {section.SectionName}.");
                foreach (var property in section.Properties.Where(property => string.IsNullOrWhiteSpace(property.Summary)))
                    Console.WriteLine($"Warning: missing summary for {section.SectionName}.{property.Name}.");
            }
            if (sections.Count == 0)
            {
                Console.Error.WriteLine("No configuration sections found. Register unannotated types with configurationTypes or adjust sourcePaths.");
                return 1;
            }
            if (command == "check") return unmapped.Count == 0 ? 0 : 1;
            var targetSlug = values.GetValueOrDefault("--container");
            var target = targetSlug is null ? null : map.Containers.SingleOrDefault(container => container.Slug == targetSlug)
                ?? throw new InvalidDataException($"Unknown container slug '{targetSlug}'.");
            if (unmapped.Count == 0)
            {
                Console.WriteLine("Configuration is synchronized; no changes needed.");
                return 0;
            }
            if (target is null)
            {
                Console.Error.WriteLine("Select --container <slug> to assign unmapped sections, or enable autoDiscover on the intended page. No files changed.");
                return 1;
            }
            foreach (var name in unmapped) Console.WriteLine($"Add '{name}' to '{target.Slug}'.");
            if (!write)
            {
                Console.WriteLine("Preview only. Repeat with --write to update the configuration.");
                return 0;
            }
            var json = JsonNode.Parse(File.ReadAllText(mapPath), documentOptions:
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
            var containers = GetValue(json, "containers")!.AsArray();
            var page = containers.Select(item => item!.AsObject())
                .Single(item => GetValue(item, "slug")!.GetValue<string>() == target.Slug);
            var list = GetValue(page, "sections")!.AsArray();
            foreach (var name in unmapped) list.Add(name);
            WriteMap(root, mapPath, json.ToJsonString(WriteOptions) + "\n", overwrite: true);
            Console.WriteLine($"Updated {mapPath}. Review the configuration diff before committing.");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(HelpText);
            return 2;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Setup failed: {exception.Message}");
            return 1;
        }
    }

    private static int Init(string root, string mapPath, IReadOnlyList<string> paths, string product)
    {
        if (File.Exists(mapPath)) throw new InvalidDataException($"Configuration already exists: {mapPath}. Use check or sync; init will not overwrite it.");
        var sourcePaths = paths.Select(path => Path.GetRelativePath(root,
            SourcePath.ResolveWithinRoot(root, path, "Discovery source path")).Replace('\\', '/')).ToArray();
        var sections = new ConfigurationSourceAnalyzer().Analyze(root, sourcePaths);
        var logPaths = new SortedSet<string>(StringComparer.Ordinal);
        var candidates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles.Enumerate(root, sourcePaths))
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
            if (syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
                    method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                        attribute.Name.ToString().Split('.').Last() is "LoggerMessage" or "LoggerMessageAttribute")))
                logPaths.Add(Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace('\\', '/'));
            foreach (var type in syntax.DescendantNodes().OfType<TypeDeclarationSyntax>())
                if ((type.Identifier.ValueText.EndsWith("Configuration", StringComparison.Ordinal)
                     || type.Identifier.ValueText.EndsWith("Options", StringComparison.Ordinal))
                    && !type.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                        attribute.Name.ToString().Split('.').Last() is "ConfigurationSection" or "ConfigurationSectionAttribute"))
                    candidates.Add($"{type.Identifier.ValueText} ({Path.GetRelativePath(root, file).Replace('\\', '/')})");
        }
        var map = new DocumentationMap
        {
            SchemaVersion = 2, ProductName = product, SourcePaths = sourcePaths,
            LogMessages = new LogMessageMap
            {
                Enabled = logPaths.Count > 0, SourcePaths = logPaths.ToArray(), Description = $"Structured log events for {product}."
            },
            Containers = [new ContainerDefinition { Name = product, Slug = "configuration", AutoDiscover = true }]
        };
        DocumentationMapLoader.Validate(map);
        var json = JsonSerializer.SerializeToNode(map, WriteOptions)!.AsObject();
        foreach (var key in new[] { "$schema", "site", "examples", "configurationTypes", "excludedProperties", "propertyDescriptions", "excludedSections" })
            json.Remove(key);
        // Do not serialize an empty singular path alongside the multiple-folder form.
        json["logMessages"]!.AsObject().Remove("sourcePath");
        if (!map.LogMessages.Enabled) json.Remove("logMessages");
        json["containers"]![0]!.AsObject().Remove("sections");
        WriteMap(root, mapPath, json.ToJsonString(WriteOptions) + "\n", overwrite: false);
        Console.WriteLine($"Created {mapPath}. Found {sections.Count} attributed sections and {logPaths.Count} log folders.");
        foreach (var candidate in candidates) Console.WriteLine($"Review unannotated candidate for explicit registration: {candidate}");
        Console.WriteLine("Review sourcePaths, examples and service grouping, then run check. Service ownership and duplicate-ID exceptions are not inferred.");
        return 0;
    }

    private static JsonNode? GetValue(JsonObject value, string key) =>
        value.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static void WriteMap(string root, string path, string contents, bool overwrite)
    {
        if (!PathSafety.IsStrictlyWithin(root, path))
            throw new InvalidDataException("Configuration writes must remain inside source-root.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
