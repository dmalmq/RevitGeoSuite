using System;
using System.Globalization;

namespace RevitGeoSuite.PlateauImport;

public sealed class DxfLayerColor
{
    private static readonly (int Aci, int R, int G, int B)[] AciPalette =
    {
        (1, 255, 0, 0),
        (2, 255, 255, 0),
        (3, 0, 255, 0),
        (4, 0, 255, 255),
        (5, 0, 0, 255),
        (6, 255, 0, 255),
        (7, 255, 255, 255),
        (8, 128, 128, 128),
        (9, 192, 192, 192),
    };

    public DxfLayerColor(int trueColor, int aci)
    {
        TrueColor = Clamp(trueColor, 0, 0xFFFFFF);
        Aci = Clamp(aci, 1, 255);
    }

    public int TrueColor { get; }

    public int Aci { get; }

    public static DxfLayerColor FromRgb(int red, int green, int blue)
    {
        int r = Clamp(red, 0, 255);
        int g = Clamp(green, 0, 255);
        int b = Clamp(blue, 0, 255);
        int trueColor = (r << 16) | (g << 8) | b;
        return new DxfLayerColor(trueColor, FindNearestAci(r, g, b));
    }

    public static bool TryParseHex(string? value, out DxfLayerColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string hex = value!.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex.Substring(1);
        }

        if (hex.Length != 6
            || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int trueColor))
        {
            return false;
        }

        color = FromRgb((trueColor >> 16) & 0xFF, (trueColor >> 8) & 0xFF, trueColor & 0xFF);
        return true;
    }

    private static int FindNearestAci(int red, int green, int blue)
    {
        int nearestAci = 7;
        int nearestDistance = int.MaxValue;
        foreach ((int aci, int r, int g, int b) in AciPalette)
        {
            int dr = red - r;
            int dg = green - g;
            int db = blue - b;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < nearestDistance)
            {
                nearestAci = aci;
                nearestDistance = distance;
            }
        }

        return nearestAci;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
