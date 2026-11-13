using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A Revit level as the export numbers it: its height and its <c>ordinal</c> parameter, if set.</summary>
public sealed class LevelOrdinalInput
{
    public LevelOrdinalInput(long id, string name, double elevationFeet, int? ordinal)
    {
        Id = id;
        Name = name;
        ElevationFeet = elevationFeet;
        Ordinal = ordinal;
    }

    public long Id { get; }

    public string Name { get; }

    /// <summary>Level.Elevation: from the base point, so 0 is the floor the model calls ground.</summary>
    public double ElevationFeet { get; }

    public int? Ordinal { get; }
}

/// <summary>
/// The ordinal each level is exported with. A level's own <c>ordinal</c> parameter wins. A level
/// without one counts up or down, in height order, from the nearest level that has one. When no
/// level has one, ground is the level nearest elevation 0, as before. Every number not taken from
/// the parameter comes with a warning, because a model at a real height (LUMINE EST's 1F is at
/// 37.71 m) has its lowest basement nearest 0.
/// </summary>
public static class LevelOrdinals
{
    /// <param name="levels">Every level of the model; all of them count.</param>
    /// <param name="exported">The levels the export writes. Warnings name only these.</param>
    public static (IReadOnlyDictionary<long, int> Ordinals, IReadOnlyList<string> Warnings) Resolve(
        IEnumerable<LevelOrdinalInput> levels,
        ICollection<long> exported)
    {
        List<LevelOrdinalInput> ordered = levels.OrderBy(level => level.ElevationFeet).ToList();
        Dictionary<long, int> ordinals = new();
        List<string> warnings = new();
        if (ordered.Count == 0)
        {
            return (ordinals, warnings);
        }

        List<int> anchors = Enumerable.Range(0, ordered.Count).Where(index => ordered[index].Ordinal.HasValue).ToList();
        if (anchors.Count == 0)
        {
            int ground = Enumerable.Range(0, ordered.Count).OrderBy(index => Math.Abs(ordered[index].ElevationFeet)).First();
            for (int index = 0; index < ordered.Count; index++)
            {
                ordinals[ordered[index].Id] = index - ground;
            }

            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "No level has an ordinal, so level {0}, the one nearest elevation 0, is exported as ground (0) and the others are counted from it. Set each level's ordinal to be sure.",
                ordered[ground].Name));
            return (ordinals, warnings);
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            LevelOrdinalInput level = ordered[index];
            if (level.Ordinal is int given)
            {
                ordinals[level.Id] = given;
                continue;
            }

            int anchor = anchors.OrderBy(candidate => Math.Abs(candidate - index)).ThenBy(candidate => candidate).First();
            int counted = ordered[anchor].Ordinal!.Value + (index - anchor);
            ordinals[level.Id] = counted;
            if (!exported.Contains(level.Id))
            {
                continue;
            }

            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Level {0} has no ordinal, so it is exported as {1}, counted from level {2}. Set its ordinal to be sure.",
                level.Name,
                counted,
                ordered[anchor].Name));
        }

        foreach (IGrouping<int, LevelOrdinalInput> shared in ordered.Where(level => exported.Contains(level.Id)).GroupBy(level => ordinals[level.Id]).Where(group => group.Count() > 1))
        {
            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Levels {0} are all exported with ordinal {1}.",
                string.Join(", ", shared.Select(level => level.Name)),
                shared.Key));
        }

        return (ordinals, warnings);
    }
}
