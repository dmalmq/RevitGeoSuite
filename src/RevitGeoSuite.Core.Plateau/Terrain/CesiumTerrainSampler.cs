using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Dem;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// Fetches and decodes the Cesium quantized-mesh tiles covering a lon/lat box, reprojects every
/// terrain vertex into the model's projected CRS, and assembles a <see cref="DemSampler"/> — the same
/// sampler abstraction the local PLATEAU-dem source produces, so the ground builder/importer is shared.
/// Terrain heights stay ellipsoidal here; the caller applies the geoid offset when building the surface.
/// </summary>
public sealed class CesiumTerrainSampler
{
    public const int DefaultMaxTiles = 24;

    private readonly ICesiumTerrainTransport transport;

    public CesiumTerrainSampler(ICesiumTerrainTransport? transport = null)
    {
        this.transport = transport ?? new CesiumTerrainHttpTransport();
    }

    public async Task<DemSampler> BuildAsync(
        CesiumTerrainSource source,
        double westDegrees,
        double southDegrees,
        double eastDegrees,
        double northDegrees,
        ICoordinateTransformer transformer,
        CrsReference targetCrs,
        ICollection<string> warnings,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        int maxTiles = DefaultMaxTiles,
        int fallbackMaxZoom = 15)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (transformer is null) throw new ArgumentNullException(nameof(transformer));
        if (targetCrs is null) throw new ArgumentNullException(nameof(targetCrs));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        // Pick the zoom from the layer's declared max (maxzoom or availability depth); only fall back
        // when the layer told us nothing. Availability, when present, gates individual tiles below.
        int effectiveMaxZoom = source.MaxZoom > 0 ? source.MaxZoom : fallbackMaxZoom;
        int level = PickLevel(effectiveMaxZoom, westDegrees, southDegrees, eastDegrees, northDegrees, maxTiles);
        TerrainTileRange range = GeographicTilingScheme.TileRange(level, westDegrees, southDegrees, eastDegrees, northDegrees);

        List<(Vector3d A, Vector3d B, Vector3d C)> triangles = new List<(Vector3d, Vector3d, Vector3d)>();
        int skippedUnavailable = 0;
        int failed = 0;
        int fetched = 0;
        for (int x = range.XStart; x <= range.XEnd; x++)
        {
            for (int y = range.YStart; y <= range.YEnd; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Soft gate: only skip when the layer told us this tile doesn't exist.
                if (source.HasAvailability && !source.IsAvailable(level, x, y))
                {
                    skippedUnavailable++;
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = await transport.GetTerrainTileAsync(source.TileUrl(level, x, y), source.BearerToken, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (failed < 3)
                    {
                        warnings.Add(string.Format(CultureInfo.InvariantCulture, "Terrain tile {0}/{1}/{2} download failed: {3}", level, x, y, ex.Message));
                    }
                    failed++;
                    continue;
                }

                QuantizedMeshTile tile;
                try
                {
                    tile = QuantizedMeshDecoder.Decode(bytes);
                }
                catch (Exception ex)
                {
                    if (failed < 3)
                    {
                        warnings.Add(string.Format(CultureInfo.InvariantCulture, "Terrain tile {0}/{1}/{2} decode failed: {3}", level, x, y, ex.Message));
                    }
                    failed++;
                    continue;
                }

                AppendTriangles(tile, GeographicTilingScheme.TileRectangle(level, x, y), transformer, targetCrs, triangles);
                fetched++;
                progress?.Report(string.Format(CultureInfo.InvariantCulture, "Downloading terrain… {0} tile(s)", fetched));
            }
        }

        if (triangles.Count == 0)
        {
            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "No terrain collected. bbox=[{0:0.####},{1:0.####},{2:0.####},{3:0.####}] level={4} maxZoom={5} hasAvailability={6} tilesInRange={7} skippedUnavailable={8} downloadFailed={9}.",
                westDegrees, southDegrees, eastDegrees, northDegrees, level, effectiveMaxZoom, source.HasAvailability, range.TileCount, skippedUnavailable, failed));
        }

        return new DemSampler(triangles);
    }

    /// <summary>Deepest level whose tile coverage of the box stays within <paramref name="maxTiles"/>.</summary>
    private static int PickLevel(int maxZoom, double west, double south, double east, double north, int maxTiles)
    {
        for (int level = maxZoom; level > 0; level--)
        {
            if (GeographicTilingScheme.TileRange(level, west, south, east, north).TileCount <= maxTiles)
            {
                return level;
            }
        }

        return 0;
    }

    private static void AppendTriangles(
        QuantizedMeshTile tile,
        GeoTileRectangle rectangle,
        ICoordinateTransformer transformer,
        CrsReference targetCrs,
        List<(Vector3d A, Vector3d B, Vector3d C)> triangles)
    {
        Vector3d[] projected = new Vector3d[tile.VertexCount];
        for (int i = 0; i < tile.VertexCount; i++)
        {
            (double lon, double lat) = rectangle.ToLonLat(tile.U[i], tile.V[i]);
            ProjectedCoordinate point = transformer.Project(new GeographicCoordinate(lat, lon), targetCrs);
            projected[i] = new Vector3d(point.Easting, point.Northing, tile.HeightMeters[i]);
        }

        int[] indices = tile.TriangleIndices;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            triangles.Add((projected[indices[t]], projected[indices[t + 1]], projected[indices[t + 2]]));
        }
    }
}
