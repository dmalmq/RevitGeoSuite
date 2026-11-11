using System;
using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// How escalators, moving walkways, elevators and ticket gates are recognised, and the direction
/// fields stairs and escalators carry for Kanizu's network.
/// </summary>
public static class VerticalCirculation
{
    /// <summary>Type or instance parameter that says what a family is, set by the Revit standard.</summary>
    public const string GisKindParameterName = "gis_kind";

    /// <summary>Type or instance parameter with an escalator's or walkway's travel direction.</summary>
    public const string GisDirectionParameterName = "gis_direction";

    public const string DirectionAttributeName = "direction";

    public const string ToLevelAttributeName = "to_level";

    public const string EscalatorCategory = "escalator";

    public const string MovingWalkwayCategory = "movingwalkway";

    public const string Up = "up";

    public const string Down = "down";

    public const string Both = "both";

    private static readonly Dictionary<string, string> CategoryByGisKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escalator"] = EscalatorCategory,
        ["moving_walkway"] = MovingWalkwayCategory,
        ["movingwalkway"] = MovingWalkwayCategory,
        ["elevator"] = "elevator",
        ["ticket_gate"] = "fixture",
    };

    private static readonly Dictionary<string, string> DirectionByValue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["up"] = Up,
        ["上り"] = Up,
        ["上"] = Up,
        ["down"] = Down,
        ["下り"] = Down,
        ["下"] = Down,
        ["both"] = Both,
        ["両方向"] = Both,
        ["双方向"] = Both,
        ["reversible"] = Both,
    };

    /// <summary>The unit category a <c>gis_kind</c> value stands for.</summary>
    public static bool TryMapGisKind(string? gisKind, out string category)
    {
        category = string.Empty;
        string key = (gisKind ?? string.Empty).Trim();
        if (key.Length == 0 || !CategoryByGisKind.TryGetValue(key, out string? mapped))
        {
            return false;
        }

        category = mapped;
        return true;
    }

    /// <summary>True for the categories drawn as a plain rectangle: escalators and moving walkways.</summary>
    public static bool IsMovingCirculation(string? category)
    {
        return string.Equals(category, EscalatorCategory, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, MovingWalkwayCategory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>up</c>, <c>down</c> or <c>both</c> from a <c>gis_direction</c> value, or null when it is blank or unknown.</summary>
    public static string? NormalizeDirection(string? value)
    {
        string key = (value ?? string.Empty).Trim();
        return key.Length > 0 && DirectionByValue.TryGetValue(key, out string? direction) ? direction : null;
    }

    /// <summary>
    /// The level at the other end, seen from the floor with <paramref name="levelId"/>: the top
    /// level from the bottom one and the bottom level from the top one, otherwise null.
    /// </summary>
    public static string? ResolveToLevel(string? levelId, string? bottomLevelId, string? topLevelId)
    {
        if (string.IsNullOrWhiteSpace(levelId))
        {
            return null;
        }

        if (string.Equals(levelId, bottomLevelId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(topLevelId))
        {
            return topLevelId;
        }

        if (string.Equals(levelId, topLevelId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(bottomLevelId))
        {
            return bottomLevelId;
        }

        return null;
    }
}
