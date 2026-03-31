using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RevitGeoSuite.Core.Plateau.Schema;

public static class PlateauSchemaHelper
{
    private static readonly Regex EpsgRegex = new Regex(@"(?<!\d)(\d{4,5})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex MeshCodeRegex = new Regex(@"(?<!\d)(\d{8})(?!\d)", RegexOptions.Compiled);

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
        Match match = MeshCodeRegex.Match(fileName);
        return match.Success ? match.Groups[1].Value : null;
    }
}
