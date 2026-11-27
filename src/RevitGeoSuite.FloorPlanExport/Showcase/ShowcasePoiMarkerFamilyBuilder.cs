using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;

namespace RevitGeoSuite.FloorPlanExport.Showcase;

/// <summary>
/// Minimal programmatic builder and loader for the instanced <c>KNZ_FRM_PoiMarker</c> Revit family
/// (44 planar triangles: 6-sided pedestal + 4-sided tapered pin stem + 4-sided beacon cap,
/// with one family type per <c>M_KS_Poi_*</c> showcase material).
/// Compatible with Revit 2024 (<c>net48</c>) and Revit 2027 (<c>net10.0-windows</c>).
/// </summary>
public static class ShowcasePoiMarkerFamilyBuilder
{
    public const string FamilyName = ShowcasePoiAndNetworkPlanner.PoiMarkerFamilyName;
    private const string BuildTransactionName = "Kanizu Forma Showcase: Build KNZ_FRM_PoiMarker family";

    private static readonly string[] PoiMaterialNames =
    {
        ShowcaseMaterialCatalog.PoiGeneral,
        ShowcaseMaterialCatalog.PoiAccessibility,
        ShowcaseMaterialCatalog.PoiTransit,
        ShowcaseMaterialCatalog.PoiSafety,
        ShowcaseMaterialCatalog.PoiCommercial,
    };

