using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RevitGeoSuite.FloorPlanExport.Core.Gis;

/// <summary>
/// Reads geometry from an ESRI Shapefile (<c>.shp</c>) using the same NetTopologySuite ESRI IO
/// library the PLATEAU export writes with. The sidecar <c>.prj</c> (when present) supplies the
/// source CRS; a shapefile holds a single geometry type, so this yields one layer named after the
/// file. The read counterpart of <see cref="Shapefile.ShapefileWriter"/>.
/// </summary>
public sealed class ShapefileGeometryReader
{
    // Matches the trailing AUTHORITY["EPSG","<code>"] in a .prj WKT string.
    private static readonly Regex EpsgAuthority = new(
        "AUTHORITY\\s*\\[\\s*\"EPSG\"\\s*,\\s*\"?(?<code>\\d+)\"?\\s*\\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public GisDataset Read(string shapefilePath)
    {
        if (string.IsNullOrWhiteSpace(shapefilePath))
        {
            throw new ArgumentException("A shapefile path is required.", nameof(shapefilePath));
        }

        if (!File.Exists(shapefilePath))
        {
            throw new FileNotFoundException("The shapefile was not found.", shapefilePath);
        }

        List<string> warnings = new();
        List<NtsGeometry> geometries = new();
        GeometryFactory geometryFactory = new();
        using ShapefileDataReader reader = new(shapefilePath, geometryFactory);
        while (reader.Read())
        {
            NtsGeometry geometry = reader.Geometry;
            if (geometry is null || geometry.IsEmpty)
            {
                continue;
            }

            geometries.Add(geometry);
        }

        if (geometries.Count == 0)
        {
            warnings.Add("The shapefile did not contain any geometry to import.");
        }

        (int? sourceEpsg, string? sourceWkt) = ReadSourceCrs(shapefilePath);
        string layerName = Path.GetFileNameWithoutExtension(shapefilePath);
        GisLayerGeometry layer = new(layerName, geometries);
        return new GisDataset(new[] { layer }, sourceEpsg, sourceWkt, warnings);
    }

    private static (int? Epsg, string? Wkt) ReadSourceCrs(string shapefilePath)
    {
        string prjPath = Path.ChangeExtension(shapefilePath, ".prj");
        if (!File.Exists(prjPath))
        {
            return (null, null);
        }

        string wkt;
        try
        {
            wkt = File.ReadAllText(prjPath).Trim();
        }
        catch
        {
            return (null, null);
        }

        if (wkt.Length == 0)
        {
            return (null, null);
        }

        // The outermost (PROJCS/GEOGCS) AUTHORITY is the last one in the WKT string; inner ones
        // describe the datum/geographic CS. Take the last match so a projected .prj resolves to its
        // projected EPSG rather than its underlying geographic EPSG.
        int? epsg = null;
        foreach (Match match in EpsgAuthority.Matches(wkt))
        {
            if (int.TryParse(match.Groups["code"].Value, out int parsed))
            {
                epsg = parsed;
            }
        }

        return (epsg, wkt);
    }
}
