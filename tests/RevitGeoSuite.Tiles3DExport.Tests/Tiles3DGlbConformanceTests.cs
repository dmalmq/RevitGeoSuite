using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// The GLB metadata Kanizu reads, pinned as a conformance fixture: Kanizu's kanizu-3d tests read a
/// copy of Fixtures/Conformance/revit-metadata.glb. When the writer's output changes on purpose,
/// run with REVITGEOSUITE_WRITE_FIXTURES=1 to rewrite the fixture and copy it to Kanizu.
/// </summary>
public sealed class Tiles3DGlbConformanceTests
{
    [Fact]
    public void Glb_metadata_matches_the_conformance_fixture()
    {
        Tiles3DMeshPrimitive Box(Tiles3DObjectMetadata metadata, double x, Tiles3DMaterialColor color)
        {
            Tiles3DPoint P(double dx, double dy, double dz) => new Tiles3DPoint(x + dx, dy, dz);
            return new Tiles3DMeshPrimitive
            {
                Metadata = metadata,
                Triangles = new List<Tiles3DTriangle>
                {
                    new Tiles3DTriangle(P(0, 0, 0), P(1, 0, 0), P(0, 1, 0), color),
                    new Tiles3DTriangle(P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), color),
                },
            };
        }
        var package = new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                Title = "Fixture",
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                AnchorProjectedCoordinate = new ProjectedCoordinate(-5995.185, -35367.23),
                AnchorElevationMeters = 40.0,
            },
            Meshes = new List<Tiles3DMeshPrimitive>
            {
                Box(new Tiles3DObjectMetadata
                {
                    RevitElementId = "101", RevitUniqueId = "wall-uid", Name = "基本壁 RC200",
                    Category = "Walls", FamilyName = "Basic Wall", TypeName = "RC200",
                    LevelName = "1FL", LevelKey = "1fl", LevelUniqueId = "lv-1",
                    LevelElevationMeters = 0, HeightMeters = 3.5, MinZMeters = 0, MaxZMeters = 3.5,
                    SourceDocument = "Tokyo.rvt",
                }, 0, new Tiles3DMaterialColor(200, 200, 200, 255)),
                Box(new Tiles3DObjectMetadata
                {
                    RevitElementId = "7", RevitUniqueId = "door-uid", Name = "Door",
                    Category = "Doors", FamilyName = "Single", TypeName = "900x2100",
                    LevelName = "B1FL", LevelKey = "b1fl", LevelUniqueId = "lv-b1",
                    LevelElevationMeters = -7.5, HeightMeters = 2.1, MinZMeters = -7.5, MaxZMeters = -5.4,
                    SourceDocument = "Platform.rvt", SourceLinkName = "Platform.rvt",
                    SourceLinkInstanceUniqueId = "link-instance-2",
                }, 2, new Tiles3DMaterialColor(200, 200, 200, 255)),
            },
        };
        string written = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"), "revit-metadata.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(written)!);
        new Tiles3DGlbWriter().Write(written, package);

        string fixture = FixturePath();
        if (Environment.GetEnvironmentVariable("REVITGEOSUITE_WRITE_FIXTURES") == "1")
        {
            File.Copy(written, fixture, overwrite: true);
        }

        Assert.Equal(File.ReadAllBytes(fixture), File.ReadAllBytes(written));
    }

    private static string FixturePath([CallerFilePath] string sourceFile = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Fixtures", "Conformance", "revit-metadata.glb");
    }
}
