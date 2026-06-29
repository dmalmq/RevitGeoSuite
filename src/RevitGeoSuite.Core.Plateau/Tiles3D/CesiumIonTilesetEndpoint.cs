using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Terrain;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class CesiumIonTilesetSource
{
    public CesiumIonTilesetSource(Uri tilesetUrl, string? bearerToken)
    {
        TilesetUrl = tilesetUrl ?? throw new ArgumentNullException(nameof(tilesetUrl));
        BearerToken = bearerToken;
    }

    public Uri TilesetUrl { get; }

    public string? BearerToken { get; }
}

public static class CesiumIonTilesetEndpoint
{
    public const int OsmBuildingsAssetId = 96188;

    public static async Task<CesiumIonTilesetSource> ResolveAsync(
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
            throw new InvalidOperationException("Cesium Ion did not return a tileset URL for this asset/token.");
        }

        Uri tilesetBase = EnsureTrailingSlash(new Uri(tilesetUrl!));
        Uri tilesetJsonUrl = new Uri(tilesetBase, "tileset.json");

        // Validate the asset serves a 3D Tiles tileset by fetching tileset.json.
        string body = await transport.GetJsonAsync(tilesetJsonUrl, bearer, cancellationToken).ConfigureAwait(false);
        JObject tileset = JObject.Parse(body);
        if (tileset["root"] is null && tileset["asset"] is null)
        {
            throw new InvalidOperationException("The Cesium Ion asset does not appear to be a valid 3D Tiles tileset.");
        }

        return new CesiumIonTilesetSource(tilesetJsonUrl, bearer);
    }

    private static Uri EnsureTrailingSlash(Uri url)
    {
        return url.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? url : new Uri(url.AbsoluteUri + "/");
    }
}
