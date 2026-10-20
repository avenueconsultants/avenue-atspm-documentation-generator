namespace AtspmDocsGenerator.Tests;

public sealed class LogMessageSourceAnalyzerTests
{
    [Fact]
    public void OrdinaryCallsAreOptInAndHaveNoInventedEventId()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("Logs/Migration.cs", """
            class Migration {
                void Run(object logger, object ex, int count) {
                    _logger.LogInformation("Inserted {Count} records", count);
                    _logger.LogWarning($"Skipped {count} records");
                    _logger.LogError(ex, "Migration failed");
                    other.LogInformation("Not a logger");
                }
                object _logger;
            }
            """);
        var analyzer = new LogMessageSourceAnalyzer();
        Assert.Empty(analyzer.Analyze(directory.Path, "Logs"));
        var messages = analyzer.Analyze(directory.Path, "Logs", includeOrdinaryCalls: true);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, message => Assert.Null(message.EventId));
        Assert.Contains(messages, message => message.Level == "Information" && message.Summary == "Inserted {Count} records");
        Assert.Contains(messages, message => message.Level == "Warning" && message.Summary == "Skipped {count} records");
        Assert.Contains(messages, message => message.Level == "Error" && message.Summary == "Migration failed");
    }

    [Fact]
    public void MultipleFoldersDeduplicateFilesAndCheckEventIdsAcrossFolders()
    {
        using var directory = new TemporaryDirectory();
        const string source = """
            public partial class Events {
                [LoggerMessage(EventId = 7, EventName = "Start", Level = LogLevel.Information)]
                public partial void Start();
            }
            """;
        directory.WriteFile("Logs/Nested/First.cs", source);
        directory.WriteFile("Logs/obj/Generated.cs", source);
        var analyzer = new LogMessageSourceAnalyzer();
        Assert.Single(analyzer.Analyze(directory.Path, new[] { "Logs", "Logs/Nested" }));
        directory.WriteFile("OtherLogs/Second.cs", source);
        Assert.Throws<InvalidDataException>(() =>
            analyzer.Analyze(directory.Path, new[] { "Logs", "OtherLogs" }));
        var messages = analyzer.Analyze(directory.Path, new[] { "OtherLogs", "Logs" }, new HashSet<int> { 7 });
        Assert.Equal(2, messages.Count);
        Assert.Equal("Logs/Nested/First.cs", messages[0].SourcePath);
    }

    [Fact]
    public void AnalyzeReadsLoggerMessagesAndSortsByEventId()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "Logs/SampleLogMessages.cs",
            """
            public partial class SampleLogMessages
            {
                /// <summary>Second message with a <paramref name="value"/>.</summary>
                [LoggerMessage(EventId = 20, EventName = "Second", Level = LogLevel.Warning, Message = "{value}")]
                public partial void Second(string value);

                /// <summary>
                /// First message.
                /// </summary>
                [LoggerMessage(EventId = 10, EventName = "First", Level = LogLevel.Information, Message = "First")]
                public partial void First();
            }
            """);

        var messages = new LogMessageSourceAnalyzer().Analyze(directory.Path, "Logs");

        Assert.Collection(
            messages,
            first =>
            {
                Assert.Equal(10, first.EventId);
                Assert.Equal("First", first.EventName);
                Assert.Equal("Information", first.Level);
                Assert.Equal("First message.", first.Summary);
            },
            second =>
            {
                Assert.Equal(20, second.EventId);
                Assert.Equal("Second", second.EventName);
                Assert.Equal("Warning", second.Level);
                Assert.Equal("Second message with a value.", second.Summary);
            });
    }

    [Fact]
    public void AnalyzeRejectsSourcePathsOutsideTheRepository()
    {
        using var directory = new TemporaryDirectory();

        var exception = Assert.Throws<InvalidDataException>(
            () => new LogMessageSourceAnalyzer().Analyze(directory.Path, ".."));

        Assert.Contains("inside the source root", exception.Message);
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(201, true)]
    public void AnalyzeOnlyAllowsExplicitDuplicateEventIds(int eventId, bool isAllowed)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile(
            "Logs/Duplicates.cs",
            $$"""
            public partial class DuplicateLogMessages
            {
                [LoggerMessage(EventId = {{eventId}}, EventName = "First", Level = LogLevel.Information, Message = "First")]
                public partial void First();

                [LoggerMessage(EventId = {{eventId}}, EventName = "Second", Level = LogLevel.Information, Message = "Second")]
                public partial void Second();
            }
            """);

        if (isAllowed)
        {
            Assert.Equal(
                2,
                new LogMessageSourceAnalyzer().Analyze(
                    directory.Path,
                    "Logs",
                    new HashSet<int> { eventId }).Count);
        }
        else
        {
            var exception = Assert.Throws<InvalidDataException>(
                () => new LogMessageSourceAnalyzer().Analyze(directory.Path, "Logs"));
            Assert.Contains(eventId.ToString(), exception.Message);
        }
    }
}
