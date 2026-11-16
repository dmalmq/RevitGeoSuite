using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// The level an element stands on, for the audit and the standard guard. Element.LevelId alone misses
/// in-place families, face- and work-plane-based families and ramps, so it falls back in order: the
/// family level, the schedule level, a ramp's or stair's base level, the host's level, the level of
/// the family it is nested in, then <see cref="LevelByHeight"/> from its bounding box. Also says which elements count at all:
/// not demolished and not in a secondary design option (G8 owns the full phase and option rule).
/// </summary>
public sealed class ElementLevelResolver
{
    private readonly Document document;
    private readonly List<(long Id, double Elevation)> levels;

    public ElementLevelResolver(Document document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
            .Select(level => (level.Id.Value, level.ProjectElevation))
            .ToList();
    }

    /// <summary>Whether the export's views can show the element: not demolished, and in no design option or the primary one.</summary>
    public static bool Counts(Element element) =>
        element.DemolishedPhaseId == ElementId.InvalidElementId && (element.DesignOption == null || element.DesignOption.IsPrimary);

    /// <summary>The element's level id, or null when none can be found.</summary>
    public long? LevelOf(Element element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (Valid(element.LevelId) is ElementId own)
        {
            return own.Value;
        }

        foreach (BuiltInParameter parameter in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM })
        {
            if (Valid(element.get_Parameter(parameter)?.AsElementId()) is ElementId level && document.GetElement(level) is Level)
            {
                return level.Value;
            }
        }

        if (element is FamilyInstance { Host: Element host })
        {
            if (host is Level hostLevel)
            {
                return hostLevel.Id.Value;
            }

            if (Valid(host.LevelId) is ElementId hostLevelId)
            {
                return hostLevelId.Value;
            }
        }

        if (element is FamilyInstance { SuperComponent: Element parent } && LevelOf(parent) is long parentLevel)
        {
            return parentLevel;
        }

        return element.get_BoundingBox(null) is BoundingBoxXYZ box
            ? LevelByHeight.At(levels, LevelByHeight.Of(element.Category?.BuiltInCategory == BuiltInCategory.OST_Floors, box.Min.Z, box.Max.Z))
            : null;
    }

    private static ElementId? Valid(ElementId? id) => id == null || id == ElementId.InvalidElementId ? null : id;
}
