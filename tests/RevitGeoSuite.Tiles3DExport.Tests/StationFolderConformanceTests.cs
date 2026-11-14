using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// The sample station folder of the Kanizu link contract (docs/kanizu-link-contract.md), pinned
/// as a conformance fixture: Kanizu's tests read a copy of Fixtures/Conformance/station. It is
/// written here by the real writers: the level, element and room indexes, the manifest builder and
/// the station folder's publish step. When the output changes on purpose, run with
/// REVITGEOSUITE_WRITE_FIXTURES=1 to rewrite the fixture, and copy it to Kanizu.
/// </summary>
public sealed class StationFolderConformanceTests
{
    private const double Easting = -5995.185;
    private const double Northing = -35367.23;
    private const double Geoid = 36.5;
    private const double Ellipsoidal = 40.0;

    /// <summary>Written by Kanizu, never by RevitGeoSuite; the fixture carries one for Kanizu's tests.</summary>
    private const string KanizuLinkFile = "kanizu-link.json";

    [Fact]
    public void The_sample_station_folder_matches_the_conformance_fixture()
    {
        string station = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"), "station");
        Directory.CreateDirectory(station);

        CesiumPackageLayout published = WriteSampleStation(station);

        Assert.StartsWith(Path.Combine(station, "revit"), published.RootDirectory, StringComparison.Ordinal);
        string fixture = FixtureDirectory();
        if (Environment.GetEnvironmentVariable("REVITGEOSUITE_WRITE_FIXTURES") == "1")
        {
            string revit = Path.Combine(fixture, "revit");
            if (Directory.Exists(revit))
            {
                Directory.Delete(revit, recursive: true);
            }

            foreach (string file in Files(station))
            {
                string target = Path.Combine(fixture, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(station, file), target, overwrite: true);
            }
        }

        string[] expected = Files(fixture).Where(file => file != KanizuLinkFile).ToArray();
        Assert.Equal(expected, Files(station));
        foreach (string file in expected)
        {
            Assert.Equal(Text(Path.Combine(fixture, file)), Text(Path.Combine(station, file)));
        }

        Assert.True(File.Exists(Path.Combine(fixture, KanizuLinkFile)), "the fixture carries Kanizu's link file");
    }

    [Fact]
    public void The_manifest_records_what_the_export_covered()
    {
        string station = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", Guid.NewGuid().ToString("N"), "station");
        Directory.CreateDirectory(station);

        CesiumPackageLayout published = WriteSampleStation(station);

        CesiumPackageManifest manifest = CesiumPackageManifestSerializer.Deserialize(File.ReadAllText(published.ManifestPath));
        CesiumPackageExport export = Assert.IsType<CesiumPackageExport>(manifest.Export);
        Assert.Equal(CesiumExportScopes.Whole, export.Scope);
        Assert.Null(export.View);
        Assert.Equal("link-platform-1", Assert.Single(export.Links).UniqueId);
        Assert.Equal("fine", export.LevelOfDetail);
        Assert.True(export.ExactPlacement);
        Assert.Equal("8d7f3c1e-0000-4000-8000-000000000001", manifest.Source!.ModelId);
        Assert.Equal("tiles/rooms.json", manifest.Tiles!.Rooms);
    }

    [Fact]
    public void A_view_export_names_its_view_and_sorts_its_links()
    {
        CesiumPackageExport export = CesiumPackageExportDescriber.Describe(
            isViewScope: true,
            viewUniqueId: "view-1",
            viewName: "{3D}",
            links: new[] { ("link-b", "B.rvt"), ("", "unplaced"), ("link-a", "A.rvt") },
            levelOfDetail: "Medium",
            exactPlacement: false);

        Assert.Equal(CesiumExportScopes.View, export.Scope);
        Assert.Equal("view-1", export.View!.UniqueId);
        Assert.Equal(new[] { "link-a", "link-b" }, export.Links.Select(link => link.UniqueId));
        Assert.Equal("medium", export.LevelOfDetail);
        Assert.False(export.ExactPlacement);
    }

