using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AtspmDocsGenerator;

public sealed partial class ConfigurationSourceAnalyzer
{
    private static readonly CSharpParseOptions ParseOptions =
        new(languageVersion: LanguageVersion.Latest, documentationMode: DocumentationMode.Parse);

    public IReadOnlyDictionary<string, ConfigurationSection> Analyze(
        string sourceRoot,
        IEnumerable<string> sourcePaths)
    {
        var normalizedRoot = Path.GetFullPath(sourceRoot);
        var trees = EnumerateSourceFiles(normalizedRoot, sourcePaths)
            .Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file),
                ParseOptions,
                path: file,
                encoding: Encoding.UTF8))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "ConfigurationDocumentationAnalysis",
            trees,
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var attributedTypes = trees
            .SelectMany(tree => tree.GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>())
            .Select(type => new
            {
                Declaration = type,
                Attribute = type.AttributeLists.SelectMany(list => list.Attributes)
                    .FirstOrDefault(IsConfigurationSectionAttribute),
                Symbol = compilation.GetSemanticModel(type.SyntaxTree).GetDeclaredSymbol(type)
            })
            .Where(item => item.Attribute is not null && item.Symbol is not null)
            .GroupBy(item => item.Symbol!, SymbolEqualityComparer.Default)
            .ToArray();
        var sections = new Dictionary<string, ConfigurationSection>(StringComparer.Ordinal);

        foreach (var group in attributedTypes)
        {
            if (group.Count() > 1)
            {
                throw new InvalidDataException(
                    $"ConfigurationSection is declared on more than one part of '{group.Key!.ToDisplayString()}'.");
            }

            var item = group.Single();
            var section = CreateSection(
                item.Symbol!,
                item.Declaration,
                item.Attribute!,
                compilation,
                normalizedRoot);
            if (!sections.TryAdd(section.SectionName, section))
            {
                throw new InvalidDataException(
                    $"Configuration section '{section.SectionName}' is declared more than once.");
            }
        }

        return sections;
    }

    private static IEnumerable<string> EnumerateSourceFiles(
        string sourceRoot,
        IEnumerable<string> sourcePaths)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var sourcePath in sourcePaths)
        {
            var fullPath = SourcePath.ResolveWithinRoot(sourceRoot, sourcePath, "Configured source path");
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Configured source path does not exist: {fullPath}");
            }

            foreach (var file in Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, file);
                if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part is ".git" or "bin" or "obj"))
                {
                    continue;
                }

                files.Add(Path.GetFullPath(file));
            }
        }

        return files;
    }

    private static ConfigurationSection CreateSection(
        INamedTypeSymbol typeSymbol,
        TypeDeclarationSyntax attributedDeclaration,
        AttributeSyntax attribute,
        CSharpCompilation compilation,
        string sourceRoot)
    {
        var arguments = attribute.ArgumentList?.Arguments ?? default;
        var sectionArgument = FindArgument(arguments, "sectionName", 0)
            ?? throw new InvalidDataException(
                $"ConfigurationSection on '{typeSymbol.ToDisplayString()}' does not define a section name.");
        var sectionName = ReadSectionName(sectionArgument.Expression);
        var descriptionArgument = FindArgument(arguments, "description", 1);
        var description = descriptionArgument is null ? null : ReadOptionalString(descriptionArgument.Expression);
        var properties = GetDocumentedProperties(typeSymbol)
            .Select(property => CreateProperty(
                property.Symbol,
                property.Syntax,
                compilation,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { typeSymbol }))
            .ToArray();
        var line = attributedDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var relativePath = Path.GetRelativePath(sourceRoot, attributedDeclaration.SyntaxTree.FilePath)
            .Replace('\\', '/');
        var summary = typeSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .OrderBy(declaration => declaration.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.SpanStart)
            .Select(declaration => XmlDocumentationReader.ReadSummary(declaration))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new ConfigurationSection(sectionName, description, summary, relativePath, line, properties);
    }

    private static IEnumerable<(IPropertySymbol Symbol, PropertyDeclarationSyntax Syntax)>
        GetDocumentedProperties(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic)
            .Select(property => new
            {
                Symbol = property,
                Syntax = property.DeclaringSyntaxReferences
                    .Select(reference => reference.GetSyntax())
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault()
            })
            .Where(item => item.Syntax is not null)
            .OrderBy(item => item.Syntax!.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Syntax!.SpanStart)
            .Select(item => (item.Symbol, item.Syntax!));

    private static ConfigurationProperty CreateProperty(
        IPropertySymbol propertySymbol,
        PropertyDeclarationSyntax property,
        CSharpCompilation compilation,
        IReadOnlySet<INamedTypeSymbol> visitedTypes)
    {
        var typeName = property.Type.WithoutTrivia().NormalizeWhitespace().ToFullString();
        var defaultExpression = property.Initializer?.Value.WithoutTrivia().NormalizeWhitespace().ToFullString()
            ?? "Not set";
        var isRequired = propertySymbol.IsRequired
            || property.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
            {
                var name = attribute.Name.ToString().Split('.').Last();
                return name is "Required" or "RequiredAttribute";
            });

        return new ConfigurationProperty(
            property.Identifier.ValueText,
            typeName,
            defaultExpression,
            isRequired,
            XmlDocumentationReader.ReadSummary(property, includeInheritDoc: true),
            GetEnvironmentVariableSuffixes(propertySymbol, property, compilation, visitedTypes),
            GetEnumOptions(propertySymbol.Type, property, compilation));
    }

    private static IReadOnlyList<string>? GetEnumOptions(
        ITypeSymbol propertyType,
        PropertyDeclarationSyntax property,
        CSharpCompilation compilation)
    {
        var type = ResolveElementType(propertyType, property, compilation).ElementType;
        return type.TypeKind == TypeKind.Enum
            ? type.GetMembers().OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue)
                .Select(field => field.Name)
                .ToArray()
            : null;
    }

    private static IReadOnlyList<string> GetEnvironmentVariableSuffixes(
        IPropertySymbol propertySymbol,
        PropertyDeclarationSyntax property,
        CSharpCompilation compilation,
        IReadOnlySet<INamedTypeSymbol> visitedTypes)
    {
        var propertyName = property.Identifier.ValueText;
        var described = ResolveElementType(propertySymbol.Type, property, compilation);
        if (described.IsDictionary)
        {
            return [$"{propertyName}__KEY"];
        }

        var index = described.IsCollection ? "__0" : string.Empty;
        if (described.ElementType is not INamedTypeSymbol nestedType
            || !nestedType.Locations.Any(location => location.IsInSource)
            || visitedTypes.Contains(nestedType))
        {
            return [$"{propertyName}{index}"];
        }

        var nextVisited = new HashSet<INamedTypeSymbol>(visitedTypes, SymbolEqualityComparer.Default)
        {
            nestedType
        };
        var childSuffixes = GetDocumentedProperties(nestedType)
            .SelectMany(child => GetEnvironmentVariableSuffixes(
                child.Symbol,
                child.Syntax,
                compilation,
                nextVisited))
            .ToArray();
        return childSuffixes.Length == 0
            ? [$"{propertyName}{index}"]
            : childSuffixes.Select(child => $"{propertyName}{index}__{child}").ToArray();
    }

    private static (ITypeSymbol ElementType, bool IsCollection, bool IsDictionary) ResolveElementType(
        ITypeSymbol propertyType,
        PropertyDeclarationSyntax property,
        CSharpCompilation compilation)
    {
        var type = propertyType;
        if (type is IErrorTypeSymbol errorType)
        {
            var candidates = errorType.CandidateSymbols.OfType<ITypeSymbol>()
                .Select(candidate => candidate.ToDisplayString())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length > 1)
            {
                throw new InvalidDataException(
                    $"Type '{property.Type}' on property '{property.Identifier.ValueText}' is ambiguous. " +
                    $"Candidates: {string.Join(", ", candidates)}.");
            }

            var semanticType = compilation.GetSemanticModel(property.SyntaxTree).GetTypeInfo(property.Type).Type;
            if (semanticType is not null)
            {
                type = semanticType;
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            return (array.ElementType, true, false);
        }

        if (type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = nullable.TypeArguments[0];
        }

        if (type is INamedTypeSymbol generic && generic.IsGenericType)
        {
            var name = generic.OriginalDefinition.Name;
            var isDictionary = name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary";
            var isCollection = name is "IEnumerable" or "ICollection" or "IReadOnlyCollection"
                or "IList" or "IReadOnlyList" or "List" or "HashSet";
            if (isCollection || isDictionary)
            {
                return (
                    generic.TypeArguments[isDictionary ? generic.TypeArguments.Length - 1 : 0],
                    isCollection,
                    isDictionary);
            }
        }

        return (type, false, false);
    }

    private static ImmutableArray<MetadataReference> GetPlatformReferences()
    {
        var assemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return assemblies.Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }

    private static AttributeArgumentSyntax? FindArgument(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        string name,
        int positionalIndex)
    {
        var named = arguments.FirstOrDefault(argument => string.Equals(
            argument.NameColon?.Name.Identifier.ValueText ?? argument.NameEquals?.Name.Identifier.ValueText,
            name,
            StringComparison.OrdinalIgnoreCase));
        return named ?? arguments
            .Where(argument => argument.NameColon is null && argument.NameEquals is null)
            .ElementAtOrDefault(positionalIndex);
    }

    private static string ReadSectionName(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (expression is InvocationExpressionSyntax invocation
            && invocation.Expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == "nameof"
            && invocation.ArgumentList.Arguments.Count == 1)
        {
            return invocation.ArgumentList.Arguments[0].Expression.ToString().Split('.').Last();
        }

        throw new InvalidDataException(
            $"Unsupported configuration section expression '{expression}'. Use a string literal or nameof(...).");
    }

    private static string? ReadOptionalString(ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return null;
        }

        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        throw new InvalidDataException(
            $"Unsupported configuration description expression '{expression}'. Use a string literal or null.");
    }

    private static bool IsConfigurationSectionAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.').Last();
        return name is "ConfigurationSection" or "ConfigurationSectionAttribute";
    }
}
