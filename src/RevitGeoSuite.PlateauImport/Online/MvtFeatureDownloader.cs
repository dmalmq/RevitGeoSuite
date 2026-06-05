using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Mvt;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>A lon/lat bounding box (degrees) for one selected PLATEAU grid.</summary>
public readonly struct MvtGridBounds
{
    public MvtGridBounds(double westDeg, double southDeg, double eastDeg, double northDeg)
    {
        WestDeg = westDeg;
        SouthDeg = southDeg;
        EastDeg = eastDeg;
        NorthDeg = northDeg;
    }

    public double WestDeg { get; }

    public double SouthDeg { get; }

    public double EastDeg { get; }

    public double NorthDeg { get; }
}

/// <summary>MVT features for one dataset, projected into the model's internal-metre frame.</summary>
public sealed class MvtProjectedFeatures
{
    public MvtProjectedFeatures(Geometry? polygonArea, IReadOnlyList<LineString> lines)
    {
        PolygonArea = polygonArea;
        Lines = lines ?? Array.Empty<LineString>();
    }

    /// <summary>Unioned polygonal geometry (Polygon/MultiPolygon) in internal metres; null/empty if none.</summary>
    public Geometry? PolygonArea { get; }

    /// <summary>Line features in internal metres (roads/rails published as lines rather than surfaces).</summary>
    public IReadOnlyList<LineString> Lines { get; }

    public bool IsEmpty => (PolygonArea is null || PolygonArea.IsEmpty) && Lines.Count == 0;
}

/// <summary>
/// Fetches a PLATEAU MVT dataset's tiles over the selected grids, decodes them
/// (<see cref="MapboxVectorTileReader"/>), and projects every vertex into the model's internal-metre
/// frame via <see cref="EcefToProjectTransformer.TransformGeographicToProject"/> — so MVT roads/land
/// use line up with the 3D-Tiles building meshes. Polygons are unioned across tiles to remove tile-edge
/// seams, then both polygons and lines are clipped to the selected grids. Revit-free and testable
/// (the HTTP client is injected).
/// </summary>
public sealed class MvtFeatureDownloader
{
    private readonly IPlateauHttpClient httpClient;
    private readonly GeometryFactory geometryFactory = new GeometryFactory();

    public MvtFeatureDownloader(IPlateauHttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<MvtProjectedFeatures> DownloadAsync(
        MvtTileJson tileJson,
        IReadOnlyList<MvtGridBounds> grids,
        EcefToProjectTransformer transformer,
        ICollection<string> warnings,
        int zoomCap = 16,
        int maxTiles = 4096,
        CancellationToken cancellationToken = default)
    {
        if (tileJson is null) throw new ArgumentNullException(nameof(tileJson));
        if (grids is null) throw new ArgumentNullException(nameof(grids));
        if (transformer is null) throw new ArgumentNullException(nameof(transformer));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        int zoom = MvtTileCoverage.ResolveZoom(tileJson.MaxZoom, zoomCap);
        IReadOnlyList<MvtTileAddress> tiles = CollectTiles(grids, zoom, maxTiles);

        List<Geometry> polygons = new List<Geometry>();
        List<LineString> lines = new List<LineString>();

        foreach (MvtTileAddress tile in tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[]? bytes = await TryFetchTileAsync(tileJson, tile, warnings, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                continue;
            }

            bytes = Inflate(bytes);

            IReadOnlyList<MvtLayer> layers;
            try
            {
                layers = MapboxVectorTileReader.Read(bytes);
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped MVT tile {tile.Zoom}/{tile.X}/{tile.Y}: {ex.Message}");
                continue;
            }

            foreach (MvtLayer layer in layers)
            {
                foreach (MvtFeature feature in layer.Features)
                {
                    if (feature.GeometryType == MvtGeometryType.Polygon)
                    {
                        AddPolygons(feature, layer.Extent, tile, transformer, polygons);
                    }
                    else if (feature.GeometryType == MvtGeometryType.LineString)
                    {
                        AddLines(feature, layer.Extent, tile, transformer, lines);
                    }
                }
            }
        }

        Geometry? area = UnionPolygons(polygons, warnings);
        Geometry? mask = BuildGridMask(grids, transformer, warnings);

        if (mask is not null && !mask.IsEmpty)
        {
            area = ClipToMask(area, mask, warnings);
            lines = ClipLines(lines, mask, warnings);
        }

        return new MvtProjectedFeatures(area, lines);
    }

    private static IReadOnlyList<MvtTileAddress> CollectTiles(IReadOnlyList<MvtGridBounds> grids, int zoom, int maxTiles)
    {
        HashSet<(int, int)> seen = new HashSet<(int, int)>();
        List<MvtTileAddress> tiles = new List<MvtTileAddress>();
        foreach (MvtGridBounds grid in grids)
        {
            foreach (MvtTileAddress tile in MvtTileCoverage.TilesForBounds(
                grid.WestDeg, grid.SouthDeg, grid.EastDeg, grid.NorthDeg, zoom, maxTiles))
            {
                if (seen.Add((tile.X, tile.Y)))
                {
                    tiles.Add(tile);
                    if (tiles.Count >= maxTiles)
                    {
                        return tiles;
                    }
                }
            }
        }

        return tiles;
    }

    private async Task<byte[]?> TryFetchTileAsync(
        MvtTileJson tileJson,
        MvtTileAddress tile,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            Uri uri = new Uri(tileJson.BuildTileUrl(tile.Zoom, tile.X, tile.Y));
            return await httpClient.GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            // Empty/absent tiles return 404 — normal across a grid's coverage box; skip quietly.
            return null;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to fetch MVT tile {tile.Zoom}/{tile.X}/{tile.Y}: {ex.Message}");
            return null;
        }
    }

