using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class TilesetJsonWriterTests
{
    [Fact]
    public void Build_json_includes_transform_and_content_uri()
    {
        Tiles3DExportPackage package = CreatePackage();
        TilesetJsonWriter writer = new TilesetJsonWriter();

        JObject document = JObject.Parse(writer.BuildJson(package));

        Assert.Equal("1.1", (string?)document["asset"]?["version"]);
        Assert.Equal("content.glb", (string?)document["root"]?["content"]?["uri"]);
        Assert.Equal(16, ((JArray?)document["root"]?["transform"])?.Count);
        Assert.Equal(12, ((JArray?)document["root"]?["boundingVolume"]?["box"])?.Count);
    }

    private static Tiles3DExportPackage CreatePackage()
    {
        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                Title = "Canonical Origin",
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
                AnchorLatitude = 36d,
                AnchorLongitude = 139.833333333333d,
                AnchorElevationMeters = 0d
            },
            LevelOfDetail = Tiles3DLevelOfDetail.Medium,
            Meshes = new List<Tiles3DMeshPrimitive>
            {
                new Tiles3DMeshPrimitive
                {
                    Name = "Box",
                    Triangles = new List<Tiles3DTriangle>
                    {
                        new Tiles3DTriangle(new Tiles3DPoint(0d, 0d, 0d), new Tiles3DPoint(1d, 0d, 0d), new Tiles3DPoint(0d, 1d, 0d))
                    }
                }
            },
            ElementCount = 1,
            TriangleCount = 1,
            GeometricError = 0.5d,
            BoundingBox = new[] { 0.5d, 0.5d, 0d, 0.5d, 0d, 0d, 0d, 0.5d, 0d, 0d, 0d, 0.01d }
        };
    }
}
