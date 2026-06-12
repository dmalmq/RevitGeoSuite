using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// Creates Revit DirectShape elements from a PLATEAU 3D Tiles building dataset.
/// Coordinates in the input model are project-CRS metres; this importer converts
/// them to internal feet inside an active transaction.
/// </summary>
public sealed class PlateauTilesImporter
{
    private const string ApplicationId = "RevitGeoSuite.PlateauImport.Online";

    public PlateauTilesImporterResult Import(
        Document document,
        PlateauTilesetModel buildings,
        PlateauOnlineGeometryMode mode)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (buildings is null) throw new ArgumentNullException(nameof(buildings));
        if (!document.IsModifiable) throw new InvalidOperationException("PLATEAU online import requires an active Revit transaction.");

        List<string> warnings = new List<string>();

        DeleteMatchingExistingImports(document, buildings);
        List<ElementId> createdElements = new List<ElementId>();
        ElementId categoryId = ResolveCategoryId(document, warnings);

        PlateauImportMaterialFactory materialFactory = new PlateauImportMaterialFactory();
        ElementId buildingMaterialId = materialFactory.GetMaterialId(document, PlateauFeatureType.Building);

        foreach (PlateauTilesetFeature feature in buildings.Features)
        {
            try
            {
                IList<GeometryObject>? geometry = BuildTessellatedGeometry(feature, warnings, buildingMaterialId);
                if (geometry is null || geometry.Count == 0) continue;

                DirectShape directShape = DirectShape.CreateElement(document, categoryId);
                directShape.ApplicationId = ApplicationId;
                directShape.ApplicationDataId = JsonConvert.SerializeObject(new
                {
                    gml_id = feature.Id,
                    feature_type = feature.GetStringAttribute("feature_type") ?? "bldg:Building",
                    source_url = buildings.SourceUrl,
                    area_code = buildings.AreaCode,
                    lod = buildings.Lod,
                    mode = mode.ToString()
                });
                directShape.Name = BuildElementName(feature, buildings);
                directShape.SetShape(geometry);
                createdElements.Add(directShape.Id);
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped {feature.Id}: {ex.Message}");
            }
        }

        int groupCount = 0;
        if (createdElements.Count > 0)
        {
            try
            {
                Group group = document.Create.NewGroup(createdElements);
                string preferred = SanitizeRevitName($"PLATEAU Online {buildings.AreaCode} {buildings.TypeEn} LOD{buildings.Lod} ({mode})");
                group.GroupType.Name = MakeUniqueGroupName(document, preferred);
                groupCount = 1;
            }
            catch (Exception ex)
            {
                warnings.Add($"Imported {createdElements.Count} elements but grouping failed: {ex.Message}");
            }
        }

        return new PlateauTilesImporterResult(createdElements.Count, groupCount, warnings);
    }

    private static IList<GeometryObject>? BuildTessellatedGeometry(PlateauTilesetFeature feature, ICollection<string> warnings, ElementId materialId)
    {
        if (feature.Triangles.Count == 0)
        {
            warnings.Add($"Skipped {feature.Id}: feature contained no triangles.");
            return null;
        }

        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
        builder.OpenConnectedFaceSet(false);
        int facesAdded = 0;
        foreach (PlateauTilesetTriangle tri in feature.Triangles)
        {
            XYZ a = ToFeet(tri.A);
            XYZ b = ToFeet(tri.B);
            XYZ c = ToFeet(tri.C);
            if (IsDegenerate(a, b, c)) continue;
            builder.AddFace(new TessellatedFace(new List<XYZ> { a, b, c }, materialId));
            facesAdded++;
        }
        builder.CloseConnectedFaceSet();

        if (facesAdded == 0)
        {
            warnings.Add($"Skipped {feature.Id}: all triangles were degenerate after unit conversion.");
            return null;
        }

        builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
        builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
        builder.Build();
        return builder.GetBuildResult().GetGeometricalObjects();
    }

    private static ElementId ResolveCategoryId(Document document, ICollection<string> warnings)
    {
        ElementId preferred = new ElementId(BuiltInCategory.OST_GenericModel);
        if (DirectShape.IsValidCategoryId(preferred, document)) return preferred;
        warnings.Add("OST_GenericModel was not valid for DirectShape; falling back to default.");
        return preferred;
    }

    private static void DeleteMatchingExistingImports(Document document, PlateauTilesetModel buildings)
    {
        List<ElementId> ids = new List<ElementId>();
        foreach (DirectShape directShape in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<DirectShape>())
        {
            if (directShape.ApplicationId != ApplicationId) continue;
            if (string.IsNullOrEmpty(directShape.ApplicationDataId)) continue;
            string data = directShape.ApplicationDataId;
            if (data.IndexOf("\"area_code\":\"" + buildings.AreaCode + "\"", StringComparison.Ordinal) < 0) continue;
            ids.Add(directShape.Id);
        }
        if (ids.Count > 0) document.Delete(ids);
    }

    private static string BuildElementName(PlateauTilesetFeature feature, PlateauTilesetModel buildings)
    {
        string baseName = feature.GetStringAttribute("feature_type") ?? buildings.TypeEn;
        return SanitizeRevitName($"{baseName} {feature.Id}");
    }

    private static readonly char[] RevitProhibitedNameChars = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

    private static string SanitizeRevitName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        char[] buffer = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            buffer[i] = Array.IndexOf(RevitProhibitedNameChars, c) >= 0 ? '_' : c;
        }
        return new string(buffer);
    }

    private static string MakeUniqueGroupName(Document document, string preferred)
    {
        HashSet<string> existing = new HashSet<string>(
            new FilteredElementCollector(document).OfClass(typeof(GroupType)).Cast<GroupType>().Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(preferred)) return preferred;
        for (int i = 2; i < 1000; i++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", preferred, i);
            if (!existing.Contains(candidate)) return candidate;
        }
        return preferred + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private static bool IsDegenerate(XYZ a, XYZ b, XYZ c) => (b - a).CrossProduct(c - a).GetLength() <= 1e-8;

    private static double MetersToFeet(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

    private static XYZ ToFeet(Core.Plateau.Tiles3D.Vector3d v) => new XYZ(MetersToFeet(v.X), MetersToFeet(v.Y), MetersToFeet(v.Z));
}
