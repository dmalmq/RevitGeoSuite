using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// <c>rooms.json</c>, pinned as a conformance fixture: Kanizu's revit tests read a copy of
/// Fixtures/Conformance/revit-rooms.json. When the writer's output changes on purpose, run with
/// REVITGEOSUITE_WRITE_FIXTURES=1 to rewrite the fixture and copy it to Kanizu.
/// </summary>
public sealed class Tiles3DRoomIndexWriterTests
{
    private const double Easting = -5995.185;
    private const double Northing = -35367.23;

    [Fact]
    public void Rooms_json_matches_the_conformance_fixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var package = new Tiles3DExportPackage { Rooms = FixtureRooms() };

        string? written = new Tiles3DRoomIndexWriter().Write(directory, package);

        Assert.NotNull(written);
        Assert.Equal("rooms.json", Path.GetFileName(written));
        string fixture = FixturePath();
        if (Environment.GetEnvironmentVariable("REVITGEOSUITE_WRITE_FIXTURES") == "1")
        {
            File.Copy(written!, fixture, overwrite: true);
        }

        Assert.Equal(File.ReadAllText(fixture, Encoding.UTF8), File.ReadAllText(written!, Encoding.UTF8));
    }

    [Fact]
    public void Rooms_are_rounded_closed_and_sorted_and_degenerate_ones_dropped()
    {
        JObject index = JObject.Parse(new Tiles3DRoomIndexWriter().BuildJson(FixtureRooms()));

        Assert.Equal(1, (int)index["version"]!);
        Assert.Equal("projected-grid-orthometric", (string?)index["frame"]);
        JArray rooms = (JArray)index["rooms"]!;
        Assert.Equal(new[] { "Platform.rvt", "Tokyo.rvt" }, rooms.Select(room => (string)room["sourceDocument"]!).ToArray());

        JArray concourse = (JArray)rooms[1]["footprint"]![0]!;
        // The repeated point is gone and the ring is closed: 4 corners + the first again.
        Assert.Equal(5, concourse.Count);
        Assert.Equal(-6000.0, (double)concourse[0][0]!, 6);
        Assert.Equal(concourse[0].ToString(), concourse[4].ToString());

        JArray platform = (JArray)rooms[0]["footprint"]!;
        Assert.Equal(2, platform.Count);
        Assert.Equal("link-instance-2", (string?)rooms[0]["sourceLinkInstanceUniqueId"]);
        Assert.Equal(32.5, (double)rooms[0]["elevation"]!, 6);
    }

    [Fact]
    public void The_hash_follows_the_footprint_not_the_name_or_the_ring_start()
    {
        Tiles3DRoomFootprint original = Room("a", "Concourse", Rectangle(0, 0, 10, 5));
        Tiles3DRoomFootprint renamed = Room("a", "Renamed", Rectangle(0, 0, 10, 5));
        List<double[]> rotated = Rectangle(0, 0, 10, 5);
        rotated.Add(rotated[0]);
        rotated.RemoveAt(0);
        Tiles3DRoomFootprint restarted = Room("a", "Concourse", rotated);
        Tiles3DRoomFootprint jittered = Room("a", "Concourse", Rectangle(0.0003, 0, 10, 5));
        Tiles3DRoomFootprint moved = Room("a", "Concourse", Rectangle(0.002, 0, 10, 5));

        string Hash(Tiles3DRoomFootprint room) => new Tiles3DRoomIndexWriter().Build(new[] { room }).Rooms.Single().Hash;

        Assert.Equal(Hash(original), Hash(renamed));
        Assert.Equal(Hash(original), Hash(restarted));
        Assert.Equal(Hash(original), Hash(jittered));
        Assert.NotEqual(Hash(original), Hash(moved));
    }

    [Fact]
    public void A_package_without_rooms_writes_no_file()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        Assert.Null(new Tiles3DRoomIndexWriter().Write(directory, new Tiles3DExportPackage()));
        Assert.False(File.Exists(Path.Combine(directory, "rooms.json")));
    }

    /// <summary>
    /// Two rooms near Tokyo Station in EPSG:6677: a host concourse and, in a linked model, a
    /// platform with a column island. A third room has too few points and is dropped.
    /// </summary>
    internal static List<Tiles3DRoomFootprint> FixtureRooms()
    {
        List<double[]> concourse = Rectangle(Easting - 4.815, Northing - 2.77, 20, 10);
        // A repeated point and sub-millimetre noise, as tessellated arcs and links give.
        concourse.Insert(1, new[] { concourse[1][0] + 0.0002, concourse[1][1] });
        concourse[0] = new[] { concourse[0][0] - 0.00004, concourse[0][1] + 0.00004 };

        return new List<Tiles3DRoomFootprint>
        {
            new Tiles3DRoomFootprint
            {
                UniqueId = "room-concourse",
                ElementId = "4101",
                SourceDocument = "Tokyo.rvt",
                LevelUniqueId = "lv-1",
                LevelName = "1FL_コンコース（TP+3.45）",
                Name = "コンコース",
                Number = "101",
                ElevationMeters = 43.45,
                AreaSquareMeters = 200.0004,
                Rings = new List<List<double[]>> { concourse },
            },
            new Tiles3DRoomFootprint
            {
                UniqueId = "room-platform",
                ElementId = "8202",
                SourceDocument = "Platform.rvt",
                SourceLinkName = "Platform.rvt : 2",
                SourceLinkInstanceUniqueId = "link-instance-2",
                LevelUniqueId = "lv-b1",
                LevelName = "B1F",
                Name = "ホーム",
                Number = "B1-01",
                ElevationMeters = 32.5,
                AreaSquareMeters = 236.0,
                Rings = new List<List<double[]>>
                {
                    Rectangle(Easting - 4.815, Northing - 20.77, 30, 8),
                    Rectangle(Easting + 5.185, Northing - 17.77, 2, 2),
                },
            },
            new Tiles3DRoomFootprint
            {
                UniqueId = "room-sliver",
                SourceDocument = "Tokyo.rvt",
                Rings = new List<List<double[]>> { new List<double[]> { new[] { Easting, Northing }, new[] { Easting + 1, Northing } } },
            },
        };
    }

    private static Tiles3DRoomFootprint Room(string uniqueId, string name, List<double[]> ring)
    {
        return new Tiles3DRoomFootprint
        {
            UniqueId = uniqueId,
            SourceDocument = "Tokyo.rvt",
            Name = name,
            Rings = new List<List<double[]>> { ring },
        };
    }

    /// <summary>An open counter-clockwise rectangle from its south-west corner.</summary>
    private static List<double[]> Rectangle(double west, double south, double width, double height)
    {
        return new List<double[]>
        {
            new[] { west, south },
            new[] { west + width, south },
            new[] { west + width, south + height },
            new[] { west, south + height },
        };
    }

    private static string FixturePath([CallerFilePath] string sourceFile = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Fixtures", "Conformance", "revit-rooms.json");
    }
}
