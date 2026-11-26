using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// Inspects a live Revit family <see cref="Document"/> (or a saved <c>.rfa</c> file on disk) and produces a
/// pure-C# <see cref="RfaInspectionSnapshot"/> suitable for <see cref="RfaLinter.Lint"/> and
/// <see cref="ExpectedManifest"/> comparisons.
/// </summary>
public static class RevitRfaInspector
{
    /// <summary>
    /// Opens an <c>.rfa</c> file read-only in memory, inspects it with its saved byte size, and closes it
    /// without saving in a <c>finally</c> block.
    /// </summary>
    public static RfaInspectionSnapshot InspectFile(Application application, string rfaFilePath)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrWhiteSpace(rfaFilePath) || !File.Exists(rfaFilePath))
        {
            throw new FileNotFoundException($"Revit family file not found at '{rfaFilePath}'.", rfaFilePath);
        }

        string fullPath = Path.GetFullPath(rfaFilePath);
        long fileSizeBytes = new FileInfo(fullPath).Length;
        Document? familyDoc = null;
        try
        {
            familyDoc = application.OpenDocumentFile(fullPath);
            if (familyDoc is null || !familyDoc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Document '{fullPath}' is not a valid Revit family document.");
            }

            return Inspect(familyDoc, fileSizeBytes);
        }
        finally
        {
            if (familyDoc != null && familyDoc.IsValidObject)
            {
                try
                {
                    familyDoc.Close(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of inspected family document.
                }
            }
        }
    }

    /// <summary>Extracts a full <see cref="RfaInspectionSnapshot"/> from an open Revit family <see cref="Document"/>.</summary>
    public static RfaInspectionSnapshot Inspect(Document familyDoc, long? fileSizeBytes = null)
    {
        if (familyDoc is null)
        {
            throw new ArgumentNullException(nameof(familyDoc));
        }

        if (!familyDoc.IsFamilyDocument || familyDoc.FamilyManager is null)
        {
            throw new InvalidOperationException("RevitRfaInspector.Inspect requires a Revit family document.");
        }

        Family? ownerFamily = familyDoc.OwnerFamily;
        string familyName = ResolveFamilyName(familyDoc, ownerFamily);
        Category? familyCategory = ownerFamily?.FamilyCategory;
        BuiltInCategory? builtInCategory = familyCategory?.BuiltInCategory;
        string categoryName = builtInCategory.HasValue && builtInCategory.Value != BuiltInCategory.INVALID
            ? builtInCategory.Value.ToString()
            : string.Empty;

        string seedKind = string.Equals(categoryName, nameof(BuiltInCategory.OST_Doors), StringComparison.Ordinal)
            ? FamilyGeometrySpec.SeedWallHostedDoor
            : FamilyGeometrySpec.SeedProcedural;

        List<string> subcategories = new();
        if (familyCategory?.SubCategories != null)
        {
            foreach (Category sub in familyCategory.SubCategories)
            {
                if (sub != null && !string.IsNullOrWhiteSpace(sub.Name))
                {
                    subcategories.Add(sub.Name);
                }
            }
        }

        Dictionary<long, Material> materialsById = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(m => m.Id.Value)
            .ToDictionary(g => g.Key, g => g.First());

        List<RfaSolidSnapshot> solids = new();
        int coarseTriangles = 0;
        int mediumFineTriangles = 0;

        List<GenericForm> forms = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(GenericForm))
            .Cast<GenericForm>()
            .Where(f => f.IsSolid)
            .OrderBy(f => f.Id.Value)
            .ToList();

        int solidIndex = 0;
        foreach (GenericForm form in forms)
        {
            solidIndex++;
            RfaSolidSnapshot? solidSnap = InspectGenericForm(form, solidIndex, materialsById);
            if (solidSnap == null)
            {
                continue;
            }

            solids.Add(solidSnap);
            if (solidSnap.VisibleInCoarse)
            {
                coarseTriangles += solidSnap.TriangleCount;
            }

            if (solidSnap.VisibleInMedium || solidSnap.VisibleInFine)
            {
                mediumFineTriangles += solidSnap.TriangleCount;
            }
        }

        // Also inspect FreeFormElement solids if present
        List<FreeFormElement> freeForms = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(FreeFormElement))
            .Cast<FreeFormElement>()
            .OrderBy(f => f.Id.Value)
            .ToList();
        foreach (FreeFormElement freeForm in freeForms)
        {
            solidIndex++;
            RfaSolidSnapshot? snap = InspectFreeForm(freeForm, solidIndex, materialsById);
            if (snap == null)
            {
                continue;
            }

            solids.Add(snap);
            if (snap.VisibleInCoarse)
            {
                coarseTriangles += snap.TriangleCount;
            }

            if (snap.VisibleInMedium || snap.VisibleInFine)
            {
                mediumFineTriangles += snap.TriangleCount;
            }
        }

        List<RfaMaterialSnapshot> materials = materialsById.Values
            .OrderBy(m => m.Name ?? string.Empty, StringComparer.Ordinal)
            .Select(m => new RfaMaterialSnapshot(
                m.Name ?? string.Empty,
                m.Color?.Red ?? 0,
                m.Color?.Green ?? 0,
                m.Color?.Blue ?? 0,
                m.Transparency))
            .ToList();

        (List<RfaParameterSnapshot> parameters, Dictionary<string, IReadOnlyDictionary<string, string>> typeParamValues) =
            InspectParameters(familyDoc.FamilyManager);

        int importedCadCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(ImportInstance))
            .GetElementCount();

        int imageInstanceCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(ImageInstance))
            .GetElementCount();
        int imageTypeCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(ImageType))
            .GetElementCount();
        int embeddedImageCount = Math.Max(imageInstanceCount, imageTypeCount);

        List<RfaNestedFamilySnapshot> nestedFamilies = InspectNestedFamilies(familyDoc);
        int maxNestingDepth = nestedFamilies.Count == 0 ? 0 : nestedFamilies.Max(n => n.NestingDepth);

        return new RfaInspectionSnapshot(
            familyName,
            categoryName,
            seedKind,
            fileSizeBytes,
            subcategories,
            solids,
            materials,
            parameters,
            coarseTriangles,
            mediumFineTriangles,
            importedCadCount,
            embeddedImageCount,
            nestedFamilies,
            maxNestingDepth,
            typeParamValues);
    }

    private static string ResolveFamilyName(Document familyDoc, Family? ownerFamily)
    {
        if (!string.IsNullOrWhiteSpace(familyDoc.PathName))
        {
            return Path.GetFileNameWithoutExtension(familyDoc.PathName);
        }

        if (!string.IsNullOrWhiteSpace(familyDoc.Title))
        {
            string title = familyDoc.Title;
            return title.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase)
                ? title.Substring(0, title.Length - 4)
                : title;
        }

        return ownerFamily?.Name ?? string.Empty;
    }

    private static RfaSolidSnapshot? InspectGenericForm(
        GenericForm form,
        int index,
        Dictionary<long, Material> materialsById)
    {
        FamilyElementVisibility? visibility = form.GetVisibility();
        bool visibleCoarse = visibility?.IsShownInCoarse ?? true;
        bool visibleMedium = visibility?.IsShownInMedium ?? true;
        bool visibleFine = visibility?.IsShownInFine ?? true;

        string subcategory = form.Subcategory?.Name ?? string.Empty;
        string materialName = ResolveMaterialName(form, materialsById);

        ViewDetailLevel detail = ResolveInspectionDetailLevel(visibleCoarse, visibleMedium, visibleFine);

        return ExtractGeometrySnapshot(
            form,
            string.IsNullOrWhiteSpace(form.Name) ? $"form_{index:D2}" : form.Name,
            subcategory,
            materialName,
            visibleCoarse,
            visibleMedium,
            visibleFine,
            detail);
    }

    private static RfaSolidSnapshot? InspectFreeForm(
        FreeFormElement freeForm,
        int index,
        Dictionary<long, Material> materialsById)
    {
        FamilyElementVisibility? visibility = freeForm.GetVisibility();
        bool visibleCoarse = visibility?.IsShownInCoarse ?? true;
        bool visibleMedium = visibility?.IsShownInMedium ?? true;
        bool visibleFine = visibility?.IsShownInFine ?? true;

        string subcategory = freeForm.Subcategory?.Name ?? string.Empty;
        string materialName = ResolveMaterialName(freeForm, materialsById);
        ViewDetailLevel detail = ResolveInspectionDetailLevel(visibleCoarse, visibleMedium, visibleFine);

        return ExtractGeometrySnapshot(
            freeForm,
            string.IsNullOrWhiteSpace(freeForm.Name) ? $"freeform_{index:D2}" : freeForm.Name,
            subcategory,
            materialName,
            visibleCoarse,
            visibleMedium,
            visibleFine,
            detail);
    }

    private static ViewDetailLevel ResolveInspectionDetailLevel(bool visibleCoarse, bool visibleMedium, bool visibleFine)
    {
        if (visibleCoarse && !visibleMedium && !visibleFine)
        {
            return ViewDetailLevel.Coarse;
        }

        if (visibleMedium)
        {
            return ViewDetailLevel.Medium;
        }

        return ViewDetailLevel.Fine;
    }

    private static RfaSolidSnapshot? ExtractGeometrySnapshot(
        Element element,
        string name,
        string subcategory,
        string materialName,
        bool visibleCoarse,
        bool visibleMedium,
        bool visibleFine,
        ViewDetailLevel detailLevel)
    {
        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = detailLevel,
        };

        GeometryElement? geom = element.get_Geometry(options);
        if (geom == null)
        {
            return null;
        }

        List<Solid> solids = new();
        CollectSolids(geom, solids);
        if (solids.Count == 0)
        {
            return null;
        }

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity;
        double maxZ = double.NegativeInfinity;
        int totalTriangles = 0;
        List<RfaPlanarFaceSnapshot> planarFaces = new();

        foreach (Solid solid in solids)
        {
            foreach (Face face in solid.Faces)
            {
                Mesh? mesh = face.Triangulate();
                double faceMinZ = double.PositiveInfinity;
                double faceMaxZ = double.NegativeInfinity;
                if (mesh != null)
                {
                    totalTriangles += mesh.NumTriangles;
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        XYZ v = mesh.Vertices[i];
                        double xMm = FeetToMm(v.X);
                        double yMm = FeetToMm(v.Y);
                        double zMm = FeetToMm(v.Z);
                        if (xMm < minX) minX = xMm;
                        if (xMm > maxX) maxX = xMm;
                        if (yMm < minY) minY = yMm;
                        if (yMm > maxY) maxY = yMm;
                        if (zMm < minZ) minZ = zMm;
                        if (zMm > maxZ) maxZ = zMm;
                        if (zMm < faceMinZ) faceMinZ = zMm;
                        if (zMm > faceMaxZ) faceMaxZ = zMm;
                    }
                }

                if (face is PlanarFace planar)
                {
                    XYZ normal = planar.FaceNormal;
                    double originZMm = FeetToMm(planar.Origin.Z);
                    double effectiveMinZ = double.IsInfinity(faceMinZ) ? originZMm : faceMinZ;
                    double effectiveMaxZ = double.IsInfinity(faceMaxZ) ? originZMm : faceMaxZ;
                    double areaSqMm = planar.Area * FamilyGeometryPlanBuilder.MillimetersPerFoot * FamilyGeometryPlanBuilder.MillimetersPerFoot;

                    planarFaces.Add(new RfaPlanarFaceSnapshot(
                        normal.X,
                        normal.Y,
                        normal.Z,
                        originZMm,
                        effectiveMinZ,
                        effectiveMaxZ,
                        areaSqMm));
                }
            }
        }

        if (double.IsInfinity(minX))
        {
            BoundingBoxXYZ? bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                minX = FeetToMm(bb.Min.X);
                maxX = FeetToMm(bb.Max.X);
                minY = FeetToMm(bb.Min.Y);
                maxY = FeetToMm(bb.Max.Y);
                minZ = FeetToMm(bb.Min.Z);
                maxZ = FeetToMm(bb.Max.Z);
            }
            else
            {
                minX = maxX = minY = maxY = minZ = maxZ = 0.0;
            }
        }

        return new RfaSolidSnapshot(
            name,
            subcategory,
            materialName,
            visibleCoarse,
            visibleMedium,
            visibleFine,
            minX,
            maxX,
            minY,
            maxY,
            minZ,
            maxZ,
            totalTriangles,
            planarFaces);
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid solid && solid.Volume > 1e-9 && solid.Faces.Size > 0)
            {
                solids.Add(solid);
            }
            else if (obj is GeometryInstance instance)
            {
                GeometryElement? instGeom = instance.GetInstanceGeometry();
                if (instGeom != null)
                {
                    CollectSolids(instGeom, solids);
                }
            }
        }
    }

    private static string ResolveMaterialName(Element element, Dictionary<long, Material> materialsById)
    {
        Parameter? matParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (matParam == null || matParam.StorageType != StorageType.ElementId)
        {
            return string.Empty;
        }

        ElementId matId = matParam.AsElementId();
        if (matId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        return materialsById.TryGetValue(matId.Value, out Material? material) && material != null
            ? material.Name ?? string.Empty
            : string.Empty;
    }

    private static (List<RfaParameterSnapshot> Parameters, Dictionary<string, IReadOnlyDictionary<string, string>> TypeValues)
        InspectParameters(FamilyManager manager)
    {
        List<FamilyParameter> allParams = new();
        foreach (FamilyParameter fp in manager.Parameters)
        {
            if (fp?.Definition != null && !string.IsNullOrWhiteSpace(fp.Definition.Name))
            {
                allParams.Add(fp);
            }
        }

        FamilyType? currentType = manager.CurrentType;
        List<FamilyType> allTypes = new();
        foreach (FamilyType ft in manager.Types)
        {
            if (ft != null && !string.IsNullOrWhiteSpace(ft.Name))
            {
                allTypes.Add(ft);
            }
        }

        if (currentType == null && allTypes.Count > 0)
        {
            currentType = allTypes[0];
        }

        List<RfaParameterSnapshot> snapshots = new();
        foreach (FamilyParameter fp in allParams.OrderBy(p => p.Definition.Name, StringComparer.Ordinal))
        {
            string name = fp.Definition.Name;
            Guid? guid = fp.IsShared ? fp.GUID : (Guid?)null;
            string storageKind = MapStorageKind(fp);
            string defaultText = currentType != null ? ReadParamText(currentType, fp) : string.Empty;
            double? numericMm = currentType != null && fp.StorageType == StorageType.Double && currentType.HasValue(fp)
                ? ReadLengthMm(currentType, fp)
                : null;

            snapshots.Add(new RfaParameterSnapshot(
                name,
                fp.IsShared,
                guid,
                fp.IsInstance,
                storageKind,
                defaultText,
                numericMm));
        }

        Dictionary<string, IReadOnlyDictionary<string, string>> typeMap = new(StringComparer.Ordinal);
        foreach (FamilyType ft in allTypes)
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (FamilyParameter fp in allParams)
            {
                string val = ReadParamText(ft, fp);
                if (!string.IsNullOrEmpty(val))
                {
                    values[fp.Definition.Name] = val;
                }
            }

            typeMap[ft.Name] = values;
        }

        return (snapshots, typeMap);
    }

    private static string MapStorageKind(FamilyParameter fp)
    {
        if (fp.StorageType == StorageType.String)
        {
            return "Text";
        }

        if (fp.StorageType == StorageType.Integer)
        {
            return "Integer";
        }

        if (fp.StorageType == StorageType.Double)
        {
            ForgeTypeId dataType = fp.Definition.GetDataType();
            return dataType == SpecTypeId.Length ? "Length" : "Double";
        }

        return fp.StorageType.ToString();
    }

    private static string ReadParamText(FamilyType familyType, FamilyParameter param)
    {
        if (!familyType.HasValue(param))
        {
            return string.Empty;
        }

        switch (param.StorageType)
        {
            case StorageType.String:
                return familyType.AsString(param) ?? string.Empty;
            case StorageType.Integer:
                int? iv = familyType.AsInteger(param);
                return iv.HasValue ? iv.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            case StorageType.Double:
                double? dv = familyType.AsDouble(param);
                return dv.HasValue ? FeetToMm(dv.Value).ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
            default:
                return familyType.AsValueString(param) ?? string.Empty;
        }
    }

    private static double? ReadLengthMm(FamilyType familyType, FamilyParameter param)
    {
        double? raw = familyType.AsDouble(param);
        if (!raw.HasValue)
        {
            return null;
        }

        ForgeTypeId dataType = param.Definition.GetDataType();
        return dataType == SpecTypeId.Length ? FeetToMm(raw.Value) : raw.Value;
    }

    private static List<RfaNestedFamilySnapshot> InspectNestedFamilies(Document familyDoc)
    {
        List<RfaNestedFamilySnapshot> nested = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        IEnumerable<FamilyInstance> instances = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();
        foreach (FamilyInstance inst in instances)
        {
            string? famName = inst.Symbol?.Family?.Name;
            if (string.IsNullOrWhiteSpace(famName))
            {
                continue;
            }

            int depth = 1;
            if (inst.SuperComponent != null)
            {
                depth = 2;
            }

            BuiltInCategory? bic = inst.Symbol?.Family?.FamilyCategory?.BuiltInCategory;
            string cat = bic.HasValue && bic.Value != BuiltInCategory.INVALID ? bic.Value.ToString() : string.Empty;
            string key = $"{famName}|{depth}";
            if (seen.Add(key))
            {
                nested.Add(new RfaNestedFamilySnapshot(famName!, depth, cat));
            }
        }

        return nested;
    }

    private static double FeetToMm(double feet) =>
        UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
