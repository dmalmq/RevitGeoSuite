using System;
using System.Collections.Generic;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RevitGeoSuite.FloorPlanExport.Core.Gis;

/// <summary>
/// One named layer of raw geometries read from a GIS file, still in the file's own CRS.
/// A shapefile yields a single layer; a GeoPackage yields one layer per feature table.
/// </summary>
public sealed class GisLayerGeometry
{
    public GisLayerGeometry(string name, IReadOnlyList<NtsGeometry> geometries)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "layer" : name;
        Geometries = geometries ?? throw new ArgumentNullException(nameof(geometries));
    }

    public string Name { get; }

    public IReadOnlyList<NtsGeometry> Geometries { get; }
}

/// <summary>
/// The geometry-only result of reading a shapefile or GeoPackage: the layers (in the source CRS)
/// plus whatever source CRS the file declared (EPSG and/or WKT). Attributes are intentionally not
/// read — the import lands as a flat CAD basemap, which cannot carry feature attributes anyway.
/// </summary>
public sealed class GisDataset
{
    public GisDataset(
        IReadOnlyList<GisLayerGeometry> layers,
        int? sourceEpsg,
        string? sourceWkt,
        IReadOnlyList<string> warnings)
    {
        Layers = layers ?? throw new ArgumentNullException(nameof(layers));
        SourceEpsg = sourceEpsg;
        SourceWkt = sourceWkt;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public IReadOnlyList<GisLayerGeometry> Layers { get; }

    /// <summary>EPSG code declared by the file, when known (shapefile .prj authority or GeoPackage srs).</summary>
    public int? SourceEpsg { get; }

    /// <summary>Source CRS as WKT, when known. Used as a fallback when <see cref="SourceEpsg"/> is absent.</summary>
    public string? SourceWkt { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int FeatureCount
    {
        get
        {
            int total = 0;
            foreach (GisLayerGeometry layer in Layers)
            {
                total += layer.Geometries.Count;
            }

            return total;
        }
    }
}
