namespace AtspmDocsGenerator;

internal static class SourcePath
{
    public static string ResolveWithinRoot(string sourceRoot, string relativePath, string description)
    {
        var normalizedRoot = PathSafety.Canonicalize(sourceRoot);
        var fullPath = PathSafety.Canonicalize(Path.Combine(normalizedRoot, relativePath));

        if (!fullPath.Equals(normalizedRoot, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
            && !PathSafety.IsStrictlyWithin(normalizedRoot, fullPath))
        {
            throw new InvalidDataException(
                $"{description} must remain inside the source root: {fullPath}");
        }

        return fullPath;
    }
}
