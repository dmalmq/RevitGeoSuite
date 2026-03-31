using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class GeoreferenceViewModelTests
{
    [Fact]
    public void Unsupported_document_blocks_progress_on_current_state_step()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(new CurrentProjectStateSummary
        {
            DocumentTitle = "Family.rfa",
            IsSupportedDocument = false,
            StatusMessage = "Family documents are not supported by the georeference workflow."
        });

        Assert.Equal(GeoreferenceStep.CurrentState, viewModel.CurrentStep);
        Assert.False(viewModel.CanGoNext);
        Assert.True(viewModel.HasStatusMessage);
    }

    [Fact]
    public void Csr_selection_is_required_before_map_step_can_continue()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());

        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.ChooseCrs, viewModel.CurrentStep);
        Assert.False(viewModel.CanGoNext);

        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);

        Assert.True(viewModel.CanGoNext);
        Assert.Contains("EPSG:6677", viewModel.SelectedCrsSummary);
    }

    [Fact]
    public void Selecting_map_point_projects_it_into_the_selected_crs_and_enables_review()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();

        viewModel.SetSelectedMapPoint(35.681236, 139.767125);

        Assert.True(viewModel.HasSelectedPoint);
        Assert.True(viewModel.CanGoNext);
        Assert.NotNull(viewModel.SelectedPoint);
        Assert.Equal(-5992.9196, viewModel.SelectedPoint!.ProjectedCoordinate.Easting, 3);
        Assert.Equal(-35363.2377, viewModel.SelectedPoint.ProjectedCoordinate.Northing, 3);

        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.ReviewPoint, viewModel.CurrentStep);
        Assert.Contains(viewModel.SelectedPointRows, row => row.Label == "Source" && row.Value == "Selected from OSM map");
    }

    [Fact]
    public void Known_crs_coordinates_can_define_survey_point_origin_before_preview()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();

        viewModel.SelectedSiteSelectionModeOption = viewModel.SiteSelectionModeOptions.Single(option => option.Mode == SiteSelectionInputMode.KnownCoordinates);
        viewModel.KnownCoordinateEastingInput = "0";
        viewModel.KnownCoordinateNorthingInput = "0";
        viewModel.SelectedAnchorTargetOption = viewModel.AnchorTargetOptions.Single(option => option.Target == PlacementAnchorTarget.SurveyPoint);

        bool success = viewModel.TryUseKnownCoordinates();

        Assert.True(success);
        Assert.NotNull(viewModel.SelectedPoint);
        Assert.Equal(36.0, viewModel.SelectedPoint!.Latitude, 3);
        Assert.Equal(139.833333, viewModel.SelectedPoint.Longitude, 3);
        Assert.Equal(PlacementAnchorTarget.SurveyPoint, viewModel.SelectedPoint.AnchorTarget);
        Assert.True(viewModel.SelectedPoint.IsKnownCoordinateInput);
        Assert.Contains(viewModel.SelectedPointRows, row => row.Label == "Anchor Target" && row.Value == "Survey Point");
    }

    [Fact]
    public void Working_project_base_point_can_be_captured_without_replacing_primary_anchor()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();

        viewModel.SetSelectedMapPoint(35.681236, 139.767125);
        viewModel.SelectedCaptureTargetOption = viewModel.CaptureTargetOptions.Single(option => option.Target == ReferenceCaptureTarget.WorkingProjectBasePoint);
        viewModel.SelectedSiteSelectionModeOption = viewModel.SiteSelectionModeOptions.Single(option => option.Mode == SiteSelectionInputMode.KnownCoordinates);
        viewModel.KnownCoordinateEastingInput = "120";
        viewModel.KnownCoordinateNorthingInput = "340";

        bool success = viewModel.TryUseKnownCoordinates();

        Assert.True(success);
        Assert.NotNull(viewModel.SelectedPoint);
        Assert.True(viewModel.HasWorkingProjectBasePoint);
        Assert.NotNull(viewModel.WorkingProjectBasePoint);
        Assert.Contains(viewModel.WorkingProjectBasePointRows, row => row.Label == "Role" && row.Value == "Working Project Base Point");
        Assert.NotEqual(viewModel.SelectedPointSummary, viewModel.WorkingProjectBasePointSummary);
    }

    [Fact]
    public void Existing_setup_message_is_exposed_for_current_state_screen()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(new CurrentProjectStateSummary
        {
            DocumentTitle = "Existing Setup Project",
            IsSupportedDocument = true,
            ExistingSetupDetected = true,
            ExistingSetupMessage = "Existing coordinate setup detected because stored geo metadata already exists."
        });

        Assert.True(viewModel.HasExistingSetupMessage);
        Assert.Contains("stored geo metadata", viewModel.ExistingSetupMessage);
    }

    [Fact]
    public void Site_and_project_base_point_location_availability_are_exposed_for_map_zoom_actions()
    {
        GeoreferenceViewModel withAnchors = CreateViewModel(CreateSupportedSummary());
        GeoreferenceViewModel withStoredWorkingPoint = CreateViewModel(new CurrentProjectStateSummary
        {
            DocumentTitle = "Stored Working Point Project",
            IsSupportedDocument = true,
            ProjectPosition = new ProjectPositionSnapshot(),
            ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" },
            StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new RevitGeoSuite.Core.ProjectMetadata.ProjectOrigin { Latitude = 35.689592, Longitude = 139.700413, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(10.0, 20.0),
                Confidence = RevitGeoSuite.Core.ProjectMetadata.GeoConfidenceLevel.Verified,
                SetupSource = "Stored working point"
            }
        });
        GeoreferenceViewModel withoutAnchors = CreateViewModel(new CurrentProjectStateSummary
        {
            DocumentTitle = "No Site Coordinates",
            IsSupportedDocument = true,
            ProjectPosition = new ProjectPositionSnapshot(),
            ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
        });

        Assert.True(withAnchors.HasSiteLocation);
        Assert.True(withAnchors.HasProjectBasePointLocation);
        Assert.Equal("Zoom To Project Base Point", withAnchors.ProjectBasePointZoomButtonText);
        Assert.True(withStoredWorkingPoint.HasProjectBasePointLocation);
        Assert.Equal("Zoom To Working Project Base Point", withStoredWorkingPoint.ProjectBasePointZoomButtonText);
        Assert.True(withStoredWorkingPoint.TryGetProjectBasePointLocation(out double workingLatitude, out double workingLongitude));
        Assert.Equal(35.689592, workingLatitude, 6);
        Assert.Equal(139.700413, workingLongitude, 6);
        Assert.False(withoutAnchors.HasSiteLocation);
        Assert.False(withoutAnchors.HasProjectBasePointLocation);
    }

    [Fact]
    public void Workflow_reaches_preview_and_enables_apply_only_after_setup_intent_is_valid()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());

        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SetSelectedMapPoint(35.681236, 139.767125);
        viewModel.GoNext();
        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.SetupIntent, viewModel.CurrentStep);
        Assert.True(viewModel.CanGoNext);
        Assert.Equal("Preview", viewModel.NextButtonText);

        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.False(viewModel.ShowNextButton);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanApply);
        Assert.Contains(viewModel.PreviewFields, field => field.Label == "CRS");
        Assert.Contains(viewModel.PreviewFields, field => field.Label == "Projected Coordinate");
        Assert.Contains(viewModel.PreviewWhatWillNotChange, line => line.IndexOf("True north angle", System.StringComparison.OrdinalIgnoreCase) >= 0);

        PlacementIntent applyIntent = viewModel.GetApplyIntent();
        Assert.Equal(6677, applyIntent.SelectedCrs!.EpsgCode);
        Assert.True(applyIntent.SelectedProjectedCoordinate.HasValue);
    }

    [Fact]
    public void Read_only_document_blocks_apply_even_after_preview()
    {
        CurrentProjectStateSummary summary = CreateSupportedSummary();
        summary.IsReadOnly = true;
        summary.StatusMessage = "This project is read-only. Preview is still available, but apply requires an editable model.";

        GeoreferenceViewModel viewModel = CreateViewModel(summary);
        MoveToPreview(viewModel);

        Assert.True(viewModel.HasPreview);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void Angle_mode_requires_parseable_true_north_angle_before_preview()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateSupportedSummary());

        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SetSelectedMapPoint(35.681236, 139.767125);
        viewModel.GoNext();
        viewModel.GoNext();

        viewModel.SelectedApplyModeOption = viewModel.ApplyModeOptions.Single(option => option.Mode == PlacementApplyMode.ProjectLocationAndAngle);
        viewModel.TrueNorthAngleInput = "not-a-number";

        Assert.False(viewModel.CanGoNext);

        viewModel.TrueNorthAngleInput = "22.5";

        Assert.True(viewModel.CanGoNext);
    }

    private static GeoreferenceViewModel CreateViewModel(CurrentProjectStateSummary summary)
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        CoordinateValidator validator = new CoordinateValidator(registry, transformer, new JapanMeshCalculator());
        return new GeoreferenceViewModel(
            summary,
            registry.GetAvailableDefinitions(),
            transformer,
            new SiteSelectionService(),
            new PlacementPreviewService(validator));
    }

    private static void MoveToPreview(GeoreferenceViewModel viewModel)
    {
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SetSelectedMapPoint(35.681236, 139.767125);
        viewModel.GoNext();
        viewModel.GoNext();
        viewModel.GoNext();
    }

    private static CurrentProjectStateSummary CreateSupportedSummary()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Sample Project",
            IsSupportedDocument = true,
            SiteLatitudeDegrees = 35.681236,
            SiteLongitudeDegrees = 139.767125,
            ProjectPosition = new ProjectPositionSnapshot(),
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                EstimatedLatitudeDegrees = 35.680910,
                EstimatedLongitudeDegrees = 139.768015
            }
        };
    }
}




