using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExportViewModelTests
{
    [Fact]
    public void Working_project_base_point_is_preferred_for_reference_context_when_available()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" },
                StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                    Origin = new ProjectOrigin { Latitude = 35.67916666666667, Longitude = 139.76875, ElevationMeters = 0d },
                    ProjectedCoordinate = new ProjectedCoordinate(150d, 200d),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Test"
                }
            },
            CreateGeoProjectInfo());

        Assert.Equal(Tiles3DExportReferenceSource.WorkingProjectBasePoint, viewModel.SelectedReferenceSource);
        Assert.Equal("Working Project Base Point", viewModel.ReferenceSourceTitle);
    }

    [Fact]
    public void Default_scope_is_whole_model_and_prepare_is_allowed_without_view_selection()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo());

        Assert.Equal(Tiles3DExportScopeMode.WholeModel, viewModel.SelectedScopeMode);
        Assert.True(viewModel.CanPrepareExport);
    }

    [Fact]
    public void Previous_export_state_is_restored_on_startup()
    {
        Tiles3DExportState exportState = new Tiles3DExportState
        {
            LastExportPath = @"C:\\exports\\tiles",
            LastLodSetting = Tiles3DLevelOfDetail.Fine.ToString(),
            LastExportDateUtc = new DateTime(2026, 3, 31, 8, 0, 0, DateTimeKind.Utc),
            LastReferenceSource = Tiles3DExportReferenceSource.CanonicalOrigin,
            LastScopeMode = Tiles3DExportScopeMode.Selected3DView,
            LastViewUniqueId = "view-2",
            LastViewName = "Export View",
            LastSelectedLinkUniqueIds = new List<string> { "link-2" },
            LastSelectedLinkNames = new List<string> { "Structural Link" },
            LastExportedElementCount = 3,
            LastExportedTriangleCount = 18
        };

        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo(),
            exportState,
            CreateViews(),
            CreateLinks(),
            activeViewUniqueId: "view-1");

        Assert.Equal(exportState.LastExportPath, viewModel.OutputDirectory);
        Assert.Equal(Tiles3DExportScopeMode.Selected3DView, viewModel.SelectedScopeMode);
        Assert.Equal("view-2", viewModel.SelectedViewOption?.UniqueId);
        Assert.Contains(viewModel.LinkedModelOptions, option => option.UniqueId == "link-2" && option.IsSelected);
        Assert.True(viewModel.HasLastExportRows);
        Assert.Contains(viewModel.LastExportRows, row => row.Label == "Last Scope" && row.Value == "Selected 3D View");
        Assert.Contains(viewModel.LastExportRows, row => row.Label == "Last View" && row.Value == "Export View");
        Assert.Contains(viewModel.LastExportRows, row => row.Label == "Last Linked Models" && row.Value == "Structural Link");
    }

    [Fact]
    public void Active_view_is_selected_when_no_persisted_view_exists()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo(),
            state: null,
            CreateViews(),
            CreateLinks(),
            activeViewUniqueId: "view-2");

        Assert.Equal("view-2", viewModel.SelectedViewOption?.UniqueId);
    }

    [Fact]
    public void Prepare_is_blocked_only_when_selected_view_scope_has_no_view()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo(),
            state: null,
            views: Array.Empty<Tiles3DExportViewOption>(),
            links: Array.Empty<Tiles3DExportLinkOption>());

        Assert.True(viewModel.CanPrepareExport);
        viewModel.SelectedScopeModeOption = viewModel.ScopeModeOptions.Single(option => option.Mode == Tiles3DExportScopeMode.Selected3DView);
        Assert.False(viewModel.CanPrepareExport);
    }

    [Fact]
    public void Changing_scope_view_or_link_selection_clears_prepared_state()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo(),
            state: null,
            CreateViews(),
            CreateLinks());

        viewModel.MarkPrepared(CreatePreparedResult());
        viewModel.SelectedScopeModeOption = viewModel.ScopeModeOptions.Single(option => option.Mode == Tiles3DExportScopeMode.Selected3DView);
        Assert.Null(viewModel.PreparedPackage);

        viewModel.MarkPrepared(CreatePreparedResult());
        viewModel.SelectedViewOption = viewModel.AvailableViewOptions.Last();
        Assert.Null(viewModel.PreparedPackage);

        viewModel.MarkPrepared(CreatePreparedResult());
        Tiles3DExportLinkOption link = viewModel.LinkedModelOptions.First();
        link.IsSelected = true;
        Assert.Null(viewModel.PreparedPackage);
    }

    [Fact]
    public void Prepared_package_enables_export_when_output_directory_is_set()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            CreateCurrentState(),
            CreateGeoProjectInfo());

        viewModel.MarkPrepared(CreatePreparedResult());

        Assert.False(viewModel.CanExport);
        viewModel.OutputDirectory = @"C:\\exports\\tiles";
        Assert.True(viewModel.CanExport);
    }

    private static Tiles3DExportViewModel CreateViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo info,
        Tiles3DExportState? state = null,
        IReadOnlyCollection<Tiles3DExportViewOption>? views = null,
        IReadOnlyCollection<Tiles3DExportLinkOption>? links = null,
        string? activeViewUniqueId = null)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new Tiles3DExportViewModel(
            currentState,
            info,
            state,
            new Tiles3DExportReferenceResolver(coordinateTransformer),
            views,
            links,
            activeViewUniqueId);
    }

    private static CurrentProjectStateSummary CreateCurrentState()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Tiles Project",
            IsSupportedDocument = true,
            HasStoredGeoInfo = true,
            ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
        };
    }

    private static GeoProjectInfo CreateGeoProjectInfo()
    {
        return new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };
    }

    private static IReadOnlyCollection<Tiles3DExportViewOption> CreateViews()
    {
        return new[]
        {
            new Tiles3DExportViewOption
            {
                ViewId = new ElementId(101),
                UniqueId = "view-1",
                Title = "Default View",
                Description = "Default export view."
            },
            new Tiles3DExportViewOption
            {
                ViewId = new ElementId(102),
                UniqueId = "view-2",
                Title = "Export View",
                Description = "Focused export view."
            }
        };
    }

    private static IReadOnlyCollection<Tiles3DExportLinkOption> CreateLinks()
    {
        return new[]
        {
            new Tiles3DExportLinkOption
            {
                LinkInstanceId = new ElementId(201),
                UniqueId = "link-1",
                Title = "Architectural Link",
                Description = "Architectural linked model."
            },
            new Tiles3DExportLinkOption
            {
                LinkInstanceId = new ElementId(202),
                UniqueId = "link-2",
                Title = "Structural Link",
                Description = "Structural linked model."
            }
        };
    }

    private static Tiles3DExportPreparationResult CreatePreparedResult()
    {
        return new Tiles3DExportPreparationResult
        {
            Package = new Tiles3DExportPackage
            {
                ReferenceContext = new Tiles3DExportReferenceContext
                {
                    Title = "Canonical Origin",
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" }
                },
                Meshes = new List<Tiles3DMeshPrimitive>
                {
                    new Tiles3DMeshPrimitive
                    {
                        Name = "Triangle",
                        Triangles = new List<Tiles3DTriangle>
                        {
                            new Tiles3DTriangle(new Tiles3DPoint(0d, 0d, 0d), new Tiles3DPoint(1d, 0d, 0d), new Tiles3DPoint(0d, 1d, 0d))
                        }
                    }
                }
            },
            PreparedRows = new[] { new DetailRow("Exportable Elements", "1") },
            FeatureNames = new[] { "Triangle" },
            StatusMessage = "Prepared."
        };
    }
}