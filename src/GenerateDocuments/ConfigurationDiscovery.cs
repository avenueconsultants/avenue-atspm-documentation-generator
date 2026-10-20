namespace AtspmDocsGenerator;

public static class ConfigurationDiscovery
{
    public static IReadOnlyDictionary<string, ConfigurationSection> Analyze(string sourceRoot, DocumentationMap map)
    {
        var sections = new ConfigurationSourceAnalyzer().Analyze(sourceRoot, map.SourcePaths, map.ConfigurationTypes)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var exclusion in map.ExcludedProperties)
        {
            if (!sections.TryGetValue(exclusion.Key, out var section))
                throw new InvalidDataException($"excludedProperties references unknown section '{exclusion.Key}'.");
            var unknown = exclusion.Value.Except(section.Properties.Select(property => property.Name), StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                throw new InvalidDataException($"Unknown excluded properties in '{exclusion.Key}': {string.Join(", ", unknown)}.");
            sections[exclusion.Key] = section with
            {
                Properties = section.Properties.Where(property => !exclusion.Value.Contains(property.Name, StringComparer.Ordinal)).ToArray()
            };
        }
        foreach (var overrideSection in map.PropertyDescriptions)
        {
            if (!sections.TryGetValue(overrideSection.Key, out var section))
                throw new InvalidDataException($"propertyDescriptions references unknown section '{overrideSection.Key}'.");
            var unknown = overrideSection.Value.Keys.Except(section.Properties.Select(property => property.Name), StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                throw new InvalidDataException($"Unknown described properties in '{overrideSection.Key}': {string.Join(", ", unknown)}.");
            sections[overrideSection.Key] = section with
            {
                Properties = section.Properties.Select(property =>
                    overrideSection.Value.TryGetValue(property.Name, out var description)
                        ? property with { Summary = description }
                        : property).ToArray()
            };
        }
        foreach (var name in map.ExcludedSections)
        {
            if (!sections.Remove(name))
                throw new InvalidDataException($"excludedSections references unknown section '{name}'.");
        }
        return sections;
    }

    public static IReadOnlyList<string> GetSections(ContainerDefinition container,
        IReadOnlyDictionary<string, ConfigurationSection> available)
    {
        var names = container.Sections.ToList();
        if (container.AutoDiscover)
        {
            var mapped = names.Select(name => available.ContainsKey(name) ? name : MappedSectionName.GetBaseName(name))
                .ToHashSet(StringComparer.Ordinal);
            names.AddRange(available.Keys.Except(mapped).Order(StringComparer.Ordinal));
        }
        return names;
    }

    public static ConfigurationSection ResolveSection(string name,
        IReadOnlyDictionary<string, ConfigurationSection> available)
    {
        if (available.TryGetValue(name, out var section)) return section;
        // A mapped path such as Database:Main reuses the discovered Database
        // type while retaining the path used by JSON and environment variables.
        var baseName = MappedSectionName.GetBaseName(name);
        if (available.TryGetValue(baseName, out section))
            return section with { SectionName = name };
        throw new InvalidDataException($"Unknown configuration section '{name}'.");
    }

    public static IReadOnlyList<string> Unmapped(DocumentationMap map,
        IReadOnlyDictionary<string, ConfigurationSection> available)
    {
        var mapped = map.Containers.SelectMany(container => GetSections(container, available))
            .Select(name => available.ContainsKey(name) ? name : MappedSectionName.GetBaseName(name))
            .ToHashSet(StringComparer.Ordinal);
        return available.Keys.Except(mapped).Order(StringComparer.Ordinal).ToArray();
    }

    public static void ValidateReferences(DocumentationMap map, IReadOnlyDictionary<string, ConfigurationSection> available)
    {
        foreach (var container in map.Containers)
        {
            foreach (var name in container.Sections)
                if (!available.ContainsKey(name) && !available.ContainsKey(MappedSectionName.GetBaseName(name)))
                    throw new InvalidDataException($"Container '{container.Name}' references unknown configuration section '{name}'.");
        }
    }
}
