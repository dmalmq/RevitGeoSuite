using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// Layer 2 & Layer 4 pure-C# verification for 3D Tiles export-loop guards and byte-identical
/// authoring-export invariance when linked showcase documents and showcase elements are present.
/// </summary>
public sealed class Tiles3DShowcaseGuardInvarianceTests : IDisposable
{
    private readonly string _tempDir;

    public Tiles3DShowcaseGuardInvarianceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "tiles3d-showcase-invariance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Tiles3DShowcaseGuardConstantsStayPinnedToShowcaseExportGuard()
    {
        Assert.Equal(ShowcaseExportGuard.AuthoringModelRole, Tiles3DShowcaseGuard.AuthoringModelRole);
        Assert.Equal(ShowcaseExportGuard.ShowcaseModelRole, Tiles3DShowcaseGuard.ShowcaseModelRole);
        Assert.Equal(ShowcaseExportGuard.ShowcaseApplicationId, Tiles3DShowcaseGuard.ShowcaseApplicationId);
        Assert.Equal(ShowcaseExportGuard.ShowcaseFamilyNamePrefix, Tiles3DShowcaseGuard.ShowcaseFamilyNamePrefix);
        Assert.Equal(ShowcaseExportGuard.ShowcaseParameterPrefix, Tiles3DShowcaseGuard.ShowcaseParameterPrefix);
        Assert.Equal(ShowcaseExportGuard.GisModelRoleParameterGuid, Tiles3DShowcaseGuard.GisModelRoleParameterGuid);

        Assert.Throws<InvalidOperationException>(() => Tiles3DShowcaseGuard.ThrowIfModelRoleBlocked("SHOWCASE"));
        Tiles3DShowcaseGuard.ThrowIfModelRoleBlocked("authoring");
        Tiles3DShowcaseGuard.ThrowIfModelRoleBlocked(null);
    }

    [Theory]
    [InlineData("K_Footprint")]
    [InlineData("KNZ_FRM_PoiMarker")]
    [InlineData("knz_frm_network_ribbon")]
    [InlineData("  Knz_Frm_Unit  ")]
    public void SubcategoryExclusionCoversFootprintAndKnzFrmShowcaseSubcategories(string subcategory)
    {
        Assert.True(Tiles3DGeometryExtractor.IsExcludedSubcategoryName(subcategory));
    }

    [Fact]
    public void Tiles3DExportPackageModelRoleDefaultsToNullAndRoundTripsThroughCesiumManifest()
    {
        Tiles3DExportPackage legacyPackage = new();
        Assert.Null(legacyPackage.ModelRole);

        Tiles3DExportPackage authoringPackage = new() { ModelRole = ShowcaseExportGuard.AuthoringModelRole };
        Tiles3DExportPackage showcasePackage = new() { ModelRole = ShowcaseExportGuard.ShowcaseModelRole };

        CesiumPackageLayoutBuilder builder = new();
        string legacyRoot = Path.Combine(_tempDir, "manifest-legacy");
        CesiumPackageLayout legacyLayout = builder.CreateLayout(legacyRoot);
        File.WriteAllText(Path.Combine(legacyLayout.TilesDirectory, "tileset.json"), "{}");
        CesiumPackageManifest legacyManifest = builder.WriteManifest(legacyLayout, BuildInputs(legacyPackage.ModelRole));
        Assert.Null(legacyManifest.Source!.ModelRole);
        Assert.DoesNotContain("modelRole", File.ReadAllText(legacyLayout.ManifestPath));

        string authoringRoot = Path.Combine(_tempDir, "manifest-authoring");
        CesiumPackageLayout authoringLayout = builder.CreateLayout(authoringRoot);
        File.WriteAllText(Path.Combine(authoringLayout.TilesDirectory, "tileset.json"), "{}");
        CesiumPackageManifest authoringManifest = builder.WriteManifest(authoringLayout, BuildInputs(authoringPackage.ModelRole));
        Assert.Equal("authoring", authoringManifest.Source!.ModelRole);

        string showcaseRoot = Path.Combine(_tempDir, "manifest-showcase");
        CesiumPackageLayout showcaseLayout = builder.CreateLayout(showcaseRoot);
        File.WriteAllText(Path.Combine(showcaseLayout.TilesDirectory, "tileset.json"), "{}");
        CesiumPackageManifest showcaseManifest = builder.WriteManifest(showcaseLayout, BuildInputs(showcasePackage.ModelRole));
        Assert.Equal("showcase", showcaseManifest.Source!.ModelRole);
        Assert.True(CesiumModelRoles.IsShowcase(showcaseManifest.Source!.ModelRole));
    }

