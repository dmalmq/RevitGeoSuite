using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DGlbWriterTests
{
    [Fact]
    public void Write_creates_valid_glb_header_and_embeds_mesh_json()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string outputPath = Path.Combine(tempDirectory, "content.glb");
        Tiles3DExportPackage package = CreatePackage();

        new Tiles3DGlbWriter().Write(outputPath, package);

        byte[] bytes = File.ReadAllBytes(outputPath);
        uint magic = BitConverter.ToUInt32(bytes, 0);
        uint version = BitConverter.ToUInt32(bytes, 4);
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        string json = Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0');

        Assert.Equal(0x46546C67u, magic);
        Assert.Equal(2u, version);
        Assert.Contains("\"meshes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"materials\"", json, StringComparison.Ordinal);
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
                    Name = "Triangle",
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
