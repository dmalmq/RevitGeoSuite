using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Cached get-or-create of Revit materials for PLATEAU feature types. Reused by every importer
/// within one import so re-importing doesn't pile up duplicate materials. The material shading
/// color comes from <see cref="PlateauLayerStyle.ForFeatureType"/> — the same FILL_RGB palette
/// used by the DXF/shapefile exports.
/// </summary>
public sealed class PlateauImportMaterialFactory
{
    private const string MaterialNamePrefix = "PLATEAU ";

    private readonly Dictionary<PlateauFeatureType, ElementId> cache = new();

    public ElementId GetMaterialId(Document document, PlateauFeatureType featureType)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        if (cache.TryGetValue(featureType, out ElementId cached))
        {
            return cached;
        }

        ElementId materialId = FindOrCreateMaterial(document, featureType);
        cache[featureType] = materialId;
        return materialId;
    }

    private static ElementId FindOrCreateMaterial(Document document, PlateauFeatureType featureType)
    {
        string materialName = MaterialNamePrefix + featureType.GetPluralDisplayName();

        Material? existing = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing.Id;
        }

        PlateauLayerStyle style = PlateauLayerStyle.ForFeatureType(featureType);
        int trueColor = style.TrueColor;
        byte red = (byte)((trueColor >> 16) & 0xFF);
        byte green = (byte)((trueColor >> 8) & 0xFF);
        byte blue = (byte)(trueColor & 0xFF);

        ElementId newId = Material.Create(document, materialName);
        Material? material = document.GetElement(newId) as Material;
        if (material is null)
        {
            return ElementId.InvalidElementId;
        }

        material.Color = new Color(red, green, blue);
        material.UseRenderAppearanceForShading = false;
        material.Transparency = 0;

        return newId;
    }
}