    /// <summary>
    /// One export of the sample model into <paramref name="station"/>: a host wall on 1FL and a
    /// door of a linked platform model on B1FL, with one room on each.
    /// </summary>
    private static CesiumPackageLayout WriteSampleStation(string station)
    {
        Tiles3DExportPackage package = SamplePackage();
        GisRulesStamp rules = WriteStationRules(station);
        WriteGisModel(station);
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(station);

        // The tileset is not part of the link contract (Kanizu places the model from the
        // manifest), so the sample carries a fixed one without content.
        WriteText(Path.Combine(staging.TilesDirectory, "tileset.json"),
            "{\"asset\":{\"version\":\"1.1\"},\"geometricError\":0,\"root\":{\"boundingVolume\":{\"box\":[0,0,0,1,0,0,0,1,0,0,0,1]},\"geometricError\":0}}");
        WriteText(Path.Combine(staging.TilesDirectory, package.LevelManifestFileName), new Tiles3DLevelManifestWriter().BuildJson(package));
        WriteText(Path.Combine(staging.TilesDirectory, package.ElementIndexFileName), new Tiles3DElementIndexWriter().BuildJson(package));
        WriteText(Path.Combine(staging.TilesDirectory, package.RoomIndexFileName), new Tiles3DRoomIndexWriter().BuildJson(package.Rooms!));

        // The GIS export's database is not part of the link contract either; the manifest only needs
        // an artifact to describe, so the sample carries a fixed stand-in.
        Directory.CreateDirectory(staging.GisDirectory);
        WriteText(Path.Combine(staging.GisDirectory, "tokyo-station.gpkg"), "stand-in for the sample's GeoPackage");

        CesiumPackageManifest manifest = new CesiumPackageLayoutBuilder().WriteManifest(staging, new CesiumPackageBuildInputs
        {
            BuildingId = "tokyo-station-1a2b3c4d",
            BuildingName = "Tokyo Station",
            SourceModel = "Tokyo.rvt",
            DocumentKey = "doc-sample",
            GeneratorVersion = "1.0.0.0",
            ProjectEpsg = 6677,
            CoordinateMode = "SharedCoordinates",
            GisEpsg = 6677,
            AnchorLat = 35.6812000012,
            AnchorLon = 139.7671000018,
            AnchorElevationMeters = Ellipsoidal,
            GeoidOffsetMeters = Geoid,
            GeoidModel = Tiles3DGeoidModels.Egm2008,
            HeightDatum = CesiumHeightDatums.Tp,
            AnchorEasting = Easting,
            AnchorNorthing = Northing,
            ModelId = "8d7f3c1e-0000-4000-8000-000000000001",
            GisRules = new CesiumPackageGisRules { Source = rules.Source, Profile = rules.Profile, Hash = rules.Hash },
            Export = CesiumPackageExportDescriber.Describe(
                isViewScope: false,
                viewUniqueId: null,
                viewName: null,
                links: new[] { ("link-platform-1", "Platform.rvt : 1") },
                levelOfDetail: Tiles3DLevelOfDetail.Fine.ToString(),
                exactPlacement: true),
        });

        // A real export stamps a fresh id and the clock; the fixture pins both.
        manifest.PackageId = "00000000-0000-4000-8000-000000000001";
        manifest.CreatedUtc = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        WriteText(staging.ManifestPath, CesiumPackageManifestSerializer.Serialize(manifest));
        return CesiumStationFolder.Publish(staging, station, manifest);
    }

    /// <summary>The model's <c>gis-model.json</c> after a station export, written by the real writer with a fixed clock.</summary>
    private static void WriteGisModel(string station)
    {
        GisModelFile file = GisModelFile.CreateFromTemplate();
        file.Identity.ModelId = "8d7f3c1e-0000-4000-8000-000000000001";
        file.Identity.Station = new DirectoryInfo(station).Name;
        file.Identity.Building = "Tokyo Station";
        file.Identity.CrsEpsg = 6677;
        file.Levels = new List<GisModelLevel>
        {
            new() { UniqueId = "lv-host-1fl", Name = "1FL", LevelId = "gis-1f", Ordinal = 1, ShortName = "1F" },
        };
        file.Views = new List<GisModelView> { new() { UniqueId = "view-gis-1fl", Name = "GIS_1FL" } };
        file.Links = new List<GisModelLink> { new() { UniqueId = "link-platform-1", Name = "Platform.rvt : 1" } };
        file.Versions.Standard = 1;
        file.Versions.LastExportRevitGeoSuite = "1.0.0.0";
        file.Versions.LastExportUtc = "2026-11-08T12:00:00Z";
        file.Save(GisModelFile.PathFor(station, file.Identity.ModelId));
    }