    /// <summary>
    /// Locates an already-loaded <c>KNZ_FRM_PoiMarker</c> family in <paramref name="companionDocument"/>,
    /// loads it from <see cref="ShowcaseCompanionBuildOptions.PoiMarkerFamilyPath"/>, or programmatically
    /// builds and loads it from <see cref="ShowcaseCompanionBuildOptions.PoiMarkerFamilyTemplatePath"/>.
    /// Returns a map from <c>M_KS_Poi_*</c> material name to active <see cref="FamilySymbol"/> (empty when
    /// no family/template is available, allowing <see cref="ShowcaseCompanionBuilder"/> to fall back cleanly
    /// to instanced <see cref="DirectShapeType"/> prototypes).
    /// </summary>
    public static Dictionary<string, FamilySymbol> TryEnsurePoiMarkerSymbols(
        Document companionDocument,
        ShowcaseCompanionBuildOptions options,
        ICollection<string> warnings)
    {
        if (companionDocument is null)
        {
            throw new ArgumentNullException(nameof(companionDocument));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.UsePoiMarkerFamilyInstances)
        {
            return new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, FamilySymbol> existing = CollectLoadedSymbols(companionDocument);
        if (existing.Count > 0)
        {
            ActivateSymbols(existing.Values);
            return existing;
        }

        if (!string.IsNullOrWhiteSpace(options.PoiMarkerFamilyPath))
        {
            string rfaPath = Path.GetFullPath(options.PoiMarkerFamilyPath!.Trim());
            if (File.Exists(rfaPath))
            {
                try
                {
                    companionDocument.LoadFamily(rfaPath, new ShowcaseOverwriteFamilyLoadOptions(), out _);
                    Dictionary<string, FamilySymbol> loaded = CollectLoadedSymbols(companionDocument);
                    ActivateSymbols(loaded.Values);
                    return loaded;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to load KNZ_FRM_PoiMarker family from '{rfaPath}' (falling back to instanced DirectShapeType): {ex.Message}");
                    return new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                }
            }

            warnings.Add($"Configured PoiMarkerFamilyPath '{rfaPath}' does not exist; falling back to instanced DirectShapeType.");
        }

        if (!string.IsNullOrWhiteSpace(options.PoiMarkerFamilyTemplatePath))
        {
            string rftPath = Path.GetFullPath(options.PoiMarkerFamilyTemplatePath!.Trim());
            if (!File.Exists(rftPath))
            {
                warnings.Add($"Configured PoiMarkerFamilyTemplatePath '{rftPath}' does not exist; falling back to instanced DirectShapeType.");
                return new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            }

            string tempRfaPath = Path.Combine(
                Path.GetTempPath(),
                $"{FamilyName}.{Guid.NewGuid():N}.rfa");
            try
            {
                BuildPoiMarkerFamilyFile(
                    companionDocument.Application,
                    rftPath,
                    tempRfaPath,
                    options.PoiAndNetworkOptions);

                companionDocument.LoadFamily(tempRfaPath, new ShowcaseOverwriteFamilyLoadOptions(), out _);
                Dictionary<string, FamilySymbol> loaded = CollectLoadedSymbols(companionDocument);
                ActivateSymbols(loaded.Values);
                return loaded;
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to programmatically build KNZ_FRM_PoiMarker from '{rftPath}' (falling back to instanced DirectShapeType): {ex.Message}");
                return new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                TryDeleteFile(tempRfaPath);
            }
        }

        return new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Programmatically builds and saves a lightweight 44-triangle <c>KNZ_FRM_PoiMarker.rfa</c> Generic Model family
    /// from a Revit Generic Model <c>.rft</c> seed template.
    /// </summary>
    public static void BuildPoiMarkerFamilyFile(
        Application application,
        string genericModelTemplatePath,
        string outputRfaPath,
        ShowcasePoiAndNetworkPlanOptions? options = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrWhiteSpace(genericModelTemplatePath))
        {
            throw new ArgumentException("Family template path is required.", nameof(genericModelTemplatePath));
        }

        if (string.IsNullOrWhiteSpace(outputRfaPath))
        {
            throw new ArgumentException("Output .rfa path is required.", nameof(outputRfaPath));
        }

        ShowcasePoiAndNetworkPlanOptions effective = options ?? ShowcasePoiAndNetworkPlanOptions.Default;
        double baseRadiusFeet = ShowcaseCoordinateTransform.MetersToFeet(effective.PoiMarkerRadiusMeters);
        double totalHeightFeet = ShowcaseCoordinateTransform.MetersToFeet(effective.PoiMarkerHeightMeters);

        string originalSharedParams = application.SharedParametersFilename ?? string.Empty;
        Document? familyDoc = null;
        try
        {
            familyDoc = application.NewFamilyDocument(genericModelTemplatePath);
            if (familyDoc is null || !familyDoc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Could not create family document from template '{genericModelTemplatePath}'.");
            }

            using (Transaction tx = new(familyDoc, BuildTransactionName))
            {
                tx.Start();
                try
                {
                    Category? genericModelCat = Category.GetCategory(familyDoc, BuiltInCategory.OST_GenericModel);
                    if (genericModelCat != null && familyDoc.OwnerFamily != null)
                    {
                        familyDoc.OwnerFamily.FamilyCategory = genericModelCat;
                    }

                    Dictionary<string, ElementId> materialIds = EnsurePoiMaterials(familyDoc);

                    FamilyManager manager = familyDoc.FamilyManager;
                    FamilyParameter materialParam = manager.AddParameter(
                        "MarkerMaterial",
                        GroupTypeId.Materials,
                        SpecTypeId.Reference.Material,
                        isInstance: false);

                    SketchPlane basePlane = SketchPlane.Create(familyDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));

                    // 1. Pedestal: 6-sided regular prism (20 triangles), Z = 0 .. 0.12*H
                    double pedestalHeight = totalHeightFeet * 0.12d;
                    CurveArrArray pedestalProfile = new();
                    pedestalProfile.Append(CreateRegularPolygonCurveArray(baseRadiusFeet, sides: 6, zFeet: 0d, angleOffsetRadians: 0d));
                    Extrusion pedestal = familyDoc.FamilyCreate.NewExtrusion(true, pedestalProfile, basePlane, pedestalHeight);
                    AssociateMaterial(manager, pedestal, materialParam);

                    // 2. Lower pin stem: 4-sided diamond frustum (12 triangles), Z = 0.12*H .. 0.72*H
                    double midHeight = totalHeightFeet * 0.72d;
                    CurveArray stemBase = CreateRegularPolygonCurveArray(baseRadiusFeet * 0.28d, sides: 4, zFeet: 0d, angleOffsetRadians: Math.PI * 0.25d);
                    CurveArray stemTop = CreateRegularPolygonCurveArray(baseRadiusFeet * 0.90d, sides: 4, zFeet: 0d, angleOffsetRadians: Math.PI * 0.25d);
                    Blend lowerStem = familyDoc.FamilyCreate.NewBlend(true, stemTop, stemBase, basePlane);
                    lowerStem.BottomOffset = pedestalHeight;
                    lowerStem.TopOffset = midHeight;
                    AssociateMaterial(manager, lowerStem, materialParam);

                    // 3. Upper beacon cap: 4-sided tapered frustum (12 triangles), Z = 0.72*H .. H
                    CurveArray capBase = CreateRegularPolygonCurveArray(baseRadiusFeet * 0.90d, sides: 4, zFeet: 0d, angleOffsetRadians: Math.PI * 0.25d);
                    CurveArray capTop = CreateRegularPolygonCurveArray(baseRadiusFeet * 0.22d, sides: 4, zFeet: 0d, angleOffsetRadians: Math.PI * 0.25d);
                    Blend upperCap = familyDoc.FamilyCreate.NewBlend(true, capTop, capBase, basePlane);
                    upperCap.BottomOffset = midHeight;
                    upperCap.TopOffset = totalHeightFeet;
                    AssociateMaterial(manager, upperCap, materialParam);

                    // Create one family type per M_KS_Poi_* material so instances share geometry & render color-coded.
                    foreach (string matName in PoiMaterialNames)
                    {
                        FamilyType familyType = manager.NewType(matName);
                        manager.CurrentType = familyType;
                        if (materialIds.TryGetValue(matName, out ElementId? matId))
                        {
                            manager.Set(materialParam, matId);
                        }
                    }

                    familyDoc.Regenerate();
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    throw;
                }
            }

            string? dir = Path.GetDirectoryName(Path.GetFullPath(outputRfaPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            SaveAsOptions saveOptions = new()
            {
                OverwriteExistingFile = true,
                Compact = true,
            };
            familyDoc.SaveAs(outputRfaPath, saveOptions);
        }
        finally
        {
            application.SharedParametersFilename = originalSharedParams;
            if (familyDoc != null && familyDoc.IsValidObject)
            {
                try
                {
                    familyDoc.Close(saveModified: false);
                }
                catch
                {
                    // Best-effort cleanup of scratch family document.
                }
            }
        }
    }

    private static Dictionary<string, FamilySymbol> CollectLoadedSymbols(Document document)
    {
        Dictionary<string, FamilySymbol> symbols = new(StringComparer.OrdinalIgnoreCase);
        foreach (FamilySymbol symbol in new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
        {
            if (symbol.Family is null)
            {
                continue;
            }

            if (!symbol.Family.Name.StartsWith(FamilyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!symbols.ContainsKey(symbol.Name))
            {
                symbols[symbol.Name] = symbol;
            }
        }

        return symbols;
    }

    private static void ActivateSymbols(IEnumerable<FamilySymbol> symbols)
    {
        foreach (FamilySymbol symbol in symbols)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }
        }
    }

    private static Dictionary<string, ElementId> EnsurePoiMaterials(Document familyDoc)
    {
        Dictionary<string, Material> existing = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(m => m.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ElementId> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string matName in PoiMaterialNames)
        {
            ShowcaseMaterialSpec spec = ShowcaseMaterialCatalog.GetRequired(matName);
            if (!existing.TryGetValue(spec.MaterialName, out Material? material))
            {
                ElementId id = Material.Create(familyDoc, spec.MaterialName);
                material = (Material)familyDoc.GetElement(id);
                existing[spec.MaterialName] = material;
            }

            material.Color = new Color(spec.Color.R, spec.Color.G, spec.Color.B);
            material.Transparency = spec.Color.TransparencyPercent;
            material.UseRenderAppearanceForShading = false;
            result[spec.MaterialName] = material.Id;
        }

        return result;
    }

    private static void AssociateMaterial(FamilyManager manager, GenericForm form, FamilyParameter materialParam)
    {
        Parameter? elemMatParam = form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (elemMatParam != null && !elemMatParam.IsReadOnly)
        {
            manager.AssociateElementParameterToFamilyParameter(elemMatParam, materialParam);
        }
    }

    private static CurveArray CreateRegularPolygonCurveArray(double radiusFeet, int sides, double zFeet, double angleOffsetRadians)
    {
        XYZ[] pts = new XYZ[sides];
        double step = (2d * Math.PI) / sides;
        for (int i = 0; i < sides; i++)
        {
            double angle = angleOffsetRadians + (i * step);
            pts[i] = new XYZ(radiusFeet * Math.Cos(angle), radiusFeet * Math.Sin(angle), zFeet);
        }

        CurveArray curves = new();
        for (int i = 0; i < sides; i++)
        {
            curves.Append(Line.CreateBound(pts[i], pts[(i + 1) % sides]));
        }

        return curves;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temp file cleanup.
        }
    }

    private sealed class ShowcaseOverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
