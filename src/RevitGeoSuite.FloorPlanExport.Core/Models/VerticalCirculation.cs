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

    /// <summary>The unit category of the slope floor types, which export with a <c>to_level</c> like stairs.</summary>
    public const string SlopeCategory = "ramp";

    /// <summary>A slope rising less than this stays on its own level: a ramp inside one floor, not a level change.</summary>
    public const double SlopeMinRiseMetres = 0.3;

    /// <summary>How far a slope's near end may sit from its own level's elevation.</summary>
    public const double SlopeOwnLevelToleranceMetres = 0.3;

    /// <summary>How far a slope's far end may sit from the level it reaches, for a slab finish or a slope that stops short.</summary>
    public const double SlopeLevelToleranceMetres = 0.5;

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

    private static readonly char[] LevelListSeparators = { ',', '、', '，', ';', '；', '\n' };

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

    /// <summary>
    /// An escalator's top level: of the levels at least 1 m above the bottom one, the one nearest
    /// the top of its geometry, which sits under the upper floor's slab. Null when none is within
    /// 2.5 m. Elevations and the top are in the same model coordinates (Level.ProjectElevation).
    /// </summary>
    public static TLevel? EscalatorTopLevel<TLevel>(double bottomElevationFeet, double topFeet, IEnumerable<TLevel> levels, Func<TLevel, double> elevationFeet)
        where TLevel : class
    {
        const double MinimumRiseFeet = 1.0 / 0.3048;
        const double ToleranceFeet = 2.5 / 0.3048;
        TLevel? top = levels
            .Where(level => elevationFeet(level) > bottomElevationFeet + MinimumRiseFeet)
            .OrderBy(level => Math.Abs(elevationFeet(level) - topFeet))
            .FirstOrDefault();
        return top != null && Math.Abs(elevationFeet(top) - topFeet) <= ToleranceFeet ? top : null;
    }

    /// <summary>
    /// The level a slope floor leads to. Its near end must sit on its own level and its far end on
    /// another level on the far side, each within its tolerance. Heights and elevations are in the
    /// same model coordinates (Level.ProjectElevation), in feet. The own level is never the answer.
    /// </summary>
    public static SlopeReach<TKey> SlopeToLevel<TKey>(
        TKey ownLevel,
        double lowFeet,
        double highFeet,
        IEnumerable<KeyValuePair<TKey, double>> levelElevationsFeet)
        where TKey : struct, IEquatable<TKey>
    {
        const double Metre = 1 / 0.3048;
        List<KeyValuePair<TKey, double>> levels = levelElevationsFeet.ToList();
        KeyValuePair<TKey, double>[] own = levels.Where(level => level.Key.Equals(ownLevel)).ToArray();
        if (own.Length == 0 || highFeet - lowFeet < SlopeMinRiseMetres * Metre)
        {
            return new SlopeReach<TKey>(SlopeOutcome.Flat, null, 0, 0, null, 0);
        }

        double ownElevation = own[0].Value;
        bool lowIsNear = Math.Abs(lowFeet - ownElevation) <= Math.Abs(highFeet - ownElevation);
        double near = (lowIsNear ? lowFeet : highFeet) - ownElevation;
        double far = (lowIsNear ? highFeet : lowFeet) - ownElevation;
        List<KeyValuePair<TKey, double>> others = levels
            .Where(level => !level.Key.Equals(ownLevel))
            .OrderBy(level => Math.Abs(level.Value - ownElevation - far))
            .ToList();
        TKey? nearest = others.Count == 0 ? null : others[0].Key;
        double nearestDistance = others.Count == 0 ? 0 : Math.Abs(others[0].Value - ownElevation - far);
        if (Math.Abs(near) > SlopeOwnLevelToleranceMetres * Metre)
        {
            return new SlopeReach<TKey>(SlopeOutcome.OwnLevelAtNeitherEnd, null, near, far, nearest, nearestDistance);
        }

        // The level must lie on the far side, clear of the own level's height, so a second level at
        // the same height as the own one is never taken.
        KeyValuePair<TKey, double>[] reached = others
            .Where(level => (level.Value - ownElevation) * Math.Sign(far) >= SlopeMinRiseMetres * Metre)
            .Where(level => Math.Abs(level.Value - ownElevation - far) <= SlopeLevelToleranceMetres * Metre)
            .Take(1)
            .ToArray();
        return reached.Length == 1
            ? new SlopeReach<TKey>(SlopeOutcome.Reaches, reached[0].Key, near, far, reached[0].Key, Math.Abs(reached[0].Value - ownElevation - far))
            : new SlopeReach<TKey>(SlopeOutcome.FarEndBetweenLevels, null, near, far, nearest, nearestDistance);
    }
}

public enum SlopeOutcome
{
    /// <summary>Rises less than <see cref="VerticalCirculation.SlopeMinRiseMetres"/>: no level change.</summary>
    Flat,

    /// <summary>The far end sits on another level.</summary>
    Reaches,

    /// <summary>Neither end sits on the floor's own level, so which way it leads is a guess.</summary>
    OwnLevelAtNeitherEnd,

    /// <summary>The near end is on its own level but the far end is on no level, as a half-level ramp is.</summary>
    FarEndBetweenLevels,
}

/// <summary>
/// Where a slope leads: the level it reaches, if any, both ends' heights above its own level and
/// the other level nearest the far end with its distance, all in feet, for the audit's message.
/// </summary>
public sealed class SlopeReach<TKey>
    where TKey : struct
{
    public SlopeReach(SlopeOutcome outcome, TKey? toLevel, double nearEndFeet, double farEndFeet, TKey? nearestLevel, double nearestDistanceFeet)
    {
        Outcome = outcome;
        ToLevel = toLevel;
        NearEndFeet = nearEndFeet;
        FarEndFeet = farEndFeet;
        NearestLevel = nearestLevel;
        NearestDistanceFeet = nearestDistanceFeet;
    }

    public SlopeOutcome Outcome { get; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="SlopeOutcome.Reaches"/>.</summary>
    public TKey? ToLevel { get; }

    /// <summary>The end nearer the own level, as height above it (negative below).</summary>
    public double NearEndFeet { get; }

    /// <summary>The other end, as height above the own level (negative below).</summary>
    public double FarEndFeet { get; }

    public TKey? NearestLevel { get; }

    public double NearestDistanceFeet { get; }
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
