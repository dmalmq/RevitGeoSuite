using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitGeoSuite.Core.Plateau.Schema;

public static class PlateauSchemaHelper
{
    private static readonly Regex EpsgRegex = new Regex(@"(?<!\d)(\d{4,5})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex TertiaryMeshCodeRegex = new Regex(@"(?<!\d)(\d{8})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex SecondaryMeshCodeRegex = new Regex(@"(?<!\d)(\d{6})(?!\d)", RegexOptions.Compiled);

    public static bool TryExtractEpsgCode(string? srsName, out int epsgCode)
    {
        epsgCode = 0;
        if (string.IsNullOrWhiteSpace(srsName))
        {
            return false;
        }

        MatchCollection matches = EpsgRegex.Matches(srsName);
        if (matches.Count == 0)
        {
            return false;
        }

        return int.TryParse(matches[matches.Count - 1].Groups[1].Value, out epsgCode);
    }

    public static string NormalizeSrsName(string? srsName)
    {
        if (srsName is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(srsName)
            ? string.Empty
            : srsName.Trim();
    }

    public static string? TryExtractTileIdFromPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        Match tertiaryMatch = TertiaryMeshCodeRegex.Match(fileName);
        if (tertiaryMatch.Success)
        {
            return tertiaryMatch.Groups[1].Value;
        }

        Match secondaryMatch = SecondaryMeshCodeRegex.Match(fileName);
        return secondaryMatch.Success ? secondaryMatch.Groups[1].Value : null;
    }

    /// <summary>
    /// Returns the distinct tile ids that are not a coarser parent of another id in the set.
    /// Japan mesh codes are hierarchical by prefix (tertiary <c>53394574</c> extends secondary
    /// <c>533945</c> extends primary <c>5339</c>), so a parent mesh is dropped only when one of
    /// its finer children is present; a lone coarse tile with no child is kept. Used to stop a
    /// secondary-mesh file (e.g. roads/relief) from rendering as one giant cell over the 1 km
    /// tertiary tiles inside it.
    /// </summary>
    public static IReadOnlyList<string> SelectLeafTileIds(IEnumerable<string>? tileIds)
    {
        if (tileIds is null)
        {
            return Array.Empty<string>();
        }

        string[] distinct = tileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinct
            .Where(id => !distinct.Any(other =>
                other.Length > id.Length && other.StartsWith(id, StringComparison.Ordinal)))
            .ToList();
    }
}
