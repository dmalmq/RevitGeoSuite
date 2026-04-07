using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public sealed partial class GeoreferenceViewModel
{
    private string currentRevitSetupErrorMessage = string.Empty;

    public string CurrentRevitSetupErrorMessage
    {
        get => currentRevitSetupErrorMessage;
        private set
        {
            if (string.Equals(currentRevitSetupErrorMessage, value, System.StringComparison.Ordinal))
            {
                return;
            }

            currentRevitSetupErrorMessage = value ?? string.Empty;
            RaisePropertyChanged(nameof(CurrentRevitSetupErrorMessage));
            RaisePropertyChanged(nameof(HasCurrentRevitSetupErrorMessage));
        }
    }

    public bool HasCurrentRevitSetupErrorMessage => !string.IsNullOrWhiteSpace(CurrentRevitSetupErrorMessage);

    public bool HasReusableCurrentRevitSetup => CurrentState.SurveyPoint.HasEstimatedLocation || CurrentState.ProjectBasePoint.HasEstimatedLocation;

    public bool CanUseCurrentRevitSetup => SelectedCrs is not null && TryResolveCurrentRevitSetupLocation(out _, out _, out _, out _);

    public string CurrentRevitSetupButtonText => IsCapturingWorkingProjectBasePoint
        ? "Refresh Current Project Base Point"
        : ResolvePrimaryAnchorTarget() == PlacementAnchorTarget.ProjectBasePoint
            ? "Refresh Current Project Base Point"
            : "Refresh Current Survey Point";

    public string CurrentRevitSetupHint => BuildCurrentRevitSetupHint();

    public bool TryUseCurrentRevitSetup()
    {
        if (SelectedCrs is null)
        {
            CurrentRevitSetupErrorMessage = "Select a coordinate reference system before reading the current Revit setup.";
            return false;
        }

        if (!TryResolveCurrentRevitSetupLocation(out double latitude, out double longitude, out string sourceLabel, out PlacementAnchorTarget anchorTarget))
        {
            CurrentRevitSetupErrorMessage = BuildCurrentRevitSetupUnavailableMessage();
            return false;
        }

        SelectedMapPoint capturedPoint = BuildCurrentRevitSelectionPoint(latitude, longitude, sourceLabel, anchorTarget);
        ApplyCapturedPoint(capturedPoint);
        CurrentRevitSetupErrorMessage = string.Empty;
        return true;
    }

    private void InitializeCurrentRevitSetupSelections(bool overwritePrimary, bool overwriteWorkingProjectBasePoint)
    {
        if (SelectedCrs is null || SelectedSiteSelectionModeOption?.Mode != SiteSelectionInputMode.CurrentRevitSetup)
        {
            return;
        }

        bool applied = false;
        if ((overwritePrimary || SelectedPoint is null) && TryBuildCurrentRevitPrimaryPoint(out SelectedMapPoint? primaryPoint))
        {
            SelectedPoint = primaryPoint;
            applied = true;
        }

        if ((overwriteWorkingProjectBasePoint || WorkingProjectBasePoint is null)
            && TryBuildCurrentRevitWorkingProjectBasePoint(out SelectedMapPoint? workingPoint)
            && !WouldDuplicatePrimaryAnchor(workingPoint))
        {
            WorkingProjectBasePoint = workingPoint;
            applied = true;
        }

        if (applied)
        {
            DisplayedMapPoint = SelectedPoint ?? WorkingProjectBasePoint;
            CurrentRevitSetupErrorMessage = string.Empty;
        }
    }

    private bool TryBuildCurrentRevitPrimaryPoint(out SelectedMapPoint point)
    {
        point = null!;
        PlacementAnchorTarget anchorTarget = ResolvePrimaryAnchorTarget();
        if (anchorTarget == PlacementAnchorTarget.ProjectBasePoint)
        {
            return TryBuildCurrentRevitProjectBasePoint("Read from current Revit Project Base Point and project location", anchorTarget, out point);
        }

        return TryBuildCurrentRevitSurveyPoint("Read from current Revit Survey Point and project location", out point);
    }

    private bool TryBuildCurrentRevitWorkingProjectBasePoint(out SelectedMapPoint point)
    {
        return TryBuildCurrentRevitProjectBasePoint(
            "Read from current Revit Project Base Point and project location for Working Project Base Point",
            PlacementAnchorTarget.ProjectBasePoint,
            out point);
    }

    private bool TryResolveCurrentRevitSetupLocation(out double latitude, out double longitude, out string sourceLabel, out PlacementAnchorTarget anchorTarget)
    {
        latitude = 0d;
        longitude = 0d;
        sourceLabel = string.Empty;
        anchorTarget = PlacementAnchorTarget.Unspecified;

        if (IsCapturingWorkingProjectBasePoint)
        {
            if (TryBuildCurrentRevitWorkingProjectBasePoint(out SelectedMapPoint workingPoint))
            {
                latitude = workingPoint.Latitude;
                longitude = workingPoint.Longitude;
                sourceLabel = workingPoint.SourceLabel;
                anchorTarget = workingPoint.AnchorTarget;
                return true;
            }

            return false;
        }

        if (TryBuildCurrentRevitPrimaryPoint(out SelectedMapPoint primaryPoint))
        {
            latitude = primaryPoint.Latitude;
            longitude = primaryPoint.Longitude;
            sourceLabel = primaryPoint.SourceLabel;
            anchorTarget = primaryPoint.AnchorTarget;
            return true;
        }

        return false;
    }

    private PlacementAnchorTarget ResolvePrimaryAnchorTarget()
    {
        if (IsSplitWorkflowMode)
        {
            return PlacementAnchorTarget.SurveyPoint;
        }

        return SelectedAnchorTargetOption?.Target ?? PlacementAnchorTarget.SurveyPoint;
    }

    private string BuildCurrentRevitSetupHint()
    {
        string availability = CanUseCurrentRevitSetup
            ? IsSplitWorkflowMode
                ? (IsCapturingWorkingProjectBasePoint
                    ? "Readable current Revit Project Base Point values can be preloaded as the local split-workflow Project Base Point. Use refresh only if the Project Base Point changed while this window was open."
                    : "Readable current Revit Survey Point values can be preloaded as the shared split-workflow Survey target. Use refresh only if the Survey Point changed while this window was open.")
                : "Readable Survey Point, Project Base Point, and true north values from the current Revit project are preloaded automatically for review. Use the refresh button if the project location changed while the window is open."
            : BuildCurrentRevitSetupUnavailableMessage();

        return availability + " Project north remains the current model-axis reference; Revit V1 does not expose a separate standalone project north angle to store here.";
    }

    private string BuildCurrentRevitSetupUnavailableMessage()
    {
        if (IsCapturingWorkingProjectBasePoint)
        {
            return "Current Revit Project Base Point could not be converted. Make sure the project already has a readable site/project location setup.";
        }

        return ResolvePrimaryAnchorTarget() == PlacementAnchorTarget.ProjectBasePoint
            ? "Current Revit Project Base Point could not be converted. Make sure the project already has a readable site/project location setup."
            : "Current Revit Survey Point could not be converted. Make sure the project already has a readable site/project location setup.";
    }

    private SelectedMapPoint ReprojectCapturedPoint(SelectedMapPoint point)
    {
        return new SelectedMapPoint
        {
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            ProjectedCoordinate = coordinateTransformer.Project(
                new GeographicCoordinate(point.Latitude, point.Longitude),
                SelectedCrs!.ToReference()),
            SourceLabel = point.SourceLabel,
            ConfidenceLabel = point.ConfidenceLabel,
            ConfidenceLevel = point.ConfidenceLevel,
            AnchorTarget = point.AnchorTarget,
            ReprojectWithSelectedCrs = point.ReprojectWithSelectedCrs,
            IsKnownCoordinateInput = point.IsKnownCoordinateInput
        };
    }

    private bool TryBuildCurrentRevitSurveyPoint(string sourceLabel, out SelectedMapPoint point)
    {
        point = null!;
        if (!CurrentState.SurveyPoint.HasEstimatedLocation)
        {
            return false;
        }

        point = BuildCurrentRevitSelectionPoint(
            CurrentState.SurveyPoint.EstimatedLatitudeDegrees!.Value,
            CurrentState.SurveyPoint.EstimatedLongitudeDegrees!.Value,
            sourceLabel,
            PlacementAnchorTarget.SurveyPoint);
        return true;
    }

    private bool TryBuildCurrentRevitProjectBasePoint(string sourceLabel, PlacementAnchorTarget anchorTarget, out SelectedMapPoint point)
    {
        point = null!;
        if (!CurrentState.ProjectBasePoint.HasEstimatedLocation)
        {
            return false;
        }

        point = BuildCurrentRevitSelectionPoint(
            CurrentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
            CurrentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
            sourceLabel,
            anchorTarget);
        return true;
    }

    private SelectedMapPoint BuildCurrentRevitSelectionPoint(double latitude, double longitude, string sourceLabel, PlacementAnchorTarget anchorTarget)
    {
        ProjectedCoordinate projectedCoordinate = coordinateTransformer.Project(
            new GeographicCoordinate(latitude, longitude),
            SelectedCrs!.ToReference());

        return new SelectedMapPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            ProjectedCoordinate = projectedCoordinate,
            SourceLabel = sourceLabel,
            ConfidenceLabel = "Verified (read from current Revit setup)",
            ConfidenceLevel = GeoConfidenceLevel.Verified,
            AnchorTarget = anchorTarget,
            ReprojectWithSelectedCrs = true,
            IsKnownCoordinateInput = false
        };
    }

    private bool WouldDuplicatePrimaryAnchor(SelectedMapPoint candidate)
    {
        return SelectedPoint is not null
            && SelectedPoint.AnchorTarget == PlacementAnchorTarget.ProjectBasePoint
            && System.Math.Abs(SelectedPoint.Latitude - candidate.Latitude) < 0.0000001d
            && System.Math.Abs(SelectedPoint.Longitude - candidate.Longitude) < 0.0000001d;
    }
}


