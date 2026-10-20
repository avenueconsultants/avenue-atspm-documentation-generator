using System.Text.Json;

namespace AtspmDocsGenerator;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() is "init" or "check" or "sync")
            return SetupCommands.Run(args);
        var parseResult = CliOptions.Parse(args);

        if (parseResult.ShowHelp)
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        if (parseResult.Options is null)
        {
            Console.Error.WriteLine(parseResult.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.HelpText);
            return 2;
        }

        try
        {
            var options = parseResult.Options;
            var map = DocumentationMapLoader.Load(options.MapPath);
            GeneratedOutputPolicy.ValidateExisting(options.OutputRoot, map);
            var previousSnapshot = ConfigurationChangeGenerator.LoadPrevious(options.OutputRoot);
            var sections = ConfigurationDiscovery.Analyze(options.SourceRoot, map);
            var snapshot = ConfigurationChangeGenerator.Capture(map, sections, options, previousSnapshot);
            var logMessages = map.LogMessages.Enabled ? new LogMessageSourceAnalyzer().Analyze(
                options.SourceRoot,
                map.LogMessages.GetSourcePaths(),
                map.LogMessages.AllowedDuplicateEventIds.ToHashSet(),
                map.LogMessages.IncludeOrdinaryCalls) : [];
            GenerationResult? result = null;
            LogMessageGenerationResult? logResult = null;

            OutputDirectoryTransaction.Run(options.OutputRoot, stagingRoot =>
            {
                var stagingOptions = options with { OutputRoot = stagingRoot };
                result = new MarkdownDocumentationGenerator().Generate(
                    map,
                    sections,
                    stagingOptions);
                ConfigurationTemplateGenerator.Generate(map, sections, stagingRoot);
                if (map.LogMessages.Enabled)
                    logResult = new LogMessageDocumentationGenerator().Generate(
                    logMessages,
                    Path.Combine(stagingRoot, "log-messages.md"),
                    stagingOptions,
                    map.LogMessages.Description);
                GeneratedOutputPolicy.WriteManifest(stagingRoot, map);
                ConfigurationChangeGenerator.Write(stagingRoot, snapshot);
            });

            Console.WriteLine(
                $"Generated {result!.PageCount} container pages from " +
                $"{result.DocumentedSectionCount} configuration sections.");
            if (logResult is not null)
                Console.WriteLine(
                    $"Generated a log message reference containing {logResult.MessageCount} messages " +
                    $"({logResult.DuplicateEventIdCount} duplicated event IDs).");
            Console.WriteLine(ReferenceEquals(snapshot, previousSnapshot)
                ? "Configuration unchanged; retained the previous snapshot and comparison."
                : previousSnapshot is null ? "Established the initial configuration baseline."
                : $"Recorded {snapshot.Report.Changes.Count} configuration change entries.");

            foreach (var section in ConfigurationDiscovery.Unmapped(map, sections))
            {
                Console.WriteLine($"Warning: configuration section '{section}' is not mapped to a container.");
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            Console.Error.WriteLine($"Generation failed: {exception.Message}");
            return 1;
        }
    }
}
