using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.SharedUI.Controls;

public static class CrsFilter
{
    public static IReadOnlyList<CrsDefinition> Apply(IEnumerable<CrsDefinition>? source, string? query)
    {
        IReadOnlyList<CrsDefinition> definitions = (source ?? Enumerable.Empty<CrsDefinition>()).ToArray();
        if (string.IsNullOrWhiteSpace(query))
        {
            return definitions;
        }

        string[] tokens = query!
            .Split(' ')
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim().ToLowerInvariant())
            .ToArray();

        return definitions
            .Where(definition => Matches(definition, tokens))
            .ToArray();
    }

    private static bool Matches(CrsDefinition definition, IReadOnlyCollection<string> tokens)
    {
        string searchable = string.Join(
            " ",
            definition.Name,
            definition.AreaSummary,
            definition.DatumName,
            definition.UnitName,
            definition.ZoneLabel,
            $"zone {definition.ZoneLabel}",
            definition.JapanZoneNumber,
            $"zone {definition.JapanZoneNumber}",
            definition.EpsgCode,
            $"EPSG:{definition.EpsgCode}")
            .ToLowerInvariant();

        return tokens.All(token => searchable.Contains(token));
    }
}
