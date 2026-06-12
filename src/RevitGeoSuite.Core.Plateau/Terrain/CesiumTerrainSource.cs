using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// A resolved Cesium quantized-mesh terrain source: the tile base URL, auth token, tile-URL template,
/// and the per-level availability ranges from <c>layer.json</c>. Knows how to build a tile URL and
/// test whether a tile exists.
/// </summary>
public sealed class CesiumTerrainSource
{
    private readonly Uri baseUrl;
    private readonly string tileTemplate;
    private readonly string version;
    private readonly IReadOnlyList<IReadOnlyList<TerrainTileRange>> availableByLevel;

    public CesiumTerrainSource(
        Uri baseUrl,
        string tileTemplate,
        string version,
        string? bearerToken,
        int maxZoom,
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> availableByLevel)
    {
        this.baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        this.tileTemplate = tileTemplate ?? throw new ArgumentNullException(nameof(tileTemplate));
        this.version = version ?? string.Empty;
        this.availableByLevel = availableByLevel ?? throw new ArgumentNullException(nameof(availableByLevel));
        BearerToken = bearerToken;
        // Prefer the explicit layer.json maxzoom; fall back to the availability depth if that's all we have.
        MaxZoom = maxZoom > 0 ? maxZoom : Math.Max(0, availableByLevel.Count - 1);
    }

    public string? BearerToken { get; }

    /// <summary>Deepest zoom level the layer advertises (0 when no availability was published).</summary>
    public int MaxZoom { get; }

    /// <summary>True when layer.json published per-level tile availability we can gate on.</summary>
    public bool HasAvailability => availableByLevel.Count > 0;

    public bool IsAvailable(int level, int x, int y)
    {
        if (level < 0 || level >= availableByLevel.Count)
        {
            return false;
        }

        foreach (TerrainTileRange range in availableByLevel[level])
        {
            if (x >= range.XStart && x <= range.XEnd && y >= range.YStart && y <= range.YEnd)
            {
                return true;
            }
        }

        return false;
    }

    public Uri TileUrl(int level, int x, int y)
    {
        string relative = tileTemplate
            .Replace("{z}", level.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture))
            .Replace("{version}", version);
        return new Uri(baseUrl, relative);
    }
}