    /// <summary>The station's <c>gis-rules.json</c>, written by the real writer with a fixed stamp.</summary>
    private static GisRulesStamp WriteStationRules(string station)
    {
        GisRulesProfile profile = new()
        {
            Name = GisModelFile.DefaultProfileName,
            SchemaProfiles = new List<RevitGeoSuite.FloorPlanExport.Core.Schema.SchemaProfile>(),
            ValidationPolicies = new List<RevitGeoSuite.FloorPlanExport.Core.Validation.ValidationPolicyProfile>(),
        };
        GisRulesStore store = new(
            GisRulesFile.PathFor(station),
            new GisRulesStoreOptions { Stamp = () => new GisRulesChanged { Utc = "2026-11-08T12:00:00Z", User = "station-pc", RevitGeoSuite = "1.0.0.0" } });
        GisRulesFile written = store.Apply(new[]
        {
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.FloorCategory, MatchValue = "Concourse Floor", ResolvedValue = "walkway" }),
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.AcceptedOpeningFamily, MatchValue = "Gate Opening" }),
            GisRulesOperation.UpsertProfile(profile),
        });
        return new GisRulesStamp(
            GisRulesStamp.StationKind,
            profile.Name,
            GisRulesHash.Compute(written.ToMappingRules().Rules, written.FindProfile(profile.Name)!));
    }

    private static Tiles3DExportPackage SamplePackage()
    {
        Tiles3DMeshPrimitive wall = Cube("wall-1", 0d, 0d, 0d);
        wall.Metadata.RevitElementId = "1001";
        wall.Metadata.Category = "Walls";
        wall.Metadata.SourceDocument = "Tokyo";
        SetLevel(wall, "lv-host-1fl", "1FL", 0d);

        Tiles3DMeshPrimitive door = Cube("door-1", 10d, 0d, -7.5d);
        door.Metadata.RevitElementId = "2001";
        door.Metadata.Category = "Doors";
        door.Metadata.SourceDocument = "Platform";
        door.Metadata.SourceLinkName = "Platform.rvt : 1";
        door.Metadata.SourceLinkInstanceUniqueId = "link-platform-1";
        SetLevel(door, "lv-platform-b1fl", "B1FL", -7.5d);

        return new Tiles3DExportPackage
        {
            ReferenceContext = new Tiles3DExportReferenceContext
            {
                AnchorProjectedCoordinate = new ProjectedCoordinate(Easting, Northing),
                AnchorLatitude = 35.6812000012,
                AnchorLongitude = 139.7671000018,
                AnchorElevationMeters = Ellipsoidal,
            },
            GeoidHeightOffsetMeters = Geoid,
            LevelOfDetail = Tiles3DLevelOfDetail.Fine,
            Meshes = new List<Tiles3DMeshPrimitive> { wall, door },
            Rooms = new List<Tiles3DRoomFootprint>
            {
                new()
                {
                    UniqueId = "room-concourse",
                    ElementId = "3001",
                    SourceDocument = "Tokyo",
                    LevelUniqueId = "lv-host-1fl",
                    LevelName = "1FL",
                    Name = "コンコース",
                    Number = "101",
                    ElevationMeters = 3.3,
                    AreaSquareMeters = 200.0,
                    Rings = new List<List<double[]>> { Rectangle(Easting - 10, Northing - 5, 20, 10) },
                },
                new()
                {
                    UniqueId = "room-platform",
                    ElementId = "4001",
                    SourceDocument = "Platform",
                    SourceLinkName = "Platform.rvt : 1",
                    SourceLinkInstanceUniqueId = "link-platform-1",
                    LevelUniqueId = "lv-platform-b1fl",
                    LevelName = "B1FL",
                    Name = "ホーム",
                    Number = "B1-01",
                    ElevationMeters = -4.2,
                    AreaSquareMeters = 240.0,
                    Rings = new List<List<double[]>> { Rectangle(Easting, Northing - 20, 30, 8) },
                },
            },
        };
    }

    private static void SetLevel(Tiles3DMeshPrimitive mesh, string uniqueId, string name, double elevation)
    {
        mesh.Metadata.LevelUniqueId = uniqueId;
        mesh.Metadata.LevelName = name;
        mesh.Metadata.LevelKey = Tiles3DLevelMetadata.BuildLevelKey(name);
        mesh.Metadata.LevelElevationMeters = elevation;
        mesh.Metadata.MinZMeters = elevation;
        mesh.Metadata.MaxZMeters = elevation + 1d;
        mesh.Metadata.HeightMeters = 1d;
    }

    /// <summary>A unit cube from (x, y, z), in metres from the anchor, the anchor's height datum.</summary>
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
            Metadata = new Tiles3DObjectMetadata { RevitUniqueId = uniqueId, Name = uniqueId },
            Triangles = faces.Select(f => new Tiles3DTriangle(v[f[0]], v[f[1]], v[f[2]])).ToList(),
        };
    }

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

    /// <summary>Writes with LF line ends, so the content hash and the fixture match on every OS.</summary>
    private static void WriteText(string path, string text)
    {
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }

    private static string Text(string path)
    {
        return File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
    }

    /// <summary>Every file under <paramref name="root"/>, relative, with '/' between parts, sorted.</summary>
    private static string[] Files(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => path.Substring(root.Length + 1).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FixtureDirectory([CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Fixtures", "Conformance", "station"));
    }
}
