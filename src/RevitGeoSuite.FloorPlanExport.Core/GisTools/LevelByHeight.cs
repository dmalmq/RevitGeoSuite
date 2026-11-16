using System;
using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>
/// The level an element stands on when nothing in the model names one: the highest level at or below
/// a height. A floor's height is the top of its bounding box, since a slab hangs down from its level;
/// any other element's is the bottom.
/// </summary>
public static class LevelByHeight
{
    /// <summary>A height within this of a level counts as on it (feet).</summary>
    public const double Tolerance = 0.01;

    /// <summary>The id of the highest level at or below <paramref name="height"/>, or null when every level is above it.</summary>
    public static long? At(IEnumerable<(long Id, double Elevation)> levels, double height)
    {
        if (levels is null)
        {
            throw new ArgumentNullException(nameof(levels));
        }

        long? found = null;
        double best = double.NegativeInfinity;
        foreach ((long id, double elevation) in levels)
        {
            if (elevation <= height + Tolerance && elevation > best)
            {
                best = elevation;
                found = id;
            }
        }

        return found;
    }

    /// <summary>The height to compare with the levels: the box's top for a floor, its bottom for anything else.</summary>
    public static double Of(bool isFloor, double boxBottom, double boxTop) => isFloor ? boxTop : boxBottom;
}
