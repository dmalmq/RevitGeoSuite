using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DPackageWriterTests
{
    [Fact]
    public void Write_creates_tileset_content_and_level_manifest()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Tiles3DExportPackage package = CreatePackage();

        (string tilesetPath, string contentPath) = new Tiles3DPackageWriter().Write(outputDirectory, package);

        Assert.True(File.Exists(tilesetPath));
        Assert.True(File.Exists(contentPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "levels.json")));
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
            Meshes = new List<Tiles3DMeshPrimitive>
            {
                new Tiles3DMeshPrimitive
                {
                    Metadata = new Tiles3DObjectMetadata
                    {
                        Name = "Triangle",
                        LevelName = "Ground Floor",
                        LevelKey = "ground_floor"
                    },
                    Triangles = new List<Tiles3DTriangle>
                    {
                        new Tiles3DTriangle(
                            new Tiles3DPoint(0d, 0d, 0d),
                            new Tiles3DPoint(1d, 0d, 0d),
                            new Tiles3DPoint(0d, 1d, 0d))
                    }
                }
            },
            ElementCount = 1,
            TriangleCount = 1,
            GeometricError = 1d,
            BoundingBox = new[] { 0.5d, 0.5d, 0d, 0.5d, 0d, 0d, 0d, 0.5d, 0d, 0d, 0d, 0.01d }
        };
    }
}
