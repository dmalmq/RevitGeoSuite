using System;
using System.Globalization;

namespace RevitGeoSuite.Tiles3DExport;

internal static class Tiles3DGeoidHeightOffsetValidator
{
    public const double MinMeters = -150d;
    public const double MaxMeters = 150d;

    public static bool TryParse(string? text, out double meters, out string validationMessage)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            meters = 0d;
            validationMessage = BuildRangeMessage();
            return false;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out meters))
        {
            validationMessage = BuildRangeMessage();
            return false;
        }

        return IsValid(meters, out validationMessage);
    }

    public static void ValidateOrThrow(double meters)
    {
        if (!IsValid(meters, out string validationMessage))
        {
            throw new ArgumentOutOfRangeException(nameof(meters), meters, validationMessage);
        }
    }

    private static bool IsValid(double meters, out string validationMessage)
    {
        if (double.IsNaN(meters) || double.IsInfinity(meters))
        {
            validationMessage = "Geoid undulation must be a finite number.";
            return false;
        }

        if (meters < MinMeters || meters > MaxMeters)
        {
            validationMessage = BuildRangeMessage();
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static string BuildRangeMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Enter a geoid undulation between {0:+0;-0;0} and {1:+0;-0;0} m.",
            MinMeters,
            MaxMeters);
    }
}
