using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AtspmDocsGenerator;

public sealed partial class LogMessageSourceAnalyzer
{
    private static readonly CSharpParseOptions ParseOptions =
        new(languageVersion: LanguageVersion.Latest, documentationMode: DocumentationMode.Parse);

    public IReadOnlyList<LogMessageDefinition> Analyze(
        string sourceRoot,
        string sourcePath,
        IReadOnlySet<int>? allowedDuplicateEventIds = null,
        bool includeOrdinaryCalls = false)
        => Analyze(sourceRoot, new[] { sourcePath }, allowedDuplicateEventIds, includeOrdinaryCalls);

    public IReadOnlyList<LogMessageDefinition> Analyze(
        string sourceRoot,
        IEnumerable<string> sourcePaths,
        IReadOnlySet<int>? allowedDuplicateEventIds = null,
        bool includeOrdinaryCalls = false)
    {
        var normalizedRoot = Path.GetFullPath(sourceRoot);
        var files = SourceFiles.Enumerate(normalizedRoot, sourcePaths);

        var messages = new List<LogMessageDefinition>();

        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var tree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(file),
                ParseOptions,
                path: file,
                encoding: Encoding.UTF8);
            var root = tree.GetCompilationUnitRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var attribute = method.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .FirstOrDefault(IsLoggerMessageAttribute);

                if (attribute is not null)
                {
                    messages.Add(CreateDefinition(method, attribute, normalizedRoot));
                }
            }
            if (includeOrdinaryCalls)
            {
                foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    if (CreateOrdinaryDefinition(call, normalizedRoot) is { } definition)
                        messages.Add(definition);
            }
        }

        var unexpectedDuplicates = messages
            .Where(message => message.EventId is not null)
            .GroupBy(message => message.EventId)
            .Where(group => group.Count() > 1
                && !(allowedDuplicateEventIds?.Contains(group.Key!.Value) ?? false))
            .Select(group => group.Key!.Value)
            .Order()
            .ToArray();
        if (unexpectedDuplicates.Length > 0)
        {
            throw new InvalidDataException(
                $"Duplicate logger event IDs are not allowed: {string.Join(", ", unexpectedDuplicates)}.");
        }

        return messages
            .OrderBy(message => message.EventId is null ? 1 : 0)
            .ThenBy(message => message.EventId)
            .ThenBy(message => message.EventName, StringComparer.Ordinal)
            .ThenBy(message => message.SourcePath, StringComparer.Ordinal)
            .ThenBy(message => message.SourceLine)
            .ToArray();
    }

    private static LogMessageDefinition? CreateOrdinaryDefinition(
        InvocationExpressionSyntax call, string sourceRoot)
    {
        if (call.Expression is not MemberAccessExpressionSyntax access)
            return null;
        var method = access.Name.Identifier.ValueText;
        if (method is not ("LogInformation" or "LogWarning" or "LogError" or "LogDebug"
            or "LogTrace" or "LogCritical"))
            return null;
        var receiver = access.Expression.ToString().Split('.').Last().TrimStart('_');
        if (!receiver.Contains("log", StringComparison.OrdinalIgnoreCase))
            return null;
        // Ordinary ILogger calls have no declaration-level EventId. Capture only
        // source-authored literal templates. Runtime-computed strings are unknown.
        var template = call.ArgumentList.Arguments.Select(argument => argument.Expression)
            .FirstOrDefault(expression => expression is InterpolatedStringExpressionSyntax
                || expression is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression));
        if (template is null) return null;
        var summary = template switch
        {
            LiteralExpressionSyntax literal => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(content => content switch
            {
                InterpolatedStringTextSyntax text => text.TextToken.ValueText,
                InterpolationSyntax value => $"{{{value.Expression}}}",
                _ => string.Empty
            })),
            _ => string.Empty
        };
        var relative = Path.GetRelativePath(sourceRoot, call.SyntaxTree.FilePath).Replace('\\', '/');
        var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new LogMessageDefinition(null, method, method[3..], summary, relative, line);
    }

    private static LogMessageDefinition CreateDefinition(
        MethodDeclarationSyntax method,
        AttributeSyntax attribute,
        string sourceRoot)
    {
        var arguments = attribute.ArgumentList?.Arguments ?? default;
        var eventIdExpression = FindNamedArgument(arguments, "EventId")?.Expression
            ?? throw InvalidAttribute(method, "EventId");
        var eventNameExpression = FindNamedArgument(arguments, "EventName")?.Expression
            ?? throw InvalidAttribute(method, "EventName");
        var levelExpression = FindNamedArgument(arguments, "Level")?.Expression
            ?? throw InvalidAttribute(method, "Level");

        if (eventIdExpression is not LiteralExpressionSyntax eventIdLiteral
            || !int.TryParse(eventIdLiteral.Token.ValueText, out var eventId))
        {
            throw new InvalidDataException(
                $"LoggerMessage on '{method.Identifier.ValueText}' must use an integer EventId literal.");
        }

        if (eventNameExpression is not LiteralExpressionSyntax eventNameLiteral
            || !eventNameLiteral.IsKind(SyntaxKind.StringLiteralExpression))
        {
            throw new InvalidDataException(
                $"LoggerMessage on '{method.Identifier.ValueText}' must use a string EventName literal.");
        }

        var level = levelExpression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => throw new InvalidDataException(
                $"LoggerMessage on '{method.Identifier.ValueText}' has unsupported Level expression '{levelExpression}'.")
        };
        var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var relativePath = Path.GetRelativePath(sourceRoot, method.SyntaxTree.FilePath)
            .Replace('\\', '/');

        return new LogMessageDefinition(
            eventId,
            eventNameLiteral.Token.ValueText,
            level,
            XmlDocumentationReader.ReadSummary(method),
            relativePath,
            line);
    }

    private static AttributeArgumentSyntax? FindNamedArgument(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        string name) =>
        arguments.FirstOrDefault(argument =>
            string.Equals(
                argument.NameEquals?.Name.Identifier.ValueText
                    ?? argument.NameColon?.Name.Identifier.ValueText,
                name,
                StringComparison.Ordinal));

    private static bool IsLoggerMessageAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.').Last();
        return name is "LoggerMessage" or "LoggerMessageAttribute";
    }

    private static InvalidDataException InvalidAttribute(
        MethodDeclarationSyntax method,
        string missingArgument) =>
        new($"LoggerMessage on '{method.Identifier.ValueText}' does not define {missingArgument}.");

}
