namespace AtspmDocsGenerator;

internal static class PathSafety
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException($"Path has no root: {fullPath}");
        var current = root;
        var remainder = fullPath[root.Length..];

        foreach (var segment in remainder.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo? link = null;
            try
            {
                link = Directory.Exists(candidate)
                    ? new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)
                    : File.Exists(candidate)
                        ? new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)
                        : null;
            }
            catch (UnauthorizedAccessException)
            {
                // Some sandboxed parent directories cannot expose reparse-point metadata.
            }
            current = link?.FullName ?? candidate;
        }

        return Path.GetFullPath(current);
    }

    public static bool IsStrictlyWithin(string root, string candidate)
    {
        var normalizedRoot = Canonicalize(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Canonicalize(candidate);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

        return !normalizedCandidate.Equals(normalizedRoot, PathComparison)
            && normalizedCandidate.StartsWith(rootWithSeparator, PathComparison);
    }
}
