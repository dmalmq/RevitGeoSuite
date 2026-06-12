using System;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// HTTP access for Cesium terrain: JSON (Ion endpoint + layer.json) and binary quantized-mesh tiles.
/// Abstracted so the sampler can be unit-tested with canned responses, and so the real implementation
/// can set the Bearer auth and quantized-mesh Accept headers that <c>PlateauHttpClient</c> doesn't expose.
/// </summary>
public interface ICesiumTerrainTransport
{
    Task<string> GetJsonAsync(Uri url, string? bearerToken, CancellationToken cancellationToken);

    Task<byte[]> GetTerrainTileAsync(Uri url, string? bearerToken, CancellationToken cancellationToken);
}
