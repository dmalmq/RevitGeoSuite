using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public readonly struct TilesetLeaf
{
    public TilesetLeaf(Uri b3dmUrl, Matrix4x4d transform)
    {
        B3dmUrl = b3dmUrl;
        Transform = transform;
    }

    public Uri B3dmUrl { get; }

    public Matrix4x4d Transform { get; }
}

public sealed class TilesetWalker
{
    private readonly IPlateauHttpClient httpClient;

    public TilesetWalker(IPlateauHttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<TilesetLeaf>> WalkAsync(Uri tilesetUrl, CancellationToken cancellationToken)
    {
        if (tilesetUrl is null) throw new ArgumentNullException(nameof(tilesetUrl));
        List<TilesetLeaf> leaves = new List<TilesetLeaf>();
        await WalkTilesetAsync(tilesetUrl, Matrix4x4d.Identity, leaves, cancellationToken).ConfigureAwait(false);
        return leaves;
    }

    private async Task WalkTilesetAsync(Uri tilesetUrl, Matrix4x4d parentTransform, List<TilesetLeaf> leaves, CancellationToken ct)
    {
        string body = await httpClient.GetStringAsync(tilesetUrl, ct).ConfigureAwait(false);
        TilesetJson? tileset = JsonConvert.DeserializeObject<TilesetJson>(body);
        if (tileset?.Root is null) return;
        await WalkTileAsync(tileset.Root, parentTransform, tilesetUrl, leaves, ct).ConfigureAwait(false);
    }

    private async Task WalkTileAsync(TilesetTile tile, Matrix4x4d parentTransform, Uri baseUrl, List<TilesetLeaf> leaves, CancellationToken ct)
    {
        Matrix4x4d local = tile.Transform is null ? Matrix4x4d.Identity : Matrix4x4d.FromColumnMajor(tile.Transform);
        Matrix4x4d world = Matrix4x4d.Multiply(parentTransform, local);

        string? uri = tile.Content?.ResolvedUri;
        if (!string.IsNullOrEmpty(uri))
        {
            Uri resolved = new Uri(baseUrl, uri);
            if (LooksLikeTileset(resolved))
            {
                await WalkTilesetAsync(resolved, world, leaves, ct).ConfigureAwait(false);
            }
            else if (LooksLikeB3dm(resolved))
            {
                leaves.Add(new TilesetLeaf(resolved, world));
            }
        }

        if (tile.Children is not null)
        {
            foreach (TilesetTile child in tile.Children)
            {
                await WalkTileAsync(child, world, baseUrl, leaves, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool LooksLikeTileset(Uri uri) => uri.AbsoluteUri.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeB3dm(Uri uri) => uri.AbsoluteUri.EndsWith(".b3dm", StringComparison.OrdinalIgnoreCase);
}
