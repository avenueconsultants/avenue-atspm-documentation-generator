namespace AtspmDocsGenerator;

public static class OutputDirectoryTransaction
{
    public static void Run(string outputRoot, Action<string> generate)
        => Run(outputRoot, generate, Directory.Move, Directory.Delete);

    internal static void Run(
        string outputRoot,
        Action<string> generate,
        Action<string, bool> deleteDirectory)
        => Run(outputRoot, generate, Directory.Move, deleteDirectory);

    internal static void Run(
        string outputRoot,
        Action<string> generate,
        Action<string, string> moveDirectory,
        Action<string, bool> deleteDirectory)
    {
        var target = Path.GetFullPath(outputRoot);
        var parent = Path.GetDirectoryName(target)
            ?? throw new InvalidDataException($"Output directory has no parent: {target}");
        Directory.CreateDirectory(parent);

        var name = Path.GetFileName(target);
        var transactionId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, $".{name}.staging-{transactionId}");
        var backup = Path.Combine(parent, $".{name}.backup-{transactionId}");
        var movedOriginal = false;
        var swapped = false;

        Directory.CreateDirectory(staging);
        try
        {
            generate(staging);

            if (Directory.Exists(target))
            {
                moveDirectory(target, backup);
                movedOriginal = true;
            }

            try
            {
                moveDirectory(staging, target);
                swapped = true;
            }
            catch
            {
                if (movedOriginal && !Directory.Exists(target))
                {
                    moveDirectory(backup, target);
                    movedOriginal = false;
                }
                throw;
            }

        }
        finally
        {
            if (Directory.Exists(staging))
            {
                deleteDirectory(staging, true);
            }

            if (movedOriginal && Directory.Exists(backup) && !Directory.Exists(target))
            {
                moveDirectory(backup, target);
            }

            if (swapped && Directory.Exists(backup))
            {
                try
                {
                    deleteDirectory(backup, true);
                }
                catch (Exception firstException)
                {
                    try
                    {
                        deleteDirectory(backup, true);
                    }
                    catch (Exception retryException)
                    {
                        throw new IOException(
                            $"Generated output was installed, but its backup could not be removed: {backup}",
                            new AggregateException(firstException, retryException));
                    }
                }
            }
        }
    }
}