    [Fact]
    public void ThreeDTilesAuthoringExportIsByteIdenticalWithLinkedShowcaseAndLeakedShowcaseElements()
    {
        // Authoring baseline: host wall + authoring linked platform door + 2 rooms.
        MeshCandidate[] hostMeshesBaseline =
        {
            new(
                BuildCube("wall-host-1", "1001", "Walls", "Basic Wall", "Generic 200mm", "Host", string.Empty, string.Empty, "lv-1f", "1FL", 0d, 0d, 0d),
                new ShowcaseElementDescriptor(familyName: "Basic Wall", typeName: "Generic 200mm", categoryName: "Walls", parameterNames: new[] { "Mark", "id" })),
        };
        RoomCandidate[] hostRoomsBaseline =
        {
            new(
                BuildRoom("room-host-1", "3001", "Host", string.Empty, string.Empty, "lv-1f", "1FL", "Concourse", 0d, 0d),
                new ShowcaseElementDescriptor(categoryName: "Rooms", parameterNames: new[] { "Name", "Number" })),
        };

        Linked3DModel authoringLink = new(
            "Platform.rvt",
            modelRole: "authoring",
            meshes: new[]
            {
                new MeshCandidate(
                    BuildCube("door-link-1", "2001", "Doors", "StationDoor", "Sliding_1800", "Platform", "Platform.rvt : 1", "link-platform-1", "lv-b1", "B1FL", 10d, 0d, -4d),
                    new ShowcaseElementDescriptor(familyName: "StationDoor", typeName: "Sliding_1800", categoryName: "Doors", parameterNames: new[] { "gis_door" }, sourceDocumentModelRole: "authoring")),
            },
            rooms: new[]
            {
                new RoomCandidate(
                    BuildRoom("room-link-1", "4001", "Platform", "Platform.rvt : 1", "link-platform-1", "lv-b1", "B1FL", "Platform", 10d, -4d),
                    new ShowcaseElementDescriptor(categoryName: "Rooms", sourceDocumentModelRole: "authoring")),
            });

        // Polluted authoring model: adds a linked Forma Companion showcase document + host-leaked
        // Kanizu.Showcase DirectShape + KNZ_FRM_* family/type/category meshes + KS_*/ks_* tagged elements.
        Linked3DModel showcaseLink = new(
            "Tokyo_GIS_Showcase.rvt",
            modelRole: "SHOWCASE",
            meshes: new[]
            {
                new MeshCandidate(
                    BuildCube("showcase-link-ds", "9001", "Generic Models", "DirectShape", "Unit_01", "Tokyo_GIS_Showcase", "Tokyo_GIS_Showcase.rvt : 1", "link-showcase-1", "lv-1f", "1FL", 50d, 50d, 0d),
                    new ShowcaseElementDescriptor(applicationId: "Kanizu.Showcase", familyName: "DirectShape", sourceDocumentModelRole: "SHOWCASE")),
            },
            rooms: new[]
            {
                new RoomCandidate(
                    BuildRoom("showcase-link-room", "9002", "Tokyo_GIS_Showcase", "Tokyo_GIS_Showcase.rvt : 1", "link-showcase-1", "lv-1f", "1FL", "ShowcaseRoom", 50d, 0d),
                    new ShowcaseElementDescriptor(sourceDocumentModelRole: "SHOWCASE")),
            });

        MeshCandidate[] hostMeshesPolluted = hostMeshesBaseline
            .Concat(new[]
            {
                new MeshCandidate(
                    BuildCube("leaked-ds", "9101", "Generic Models", "DirectShape", "NetworkRibbon", "Host", string.Empty, string.Empty, "lv-1f", "1FL", 25d, 10d, 0d),
                    new ShowcaseElementDescriptor(applicationId: "Kanizu.Showcase", categoryName: "Generic Models")),
                new MeshCandidate(
                    BuildCube("leaked-poi", "9102", "Generic Models", "KNZ_FRM_PoiMarker", "Restroom", "Host", string.Empty, string.Empty, "lv-1f", "1FL", 28d, 10d, 0d),
                    new ShowcaseElementDescriptor(familyName: "KNZ_FRM_PoiMarker", typeName: "Restroom", categoryName: "Generic Models")),
                new MeshCandidate(
                    BuildCube("leaked-ks", "9103", "Specialty Equipment", "CustomBox", "Box1", "Host", string.Empty, string.Empty, "lv-1f", "1FL", 31d, 10d, 0d),
                    new ShowcaseElementDescriptor(familyName: "CustomBox", typeName: "Box1", parameterNames: new[] { "ks_layer", "KS_Id" })),
            })
            .ToArray();

        RoomCandidate[] hostRoomsPolluted = hostRoomsBaseline
            .Concat(new[]
            {
                new RoomCandidate(
                    BuildRoom("leaked-room", "9201", "Host", string.Empty, string.Empty, "lv-1f", "1FL", "LeakedShowcase", 40d, 0d),
                    new ShowcaseElementDescriptor(parameterNames: new[] { "ks_attributes_json" })),
            })
            .ToArray();

        string baselineDir = Path.Combine(_tempDir, "baseline-pkg");
        string pollutedDir = Path.Combine(_tempDir, "polluted-pkg");

        CesiumPackageManifest baselineManifest = WriteFiltered3DPackage(baselineDir, hostMeshesBaseline, hostRoomsBaseline, new[] { authoringLink });
        CesiumPackageManifest pollutedManifest = WriteFiltered3DPackage(pollutedDir, hostMeshesPolluted, hostRoomsPolluted, new[] { authoringLink, showcaseLink });

        // Every generated 3D Tiles artifact and content hash must be byte-identical.
        string[] files = { "tileset.json", "content.glb", "levels.json", "elements.json", "rooms.json" };
        foreach (string file in files)
        {
            byte[] expectedBytes = File.ReadAllBytes(Path.Combine(baselineDir, "tiles", file));
            byte[] actualBytes = File.ReadAllBytes(Path.Combine(pollutedDir, "tiles", file));
            Assert.Equal(expectedBytes, actualBytes);
        }

        Assert.Equal(baselineManifest.ContentHash, pollutedManifest.ContentHash);
    }

