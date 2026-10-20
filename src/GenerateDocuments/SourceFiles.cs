namespace AtspmDocsGenerator;

internal static class SourceFiles
{
    // Prune generated/dependency trees before traversing them, including nested checkouts.
    private static bool SkipDirectory(string name) =>
        name.StartsWith('.') || name.StartsWith('_')
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("packages", StringComparison.OrdinalIgnoreCase)
        || name.Equals("vendor", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tests", StringComparison.OrdinalIgnoreCase)
        || name.Equals("test", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> Enumerate(string sourceRoot, IEnumerable<string> sourcePaths)
    {
        var files = new SortedSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var sourcePath in sourcePaths)
        {
            var root = SourcePath.ResolveWithinRoot(sourceRoot, sourcePath, "Configured source path");
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Configured source path does not exist: {root}");
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    // Do not follow links/junctions while discovering source.
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if (entry is DirectoryInfo)
                    {
                        if (!SkipDirectory(entry.Name))
                            pending.Push(entry.FullName);
                    }
                    else if (entry.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                        files.Add(entry.FullName);
                }
            }
        }
        return files.Order(StringComparer.Ordinal);
    }
}
