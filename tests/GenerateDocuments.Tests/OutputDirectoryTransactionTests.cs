namespace AtspmDocsGenerator.Tests;

public sealed class OutputDirectoryTransactionTests
{
    [Fact]
    public void RunPreservesExistingOutputWhenGenerationFails()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "existing.md"), "existing");

        Assert.Throws<InvalidDataException>(() =>
            OutputDirectoryTransaction.Run(output, staging =>
            {
                File.WriteAllText(System.IO.Path.Combine(staging, "partial.md"), "partial");
                throw new InvalidDataException("invalid");
            }));

        Assert.Equal("existing", File.ReadAllText(System.IO.Path.Combine(output, "existing.md")));
        Assert.False(File.Exists(System.IO.Path.Combine(output, "partial.md")));
    }

    [Fact]
    public void RunReplacesExistingOutputAfterSuccessfulGeneration()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "stale.md"), "stale");

        OutputDirectoryTransaction.Run(
            output,
            staging => File.WriteAllText(System.IO.Path.Combine(staging, "current.md"), "current"));

        Assert.False(File.Exists(System.IO.Path.Combine(output, "stale.md")));
        Assert.Equal("current", File.ReadAllText(System.IO.Path.Combine(output, "current.md")));
    }

    [Fact]
    public void RunRetriesBackupCleanupAfterSuccessfulSwap()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "old.md"), "old");
        var backupAttempts = 0;

        OutputDirectoryTransaction.Run(
            output,
            staging => File.WriteAllText(System.IO.Path.Combine(staging, "current.md"), "current"),
            (path, recursive) =>
            {
                if (path.Contains(".backup-", StringComparison.Ordinal) && ++backupAttempts == 1)
                {
                    throw new IOException("Transient cleanup failure.");
                }

                Directory.Delete(path, recursive);
            });

        Assert.Equal(2, backupAttempts);
        Assert.Equal("current", File.ReadAllText(System.IO.Path.Combine(output, "current.md")));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".output.backup-*"));
    }

    [Fact]
    public void RunRestoresBackupWhenInstallingStagingFails()
    {
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "existing.md"), "existing");

        Assert.Throws<IOException>(() => OutputDirectoryTransaction.Run(
            output,
            staging => File.WriteAllText(System.IO.Path.Combine(staging, "new.md"), "new"),
            (source, destination) =>
            {
                if (source.Contains(".staging-", StringComparison.Ordinal))
                {
                    throw new IOException("Simulated staging installation failure.");
                }

                Directory.Move(source, destination);
            },
            Directory.Delete));

        Assert.Equal("existing", File.ReadAllText(System.IO.Path.Combine(output, "existing.md")));
        Assert.False(File.Exists(System.IO.Path.Combine(output, "new.md")));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".output.backup-*"));
    }
}