    private static CesiumPackageManifest WriteFiltered3DPackage(
        string rootDir,
        IReadOnlyList<MeshCandidate> hostMeshes,
        IReadOnlyList<RoomCandidate> hostRooms,
        IReadOnlyList<Linked3DModel> links)
    {
        IReadOnlyList<Linked3DModel> authoringLinks =
            ShowcaseExportGuard.FilterAuthoringLinkedDocuments(links, link => link.ModelRole, link => link.Name);

        List<Tiles3DMeshPrimitive> meshes = ShowcaseExportGuard.FilterAuthoringItems(
                hostMeshes.Concat(authoringLinks.SelectMany(link => link.Meshes)),
                candidate => candidate.Descriptor)
            .Select(candidate => candidate.Primitive)
            .ToList();

        List<Tiles3DRoomFootprint> rooms = ShowcaseExportGuard.FilterAuthoringItems(
                hostRooms.Concat(authoringLinks.SelectMany(link => link.Rooms)),
                candidate => candidate.Descriptor)
            .Select(candidate => candidate.Footprint)
            .ToList();

        Tiles3DExportReferenceContext reference = new()
        {
            Title = "Canonical Origin",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(1000d, 2000d),
            AnchorLatitude = 35.6812d,
            AnchorLongitude = 139.7671d,
            AnchorElevationMeters = 3.5d,
        };

        Tiles3DExportPackage package = Tiles3DExportCoordinator.BuildPackage(
            reference,
            meshes,
            Tiles3DLevelOfDetail.Medium,
            geoidHeightOffsetMeters: 36.5d,
            Tiles3DMaterialPalette.Source);
        package.Rooms = rooms;
        package.ModelRole = ShowcaseExportGuard.AuthoringModelRole;

        CesiumPackageLayoutBuilder builder = new();
        CesiumPackageLayout layout = builder.CreateLayout(rootDir);
        new Tiles3DPackageWriter().Write(layout.TilesDirectory, package);
        return builder.WriteManifest(layout, BuildInputs(package.ModelRole));
    }

