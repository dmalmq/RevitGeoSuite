using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class ProjectBasePointMoveViewModelTests
{
    [Fact]
    public void Advanced_move_stays_blocked_without_established_georeference_context()
    {
        CurrentProjectStateSummary summary = CreateSummary(existingSetupDetected: false, hasStoredGeoInfo: false, withReadableProjectBasePointSharedPosition: true);
        GeoreferenceViewModel viewModel = CreateViewModel(summary);

        CaptureWorkingProjectBasePointFromMap(viewModel);

        Assert.True(viewModel.HasActualProjectBasePointMoveTarget);
        Assert.False(viewModel.CanShowActualProjectBasePointMoveSection);
        Assert.False(viewModel.CanMoveActualProjectBasePoint);
        Assert.Contains("Complete and save the normal georeference setup first", viewModel.ActualProjectBasePointMoveStatusMessage);
    }

    [Fact]
    public void Advanced_move_enables_when_context_target_and_readable_shared_position_exist()
    {
        CurrentProjectStateSummary summary = CreateSummary(existingSetupDetected: true, hasStoredGeoInfo: false, withReadableProjectBasePointSharedPosition: true);
        GeoreferenceViewModel viewModel = CreateViewModel(summary);

        CaptureWorkingProjectBasePointFromMap(viewModel);

        Assert.True(viewModel.CanShowActualProjectBasePointMoveSection);
        Assert.True(viewModel.HasActualProjectBasePointMoveTarget);
        Assert.True(viewModel.CanMoveActualProjectBasePoint);
        Assert.False(viewModel.IsActualProjectBasePointMoveNoOp);
        Assert.False(viewModel.DoesActualProjectBasePointMoveExceedDistanceLimit);
        Assert.Contains(viewModel.ActualProjectBasePointMoveRows, row => row.Label == "Shared Coordinate Behavior");
        Assert.Contains(viewModel.ActualProjectBasePointMoveRows, row => row.Label == "Elevation Behavior");
        Assert.Contains(viewModel.ActualProjectBasePointMoveRows, row => row.Label == "Move Feasibility" && row.Value.Contains("Within Revit"));
    }

    [Fact]
    public void Advanced_move_detects_no_op_when_working_point_matches_current_shared_position()
    {
        CurrentProjectStateSummary summary = CreateSummary(existingSetupDetected: true, hasStoredGeoInfo: false, withReadableProjectBasePointSharedPosition: true);
        summary.ProjectBasePoint.SharedEastWestFeet = 100d / 0.3048d;
        summary.ProjectBasePoint.SharedNorthSouthFeet = 200d / 0.3048d;
        summary.ProjectBasePoint.SharedElevationFeet = 10d / 0.3048d;

        GeoreferenceViewModel viewModel = CreateViewModel(summary);
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SelectedCaptureTargetOption = viewModel.CaptureTargetOptions.Single(option => option.Target == ReferenceCaptureTarget.WorkingProjectBasePoint);
        viewModel.SelectedSiteSelectionModeOption = viewModel.SiteSelectionModeOptions.Single(option => option.Mode == SiteSelectionInputMode.KnownCoordinates);
        viewModel.KnownCoordinateEastingInput = "100";
        viewModel.KnownCoordinateNorthingInput = "200";

        bool success = viewModel.TryUseKnownCoordinates();

        Assert.True(success);
        Assert.True(viewModel.HasActualProjectBasePointMoveTarget);
        Assert.True(viewModel.IsActualProjectBasePointMoveNoOp);
        Assert.False(viewModel.CanMoveActualProjectBasePoint);
        Assert.Contains("already matches", viewModel.ActualProjectBasePointMoveStatusMessage);
    }

    [Fact]
    public void Advanced_move_is_blocked_when_target_exceeds_revit_local_distance_limit()
    {
        CurrentProjectStateSummary summary = CreateSummary(existingSetupDetected: true, hasStoredGeoInfo: true, withReadableProjectBasePointSharedPosition: true);
        summary.ProjectBasePoint.SharedEastWestFeet = 0d;
        summary.ProjectBasePoint.SharedNorthSouthFeet = 0d;
        summary.ProjectBasePoint.SharedElevationFeet = 0d;

        GeoreferenceViewModel viewModel = CreateViewModel(summary);
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SelectedCaptureTargetOption = viewModel.CaptureTargetOptions.Single(option => option.Target == ReferenceCaptureTarget.WorkingProjectBasePoint);
        viewModel.SelectedSiteSelectionModeOption = viewModel.SiteSelectionModeOptions.Single(option => option.Mode == SiteSelectionInputMode.KnownCoordinates);
        viewModel.KnownCoordinateEastingInput = "25000";
        viewModel.KnownCoordinateNorthingInput = "0";

        bool success = viewModel.TryUseKnownCoordinates();

        Assert.True(success);
        Assert.True(viewModel.HasActualProjectBasePointMoveTarget);
        Assert.True(viewModel.DoesActualProjectBasePointMoveExceedDistanceLimit);
        Assert.False(viewModel.CanMoveActualProjectBasePoint);
        Assert.Contains("roughly 16.1 km", viewModel.ActualProjectBasePointMoveStatusMessage);
        Assert.Contains("PLATEAU and export workflows", viewModel.ActualProjectBasePointMoveStatusMessage);
    }

    private static void CaptureWorkingProjectBasePointFromMap(GeoreferenceViewModel viewModel)
    {
        viewModel.GoNext();
        viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
        viewModel.GoNext();
        viewModel.SelectedCaptureTargetOption = viewModel.CaptureTargetOptions.Single(option => option.Target == ReferenceCaptureTarget.WorkingProjectBasePoint);
        viewModel.SetSelectedMapPoint(35.681236, 139.767125);
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

    private static CurrentProjectStateSummary CreateSummary(bool existingSetupDetected, bool hasStoredGeoInfo, bool withReadableProjectBasePointSharedPosition)
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Advanced PBP Move Project",
            IsSupportedDocument = true,
            ExistingSetupDetected = existingSetupDetected,
            HasStoredGeoInfo = hasStoredGeoInfo,
            SiteLatitudeDegrees = 35.681236,
            SiteLongitudeDegrees = 139.767125,
            ProjectPosition = new ProjectPositionSnapshot(),
            SurveyPoint = new BasePointSnapshot
            {
                Name = "Survey Point",
                EstimatedLatitudeDegrees = 35.681236,
                EstimatedLongitudeDegrees = 139.767125
            },
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                XFeet = 12.5d,
                YFeet = -8.25d,
                ZFeet = 3.0d,
                SharedEastWestFeet = withReadableProjectBasePointSharedPosition ? 25d / 0.3048d : null,
                SharedNorthSouthFeet = withReadableProjectBasePointSharedPosition ? 30d / 0.3048d : null,
                SharedElevationFeet = withReadableProjectBasePointSharedPosition ? 3d / 0.3048d : null,
                EstimatedLatitudeDegrees = 35.681100,
                EstimatedLongitudeDegrees = 139.767200
            }
        };
    }
}
