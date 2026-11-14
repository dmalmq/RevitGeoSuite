using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.Extractors;
using RevitGeoSuite.FloorPlanExport.UI;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// Floors' outlines as the export writes them, in model feet: the top face after the export trims
/// the stairs, escalators and lifts that overlap it. A drift file's outline is that outline as the
/// GDB changed it, so a shape edit compares the two. Read once per level from the export preview,
/// which opens no transaction.
/// </summary>
internal sealed class ExportedOutlines
{
    private readonly Document document;
    private readonly AffineTransform2D projectToModel;
    private readonly Dictionary<long, Dictionary<long, List<Polygon2D>>?> byLevel = new();
    private ExportPreviewService? preview;

    public ExportedOutlines(Document document, AffineTransform2D modelToProject)
    {
        this.document = document;
        projectToModel = modelToProject.Inverse();
    }

    /// <summary>Levels with no plan view to export from, whose floors stand in with their top face.</summary>
    public List<string> Problems { get; } = new();

    /// <summary>The floor's exported outline; its top face when its level has no plan view or the export leaves it out.</summary>
    public IReadOnlyList<Polygon2D> Of(Floor floor)
    {
        if (Level(floor.LevelId) is Dictionary<long, List<Polygon2D>> outlines && outlines.TryGetValue(floor.Id.Value, out List<Polygon2D>? outline))
        {
            return outline;
        }

        return FloorTopFaces.Read(floor).Outline;
    }

    private Dictionary<long, List<Polygon2D>>? Level(ElementId levelId)
    {
        if (byLevel.TryGetValue(levelId.Value, out Dictionary<long, List<Polygon2D>>? cached))
        {
            return cached;
        }

        Dictionary<long, List<Polygon2D>>? outlines = null;
        if (ShapeEditWork.PlanViewOf(document, levelId) is ViewPlan view)
        {
            outlines = Read(view);
        }
        else
        {
            Problems.Add($"Level {(document.GetElement(levelId) as Level)?.Name ?? levelId.Value.ToString()} has no plan view, so its floors are compared by their top face, not by the export's outline. Run Set up model to add its GIS_ plan.");
        }

        byLevel[levelId.Value] = outlines;
        return outlines;
    }

    private Dictionary<long, List<Polygon2D>> Read(ViewPlan view)
    {
        preview ??= Preview(document);
        int changes = 0;
        void Count(object? sender, DocumentChangedEventArgs args)
        {
            if (args.GetDocument().Equals(document))
            {
                changes++;
            }
        }

        document.Application.DocumentChanged += Count;
        PreviewViewData data;
        try
        {
            data = preview.PrepareView(view, ExportFeatureType.Unit);
        }
        finally
        {
            document.Application.DocumentChanged -= Count;
        }

        // The preview must not change the model: a shape edit's plan is read on every Check again.
        if (changes > 0)
        {
            throw new InvalidOperationException($"Reading the export's outlines for {view.Name} changed the model ({changes} change(s)). Report this; nothing was applied.");
        }

        Dictionary<long, List<Polygon2D>> outlines = new();
        foreach (PreviewFeatureData feature in data.Features)
        {
            if (feature.FeatureType != ExportFeatureType.Unit || feature.SourceElementId is not long id || feature.Feature is not ExportPolygon polygon)
            {
                continue;
            }

            if (!outlines.TryGetValue(id, out List<Polygon2D>? outline))
            {
                outlines[id] = outline = new List<Polygon2D>();
            }

            outline.AddRange(GisShapeEdits.ToModel(polygon.Polygons, projectToModel));
        }

        return outlines;
    }

    // The Export page's saved settings, without the 3D section box, which creates and deletes views in transactions.
    private static ExportPreviewService Preview(Document document)
    {
        ExportDialogSettings settings = new SettingsBundle(DocumentProjectKeyBuilder.Create(document)).Load().GlobalSettings;
        return new ExportPreviewService(
            document,
            settings.UnitSource,
            settings.UnitGeometrySource,
            settings.UnitAttributeSource,
            settings.RoomCategoryParameterName,
            linkExportOptions: settings.LinkExportOptions,
            activeSchemaProfile: SchemaProfile.ResolveActive(settings.SchemaProfiles, settings.ActiveSchemaProfileName),
            simplifyStairUnits: settings.SimplifyStairUnits,
            simplifyEscalatorUnits: settings.SimplifyEscalatorUnits,
            use3DSectionBoxExport: false,
            unitCategories: settings.UnitCategories);
    }
}
