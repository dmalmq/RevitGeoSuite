using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// Resolves a Cesium Ion terrain asset into a usable <see cref="CesiumTerrainSource"/>: it calls the
/// Ion asset <c>/endpoint</c> (with the user's Ion access token) to get the tileset URL + a short-lived
/// Bearer token, then reads the tileset's <c>layer.json</c> for the format, version, tile template, and
/// per-level availability.
/// </summary>
public static class CesiumIonTerrainEndpoint
{
    /// <summary>The PLATEAU terrain asset published on Cesium Ion (the one the reference cesium app uses).</summary>
    public const int PlateauTerrainAssetId = 3258112;

    public static async Task<CesiumTerrainSource> ResolveAsync(
        int assetId,
        string ionAccessToken,
        ICesiumTerrainTransport transport,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ionAccessToken)) throw new ArgumentException("A Cesium Ion access token is required.", nameof(ionAccessToken));
        if (transport is null) throw new ArgumentNullException(nameof(transport));

        Uri endpointUrl = new Uri($"https://api.cesium.com/v1/assets/{assetId}/endpoint?access_token={Uri.EscapeDataString(ionAccessToken)}");
        JObject endpoint = JObject.Parse(await transport.GetJsonAsync(endpointUrl, null, cancellationToken).ConfigureAwait(false));

        string? tilesetUrl = endpoint.Value<string>("url");
        string? bearer = endpoint.Value<string>("accessToken");
        if (string.IsNullOrWhiteSpace(tilesetUrl))
        {
            throw new InvalidOperationException("Cesium Ion did not return a terrain tileset URL for this asset/token.");
        }

        Uri tilesetBase = EnsureTrailingSlash(new Uri(tilesetUrl!));
        Uri layerUrl = new Uri(tilesetBase, "layer.json");
        JObject layer = JObject.Parse(await transport.GetJsonAsync(layerUrl, bearer, cancellationToken).ConfigureAwait(false));

        string format = layer.Value<string>("format") ?? string.Empty;
        if (format.IndexOf("quantized-mesh", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException($"The terrain layer format '{format}' is not quantized-mesh and is not supported.");
        }

        string version = layer.Value<string>("version") ?? string.Empty;
        string template = (layer["tiles"] as JArray)?.Select(t => t.Value<string>()).FirstOrDefault(t => !string.IsNullOrEmpty(t))
            ?? "{z}/{x}/{y}.terrain?v={version}";
        // PLATEAU terrain advertises maxzoom/minzoom rather than a per-level "available" array.
        int maxZoom = layer.Value<int?>("maxzoom") ?? 0;

        return new CesiumTerrainSource(tilesetBase, template!, version, bearer, maxZoom, ParseAvailable(layer["available"]));
    }

    private static IReadOnlyList<IReadOnlyList<TerrainTileRange>> ParseAvailable(JToken? availableToken)
    {
        List<IReadOnlyList<TerrainTileRange>> levels = new List<IReadOnlyList<TerrainTileRange>>();
        if (availableToken is JArray levelArray)
        {
            foreach (JToken level in levelArray)
            {
                List<TerrainTileRange> ranges = new List<TerrainTileRange>();
                if (level is JArray rangeArray)
                {
                    foreach (JToken range in rangeArray)
                    {
                        ranges.Add(new TerrainTileRange(
                            range.Value<int>("startX"),
                            range.Value<int>("endX"),
                            range.Value<int>("startY"),
                            range.Value<int>("endY")));
                    }
                }

                levels.Add(ranges);
            }
        }

        return levels;
    }

    private static Uri EnsureTrailingSlash(Uri url)
    {
        return url.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? url : new Uri(url.AbsoluteUri + "/");
    }
}
