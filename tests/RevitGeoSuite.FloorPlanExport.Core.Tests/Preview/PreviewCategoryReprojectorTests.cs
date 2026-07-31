using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Preview;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Preview;

public sealed class PreviewCategoryReprojectorTests
{
    [Fact]
    public void Reproject_AppliesFloorOverride()
    {
        PreviewCategoryReprojector reprojector = new(
            ZoneCatalog.CreateDefault(),
            floorCategoryOverrides: new Dictionary<string, string> { ["FL-Shop-01"] = "retail" });

        ReprojectedPreviewCategory resolved = reprojector.Reproject(
            "floor",
            "FL-Shop-01",
            assignmentParsedCandidate: null,
            assignmentParameterName: null);

        Assert.Equal("retail", resolved.Category);
        Assert.False(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.Override, resolved.ResolutionSource);
    }

    [Fact]
    public void Reproject_FloorTypeWithoutOverrideOrCatalogMatchIsUnassigned()
    {
        PreviewCategoryReprojector reprojector = new(ZoneCatalog.CreateDefault());

        ReprojectedPreviewCategory resolved = reprojector.Reproject(
            "floor",
            "FL-Mystery-99",
            assignmentParsedCandidate: null,
            assignmentParameterName: null);

        Assert.True(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.FallbackUnspecified, resolved.ResolutionSource);
    }

    [Fact]
    public void Reproject_RoomDerivedFeatureUsesRoomOverrides()
    {
        PreviewCategoryReprojector reprojector = new(
            ZoneCatalog.CreateDefault(),
            floorCategoryOverrides: new Dictionary<string, string> { ["Lobby"] = "nonpublic" },
            roomCategoryOverrides: new Dictionary<string, string> { ["Lobby"] = "walkway" });

        ReprojectedPreviewCategory resolved = reprojector.Reproject(
            "room",
            "Lobby",
            assignmentParsedCandidate: null,
            assignmentParameterName: "Name");

        // Must consult the room map, not the floor map, for a room-derived feature.
        Assert.Equal("walkway", resolved.Category);
        Assert.Equal(FloorCategoryResolutionSource.Override, resolved.ResolutionSource);
    }

    [Fact]
    public void Reproject_FillColorTracksTheReassignedCategory()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        PreviewCategoryReprojector before = new(catalog);
        PreviewCategoryReprojector after = new(
            catalog,
            floorCategoryOverrides: new Dictionary<string, string> { ["FL-Mystery-99"] = "nonpublic" });

        ReprojectedPreviewCategory unassigned = before.Reproject("floor", "FL-Mystery-99", null, null);
        ReprojectedPreviewCategory reassigned = after.Reproject("floor", "FL-Mystery-99", null, null);

        Assert.NotEqual(unassigned.FillColorHex, reassigned.FillColorHex);
        Assert.Equal("979797", reassigned.FillColorHex);
    }

    [Fact]
    public void Reproject_ClearingAnOverrideFallsBackToUnassigned()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();
        PreviewCategoryReprojector assigned = new(
            catalog,
            floorCategoryOverrides: new Dictionary<string, string> { ["FL-Mystery-99"] = "retail" });
        PreviewCategoryReprojector cleared = new(catalog);

        Assert.False(assigned.Reproject("floor", "FL-Mystery-99", null, null).IsUnassigned);
        Assert.True(cleared.Reproject("floor", "FL-Mystery-99", null, null).IsUnassigned);
    }
}
