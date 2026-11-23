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

    [Fact]
    public async Task WalkAsync_with_REPLACE_refinement_emits_only_children_not_parent()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/chiyoda/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"content\":{\"uri\":\"data58.b3dm\"},\"children\":[" +
            "{\"content\":{\"uri\":\"data58_c1.b3dm\"}}," +
            "{\"content\":{\"uri\":\"data58_c2.b3dm\"}}," +
            "{\"content\":{\"uri\":\"data58_c3.b3dm\"}}" +
            "]}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        Assert.Equal(3, leaves.Count);
        Assert.DoesNotContain(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("data58.b3dm", StringComparison.Ordinal));
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("data58_c1.b3dm", StringComparison.Ordinal));
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("data58_c2.b3dm", StringComparison.Ordinal));
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("data58_c3.b3dm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WalkAsync_inherits_REPLACE_across_nested_levels_and_emits_only_deepest_leaves()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/chiyoda/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"children\":[{" +
            "\"content\":{\"uri\":\"lod1_parent.b3dm\"}," +
            "\"children\":[{\"content\":{\"uri\":\"lod2_child.b3dm\"}}]" +
            "}]}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        TilesetLeaf leaf = Assert.Single(leaves);
        Assert.EndsWith("lod2_child.b3dm", leaf.B3dmUrl.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WalkAsync_with_ADD_refinement_emits_both_parent_and_children()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/additive/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"ADD\",\"content\":{\"uri\":\"parent.b3dm\"},\"children\":[" +
            "{\"content\":{\"uri\":\"child1.b3dm\"}}," +
            "{\"content\":{\"uri\":\"child2.b3dm\"}}" +
            "]}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        Assert.Equal(3, leaves.Count);
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("parent.b3dm", StringComparison.Ordinal));
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("child1.b3dm", StringComparison.Ordinal));
        Assert.Contains(leaves, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("child2.b3dm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WalkAsync_terminates_cleanly_on_cyclic_tileset_references()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri urlA = new Uri("https://example.com/cycle/a.json");
        Uri urlB = new Uri("https://example.com/cycle/b.json");
        fake.StringResponses[urlA] =
            "{\"root\":{\"refine\":\"ADD\",\"children\":[{\"content\":{\"uri\":\"b.json\"}}]}}";
        fake.StringResponses[urlB] =
            "{\"root\":{\"refine\":\"ADD\",\"children\":[{\"content\":{\"uri\":\"a.json\"}},{\"content\":{\"uri\":\"leaf.b3dm\"}}]}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(urlA, CancellationToken.None);

        TilesetLeaf leaf = Assert.Single(leaves);
        Assert.EndsWith("leaf.b3dm", leaf.B3dmUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(2, fake.RequestedUrls.Count);
    }

    [Fact]
    public async Task WalkAsync_allows_shared_sub_tileset_referenced_under_distinct_transforms()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/dag/tileset.json");
        Uri sharedUrl = new Uri("https://example.com/dag/shared.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"children\":[" +
            "{\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,10,0,0,1],\"content\":{\"uri\":\"shared.json\"}}," +
            "{\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,20,0,0,1],\"content\":{\"uri\":\"shared.json\"}}" +
            "]}}";
        fake.StringResponses[sharedUrl] =
            "{\"root\":{\"content\":{\"uri\":\"unit.b3dm\"}}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(rootUrl, CancellationToken.None);

        Assert.Equal(2, leaves.Count);
        double[] xs = leaves.Select(l => l.Transform.TransformPoint(Vector3d.Zero).X).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 10.0, 20.0 }, xs);
    }

    [Fact]
    public async Task WalkAsync_enforces_max_depth_and_max_leaves_caps()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/caps/tileset.json");
        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"ADD\",\"children\":[" +
            "{\"content\":{\"uri\":\"a.b3dm\"}}," +
            "{\"content\":{\"uri\":\"b.b3dm\"}}," +
            "{\"content\":{\"uri\":\"c.b3dm\"}}," +
            "{\"children\":[{\"children\":[{\"content\":{\"uri\":\"deep.b3dm\"}}]}]}" +
            "]}}";

        TilesetWalker leafCappedWalker = new TilesetWalker(fake, maxDepth: 64, maxLeaves: 2);
        IReadOnlyList<TilesetLeaf> capped = await leafCappedWalker.WalkAsync(rootUrl, CancellationToken.None);
        Assert.Equal(2, capped.Count);

        TilesetWalker depthCappedWalker = new TilesetWalker(fake, maxDepth: 1, maxLeaves: 100);
        IReadOnlyList<TilesetLeaf> shallow = await depthCappedWalker.WalkAsync(rootUrl, CancellationToken.None);
        Assert.Equal(3, shallow.Count);
        Assert.DoesNotContain(shallow, leaf => leaf.B3dmUrl.AbsolutePath.EndsWith("deep.b3dm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WalkAsync_matches_query_string_uris_and_prunes_non_intersecting_subtrees()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri rootUrl = new Uri("https://example.com/area/tileset.json?v=1");
        Uri keptSubUrl = new Uri("https://example.com/area/kept/sub.json?v=2");
        Uri prunedSubUrl = new Uri("https://example.com/area/pruned/sub.json?v=2");

        fake.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"children\":[" +
            "{\"boundingVolume\":{\"region\":[2.438,0.622,2.439,0.623,0,100]},\"content\":{\"uri\":\"kept/sub.json?v=2\"}}," +
            "{\"boundingVolume\":{\"region\":[2.450,0.622,2.451,0.623,0,100]},\"content\":{\"uri\":\"pruned/sub.json?v=2\"}}" +
            "]}}";
        fake.StringResponses[keptSubUrl] =
            "{\"root\":{\"content\":{\"uri\":\"leaf.b3dm?token=abc\"}}}";
        fake.StringResponses[prunedSubUrl] =
            "{\"root\":{\"content\":{\"uri\":\"should_not_fetch.b3dm\"}}}";

        TilesetWalker walker = new TilesetWalker(fake);
        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(
            rootUrl,
            region => region.EastRadians > 2.4385 && region.WestRadians < 2.4388,
            CancellationToken.None);

        TilesetLeaf leaf = Assert.Single(leaves);
        Assert.EndsWith("leaf.b3dm", leaf.B3dmUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(keptSubUrl, fake.RequestedUrls);
        Assert.DoesNotContain(prunedSubUrl, fake.RequestedUrls);
    }

    private sealed class FakeHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, string> StringResponses { get; } = new();

        public List<Uri> RequestedUrls { get; } = new();

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(url);
            if (StringResponses.TryGetValue(url, out string? body)) return Task.FromResult(body);
            throw new InvalidOperationException($"No response for {url}");
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
