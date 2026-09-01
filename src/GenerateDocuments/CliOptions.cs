using System.Globalization;

namespace AtspmDocsGenerator;

public sealed record CliOptions(
    string WorkspaceRoot,
    string SourceRoot,
    string OutputRoot,
    string MapPath,
    string RepositoryUrl,
    string RepositoryRef,
    DateTimeOffset GeneratedAt)
{
    private static readonly string[] RequiredOptionNames =
    [
        "--workspace-root",
        "--source-root",
        "--output-root",
        "--map",
        "--repository-url",
        "--repository-ref",
        "--generated-at"
    ];

    public const string HelpText =
        """
        ATSPM configuration documentation generator

        Usage:
          dotnet run --project src/GenerateDocuments -- \
            --workspace-root <path> \
            --source-root <path> \
            --output-root <path> \
            --map <path> \
            --repository-url <url> \
            --repository-ref <git-ref-or-sha> \
            --generated-at <ISO-8601-timestamp>

        Options:
          --workspace-root  Root that must contain the generated output directory.
          --source-root     Root of the source repository to scan.
          --output-root     Dedicated directory for generated Markdown pages.
          --map             Path to the container configuration map.
          --repository-url  Public source repository URL used in generated links.
          --repository-ref  Git branch, tag, or commit SHA used in generated links.
          --generated-at    Source commit timestamp used in generated pages.
          --help            Show this help.
        """;

    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return CliParseResult.Failure("No options were provided.");
        }

        if (args.Any(argument => argument is "--help" or "-h"))
        {
            return CliParseResult.Help();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Length;)
        {
            var name = args[index];
            if (!RequiredOptionNames.Contains(name, StringComparer.Ordinal))
            {
                return CliParseResult.Failure($"Unknown option '{name}'.");
            }

            if (index + 1 >= args.Length
                || RequiredOptionNames.Contains(args[index + 1], StringComparer.Ordinal)
                || args[index + 1] is "--help" or "-h")
            {
                return CliParseResult.Failure($"Option '{name}' requires a value.");
            }

            if (!values.TryAdd(name, args[index + 1]))
            {
                return CliParseResult.Failure($"Option '{name}' was provided more than once.");
            }

            index += 2;
        }

        var missing = RequiredOptionNames.Where(name => !values.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            return CliParseResult.Failure($"Missing required options: {string.Join(", ", missing)}.");
        }

        var workspaceRoot = PathSafety.Canonicalize(values["--workspace-root"]);
        if (!Directory.Exists(workspaceRoot))
        {
            return CliParseResult.Failure($"Workspace root does not exist: {workspaceRoot}");
        }

        var sourceRoot = Path.GetFullPath(values["--source-root"]);
        if (!Directory.Exists(sourceRoot))
        {
            return CliParseResult.Failure($"Source root does not exist: {sourceRoot}");
        }

        var mapPath = Path.GetFullPath(values["--map"]);
        if (!File.Exists(mapPath))
        {
            return CliParseResult.Failure($"Configuration map does not exist: {mapPath}");
        }

        if (!Uri.TryCreate(values["--repository-url"], UriKind.Absolute, out var repositoryUri)
            || repositoryUri.Scheme is not ("http" or "https"))
        {
            return CliParseResult.Failure("--repository-url must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(values["--repository-ref"]))
        {
            return CliParseResult.Failure("--repository-ref cannot be empty.");
        }

        if (!DateTimeOffset.TryParse(
                values["--generated-at"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var generatedAt))
        {
            return CliParseResult.Failure("--generated-at must be a valid ISO-8601 timestamp.");
        }

        var outputRoot = PathSafety.Canonicalize(values["--output-root"]);
        if (!PathSafety.IsStrictlyWithin(workspaceRoot, outputRoot))
        {
            return CliParseResult.Failure(
                $"Output root must be a child of the workspace root '{workspaceRoot}': {outputRoot}");
        }

        var repositoryUrl = repositoryUri
            .GetLeftPart(UriPartial.Path)
            .TrimEnd('/');

        if (repositoryUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryUrl = repositoryUrl[..^4];
        }

        return CliParseResult.Success(new CliOptions(
            workspaceRoot,
            sourceRoot,
            outputRoot,
            mapPath,
            repositoryUrl,
            values["--repository-ref"],
            generatedAt));
    }
}

public sealed record CliParseResult(CliOptions? Options, string? Error, bool ShowHelp)
{
    public static CliParseResult Success(CliOptions options) => new(options, null, false);

    public static CliParseResult Failure(string error) => new(null, error, false);

    public static CliParseResult Help() => new(null, null, true);
}
