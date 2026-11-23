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
    public const int DefaultMaxDepth = 64;
    public const int DefaultMaxLeaves = 10_000;

    private readonly IPlateauHttpClient httpClient;
    private readonly int maxDepth;
    private readonly int maxLeaves;

    public TilesetWalker(IPlateauHttpClient httpClient)
        : this(httpClient, DefaultMaxDepth, DefaultMaxLeaves)
    {
    }

    public TilesetWalker(IPlateauHttpClient httpClient, int maxDepth, int maxLeaves)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "Max depth must be at least 1.");
        if (maxLeaves < 1) throw new ArgumentOutOfRangeException(nameof(maxLeaves), maxLeaves, "Max leaves must be at least 1.");
        this.maxDepth = maxDepth;
        this.maxLeaves = maxLeaves;
    }

    public Task<IReadOnlyList<TilesetLeaf>> WalkAsync(Uri tilesetUrl, CancellationToken cancellationToken)
    {
        return WalkAsync(tilesetUrl, regionFilter: null, cancellationToken);
    }

    public async Task<IReadOnlyList<TilesetLeaf>> WalkAsync(
        Uri tilesetUrl,
        Func<TilesetRegion, bool>? regionFilter,
        CancellationToken cancellationToken)
    {
        if (tilesetUrl is null) throw new ArgumentNullException(nameof(tilesetUrl));

        WalkContext context = new WalkContext(regionFilter, maxDepth, maxLeaves);
        await WalkTilesetAsync(
            tilesetUrl,
            Matrix4x4d.Identity,
            inheritedRegion: null,
            inheritedRefine: TileRefine.Replace,
            depth: 0,
            context,
            cancellationToken).ConfigureAwait(false);
        return context.Leaves;
    }

    private async Task<WalkResult> WalkTilesetAsync(
        Uri tilesetUrl,
        Matrix4x4d parentTransform,
        TilesetRegion? inheritedRegion,
        TileRefine inheritedRefine,
        int depth,
        WalkContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > context.MaxDepth || context.Leaves.Count >= context.MaxLeaves)
        {
            return WalkResult.Empty;
        }

        // Track the active ancestor stack so cyclic/self-referential tileset graphs terminate
        // cleanly while a shared sub-tileset referenced twice under different transforms is still walked.
        string tilesetKey = tilesetUrl.AbsoluteUri;
        if (!context.ActiveTilesets.Add(tilesetKey))
        {
            return WalkResult.Empty;
        }

        try
        {
            string body = await httpClient.GetStringAsync(tilesetUrl, ct).ConfigureAwait(false);
            TilesetJson? tileset = JsonConvert.DeserializeObject<TilesetJson>(body);
            if (tileset?.Root is null)
            {
                return WalkResult.Empty;
            }

            return await WalkTileAsync(
                tileset.Root,
                parentTransform,
                inheritedRegion,
                inheritedRefine,
                tilesetUrl,
                depth,
                context,
                ct).ConfigureAwait(false);
        }
        finally
        {
            context.ActiveTilesets.Remove(tilesetKey);
        }
    }

    private async Task<WalkResult> WalkTileAsync(
        TilesetTile tile,
        Matrix4x4d parentTransform,
        TilesetRegion? inheritedRegion,
        TileRefine inheritedRefine,
        Uri baseUrl,
        int depth,
        WalkContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > context.MaxDepth || context.Leaves.Count >= context.MaxLeaves)
        {
            return WalkResult.Empty;
        }

        Matrix4x4d local = tile.Transform is null ? Matrix4x4d.Identity : Matrix4x4d.FromColumnMajor(tile.Transform);
        Matrix4x4d world = Matrix4x4d.Multiply(parentTransform, local);

        bool hasOwnRegion = TilesetRegion.TryCreate(tile.BoundingVolume?.Region, out TilesetRegion parsedRegion);
        if (hasOwnRegion && context.RegionFilter is not null && !context.RegionFilter(parsedRegion))
        {
            return new WalkResult(emittedLeaves: false, prunedByRegion: true);
        }

        TilesetRegion? tileRegion = hasOwnRegion ? parsedRegion : inheritedRegion;
        TileRefine effectiveRefine = ParseRefine(tile.Refine, inheritedRefine);

        Uri? contentUri = null;
        string? rawUri = tile.Content?.ResolvedUri;
        if (!string.IsNullOrEmpty(rawUri) && Uri.TryCreate(baseUrl, rawUri, out Uri? parsedUri))
        {
            contentUri = parsedUri;
        }

        if (effectiveRefine == TileRefine.Replace)
        {
            bool childEmitted = false;
            bool childPruned = false;
            if (tile.Children is not null && tile.Children.Count > 0)
            {
                foreach (TilesetTile child in tile.Children)
                {
                    if (context.Leaves.Count >= context.MaxLeaves)
                    {
                        break;
                    }

                    WalkResult childResult = await WalkTileAsync(
                        child,
                        world,
                        tileRegion,
                        effectiveRefine,
                        baseUrl,
                        depth + 1,
                        context,
                        ct).ConfigureAwait(false);
                    childEmitted |= childResult.EmittedLeaves;
                    childPruned |= childResult.PrunedByRegion;
                }
            }

            // Under REPLACE semantics, qualifying children supersede the parent tile's own content
            // so coarse parent LOD geometry is never emitted alongside its finer child LODs.
            if (childEmitted || childPruned)
            {
                return new WalkResult(childEmitted, childPruned);
            }

            return await WalkContentAsync(contentUri, world, tileRegion, effectiveRefine, depth, context, ct).ConfigureAwait(false);
        }

        // Under ADD semantics, both the parent's content and its children contribute geometry.
        WalkResult contentResult = await WalkContentAsync(contentUri, world, tileRegion, effectiveRefine, depth, context, ct).ConfigureAwait(false);
        bool emitted = contentResult.EmittedLeaves;
        bool pruned = contentResult.PrunedByRegion;

        if (tile.Children is not null && tile.Children.Count > 0)
        {
            foreach (TilesetTile child in tile.Children)
            {
                if (context.Leaves.Count >= context.MaxLeaves)
                {
                    break;
                }

                WalkResult childResult = await WalkTileAsync(
                    child,
                    world,
                    tileRegion,
                    effectiveRefine,
                    baseUrl,
                    depth + 1,
                    context,
                    ct).ConfigureAwait(false);
                emitted |= childResult.EmittedLeaves;
                pruned |= childResult.PrunedByRegion;
            }
        }

        return new WalkResult(emitted, pruned);
    }

    private async Task<WalkResult> WalkContentAsync(
        Uri? contentUri,
        Matrix4x4d world,
        TilesetRegion? tileRegion,
        TileRefine effectiveRefine,
        int depth,
        WalkContext context,
        CancellationToken ct)
    {
        if (contentUri is null || context.Leaves.Count >= context.MaxLeaves)
        {
            return WalkResult.Empty;
        }

        if (LooksLikeTileset(contentUri))
        {
            return await WalkTilesetAsync(
                contentUri,
                world,
                tileRegion,
                effectiveRefine,
                depth + 1,
                context,
                ct).ConfigureAwait(false);
        }

        if (LooksLikeB3dm(contentUri))
        {
            context.Leaves.Add(new TilesetLeaf(contentUri, world, tileRegion));
            return new WalkResult(emittedLeaves: true, prunedByRegion: false);
        }

        return WalkResult.Empty;
    }

    private static TileRefine ParseRefine(string? refine, TileRefine inherited)
    {
        if (string.IsNullOrWhiteSpace(refine))
        {
            return inherited;
        }

        string normalized = refine!.Trim();
        if (string.Equals(normalized, "ADD", StringComparison.OrdinalIgnoreCase))
        {
            return TileRefine.Add;
        }

        if (string.Equals(normalized, "REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            return TileRefine.Replace;
        }

        return inherited;
    }

    private static bool LooksLikeTileset(Uri uri) => uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeB3dm(Uri uri) => uri.AbsolutePath.EndsWith(".b3dm", StringComparison.OrdinalIgnoreCase);

    private enum TileRefine
    {
        Replace = 0,
        Add = 1
    }

    private readonly struct WalkResult
    {
        public static WalkResult Empty => default;

        public WalkResult(bool emittedLeaves, bool prunedByRegion)
        {
            EmittedLeaves = emittedLeaves;
            PrunedByRegion = prunedByRegion;
        }

        public bool EmittedLeaves { get; }

        public bool PrunedByRegion { get; }
    }

    private sealed class WalkContext
    {
        public WalkContext(Func<TilesetRegion, bool>? regionFilter, int maxDepth, int maxLeaves)
        {
            RegionFilter = regionFilter;
            MaxDepth = maxDepth;
            MaxLeaves = maxLeaves;
        }

        public Func<TilesetRegion, bool>? RegionFilter { get; }

        public int MaxDepth { get; }

        public int MaxLeaves { get; }

        public HashSet<string> ActiveTilesets { get; } = new HashSet<string>(StringComparer.Ordinal);

        public List<TilesetLeaf> Leaves { get; } = new List<TilesetLeaf>();
    }
}
