using System;
using System.Globalization;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Canonical per-layer styling for PLATEAU context output, shared by the Shapefile export (the
/// <c>FILL_RGB</c>/<c>OUT_RGB</c> DBF fields written by <see cref="PlateauContextShapefileWriter"/>)
/// and the DXF writer (<see cref="PlateauContextOutlinesDxfWriter"/> layer colours). Keeping a single
/// source of truth means the DXF basemap layers read the same semantic colours as the shapefile fills:
/// roads grey, buildings near-white, vegetation green, water light blue, and so on.
/// </summary>
public sealed class PlateauLayerStyle
{
    public PlateauLayerStyle(string type, string fillRgb, string outlineRgb, int drawOrder, int aci)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        FillRgb = fillRgb ?? throw new ArgumentNullException(nameof(fillRgb));
        OutlineRgb = outlineRgb ?? throw new ArgumentNullException(nameof(outlineRgb));
        DrawOrder = drawOrder;
        Aci = aci;
    }

    /// <summary>Shapefile style token (e.g. ROAD, BUILDING, VEGETATION).</summary>
    public string Type { get; }

    /// <summary>Fill colour as an "r,g,b" string (the shapefile <c>FILL_RGB</c> value).</summary>
    public string FillRgb { get; }

    /// <summary>Outline colour as an "r,g,b" string (the shapefile <c>OUT_RGB</c> value).</summary>
    public string OutlineRgb { get; }

    /// <summary>Shapefile draw order.</summary>
    public int DrawOrder { get; }

    /// <summary>
    /// AutoCAD Color Index used by the R12 DXF writer (DXF group 62). It is a recognizable approximation
    /// of <see cref="FillRgb"/>; exact RGB remains available for shapefile metadata.
    /// </summary>
    public int Aci { get; }

    /// <summary>24-bit RGB parsed from <see cref="FillRgb"/> as 0xRRGGBB.</summary>
    public int TrueColor => ParseTrueColor(FillRgb);

    /// <summary>
    /// Returns the canonical style for a DXF layer name. Mirrors the cases in
    /// <c>PlateauContextShapefileWriter.GetStyle</c>; the ACI is a recognizable approximation of the
    /// fill colour (buildings white, roads/relief grey, vegetation/land use green, water/railway blue,
    /// Revit-model warm).
    /// </summary>
    public static PlateauLayerStyle ForLayer(string layer)
    {
        switch (layer)
        {
            case "PLATEAU_ROADS":     return new PlateauLayerStyle("ROAD", "205,205,205", "170,170,170", 30, 8);
            case "PLATEAU_BUILDINGS": return new PlateauLayerStyle("BUILDING", "232,235,235", "190,195,195", 40, 7);
            case "PLATEAU_BRIDGES":   return new PlateauLayerStyle("BRIDGE", "234,224,206", "190,180,165", 50, 9);
            case "PLATEAU_SIDEWALKS": return new PlateauLayerStyle("SIDEWALK", "230,215,185", "170,150,110", 28, 9);
            case "PLATEAU_VEGETATION":return new PlateauLayerStyle("VEGETATION", "150,200,150", "105,160,105", 60, 3);
            case "PLATEAU_LANDUSE":   return new PlateauLayerStyle("LANDUSE", "200,220,160", "120,160,80", 35, 3);
            case "PLATEAU_RELIEF":    return new PlateauLayerStyle("RELIEF", "238,238,238", "205,205,205", 10, 9);
            case "GSI_SIDEWALKS":     return new PlateauLayerStyle("SIDEWALK", "240,230,220", "180,160,140", 22, 9);
            case "GSI_RAILWAYS":      return new PlateauLayerStyle("RAILWAY", "200,200,220", "160,160,180", 20, 5);
            case "GSI_WATER":         return new PlateauLayerStyle("WATER", "175,210,235", "115,160,200", 15, 5);
            case "GSI_LANDUSE":       return new PlateauLayerStyle("LANDUSE", "185,220,150", "95,150,75", 18, 3);
            case "REVIT_BUILDINGS":   return new PlateauLayerStyle("REVIT_BUILDING", "255,230,180", "200,160,80", 70, 2);
            case "REVIT_WALLS":       return new PlateauLayerStyle("REVIT_WALL", "255,200,150", "180,100,40", 75, 2);
            default:                  return new PlateauLayerStyle("OTHER", "220,220,220", "180,180,180", 90, 9);
        }
    }

    /// <summary>Convenience accessor for the DXF layer colour index.</summary>
    public static int AciForLayer(string layer) => ForLayer(layer).Aci;

    private static int ParseTrueColor(string rgb)
    {
        string[] parts = rgb.Split(',');
        if (parts.Length != 3)
        {
            return 0;
        }

        int r = ParseComponent(parts[0]);
        int g = ParseComponent(parts[1]);
        int b = ParseComponent(parts[2]);
        return (r << 16) | (g << 8) | b;
    }

    private static int ParseComponent(string value)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return 0;
        }

        return parsed < 0 ? 0 : (parsed > 255 ? 255 : parsed);
    }
}
