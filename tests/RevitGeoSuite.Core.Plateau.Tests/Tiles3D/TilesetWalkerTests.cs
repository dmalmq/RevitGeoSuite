using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class TilesetWalkerTests
{
    [Fact]
    public async Task WalkAsync_yields_b3dm_leaves_with_accumulated_transform()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/area/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,100,200,300,1],\"children\":[" +
            "{\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,5,0,0,1],\"content\":{\"uri\":\"sub/sub.json\"}}," +
            "{\"content\":{\"uri\":\"leaf.b3dm\"}}" +
            "]}}";
        fake.StringResponses[new Uri("https://example.com/area/sub/sub.json")] =
            "{\"root\":{\"content\":{\"uri\":\"a.b3dm\"}}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        Assert.Equal(2, leaves.Count);
        TilesetLeaf rootLeaf = leaves.Single(l => l.B3dmUrl.AbsoluteUri.EndsWith("leaf.b3dm", StringComparison.Ordinal));
        Vector3d transformed = rootLeaf.Transform.TransformPoint(Vector3d.Zero);
        Assert.Equal(100, transformed.X);
        Assert.Equal(200, transformed.Y);
        Assert.Equal(300, transformed.Z);

        TilesetLeaf subLeaf = leaves.Single(l => l.B3dmUrl.AbsoluteUri.EndsWith("a.b3dm", StringComparison.Ordinal));
        Vector3d subTransformed = subLeaf.Transform.TransformPoint(Vector3d.Zero);
        Assert.Equal(105, subTransformed.X);
        Assert.Equal(200, subTransformed.Y);
        Assert.Equal(300, subTransformed.Z);
    }

    [Fact]
    public async Task WalkAsync_carries_leaf_bounding_regions_and_stable_ids()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/area/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"children\":[" +
            "{\"boundingVolume\":{\"region\":[2.438461,0.622908,2.438636,0.623083,0,100]},\"content\":{\"uri\":\"a.b3dm\"}}," +
            "{\"boundingVolume\":{\"region\":[2.439461,0.623908,2.439636,0.624083,0,100]},\"content\":{\"uri\":\"b.b3dm\"}}" +
            "]}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        Assert.Equal(2, leaves.Count);
        TilesetLeaf first = leaves.Single(leaf => leaf.B3dmUrl.AbsoluteUri.EndsWith("a.b3dm", StringComparison.Ordinal));
        TilesetLeaf second = leaves.Single(leaf => leaf.B3dmUrl.AbsoluteUri.EndsWith("b.b3dm", StringComparison.Ordinal));
        Assert.True(first.BoundingRegion.HasValue);
        Assert.Equal(2.438461, first.BoundingRegion!.Value.WestRadians, 6);
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.NotEqual(first.Id, second.Id);
    }

    private sealed class FakeHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, string> StringResponses { get; } = new();

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            if (StringResponses.TryGetValue(url, out string? body)) return Task.FromResult(body);
            throw new InvalidOperationException($"No response for {url}");
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
