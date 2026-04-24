using System;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Localization;
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
        Assert.False(viewModel.CanNavigateToChooseCrs);
    }

    [Fact]
    public void New_project_requires_crs_and_valid_offsets_before_preview()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.ChooseCrs, viewModel.CurrentStep);
        Assert.True(viewModel.IsNewProjectMode);
        Assert.False(viewModel.CanGoNext);
        Assert.Contains(UiLocalizer.Instance.Get("Georef.Simple.Validation.SelectCrs"), viewModel.SetupValidationMessage);

        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        Assert.False(viewModel.CanGoNext);

        viewModel.ProjectBasePointOffsetXInput = "not-a-number";
        viewModel.ProjectBasePointOffsetYInput = "1000";
        Assert.False(viewModel.CanGoNext);

        viewModel.ProjectBasePointOffsetXInput = "1250.5";
        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void Plateau_grid_mode_requires_selected_grid_before_preview()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.IsPlateauGridCoordinateMode = true;

        Assert.True(viewModel.CanUsePlateauGridMode);
        Assert.True(viewModel.IsPlateauGridCoordinateMode);
        Assert.False(viewModel.CanGoNext);
        Assert.True(viewModel.PlateauGridOptions.Count > 0);
        Assert.Contains(UiLocalizer.Instance.Get("Georef.Grid.Validation.SelectGrid"), viewModel.SetupValidationMessage);

        Assert.True(viewModel.TogglePlateauGridSelection(viewModel.PlateauGridOptions.First().TileId));
        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void Plateau_grid_preview_uses_southwest_corner_of_sparse_selection_extent()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        viewModel.GoNext();
        CrsDefinition selectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.SelectedCrs = selectedCrs;
        viewModel.IsPlateauGridCoordinateMode = true;

        PlateauGridSelectionItem[] sparseSelection = SelectSparsePlateauGridPair(viewModel, out double expectedSouthLatitude, out double expectedWestLongitude);
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        MeshBounds[] selectedBounds = sparseSelection
            .Select(option => meshCalculator.GetBounds(new MeshCode { Value = option.TileId }))
            .ToArray();

        Assert.DoesNotContain(
            selectedBounds,
            bounds => AreClose(bounds.SouthLatitude, expectedSouthLatitude) && AreClose(bounds.WestLongitude, expectedWestLongitude));

        foreach (PlateauGridSelectionItem option in sparseSelection)
        {
            Assert.True(viewModel.TogglePlateauGridSelection(option.TileId));
        }

        Assert.Equal(2, viewModel.SelectedPlateauGridCount);
        Assert.True(viewModel.HasPlateauGridAnchor);
        Assert.Equal(expectedSouthLatitude, viewModel.PlateauGridAnchorLatitude!.Value, 10);
        Assert.Equal(expectedWestLongitude, viewModel.PlateauGridAnchorLongitude!.Value, 10);

        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.True(viewModel.HasPreview);

        SplitSurveyProjectBasePointIntent intent = viewModel.GetSplitApplyIntent();
        CoordinateTransformer transformer = CreateTransformer();
        ProjectedCoordinate expectedProjected = transformer.Project(
            new GeographicCoordinate(expectedSouthLatitude, expectedWestLongitude),
            selectedCrs.ToReference());

        Assert.Equal(expectedProjected.Easting, intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(expectedProjected.Northing, intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing, 6);
        Assert.Equal(expectedSouthLatitude, intent.LocalProjectBasePoint.Origin!.Latitude, 10);
        Assert.Equal(expectedWestLongitude, intent.LocalProjectBasePoint.Origin.Longitude, 10);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, UiLocalizer.Instance.Get("Georef.Grid.SetupSource.SelectionExtent"), 2),
            intent.SetupSource);
        Assert.Equal(intent.SetupSource, intent.LocalProjectBasePoint.SetupSource);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, UiLocalizer.Instance.Get("Georef.Grid.ProjectSummary.Projected"), 2, expectedProjected.Easting, expectedProjected.Northing),
            viewModel.ProjectBasePointOffsetSummary);
    }

    [Fact]
    public void Switching_between_manual_and_grid_modes_updates_validation_state()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ProjectBasePointOffsetXInput = "200";
        viewModel.ProjectBasePointOffsetYInput = "400";

        Assert.True(viewModel.IsManualCoordinateMode);
        Assert.True(viewModel.CanGoNext);

        viewModel.IsPlateauGridCoordinateMode = true;

        Assert.True(viewModel.IsPlateauGridCoordinateMode);
        Assert.False(viewModel.CanGoNext);

        Assert.True(viewModel.TogglePlateauGridSelection(viewModel.PlateauGridOptions.First().TileId));
        Assert.True(viewModel.CanGoNext);

        viewModel.IsManualCoordinateMode = true;

        Assert.True(viewModel.IsManualCoordinateMode);
        Assert.False(viewModel.IsPlateauGridCoordinateMode);
        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void Plateau_grid_mode_is_disabled_when_no_geographic_hint_exists()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummaryWithoutHints());

        viewModel.GoNext();

        Assert.False(viewModel.CanUsePlateauGridMode);
        Assert.True(viewModel.HasPlateauGridUnavailableMessage);
        Assert.Empty(viewModel.PlateauGridOptions);

        viewModel.IsPlateauGridCoordinateMode = true;

        Assert.False(viewModel.IsPlateauGridCoordinateMode);
        Assert.True(viewModel.IsManualCoordinateMode);
    }

    [Fact]
    public void New_project_preview_builds_split_intent_with_survey_origin_at_zero_zero()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        MoveToNewProjectPreview(viewModel, "1500.25", "-245.75");

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.UsesSplitApply);
        Assert.True(viewModel.Preview!.IsReadyToApply);

        SplitSurveyProjectBasePointIntent intent = viewModel.GetSplitApplyIntent();
        Assert.Equal(6677, intent.SelectedCrs!.EpsgCode);
        Assert.Equal(0d, intent.SharedSurveyProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(0d, intent.SharedSurveyProjectedCoordinate.Value.Northing, 6);
        Assert.Equal(1500.25, intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(-245.75, intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing, 6);
        Assert.NotEmpty(viewModel.PreviewFields);
        Assert.NotEmpty(viewModel.PreviewWhatWillChange);
    }

    [Fact]
    public void Existing_setup_mode_requires_confirmation_and_crs()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();

        Assert.True(viewModel.IsConfirmExistingSetupMode);
        Assert.False(viewModel.CanGoNext);

        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        Assert.False(viewModel.CanGoNext);

        viewModel.ConfirmExistingSetup = true;
        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void Existing_setup_preview_creates_metadata_only_intent_from_current_points()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ConfirmExistingSetup = true;
        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.True(viewModel.HasPreview);
        Assert.False(viewModel.UsesSplitApply);
        Assert.True(viewModel.Preview!.IsReadyToApply);

        PlacementIntent intent = viewModel.GetApplyIntent();
        Assert.Equal(PlacementApplyMode.MetadataOnly, intent.ApplyMode);
        Assert.Equal(6677, intent.SelectedCrs!.EpsgCode);
        Assert.Equal(0d, intent.SelectedProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(0d, intent.SelectedProjectedCoordinate.Value.Northing, 6);
        Assert.Equal(35.681236, intent.SelectedOrigin!.Latitude, 6);
        Assert.Equal(139.767125, intent.SelectedOrigin.Longitude, 6);
        Assert.NotNull(intent.WorkingProjectBasePoint);
        Assert.Equal(25.0, intent.WorkingProjectBasePoint!.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(-12.5, intent.WorkingProjectBasePoint.ProjectedCoordinate.Value.Northing, 6);
    }

    [Fact]
    public void Read_only_document_blocks_apply_even_after_preview()
    {
        CurrentProjectStateSummary summary = CreateExistingSetupSummary();
        summary.IsReadOnly = true;
        summary.StatusMessage = "This project is read-only. Preview is still available, but apply requires an editable model.";

        GeoreferenceViewModel viewModel = CreateViewModel(summary);
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ConfirmExistingSetup = true;
        viewModel.GoNext();

        Assert.True(viewModel.HasPreview);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void Override_existing_setup_switches_to_new_project_mode()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();

        Assert.True(viewModel.IsConfirmExistingSetupMode);
        Assert.False(viewModel.IsNewProjectMode);

        viewModel.OverrideExistingSetup = true;

        Assert.False(viewModel.IsConfirmExistingSetupMode);
        Assert.True(viewModel.IsNewProjectMode);
        Assert.True(viewModel.UsesSplitApply);
    }

    [Fact]
    public void Override_existing_setup_resets_confirmation_checkbox()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();
        viewModel.ConfirmExistingSetup = true;
        viewModel.OverrideExistingSetup = true;

        Assert.False(viewModel.ConfirmExistingSetup);
    }

    [Fact]
    public void Override_existing_setup_uses_new_project_validation()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();
        viewModel.OverrideExistingSetup = true;
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);

        Assert.False(viewModel.CanGoNext);

        viewModel.ProjectBasePointOffsetXInput = "500";
        viewModel.ProjectBasePointOffsetYInput = "300";

        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void Override_existing_setup_preview_builds_split_intent()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateExistingSetupSummary());

        viewModel.GoNext();
        viewModel.OverrideExistingSetup = true;
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ProjectBasePointOffsetXInput = "500";
        viewModel.ProjectBasePointOffsetYInput = "300";
        viewModel.GoNext();

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.True(viewModel.UsesSplitApply);
        Assert.True(viewModel.Preview!.IsReadyToApply);

        SplitSurveyProjectBasePointIntent intent = viewModel.GetSplitApplyIntent();
        Assert.Equal(500d, intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(300d, intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing, 6);
    }

    [Fact]
    public void Step_navigation_reaches_preview_only_when_setup_is_valid()
    {
        GeoreferenceViewModel viewModel = CreateViewModel(CreateNewProjectSummary());

        Assert.True(viewModel.CanNavigateToCurrentState);
        Assert.True(viewModel.CanNavigateToChooseCrs);
        Assert.False(viewModel.CanNavigateToPreview);

        viewModel.NavigateToStep(GeoreferenceStep.ChooseCrs);
        Assert.Equal(GeoreferenceStep.ChooseCrs, viewModel.CurrentStep);

        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ProjectBasePointOffsetXInput = "200";
        viewModel.ProjectBasePointOffsetYInput = "400";

        Assert.True(viewModel.CanNavigateToPreview);

        viewModel.NavigateToStep(GeoreferenceStep.Preview);

        Assert.Equal(GeoreferenceStep.Preview, viewModel.CurrentStep);
        Assert.True(viewModel.HasPreview);
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
            new PlacementPreviewService(validator),
            new SplitSurveyProjectBasePointPreviewService(validator));
    }

    private static CoordinateTransformer CreateTransformer()
    {
        return new CoordinateTransformer(new CrsRegistry());
    }

    private static void MoveToNewProjectPreview(GeoreferenceViewModel viewModel, string offsetX, string offsetY)
    {
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.ProjectBasePointOffsetXInput = offsetX;
        viewModel.ProjectBasePointOffsetYInput = offsetY;
        viewModel.GoNext();
    }

    private static PlateauGridSelectionItem[] SelectSparsePlateauGridPair(
        GeoreferenceViewModel viewModel,
        out double expectedSouthLatitude,
        out double expectedWestLongitude)
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        var candidates = viewModel.PlateauGridOptions
            .Select(option => (Option: option, Bounds: meshCalculator.GetBounds(new MeshCode { Value = option.TileId })))
            .ToArray();

        expectedWestLongitude = candidates.Min(candidate => candidate.Bounds.WestLongitude);
        expectedSouthLatitude = candidates.Min(candidate => candidate.Bounds.SouthLatitude);

        var westEdgeNorth = candidates
            .Where(candidate => AreClose(candidate.Bounds.WestLongitude, expectedWestLongitude))
            .OrderByDescending(candidate => candidate.Bounds.SouthLatitude)
            .First();
        var southEdgeEast = candidates
            .Where(candidate => AreClose(candidate.Bounds.SouthLatitude, expectedSouthLatitude))
            .OrderByDescending(candidate => candidate.Bounds.WestLongitude)
            .First();

        Assert.NotEqual(westEdgeNorth.Option.TileId, southEdgeEast.Option.TileId);

        return new[] { westEdgeNorth.Option, southEdgeEast.Option };
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) < 1e-9;
    }

    private static CurrentProjectStateSummary CreateNewProjectSummary()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Sample Project",
            IsSupportedDocument = true,
            SiteLatitudeDegrees = 35.681236,
            SiteLongitudeDegrees = 139.767125,
            ProjectPosition = new ProjectPositionSnapshot(),
            SurveyPoint = new BasePointSnapshot
            {
                Name = "Survey Point"
            },
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point"
            }
        };
    }

    private static CurrentProjectStateSummary CreateNewProjectSummaryWithoutHints()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Hintless Project",
            IsSupportedDocument = true,
            ProjectPosition = new ProjectPositionSnapshot(),
            SurveyPoint = new BasePointSnapshot
            {
                Name = "Survey Point"
            },
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point"
            }
        };
    }

    private static CurrentProjectStateSummary CreateExistingSetupSummary()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Existing Setup Project",
            IsSupportedDocument = true,
            ExistingSetupDetected = true,
            ExistingSetupMessage = "Existing coordinate setup detected from the current Survey Point and Project Base Point.",
            HasStoredGeoInfo = false,
            ProjectPosition = new ProjectPositionSnapshot(),
            SurveyPoint = new BasePointSnapshot
            {
                Name = "Survey Point",
                SharedEastWestFeet = 0d,
                SharedNorthSouthFeet = 0d,
                SharedElevationFeet = 0d,
                EstimatedLatitudeDegrees = 35.681236,
                EstimatedLongitudeDegrees = 139.767125
            },
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                SharedEastWestFeet = 25d / 0.3048d,
                SharedNorthSouthFeet = -12.5d / 0.3048d,
                SharedElevationFeet = 0d,
                EstimatedLatitudeDegrees = 35.680910,
                EstimatedLongitudeDegrees = 139.768015
            }
        };
    }
}
