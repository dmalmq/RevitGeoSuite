using System;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Pure-core material colour remapper matching Kanizu's <c>kanizu-3d</c> massing palettes
/// (<c>soft</c>, <c>white</c>, <c>full</c>). When <see cref="Tiles3DMaterialPalette.Source"/> is
/// selected, every colour passes through untouched.
/// </summary>
public static class Tiles3DMaterialPaletteResolver
{
    private const string GisPrefix = "GIS_";
    private const string K3dPrefix = "K3D_";
    private const string M3dPrefix = "M3D_";

    public const string GisFreeArea = "GIS_free_area";
    public const string GisPaidArea = "GIS_paid_area";
    public const string GisPaidAreaShinkansen = "GIS_paid_area_shinkansen";
    public const string GisFacilities = "GIS_facilities";
    public const string GisRestricted = "GIS_restricted";
    public const string GisStairsEscalators = "GIS_stairs_escalators";
    public const string GisUnlisted = "GIS_unlisted";

    private enum GisArea
    {
        Free,
        Paid,
        PaidShinkansen,
        Facilities,
        Restricted,
        Circulation,
        Other
    }

    public static bool TryParse(string? value, out Tiles3DMaterialPalette palette)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "":
            case "source":
                palette = Tiles3DMaterialPalette.Source;
                return true;
            case "kanizusoft":
            case "kanizu-soft":
            case "kanizu_soft":
            case "soft":
                palette = Tiles3DMaterialPalette.KanizuSoft;
                return true;
            case "kanizuwhite":
            case "kanizu-white":
            case "kanizu_white":
            case "white":
                palette = Tiles3DMaterialPalette.KanizuWhite;
                return true;
            case "kanizufull":
            case "kanizu-full":
            case "kanizu_full":
            case "full":
                palette = Tiles3DMaterialPalette.KanizuFull;
                return true;
            default:
                palette = Tiles3DMaterialPalette.Source;
                return false;
        }
    }

    public static Tiles3DMaterialPalette Parse(string? value)
    {
        if (TryParse(value, out Tiles3DMaterialPalette palette))
        {
            return palette;
        }

        throw new InvalidOperationException(
            $"Unsupported 3D Tiles material palette '{value}'. Choose source, kanizuSoft, kanizuWhite, or kanizuFull.");
    }

    public static Tiles3DMaterialColor Resolve(
        Tiles3DMaterialPalette palette,
        string? materialName,
        Tiles3DMaterialColor sourceColor)
    {
        if (palette == Tiles3DMaterialPalette.Source)
        {
            return sourceColor;
        }

        string trimmed = materialName?.Trim() ?? string.Empty;
        if (trimmed.StartsWith(K3dPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(M3dPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return sourceColor;
        }

        if (TryMapGisArea(trimmed, out GisArea area))
        {
            return GetFloorColor(palette, area, sourceColor.Alpha);
        }

        // Transparent glazing keeps its authored tint and opacity.
        if (sourceColor.Alpha < 255)
        {
            return sourceColor;
        }

        return GetWallColor(palette);
    }

    public static Tiles3DMaterialColor GetWallColor(Tiles3DMaterialPalette palette)
    {
        return palette switch
        {
            Tiles3DMaterialPalette.KanizuSoft => new Tiles3DMaterialColor(0xFF, 0xFF, 0xFF, 255),
            Tiles3DMaterialPalette.KanizuWhite => new Tiles3DMaterialColor(0xFF, 0xFF, 0xFF, 255),
            Tiles3DMaterialPalette.KanizuFull => new Tiles3DMaterialColor(0xFD, 0xFD, 0xFC, 255),
            _ => Tiles3DMaterialColor.Default
        };
    }

    private static bool TryMapGisArea(string trimmedMaterialName, out GisArea area)
    {
        if (!trimmedMaterialName.StartsWith(GisPrefix, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Other;
            return false;
        }

        if (string.Equals(trimmedMaterialName, GisFreeArea, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Free;
            return true;
        }

        if (string.Equals(trimmedMaterialName, GisPaidArea, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Paid;
            return true;
        }

        if (string.Equals(trimmedMaterialName, GisPaidAreaShinkansen, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.PaidShinkansen;
            return true;
        }

        if (string.Equals(trimmedMaterialName, GisFacilities, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Facilities;
            return true;
        }

        if (string.Equals(trimmedMaterialName, GisRestricted, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Restricted;
            return true;
        }

        if (string.Equals(trimmedMaterialName, GisStairsEscalators, StringComparison.OrdinalIgnoreCase))
        {
            area = GisArea.Circulation;
            return true;
        }

        // GIS_unlisted and any unrecognized GIS_* floor material map to the palette's Other colour.
        area = GisArea.Other;
        return true;
    }

    private static Tiles3DMaterialColor GetFloorColor(Tiles3DMaterialPalette palette, GisArea area, byte alpha)
    {
        if (area == GisArea.Circulation)
        {
            return new Tiles3DMaterialColor(0xFF, 0xFF, 0xFF, alpha);
        }

        return (palette, area) switch
        {
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.Free) => new Tiles3DMaterialColor(0xFB, 0xF9, 0xF4, alpha),
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.Paid) => new Tiles3DMaterialColor(0xE6, 0xF0, 0xF7, alpha),
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.PaidShinkansen) => new Tiles3DMaterialColor(0xDC, 0xE8, 0xF4, alpha),
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.Facilities) => new Tiles3DMaterialColor(0xD8, 0xEA, 0xE8, alpha),
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.Restricted) => new Tiles3DMaterialColor(0xEC, 0xEB, 0xE6, alpha),
            (Tiles3DMaterialPalette.KanizuSoft, GisArea.Other) => new Tiles3DMaterialColor(0xEA, 0xEC, 0xE9, alpha),

            (Tiles3DMaterialPalette.KanizuWhite, GisArea.PaidShinkansen) => new Tiles3DMaterialColor(0xF2, 0xF2, 0xEE, alpha),
            (Tiles3DMaterialPalette.KanizuWhite, GisArea.Restricted) => new Tiles3DMaterialColor(0xF0, 0xEF, 0xEB, alpha),
            (Tiles3DMaterialPalette.KanizuWhite, GisArea.Other) => new Tiles3DMaterialColor(0xF0, 0xEF, 0xEB, alpha),
            (Tiles3DMaterialPalette.KanizuWhite, GisArea.Free) => new Tiles3DMaterialColor(0xF6, 0xF5, 0xF1, alpha),
            (Tiles3DMaterialPalette.KanizuWhite, GisArea.Paid) => new Tiles3DMaterialColor(0xF6, 0xF5, 0xF1, alpha),
            (Tiles3DMaterialPalette.KanizuWhite, GisArea.Facilities) => new Tiles3DMaterialColor(0xF6, 0xF5, 0xF1, alpha),

            (Tiles3DMaterialPalette.KanizuFull, GisArea.Free) => new Tiles3DMaterialColor(0xFF, 0xFF, 0xFF, alpha),
            (Tiles3DMaterialPalette.KanizuFull, GisArea.Paid) => new Tiles3DMaterialColor(0xF2, 0xF7, 0xFB, alpha),
            (Tiles3DMaterialPalette.KanizuFull, GisArea.PaidShinkansen) => new Tiles3DMaterialColor(0xE5, 0xEF, 0xF7, alpha),
            (Tiles3DMaterialPalette.KanizuFull, GisArea.Facilities) => new Tiles3DMaterialColor(0xDD, 0xEB, 0xEC, alpha),
            (Tiles3DMaterialPalette.KanizuFull, GisArea.Restricted) => new Tiles3DMaterialColor(0xF2, 0xF2, 0xF2, alpha),
            (Tiles3DMaterialPalette.KanizuFull, GisArea.Other) => new Tiles3DMaterialColor(0xD9, 0xD9, 0xD9, alpha),

            _ => Tiles3DMaterialColor.Default
        };
    }
}
