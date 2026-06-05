using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public readonly struct TilesetRegion
{
    public TilesetRegion(double westRadians, double southRadians, double eastRadians, double northRadians)
    {
        WestRadians = westRadians;
        SouthRadians = southRadians;
        EastRadians = eastRadians;
        NorthRadians = northRadians;
    }

    public double WestRadians { get; }

    public double SouthRadians { get; }

    public double EastRadians { get; }

    public double NorthRadians { get; }

    public double WestDegrees => RadiansToDegrees(WestRadians);

    public double SouthDegrees => RadiansToDegrees(SouthRadians);

    public double EastDegrees => RadiansToDegrees(EastRadians);

    public double NorthDegrees => RadiansToDegrees(NorthRadians);

    public static bool TryCreate(double[]? region, out TilesetRegion result)
    {
        result = default;
        if (region is null || region.Length < 4)
        {
            return false;
        }

        double west = region[0];
        double south = region[1];
        double east = region[2];
        double north = region[3];
        if (!IsFinite(west) || !IsFinite(south) || !IsFinite(east) || !IsFinite(north))
        {
            return false;
        }

        if (east <= west || north <= south)
        {
            return false;
        }

        result = new TilesetRegion(west, south, east, north);
        return true;
    }

    public bool IntersectsDegrees(double westDeg, double southDeg, double eastDeg, double northDeg)
    {
        return EastDegrees > westDeg
            && WestDegrees < eastDeg
            && NorthDegrees > southDeg
            && SouthDegrees < northDeg;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public readonly struct TilesetLeaf
{
    public TilesetLeaf(Uri b3dmUrl, Matrix4x4d transform)
        : this(b3dmUrl, transform, null)
    {
    }

    public TilesetLeaf(Uri b3dmUrl, Matrix4x4d transform, TilesetRegion? boundingRegion)
    {
        B3dmUrl = b3dmUrl ?? throw new ArgumentNullException(nameof(b3dmUrl));
        Transform = transform;
        BoundingRegion = boundingRegion;
        Id = BuildStableId(b3dmUrl);
    }

    public Uri B3dmUrl { get; }

    public Matrix4x4d Transform { get; }

    public TilesetRegion? BoundingRegion { get; }

    public string Id { get; }

    private static string BuildStableId(Uri b3dmUrl)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(b3dmUrl.AbsoluteUri));
        return string.Concat(hash.Take(12).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
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
        await WalkTilesetAsync(tilesetUrl, Matrix4x4d.Identity, inheritedRegion: null, leaves, cancellationToken).ConfigureAwait(false);
        return leaves;
    }

    private async Task WalkTilesetAsync(
        Uri tilesetUrl,
        Matrix4x4d parentTransform,
        TilesetRegion? inheritedRegion,
        List<TilesetLeaf> leaves,
        CancellationToken ct)
    {
        string body = await httpClient.GetStringAsync(tilesetUrl, ct).ConfigureAwait(false);
        TilesetJson? tileset = JsonConvert.DeserializeObject<TilesetJson>(body);
        if (tileset?.Root is null) return;
        await WalkTileAsync(tileset.Root, parentTransform, inheritedRegion, tilesetUrl, leaves, ct).ConfigureAwait(false);
    }

    private async Task WalkTileAsync(
        TilesetTile tile,
        Matrix4x4d parentTransform,
        TilesetRegion? inheritedRegion,
        Uri baseUrl,
        List<TilesetLeaf> leaves,
        CancellationToken ct)
    {
        Matrix4x4d local = tile.Transform is null ? Matrix4x4d.Identity : Matrix4x4d.FromColumnMajor(tile.Transform);
        Matrix4x4d world = Matrix4x4d.Multiply(parentTransform, local);
        TilesetRegion? tileRegion = TilesetRegion.TryCreate(tile.BoundingVolume?.Region, out TilesetRegion parsedRegion)
            ? parsedRegion
            : inheritedRegion;

        string? uri = tile.Content?.ResolvedUri;
        if (!string.IsNullOrEmpty(uri))
        {
            Uri resolved = new Uri(baseUrl, uri);
            if (LooksLikeTileset(resolved))
            {
                await WalkTilesetAsync(resolved, world, tileRegion, leaves, ct).ConfigureAwait(false);
            }
            else if (LooksLikeB3dm(resolved))
            {
                leaves.Add(new TilesetLeaf(resolved, world, tileRegion));
            }
        }

        if (tile.Children is not null)
        {
            foreach (TilesetTile child in tile.Children)
            {
                await WalkTileAsync(child, world, tileRegion, baseUrl, leaves, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool LooksLikeTileset(Uri uri) => uri.AbsoluteUri.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeB3dm(Uri uri) => uri.AbsoluteUri.EndsWith(".b3dm", StringComparison.OrdinalIgnoreCase);
}
