using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Creates a native Revit <see cref="TopographySurface"/> from a flat list of model-frame ground
/// points (feet). Must run on the Revit API thread; opens its own transaction when the caller has
/// not already opened one. A prior PLATEAU-ground surface is replaced so re-running the import
/// refreshes rather than stacks surfaces — identified by a marker on the Comments parameter.
/// </summary>
public sealed class PlateauTopographyImporter
{
    private const string GroundMarker = "RevitGeoSuite.PlateauGround";

    public TopographyImportResult Import(
        Document document,
        IReadOnlyList<ContextShapePoint3D> points,
        IReadOnlyCollection<string>? initialWarnings = null)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (points is null) throw new ArgumentNullException(nameof(points));

        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("PLATEAU ground import is not supported in family documents.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. PLATEAU ground import requires an editable project.");
        }

        if (points.Count < 3)
        {
            throw new InvalidOperationException("At least three ground points are required to build a topography surface.");
        }

        List<XYZ> xyz = points
            .Select(point => new XYZ(point.XFeet, point.YFeet, point.ZFeet))
            .ToList();
        List<string> warnings = new List<string>(initialWarnings ?? Array.Empty<string>());

        if (!document.IsModifiable)
        {
            using Transaction transaction = new Transaction(document, "Import PLATEAU Ground");
            transaction.Start();
            try
            {
                TopographyImportResult result = ImportCore(document, xyz, warnings);
                transaction.Commit();
                return result;
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }
        }

        return ImportCore(document, xyz, warnings);
    }

    private static TopographyImportResult ImportCore(Document document, IList<XYZ> points, List<string> warnings)
    {
        int replaced = DeleteExistingGroundSurfaces(document);
        TopographySurface surface = TopographySurface.Create(document, points);
        TryMarkGround(surface, warnings);
        TryApplyGroundMaterial(document, surface, warnings);
        return new TopographyImportResult(surface.Id, points.Count, replaced, warnings);
    }

    private static void TryApplyGroundMaterial(Document document, TopographySurface surface, ICollection<string> warnings)
    {
        try
        {
            PlateauImportMaterialFactory materialFactory = new PlateauImportMaterialFactory();
            ElementId materialId = materialFactory.GetMaterialId(document, PlateauFeatureType.Relief);
            if (materialId == ElementId.InvalidElementId)
            {
                return;
            }

            Parameter? materialParam = surface.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (materialParam is null || materialParam.IsReadOnly)
            {
                return;
            }

            materialParam.Set(materialId);
        }
        catch
        {
        }
    }

    private static int DeleteExistingGroundSurfaces(Document document)
    {
        List<ElementId> toDelete = new FilteredElementCollector(document)
            .OfClass(typeof(TopographySurface))
            .Cast<TopographySurface>()
            .Where(surface => string.Equals(ReadComments(surface), GroundMarker, StringComparison.Ordinal))
            .Select(surface => surface.Id)
            .ToList();

        if (toDelete.Count > 0)
        {
            document.Delete(toDelete);
        }

        return toDelete.Count;
    }

    private static string? ReadComments(Element element)
    {
        return element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
    }

    private static void TryMarkGround(TopographySurface surface, ICollection<string> warnings)
    {
        Parameter? comments = surface.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments is null || comments.IsReadOnly)
        {
            warnings.Add("Could not tag the ground surface, so re-running the import will add another surface rather than replace this one.");
            return;
        }

        comments.Set(GroundMarker);
    }

    public sealed class TopographyImportResult
    {
        public TopographyImportResult(ElementId surfaceId, int pointCount, int replacedSurfaceCount, IReadOnlyList<string> warnings)
        {
            SurfaceId = surfaceId;
            PointCount = pointCount;
            ReplacedSurfaceCount = replacedSurfaceCount;
            Warnings = warnings;
        }

        public ElementId SurfaceId { get; }

        public int PointCount { get; }

        public int ReplacedSurfaceCount { get; }

        public IReadOnlyList<string> Warnings { get; }
    }
}
