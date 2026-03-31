using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.SharedUI.Controls;

public static class MapSearchQueryParser
{
    public static bool TryParseCoordinatePair(string? query, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        string nonEmptyQuery = query!.Trim();
        string normalized = nonEmptyQuery
            .Replace(";", ",")
            .Replace("/", " ");

        string[] tokens = normalized
            .Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .ToArray();

        if (tokens.Length != 2)
        {
            return false;
        }

        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
            || !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
        {
            return false;
        }

        return latitude >= -90d
            && latitude <= 90d
            && longitude >= -180d
            && longitude <= 180d;
    }
}
