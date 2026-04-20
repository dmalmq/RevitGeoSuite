using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauContextImporter
{
    private const string ApplicationId = "RevitGeoSuite.PlateauImport";
    private readonly PlateauFootprintSanitizer footprintSanitizer;

    public PlateauContextImporter(PlateauFootprintSanitizer? footprintSanitizer = null)
    {
        this.footprintSanitizer = footprintSanitizer ?? new PlateauFootprintSanitizer();
    }

    public PlateauContextImportExecutionResult Import(Document document, ContextImportPlan plan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!document.IsModifiable)
        {
            throw new InvalidOperationException("PLATEAU context import requires an active Revit transaction.");
        }

        if (plan.Shapes.Count == 0)
        {
            throw new InvalidOperationException("The selected PLATEAU folder and filters did not produce any importable context geometry.");
        }

        List<string> warnings = new List<string>(plan.WarningMessages ?? Array.Empty<string>());
        DeleteMatchingExistingImports(document, plan.Shapes);

        double minSegmentLengthFeet = Math.Max(document.Application.ShortCurveTolerance, 1e-6d);
        Dictionary<string, List<ElementId>> groupedElements = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);
        int importedCount = 0;
        foreach (ContextShapePlan shapePlan in plan.Shapes)
        {
            if (!TryBuildGeometryObjects(shapePlan, warnings, minSegmentLengthFeet, out IList<GeometryObject>? geometryObjects) || geometryObjects is null)
            {
                continue;
            }

            try
            {
                ElementId categoryId = ResolveCategoryId(document, shapePlan.FeatureType, warnings);
                DirectShape directShape = DirectShape.CreateElement(document, categoryId);
                directShape.ApplicationId = ApplicationId;
                directShape.ApplicationDataId = JsonConvert.SerializeObject(new PlateauImportedElementMetadata
                {
                    SourceFeatureId = shapePlan.SourceFeatureId,
                    TileId = shapePlan.TileId,
                    FeatureType = shapePlan.FeatureType
                });
                directShape.Name = shapePlan.DisplayName;
                directShape.SetShape(geometryObjects);

                string scopeKey = BuildScopeKey(shapePlan.TileId, shapePlan.FeatureType);
                if (!groupedElements.TryGetValue(scopeKey, out List<ElementId>? elementIds))
                {
                    elementIds = new List<ElementId>();
                    groupedElements[scopeKey] = elementIds;
                }

                elementIds.Add(directShape.Id);
                importedCount++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped {BuildFeatureLabel(shapePlan)} because Revit could not create the context shape: {ex.Message}");
            }
        }

        if (importedCount == 0)
        {
            throw new InvalidOperationException("None of the filtered PLATEAU features could be converted into valid Revit context geometry. Review the warnings list and adjust the folder or filters before importing again.");
        }

        int createdGroupCount = 0;
        foreach (KeyValuePair<string, List<ElementId>> entry in groupedElements.Where(entry => entry.Value.Count > 0))
        {
            string[] parts = entry.Key.Split('|');
            PlateauFeatureType featureType = (PlateauFeatureType)Enum.Parse(typeof(PlateauFeatureType), parts[1], ignoreCase: false);

            try
            {
                Group group = document.Create.NewGroup(entry.Value);
                group.GroupType.Name = BuildUniqueGroupName(document, BuildPreferredGroupName(parts[0], featureType), group.GroupType.Id);
                createdGroupCount++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Imported {entry.Value.Count} {featureType.GetPluralDisplayName().ToLowerInvariant()} for tile {parts[0]}, but grouping failed: {ex.Message}");
            }
        }

        return new PlateauContextImportExecutionResult
        {
            ImportedElementCount = importedCount,
            CreatedGroupCount = createdGroupCount,
            WarningMessages = warnings
        };
    }

    private static void DeleteMatchingExistingImports(Document document, IReadOnlyCollection<ContextShapePlan> shapes)
    {
        HashSet<string> scopeKeys = new HashSet<string>(shapes.Select(shape => BuildScopeKey(shape.TileId, shape.FeatureType)), StringComparer.Ordinal);
        HashSet<long> groupIds = new HashSet<long>();
        HashSet<long> elementIds = new HashSet<long>();

        foreach (DirectShape directShape in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<DirectShape>())
        {
            if (!string.Equals(directShape.ApplicationId, ApplicationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadMetadata(directShape.ApplicationDataId, out PlateauImportedElementMetadata? metadata))
            {
                continue;
            }

            PlateauImportedElementMetadata resolvedMetadata = metadata!;
            if (!scopeKeys.Contains(BuildScopeKey(resolvedMetadata.TileId, resolvedMetadata.FeatureType)))
            {
                continue;
            }

            if (directShape.GroupId != ElementId.InvalidElementId)
            {
                groupIds.Add(directShape.GroupId.Value);
            }
            else
            {
                elementIds.Add(directShape.Id.Value);
            }
        }

        if (groupIds.Count > 0)
        {
            document.Delete(groupIds.Select(id => new ElementId(id)).ToList());
        }

        if (elementIds.Count > 0)
        {
            document.Delete(elementIds.Select(id => new ElementId(id)).ToList());
        }
    }

    private static bool TryReadMetadata(string? json, out PlateauImportedElementMetadata? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            metadata = JsonConvert.DeserializeObject<PlateauImportedElementMetadata>(json!);
            return metadata is not null && !string.IsNullOrWhiteSpace(metadata.TileId);
        }
        catch
        {
            return false;
        }
    }

    private static ElementId ResolveCategoryId(Document document, PlateauFeatureType featureType, ICollection<string> warnings)
    {
        BuiltInCategory preferredCategory = featureType switch
        {
            PlateauFeatureType.Building => BuiltInCategory.OST_GenericModel,
            PlateauFeatureType.Bridge => BuiltInCategory.OST_Roads,
            PlateauFeatureType.Road => BuiltInCategory.OST_Roads,
            PlateauFeatureType.Vegetation => BuiltInCategory.OST_Planting,
            PlateauFeatureType.Relief => BuiltInCategory.OST_Topography,
            _ => BuiltInCategory.OST_GenericModel
        };

        ElementId preferredId = new ElementId(preferredCategory);
        if (DirectShape.IsValidCategoryId(preferredId, document))
        {
            return preferredId;
        }

        warnings.Add($"{featureType.GetPluralDisplayName()} could not be created in Revit category '{preferredCategory}'. Falling back to Generic Models for this import batch.");
        return new ElementId(BuiltInCategory.OST_GenericModel);
    }

    private static string BuildPreferredGroupName(string tileId, PlateauFeatureType featureType)
    {
        return $"PLATEAU {tileId} {featureType.GetPluralDisplayName()}";
    }

    private static string BuildUniqueGroupName(Document document, string preferredName, ElementId currentGroupTypeId)
    {
        HashSet<string> existingNames = new HashSet<string>(
            new FilteredElementCollector(document)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .Where(groupType => groupType.Id != currentGroupTypeId)
                .Select(groupType => groupType.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(preferredName))
        {
            return preferredName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", preferredName, index);
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return preferredName + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private static string BuildScopeKey(string tileId, PlateauFeatureType featureType)
    {
        return $"{tileId}|{featureType}";
    }

    private bool TryBuildGeometryObjects(
        ContextShapePlan shapePlan,
        ICollection<string> warnings,
        double minSegmentLengthFeet,
        out IList<GeometryObject>? geometryObjects)
    {
        geometryObjects = null;
        try
        {
            geometryObjects = shapePlan.GeometryMode == PlateauGeometryImportMode.DetailedDirectShape
                ? BuildDetailedGeometry(shapePlan, warnings)
                : BuildLightweightGeometry(shapePlan, warnings, minSegmentLengthFeet);
            return geometryObjects is not null && geometryObjects.Count > 0;
        }
        catch (Exception ex)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(shapePlan)} because Revit could not build a valid shape: {ex.Message}");
            return false;
        }
    }

    private IList<GeometryObject>? BuildLightweightGeometry(ContextShapePlan shapePlan, ICollection<string> warnings, double minSegmentLengthFeet)
    {
        IReadOnlyCollection<(double XFeet, double YFeet)> sanitizedFootprint = footprintSanitizer.Sanitize(shapePlan.FootprintPointsFeet, minSegmentLengthFeet);
        if (sanitizedFootprint.Count < 3)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(shapePlan)} because its footprint collapsed below Revit's short-curve tolerance.");
            return null;
        }

        Solid solid = BuildSolid(shapePlan, sanitizedFootprint);
        return new GeometryObject[] { solid };
    }

    private static IList<GeometryObject>? BuildDetailedGeometry(ContextShapePlan shapePlan, ICollection<string> warnings)
    {
        List<TessellatedFace> faces = new List<TessellatedFace>();
        foreach (ContextShapeTriangle triangle in shapePlan.Triangles)
        {
            if (IsDegenerate(triangle))
            {
                continue;
            }

            faces.Add(new TessellatedFace(
                new List<XYZ>
                {
                    new XYZ(triangle.A.XFeet, triangle.A.YFeet, triangle.A.ZFeet),
                    new XYZ(triangle.B.XFeet, triangle.B.YFeet, triangle.B.ZFeet),
                    new XYZ(triangle.C.XFeet, triangle.C.YFeet, triangle.C.ZFeet)
                },
                ElementId.InvalidElementId));
        }

        if (faces.Count == 0)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(shapePlan)} because its detailed geometry did not contain any non-degenerate triangles.");
            return null;
        }

        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
        builder.OpenConnectedFaceSet(false);
        foreach (TessellatedFace face in faces)
        {
            builder.AddFace(face);
        }

        builder.CloseConnectedFaceSet();
        builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
        builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
        builder.Build();
        return builder.GetBuildResult().GetGeometricalObjects();
    }

    private static bool IsDegenerate(ContextShapeTriangle triangle)
    {
        XYZ ab = new XYZ(
            triangle.B.XFeet - triangle.A.XFeet,
            triangle.B.YFeet - triangle.A.YFeet,
            triangle.B.ZFeet - triangle.A.ZFeet);
        XYZ ac = new XYZ(
            triangle.C.XFeet - triangle.A.XFeet,
            triangle.C.YFeet - triangle.A.YFeet,
            triangle.C.ZFeet - triangle.A.ZFeet);
        return ab.CrossProduct(ac).GetLength() <= 1e-8d;
    }

    private static string BuildFeatureLabel(ContextShapePlan shapePlan)
    {
        string displayName = string.IsNullOrWhiteSpace(shapePlan.DisplayName) ? shapePlan.SourceFeatureId : shapePlan.DisplayName;
        string sourceFileName = string.IsNullOrWhiteSpace(shapePlan.SourceFilePath)
            ? "unknown file"
            : Path.GetFileName(shapePlan.SourceFilePath);
        return $"'{displayName}' in tile {shapePlan.TileId} ({sourceFileName})";
    }

    private static Solid BuildSolid(ContextShapePlan shapePlan, IReadOnlyCollection<(double XFeet, double YFeet)> footprintPointsFeet)
    {
        List<XYZ> points = footprintPointsFeet
            .Select(point => new XYZ(point.XFeet, point.YFeet, shapePlan.BaseElevationFeet))
            .ToList();
        if (points.Count < 3)
        {
            throw new InvalidOperationException("An imported footprint must contain at least three points.");
        }

        CurveLoop loop = new CurveLoop();
        for (int index = 0; index < points.Count; index++)
        {
            XYZ start = points[index];
            XYZ end = points[(index + 1) % points.Count];
            loop.Append(Line.CreateBound(start, end));
        }

        return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, shapePlan.HeightFeet);
    }
}
