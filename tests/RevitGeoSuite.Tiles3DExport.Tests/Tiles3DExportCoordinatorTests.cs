using System.Collections.Generic;
using Autodesk.Revit.DB;
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
                ViewId = new ElementId(101),
                UniqueId = "view-1",
                Title = "Export View"
            },
            SelectedLinkedModels = new[]
            {
                new Tiles3DExportLinkOption
                {
                    LinkInstanceId = new ElementId(201),
                    UniqueId = "link-1",
                    Title = "Architectural Link"
                },
                new Tiles3DExportLinkOption
                {
                    LinkInstanceId = new ElementId(202),
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
    }
}