    private void AddPolygons(
        MvtFeature feature,
        uint extent,
        MvtTileAddress tile,
        EcefToProjectTransformer transformer,
        ICollection<Geometry> polygons)
    {
        foreach ((IReadOnlyList<MvtPoint> shell, List<IReadOnlyList<MvtPoint>> holes) in ClassifyRings(feature.Paths))
        {
            Coordinate[]? shellRing = ProjectClosedRing(shell, extent, tile, transformer);
            if (shellRing is null)
            {
                continue;
            }

            List<LinearRing> holeRings = new List<LinearRing>(holes.Count);
            foreach (IReadOnlyList<MvtPoint> hole in holes)
            {
                Coordinate[]? holeRing = ProjectClosedRing(hole, extent, tile, transformer);
                if (holeRing is not null)
                {
                    holeRings.Add(geometryFactory.CreateLinearRing(holeRing));
                }
            }

            Polygon polygon;
            try
            {
                polygon = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(shellRing), holeRings.ToArray());
            }
            catch (ArgumentException)
            {
                continue;
            }

            Geometry repaired = polygon.IsValid ? polygon : SafeBufferZero(polygon);
            if (!repaired.IsEmpty)
            {
                polygons.Add(repaired);
            }
        }
    }

    private void AddLines(
        MvtFeature feature,
        uint extent,
        MvtTileAddress tile,
        EcefToProjectTransformer transformer,
        ICollection<LineString> lines)
    {
        foreach (IReadOnlyList<MvtPoint> path in feature.Paths)
        {
            Coordinate[]? coordinates = ProjectPath(path, extent, tile, transformer);
            if (coordinates is not null && coordinates.Length >= 2)
            {
                lines.Add(geometryFactory.CreateLineString(coordinates));
            }
        }
    }

    /// <summary>
    /// Groups a polygon feature's rings into (exterior, holes) following the MVT convention: the first
    /// ring is exterior and its winding sign marks all exteriors; opposite-sign rings are holes of the
    /// current exterior. Robust to the absolute winding direction (matches mapbox's classifyRings).
    /// </summary>
    private static IEnumerable<(IReadOnlyList<MvtPoint> Shell, List<IReadOnlyList<MvtPoint>> Holes)> ClassifyRings(
        IReadOnlyList<IReadOnlyList<MvtPoint>> rings)
    {
        List<(IReadOnlyList<MvtPoint>, List<IReadOnlyList<MvtPoint>>)> polygons =
            new List<(IReadOnlyList<MvtPoint>, List<IReadOnlyList<MvtPoint>>)>();

        int? exteriorSign = null;
        IReadOnlyList<MvtPoint>? shell = null;
        List<IReadOnlyList<MvtPoint>> holes = new List<IReadOnlyList<MvtPoint>>();

        foreach (IReadOnlyList<MvtPoint> ring in rings)
        {
            if (ring.Count < 4)
            {
                continue;
            }

            double area = TileSignedArea(ring);
            if (area == 0d)
            {
                continue;
            }

            int sign = area > 0d ? 1 : -1;
            exteriorSign ??= sign;

            if (sign == exteriorSign)
            {
                if (shell is not null)
                {
                    polygons.Add((shell, holes));
                }

                shell = ring;
                holes = new List<IReadOnlyList<MvtPoint>>();
            }
            else if (shell is not null)
            {
                holes.Add(ring);
            }
        }

        if (shell is not null)
        {
            polygons.Add((shell, holes));
        }

        return polygons;
    }

    private static double TileSignedArea(IReadOnlyList<MvtPoint> ring)
    {
        double area = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            area += ((double)ring[i].X * ring[i + 1].Y) - ((double)ring[i + 1].X * ring[i].Y);
        }

        return area / 2d;
    }

    private Coordinate[]? ProjectClosedRing(
        IReadOnlyList<MvtPoint> ring,
        uint extent,
        MvtTileAddress tile,
        EcefToProjectTransformer transformer)
    {
        List<Coordinate> coordinates = ProjectCoordinates(ring, extent, tile, transformer);
        while (coordinates.Count > 1 && coordinates[0].Equals2D(coordinates[coordinates.Count - 1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        if (coordinates.Count < 3)
        {
            return null;
        }

        coordinates.Add(new Coordinate(coordinates[0]));
        return coordinates.ToArray();
    }

    private Coordinate[]? ProjectPath(
        IReadOnlyList<MvtPoint> path,
        uint extent,
        MvtTileAddress tile,
        EcefToProjectTransformer transformer)
    {
        List<Coordinate> coordinates = ProjectCoordinates(path, extent, tile, transformer);
        return coordinates.Count >= 2 ? coordinates.ToArray() : null;
    }

    private static List<Coordinate> ProjectCoordinates(
        IReadOnlyList<MvtPoint> points,
        uint extent,
        MvtTileAddress tile,
        EcefToProjectTransformer transformer)
    {
        List<Coordinate> coordinates = new List<Coordinate>(points.Count);
        foreach (MvtPoint point in points)
        {
            (double longitude, double latitude) = WebMercatorTileMath.TileLocalToLonLat(
                tile.Zoom, tile.X, tile.Y, point.X, point.Y, extent);
            Vector3d projected = transformer.TransformGeographicToProject(latitude, longitude);
            Coordinate coordinate = new Coordinate(projected.X, projected.Y);
            if (coordinates.Count == 0 || !coordinates[coordinates.Count - 1].Equals2D(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        return coordinates;
    }

    private Geometry? UnionPolygons(IReadOnlyList<Geometry> polygons, ICollection<string> warnings)
    {
        if (polygons.Count == 0)
        {
            return null;
        }

        try
        {
            return geometryFactory.CreateGeometryCollection(System.Linq.Enumerable.ToArray(polygons)).Union();
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"MVT polygon union failed ({ex.Message}); kept overlapping polygons.");
            return geometryFactory.CreateGeometryCollection(System.Linq.Enumerable.ToArray(polygons));
        }
    }

    private Geometry? BuildGridMask(IReadOnlyList<MvtGridBounds> grids, EcefToProjectTransformer transformer, ICollection<string> warnings)
    {
        List<Geometry> gridPolygons = new List<Geometry>(grids.Count);
        foreach (MvtGridBounds grid in grids)
        {
            Coordinate[] ring =
            {
                ToCoordinate(transformer, grid.SouthDeg, grid.WestDeg),
                ToCoordinate(transformer, grid.SouthDeg, grid.EastDeg),
                ToCoordinate(transformer, grid.NorthDeg, grid.EastDeg),
                ToCoordinate(transformer, grid.NorthDeg, grid.WestDeg),
                ToCoordinate(transformer, grid.SouthDeg, grid.WestDeg)
            };

            try
            {
                gridPolygons.Add(geometryFactory.CreatePolygon(ring));
            }
            catch (ArgumentException)
            {
                // skip degenerate grid box
            }
        }

        if (gridPolygons.Count == 0)
        {
            return null;
        }

        try
        {
            return geometryFactory.CreateGeometryCollection(gridPolygons.ToArray()).Union();
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"Could not build the grid clip mask ({ex.Message}); MVT features were left unclipped.");
            return null;
        }
    }

    private static Coordinate ToCoordinate(EcefToProjectTransformer transformer, double latitude, double longitude)
    {
        Vector3d projected = transformer.TransformGeographicToProject(latitude, longitude);
        return new Coordinate(projected.X, projected.Y);
    }

    private static Geometry? ClipToMask(Geometry? area, Geometry mask, ICollection<string> warnings)
    {
        if (area is null || area.IsEmpty)
        {
            return area;
        }

        try
        {
            return area.Intersection(mask);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"MVT polygon clip to grids failed ({ex.Message}); kept the unclipped area.");
            return area;
        }
    }

    private List<LineString> ClipLines(IReadOnlyList<LineString> lines, Geometry mask, ICollection<string> warnings)
    {
        List<LineString> clipped = new List<LineString>(lines.Count);
        foreach (LineString line in lines)
        {
            try
            {
                Geometry result = line.Intersection(mask);
                CollectLineStrings(result, clipped);
            }
            catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
            {
                warnings.Add($"MVT line clip to grids failed ({ex.Message}); kept the unclipped line.");
                clipped.Add(line);
            }
        }

        return clipped;
    }

    private static void CollectLineStrings(Geometry geometry, ICollection<LineString> result)
    {
        if (geometry.IsEmpty)
        {
            return;
        }

        if (geometry is LineString lineString)
        {
            if (lineString.NumPoints >= 2)
            {
                result.Add(lineString);
            }

            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            CollectLineStrings(geometry.GetGeometryN(index), result);
        }
    }

    private Geometry SafeBufferZero(Geometry geometry)
    {
        try
        {
            return geometry.Buffer(0d);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return geometryFactory.CreatePolygon();
        }
    }

    private static byte[] Inflate(byte[] data)
    {
        // PLATEAU tiles are normally raw protobuf, but inflate defensively if a dataset serves
        // gzip-compressed tile bodies (magic 0x1F 0x8B).
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
        {
            return data;
        }

        using MemoryStream input = new MemoryStream(data);
        using GZipStream gzip = new GZipStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
