using System;
using System.Collections.Generic;
using System.Linq;

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

    public const string LevelsServedAttributeName = "levels_served";

    public const string ElevatorCategory = "elevator";

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
        ["elevator"] = ElevatorCategory,
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

    /// <summary>A car whose span reaches this far below a level still stops there (a pit, a slab).</summary>
    public const double StopBelowToleranceMeters = 0.5d;

    /// <summary>The car must reach this far above a level to stop there, so the overrun above the top stop does not count.</summary>
    public const double MinimumCarHeightMeters = 2.0d;

    private static readonly char[] LevelListSeparators = { ',', '、', ';', '\n' };

    /// <summary>
    /// The export ids of the levels an elevator stops at, lowest first. With <paramref name="levelsServed"/>
    /// (the type's <c>gis_levels_served</c>: level names or short names) those levels, and names that
    /// match no level go to <paramref name="unknownNames"/>. Without it, the levels whose elevation lies
    /// within the car's height span, and nothing when that is fewer than two.
    /// </summary>
    public static IReadOnlyList<string> ResolveLevelsServed(
        IReadOnlyList<ElevatorStop> levels,
        double bottomMeters,
        double topMeters,
        string? levelsServed,
        ICollection<string> unknownNames)
    {
        string[] names = (levelsServed ?? string.Empty)
            .Split(LevelListSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();
        IEnumerable<ElevatorStop> stops;
        if (names.Length > 0)
        {
            List<ElevatorStop> named = new();
            foreach (string name in names)
            {
                ElevatorStop? match = levels.FirstOrDefault(level => level.Answers(name));
                if (match == null)
                {
                    unknownNames.Add(name);
                }
                else
                {
                    named.Add(match);
                }
            }

            stops = named;
        }
        else
        {
            List<ElevatorStop> spanned = levels
                .Where(level => level.ElevationMeters >= bottomMeters - StopBelowToleranceMeters &&
                                level.ElevationMeters <= topMeters - MinimumCarHeightMeters)
                .ToList();
            stops = spanned.Select(level => level.ExportId).Distinct(StringComparer.Ordinal).Count() < 2
                ? Enumerable.Empty<ElevatorStop>()
                : spanned;
        }

        return stops
            .OrderBy(level => level.ElevationMeters)
            .Select(level => level.ExportId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>A level an elevator can stop at: its export id, the names a <c>gis_levels_served</c> value can use, and its elevation.</summary>
public sealed class ElevatorStop
{
    public ElevatorStop(string exportId, string name, string? shortName, double elevationMeters)
    {
        ExportId = exportId ?? throw new ArgumentNullException(nameof(exportId));
        Name = name ?? string.Empty;
        ShortName = shortName;
        ElevationMeters = elevationMeters;
    }

    public string ExportId { get; }

    public string Name { get; }

    public string? ShortName { get; }

    public double ElevationMeters { get; }

    public bool Answers(string name)
    {
        return string.Equals(Name.Trim(), name, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(ShortName) && string.Equals(ShortName!.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }
}
