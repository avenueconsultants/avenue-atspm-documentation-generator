namespace AtspmDocsGenerator.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void ParseNormalizesPathsAndRepositoryUrl()
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.WriteFile("map.json", "{}");

        var result = CliOptions.Parse(
        [
            "--workspace-root", directory.Path,
            "--source-root", directory.Path,
            "--output-root", System.IO.Path.Combine(directory.Path, "output"),
            "--map", mapPath,
            "--repository-url", "https://github.com/example/source.git/",
            "--repository-ref", "abc123",
            "--generated-at", "2026-08-18T19:15:00Z"
        ]);

        Assert.Null(result.Error);
        Assert.NotNull(result.Options);
        Assert.Equal("https://github.com/example/source", result.Options.RepositoryUrl);
        Assert.Equal("abc123", result.Options.RepositoryRef);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 18, 19, 15, 0, TimeSpan.Zero),
            result.Options.GeneratedAt);
        Assert.True(System.IO.Path.IsPathFullyQualified(result.Options.OutputRoot));
    }

    [Fact]
    public void ParseReportsMissingRequiredOptions()
    {
        var result = CliOptions.Parse(["--repository-ref", "main"]);

        Assert.Null(result.Options);
        Assert.Contains("--source-root", result.Error);
    }

    [Fact]
    public void ParseRejectsOutputOutsideWorkspace()
    {
        using var workspace = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var mapPath = workspace.WriteFile("map.json", "{}");

        var result = CliOptions.Parse(
        [
            "--workspace-root", workspace.Path,
            "--source-root", workspace.Path,
            "--output-root", System.IO.Path.Combine(outside.Path, "output"),
            "--map", mapPath,
            "--repository-url", "https://github.com/example/source",
            "--repository-ref", "abc123",
            "--generated-at", "2026-08-18T19:15:00Z"
        ]);

        Assert.Null(result.Options);
        Assert.Contains("child of the workspace", result.Error);
    }

    [Fact]
    public void ParseRecognizesHelpAnywhereAndAllowsDoubleDashValues()
    {
        Assert.True(CliOptions.Parse(["--repository-ref", "main", "--help"]).ShowHelp);

        using var directory = new TemporaryDirectory();
        var mapPath = directory.WriteFile("map.json", "{}");
        var result = CliOptions.Parse(
        [
            "--workspace-root", directory.Path,
            "--source-root", directory.Path,
            "--output-root", System.IO.Path.Combine(directory.Path, "output"),
            "--map", mapPath,
            "--repository-url", "https://github.com/example/source",
            "--repository-ref", "--detached-ref",
            "--generated-at", "2026-08-18T19:15:00Z"
        ]);

        Assert.Equal("--detached-ref", result.Options?.RepositoryRef);
    }
}