    private static CesiumPackageBuildInputs BuildInputs(string? modelRole) => new()
    {
        BuildingId = "tokyo-station-1a2b3c4d",
        BuildingName = "Tokyo Station",
        SourceModel = "Tokyo.rvt",
        DocumentKey = "doc-sample",
        GeneratorVersion = "1.0.0",
        ProjectEpsg = 6677,
        CoordinateMode = "SharedCoordinates",
        GisEpsg = 6677,
        AnchorLat = 35.6812d,
        AnchorLon = 139.7671d,
        AnchorElevationMeters = 40.0d,
        GeoidOffsetMeters = 36.5d,
        GeoidModel = Tiles3DGeoidModels.Egm2008,
        HeightDatum = CesiumHeightDatums.Tp,
        ModelId = "8d7f3c1e-0000-4000-8000-000000000001",
        ModelRole = modelRole,
    };

    private static Tiles3DMeshPrimitive BuildCube(
        string uniqueId,
        string elementId,
        string category,
        string familyName,
        string typeName,
        string sourceDocument,
        string sourceLinkName,
        string sourceLinkInstanceUniqueId,
        string levelUniqueId,
        string levelName,
        double x,
        double y,
        double z)
    {
        Tiles3DMaterialColor color = new(120, 130, 140, 255);
        Tiles3DPoint P(double dx, double dy, double dz) => new(x + dx, y + dy, z + dz);
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
                RevitElementId = elementId,
                RevitUniqueId = uniqueId,
                Name = $"{category}: {typeName}",
                Category = category,
                FamilyName = familyName,
                TypeName = typeName,
                LevelUniqueId = levelUniqueId,
                LevelName = levelName,
                LevelKey = Tiles3DLevelMetadata.BuildLevelKey(levelName),
                LevelElevationMeters = z,
                MinZMeters = z,
                MaxZMeters = z + 1d,
                HeightMeters = 1d,
                SourceDocument = sourceDocument,
                SourceLinkName = sourceLinkName,
                SourceLinkInstanceUniqueId = sourceLinkInstanceUniqueId,
            },
            Triangles = faces.Select(f => new Tiles3DTriangle(v[f[0]], v[f[1]], v[f[2]], color)).ToList(),
        };
    }

    private static Tiles3DRoomFootprint BuildRoom(
        string uniqueId,
        string elementId,
        string sourceDocument,
        string sourceLinkName,
        string sourceLinkInstanceUniqueId,
        string levelUniqueId,
        string levelName,
        string name,
        double x,
        double elevation)
    {
        return new Tiles3DRoomFootprint
        {
            UniqueId = uniqueId,
            ElementId = elementId,
            SourceDocument = sourceDocument,
            SourceLinkName = sourceLinkName,
            SourceLinkInstanceUniqueId = sourceLinkInstanceUniqueId,
            LevelUniqueId = levelUniqueId,
            LevelName = levelName,
            Name = name,
            Number = "101",
            ElevationMeters = elevation,
            AreaSquareMeters = 25d,
            Rings = new List<List<double[]>>
            {
                new()
                {
                    new[] { 1000d + x, 2000d },
                    new[] { 1005d + x, 2000d },
                    new[] { 1005d + x, 2005d },
                    new[] { 1000d + x, 2005d },
                },
            },
        };
    }

    private sealed class Linked3DModel
    {
        public Linked3DModel(string name, string? modelRole, IReadOnlyList<MeshCandidate> meshes, IReadOnlyList<RoomCandidate> rooms)
        {
            Name = name;
            ModelRole = modelRole;
            Meshes = meshes;
            Rooms = rooms;
        }

        public string Name { get; }
        public string? ModelRole { get; }
        public IReadOnlyList<MeshCandidate> Meshes { get; }
        public IReadOnlyList<RoomCandidate> Rooms { get; }
    }

    private sealed class MeshCandidate
    {
        public MeshCandidate(Tiles3DMeshPrimitive primitive, ShowcaseElementDescriptor descriptor)
        {
            Primitive = primitive;
            Descriptor = descriptor;
        }

        public Tiles3DMeshPrimitive Primitive { get; }
        public ShowcaseElementDescriptor Descriptor { get; }
    }

    private sealed class RoomCandidate
    {
        public RoomCandidate(Tiles3DRoomFootprint footprint, ShowcaseElementDescriptor descriptor)
        {
            Footprint = footprint;
            Descriptor = descriptor;
        }

        public Tiles3DRoomFootprint Footprint { get; }
        public ShowcaseElementDescriptor Descriptor { get; }
    }
}
