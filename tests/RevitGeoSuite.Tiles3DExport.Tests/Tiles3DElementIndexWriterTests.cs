using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DElementIndexWriterTests
{
    [Fact]
    public void Lists_one_entry_per_element_with_a_grid_frame_fingerprint()
    {
        Tiles3DExportPackage package = Package(1000d, 2000d, 40d, 36.7d, Cube("wall-1", 0d, 0d, 0d));

        JObject document = JObject.Parse(new Tiles3DElementIndexWriter().BuildJson(package));

        Assert.Equal(1, (int?)document["version"]);
        Assert.Equal("projected-grid-orthometric", (string?)document["frame"]);
        JObject entry = Assert.IsType<JObject>(Assert.Single(document["elements"]!));
        Assert.Equal("wall-1", (string?)entry["uniqueId"]);
        Assert.Equal("level-1f", (string?)entry["levelUniqueId"]);
        Assert.Equal(12, (int?)entry["triangleCount"]);
        Assert.Equal(new[] { 1000d, 2000d, 3.3d }, entry["min"]!.Values<double>());
        Assert.Equal(new[] { 1001d, 2001d, 4.3d }, entry["max"]!.Values<double>());
        Assert.Equal(1d, (double?)entry["volumeCubicMeters"]);
        Assert.Equal(64, ((string?)entry["hash"])!.Length);
    }

    [Fact]
    public void Hash_ignores_the_rebased_frame_triangle_order_and_small_noise()
    {
        Tiles3DExportPackage original = Package(1000d, 2000d, 40d, 36.7d, Cube("wall-1", 0d, 0d, 0d));
        // The same cube after a centroid rebase: the anchor moved by (5, 6, 7) and the vertices by
        // the opposite amount, with triangles reversed, rotated and nudged by 0.1 mm.
        Tiles3DMeshPrimitive moved = Cube("wall-1", -5d, -6d, -7d);
        moved.Triangles = moved.Triangles
            .Select(t => new Tiles3DTriangle(Nudge(t.B), Nudge(t.C), Nudge(t.A)))
            .Reverse()
            .ToList();
        Tiles3DExportPackage rebased = Package(1005d, 2006d, 47d, 36.7d, moved);

        Assert.Equal(Hash(original), Hash(rebased));
    }

    [Fact]
    public void Hash_changes_when_the_element_moves_a_millimetre_or_more()
    {
        string before = Hash(Package(1000d, 2000d, 40d, 36.7d, Cube("wall-1", 0d, 0d, 0d)));
        string after = Hash(Package(1000d, 2000d, 40d, 36.7d, Cube("wall-1", 0.002d, 0d, 0d)));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Two_instances_of_one_link_stay_two_entries()
    {
        Tiles3DMeshPrimitive west = Cube("platform-wall", 0d, 0d, 0d);
        Tiles3DMeshPrimitive east = Cube("platform-wall", 30d, 0d, 0d);
        foreach ((Tiles3DMeshPrimitive mesh, string instance) in new[] { (west, "link-west"), (east, "link-east") })
        {
            mesh.Metadata.SourceDocument = "Platform";
            mesh.Metadata.SourceLinkName = "Platform.rvt";
            mesh.Metadata.SourceLinkInstanceUniqueId = instance;
        }

        JObject document = JObject.Parse(new Tiles3DElementIndexWriter().BuildJson(Package(1000d, 2000d, 40d, 36.7d, west, east)));

        JArray elements = Assert.IsType<JArray>(document["elements"]);
        Assert.Equal(2, elements.Count);
        Assert.Equal(new[] { "link-east", "link-west" }, elements.Select(e => (string?)e["sourceLinkInstanceUniqueId"]));
        Assert.All(elements, e => Assert.Equal(12, (int?)e["triangleCount"]));
        Assert.NotEqual((string?)elements[0]["hash"], (string?)elements[1]["hash"]);
    }

    private static string Hash(Tiles3DExportPackage package)
    {
        JObject document = JObject.Parse(new Tiles3DElementIndexWriter().BuildJson(package));
        return (string)document["elements"]![0]!["hash"]!;
    }

    private static Tiles3DPoint Nudge(Tiles3DPoint point)
    {
        return new Tiles3DPoint(point.X + 0.0001d, point.Y - 0.0001d, point.Z + 0.0001d);
    }

    private static Tiles3DExportPackage Package(double easting, double northing, double ellipsoidalAnchor, double geoid, params Tiles3DMeshPrimitive[] meshes)
    {
        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                AnchorProjectedCoordinate = new ProjectedCoordinate(easting, northing),
                AnchorElevationMeters = ellipsoidalAnchor,
            },
            GeoidHeightOffsetMeters = geoid,
            Meshes = meshes.ToList(),
        };
    }

    private static Tiles3DMeshPrimitive Cube(string uniqueId, double x, double y, double z)
    {
        Tiles3DPoint P(double dx, double dy, double dz) => new Tiles3DPoint(x + dx, y + dy, z + dz);
        Tiles3DPoint[] v =
        {
            P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0),
            P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1),
        };
        int[][] faces =
        {
            new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
            new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 },
        };
        return new Tiles3DMeshPrimitive
        {
            Metadata = new Tiles3DObjectMetadata
            {
                RevitUniqueId = uniqueId,
                RevitElementId = "1",
                LevelUniqueId = "level-1f",
                LevelKey = "1f",
                Category = "Walls",
                SourceDocument = "Host",
            },
            Triangles = faces.Select(f => new Tiles3DTriangle(v[f[0]], v[f[1]], v[f[2]])).ToList(),
        };
    }
}
