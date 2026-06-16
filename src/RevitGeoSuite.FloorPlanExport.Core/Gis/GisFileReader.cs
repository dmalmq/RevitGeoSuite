using System;
using System.IO;

namespace RevitGeoSuite.FloorPlanExport.Core.Gis;

/// <summary>
/// Entry point for reading a local GIS file into a <see cref="GisDataset"/>, dispatching on the
/// file extension. Supports ESRI Shapefile (<c>.shp</c>) and OGC GeoPackage (<c>.gpkg</c>) — the two
/// formats this addin can also export. Pure file IO with no Revit dependency, so it runs on a
/// background job thread.
/// </summary>
public static class GisFileReader
{
    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        return ext.Equals(".shp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gpkg", StringComparison.OrdinalIgnoreCase);
    }

    public static GisDataset Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        string ext = Path.GetExtension(path);
        if (ext.Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            return new ShapefileGeometryReader().Read(path);
        }

        if (ext.Equals(".gpkg", StringComparison.OrdinalIgnoreCase))
        {
            return new GeoPackageGeometryReader().Read(path);
        }

        throw new NotSupportedException(
            $"Unsupported GIS file type '{ext}'. Supported formats: Shapefile (.shp) and GeoPackage (.gpkg).");
    }
}
