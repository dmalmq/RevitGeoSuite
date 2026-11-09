using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExactPlacementTests
{
    /// <summary>
    /// The conformance fixture Kanizu tests against too, made with PROJ: every vertex must land
    /// within 1 cm of PROJ's east-north-up position.
    /// </summary>
    [Fact]
    public void Vertices_match_the_shared_conformance_fixture()
    {
        JObject fixture = JObject.Parse(File.ReadAllText(FixturePath()));
        JObject anchor = (JObject)fixture["anchor"]!;
        JArray vertices = (JArray)fixture["vertices"]!;
        List<Tiles3DPoint> offsets = vertices
            .Select(v => v["gridOffset"]!.Values<double>().ToArray())
            .Select(o => new Tiles3DPoint(o[0], o[1], o[2]))
            .ToList();
        Tiles3DExportPackage package = Package(
            (int)fixture["projectEpsg"]!,
            (double)anchor["easting"]!,
            (double)anchor["northing"]!,
            (double)anchor["latitude"]!,
            (double)anchor["longitude"]!,
            (double)anchor["ellipsoidalHeight"]!,
            offsets);

        new Tiles3DExactPlacement(new CoordinateTransformer(new CrsRegistry())).Place(package);

        List<Tiles3DPoint> placed = package.Meshes.SelectMany(m => m.Triangles).Select(t => t.A).ToList();
        for (int i = 0; i < vertices.Count; i++)
        {
            double[] expected = vertices[i]["enu"]!.Values<double>().ToArray();
            Assert.True(Math.Abs(placed[i].X - expected[0]) < 0.01, $"vertex {i} east {placed[i].X} vs {expected[0]}");
            Assert.True(Math.Abs(placed[i].Y - expected[1]) < 0.01, $"vertex {i} north {placed[i].Y} vs {expected[1]}");
            Assert.True(Math.Abs(placed[i].Z - expected[2]) < 0.01, $"vertex {i} up {placed[i].Z} vs {expected[2]}");
        }

        Assert.True(package.UsedExactPlacement);
    }

    [Fact]
    public void Grid_north_turns_into_true_north_at_tokyo_station()
    {
        // 300 m grid north of the anchor lies about 0.2 m west of true north there.
        Tiles3DExportPackage package = Package(6677, -5995.185, -35367.23, 35.6812, 139.7671, 40d,
            new List<Tiles3DPoint> { new Tiles3DPoint(0d, 300d, 0d) });

        new Tiles3DExactPlacement(new CoordinateTransformer(new CrsRegistry())).Place(package);

        Tiles3DPoint placed = package.Meshes[0].Triangles[0].A;
        Assert.InRange(placed.X, -0.25d, -0.15d);
        Assert.InRange(placed.Y, 300.0d, 300.06d);
    }

    [Fact]
    public void The_element_index_refuses_vertices_that_already_moved()
    {
        Tiles3DExportPackage package = Package(6677, -5995.185, -35367.23, 35.6812, 139.7671, 40d,
            new List<Tiles3DPoint> { new Tiles3DPoint(1d, 2d, 3d) });
        package.ElementIndex = new Tiles3DElementIndexWriter().Build(package);

        new Tiles3DExactPlacement(new CoordinateTransformer(new CrsRegistry())).Place(package);

        Assert.Throws<InvalidOperationException>(() => new Tiles3DElementIndexWriter().Build(package));
        JObject written = JObject.Parse(new Tiles3DElementIndexWriter().BuildJson(package));
        Assert.Equal(-5994.185d, (double)written["elements"]![0]!["min"]![0]!, 6);
    }

    private static Tiles3DExportPackage Package(
        int epsg,
        double easting,
        double northing,
        double latitude,
        double longitude,
        double ellipsoidalHeight,
        List<Tiles3DPoint> points)
    {
        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                ProjectCrs = new CrsReference { EpsgCode = epsg },
                AnchorProjectedCoordinate = new ProjectedCoordinate(easting, northing),
                AnchorLatitude = latitude,
                AnchorLongitude = longitude,
                AnchorElevationMeters = ellipsoidalHeight,
            },
            Meshes = points.Select((p, i) => new Tiles3DMeshPrimitive
            {
                Metadata = new Tiles3DObjectMetadata { RevitUniqueId = $"e{i}" },
                Triangles = new List<Tiles3DTriangle> { new Tiles3DTriangle(p, p, p) },
            }).ToList(),
        };
    }

    private static string FixturePath([CallerFilePath] string sourceFile = "")
    {
        return Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "Fixtures",
            "Conformance",
            "placement-tokyo-station.json");
    }
}
