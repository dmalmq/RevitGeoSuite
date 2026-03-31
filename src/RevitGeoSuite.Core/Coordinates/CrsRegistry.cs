using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Core.Coordinates;

public sealed class CrsRegistry : ICrsRegistry
{
    private readonly IReadOnlyList<CrsDefinition> definitions;
    private readonly IReadOnlyList<CrsReference> references;
    private readonly Dictionary<int, CrsDefinition> definitionsByCode;

    public CrsRegistry()
    {
        definitions = JapanCrsPresets.CreateDefinitions()
            .OrderBy(definition => definition.EpsgCode)
            .ToArray();

        references = definitions
            .Select(definition => definition.ToReference())
            .ToArray();

        definitionsByCode = definitions.ToDictionary(definition => definition.EpsgCode);
    }

    public IReadOnlyCollection<CrsDefinition> GetAvailableDefinitions()
    {
        return definitions;
    }

    public IReadOnlyCollection<CrsReference> GetAvailableReferences()
    {
        return references;
    }

    public IReadOnlyCollection<CrsDefinition> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return definitions;
        }

        string queryText = query!.Trim();
        string[] tokens = queryText
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

        return definitions
            .Where(definition => TokensMatch(definition, tokens))
            .ToArray();
    }

    public bool TryGetByEpsgCode(int epsgCode, out CrsDefinition? definition)
    {
        return definitionsByCode.TryGetValue(epsgCode, out definition);
    }

    private static bool TokensMatch(CrsDefinition definition, IReadOnlyCollection<string> tokens)
    {
        string searchable = string.Join(
            " ",
            new[]
            {
                definition.Name,
                definition.DatumName,
                definition.UnitName,
                definition.AreaSummary,
                definition.ZoneLabel,
                $"zone {definition.ZoneLabel}",
                definition.JapanZoneNumber.ToString(),
                $"zone {definition.JapanZoneNumber}",
                $"EPSG:{definition.EpsgCode}",
                definition.EpsgCode.ToString()
            })
            .ToLowerInvariant();

        return tokens.All(token => searchable.Contains(token.ToLowerInvariant()));
    }
}
