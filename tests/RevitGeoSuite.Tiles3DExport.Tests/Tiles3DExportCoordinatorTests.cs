using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExportCoordinatorTests
{
    [Fact]
    public void Build_updated_state_persists_scope_view_and_selected_links()
    {
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            LevelOfDetail = Tiles3DLevelOfDetail.Fine,
            ElementCount = 4,
            TriangleCount = 12
        };
        Tiles3DExportScopeSelection scope = new Tiles3DExportScopeSelection
        {
            ScopeMode = Tiles3DExportScopeMode.Selected3DView,
            SelectedView = new Tiles3DExportViewOption
            {
                ViewId = 101,
                UniqueId = "view-1",
                Title = "Export View"
            },
            SelectedLinkedModels = new[]
            {
                new Tiles3DExportLinkOption
                {
                    LinkInstanceId = 201,
                    UniqueId = "link-1",
                    Title = "Architectural Link"
                },
                new Tiles3DExportLinkOption
                {
                    LinkInstanceId = 202,
                    UniqueId = "link-2",
                    Title = "Structural Link"
                }
            }
        };

        Tiles3DExportState state = Tiles3DExportCoordinator.BuildUpdatedState(
            null,
            @"C:\\exports\\tiles",
            package,
            Tiles3DExportReferenceSource.CanonicalOrigin,
            scope);

        Assert.Equal(@"C:\\exports\\tiles", state.LastExportPath);
        Assert.Equal(Tiles3DLevelOfDetail.Fine.ToString(), state.LastLodSetting);
        Assert.Equal(Tiles3DExportReferenceSource.CanonicalOrigin, state.LastReferenceSource);
        Assert.Equal(Tiles3DExportScopeMode.Selected3DView, state.LastScopeMode);
        Assert.Equal("view-1", state.LastViewUniqueId);
        Assert.Equal("Export View", state.LastViewName);
        Assert.Equal(new List<string> { "link-1", "link-2" }, state.LastSelectedLinkUniqueIds);
        Assert.Equal(new List<string> { "Architectural Link", "Structural Link" }, state.LastSelectedLinkNames);
        Assert.Equal(4, state.LastExportedElementCount);
        Assert.Equal(12, state.LastExportedTriangleCount);
        Assert.False(state.LastUsedPreciseCrsProjection);
    }

    [Fact]
    public void Build_updated_state_persists_precise_crs_projection_flag()
    {
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            LevelOfDetail = Tiles3DLevelOfDetail.Fine,
            ElementCount = 2,
            TriangleCount = 6,
            UsedPreciseCrsProjection = true
        };
        Tiles3DExportScopeSelection scope = new Tiles3DExportScopeSelection
        {
            ScopeMode = Tiles3DExportScopeMode.WholeModel
        };

        Tiles3DExportState state = Tiles3DExportCoordinator.BuildUpdatedState(
            null,
            @"C:\\exports\\tiles",
            package,
            Tiles3DExportReferenceSource.WorkingProjectBasePoint,
            scope);

        Assert.True(state.LastUsedPreciseCrsProjection);
    }

    [Fact]
    public void Build_updated_state_persists_geoid_height_offset()
    {
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            LevelOfDetail = Tiles3DLevelOfDetail.Fine,
            ElementCount = 2,
            TriangleCount = 6,
            GeoidHeightOffsetMeters = 37.5
        };
        Tiles3DExportScopeSelection scope = new Tiles3DExportScopeSelection
        {
            ScopeMode = Tiles3DExportScopeMode.WholeModel
        };

        Tiles3DExportState state = Tiles3DExportCoordinator.BuildUpdatedState(
            null,
            @"C:\\exports\\tiles",
            package,
            Tiles3DExportReferenceSource.WorkingProjectBasePoint,
            scope);

        Assert.Equal(37.5, state.LastGeoidHeightOffsetMeters);
    }

    [Fact]
    public void Build_package_applies_geoid_height_offset_to_package_context_only()
    {
        Tiles3DExportReferenceContext sourceContext = CreateReferenceContext(anchorElevationMeters: 12.25d);

        Tiles3DExportPackage package = Tiles3DExportCoordinator.BuildPackage(
            sourceContext,
            CreateMeshes(),
            Tiles3DLevelOfDetail.Fine,
            37.5d);

        Assert.NotSame(sourceContext, package.ReferenceContext);
        Assert.Equal(12.25d, sourceContext.AnchorElevationMeters, precision: 6);
        Assert.Equal(49.75d, package.ReferenceContext.AnchorElevationMeters, precision: 6);
        Assert.Equal(37.5d, package.GeoidHeightOffsetMeters, precision: 6);
    }

    [Fact]
    public void Build_package_does_not_accumulate_geoid_height_offset_across_calls()
    {
        Tiles3DExportReferenceContext sourceContext = CreateReferenceContext(anchorElevationMeters: 12.25d);
        IReadOnlyCollection<Tiles3DMeshPrimitive> meshes = CreateMeshes();

        Tiles3DExportPackage first = Tiles3DExportCoordinator.BuildPackage(
            sourceContext,
            meshes,
            Tiles3DLevelOfDetail.Fine,
            37.5d);
        Tiles3DExportPackage second = Tiles3DExportCoordinator.BuildPackage(
            sourceContext,
            meshes,
            Tiles3DLevelOfDetail.Fine,
            37.5d);

        Assert.Equal(49.75d, first.ReferenceContext.AnchorElevationMeters, precision: 6);
        Assert.Equal(49.75d, second.ReferenceContext.AnchorElevationMeters, precision: 6);
        Assert.Equal(12.25d, sourceContext.AnchorElevationMeters, precision: 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-151d)]
    [InlineData(151d)]
    public void Build_package_rejects_invalid_geoid_height_offset(double geoidHeightOffsetMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles3DExportCoordinator.BuildPackage(
            CreateReferenceContext(anchorElevationMeters: 12.25d),
            CreateMeshes(),
            Tiles3DLevelOfDetail.Fine,
            geoidHeightOffsetMeters));
    }

    private static Tiles3DExportReferenceContext CreateReferenceContext(double anchorElevationMeters)
    {
        return new Tiles3DExportReferenceContext
        {
            Title = "Canonical Origin",
            Description = "Test context.",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorLatitude = 36d,
            AnchorLongitude = 139.833333333333d,
            AnchorElevationMeters = anchorElevationMeters,
            AnchorXFeet = 1d,
            AnchorYFeet = 2d,
            AnchorZFeet = 3d
        };
    }

    private static IReadOnlyCollection<Tiles3DMeshPrimitive> CreateMeshes()
    {
        return new[]
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
        };
    }
}
