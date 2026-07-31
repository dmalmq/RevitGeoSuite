using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public sealed class SiteSelectionService
{
    public PlacementIntent CreateIntent(
        CrsDefinition selectedCrs,
        SelectedMapPoint selectedPoint,
        PlacementApplyMode applyMode,
        string setupSource,
        double? trueNorthAngleDegrees,
        SelectedMapPoint? workingProjectBasePoint = null)
    {
        return new PlacementIntent
        {
            SelectedCrs = selectedCrs.ToReference(),
            SelectedOrigin = new ProjectOrigin
            {
                Latitude = selectedPoint.Latitude,
                Longitude = selectedPoint.Longitude,
                ElevationMeters = 0d
            },
            SelectedProjectedCoordinate = selectedPoint.ProjectedCoordinate,
            TrueNorthAngle = trueNorthAngleDegrees,
            Confidence = selectedPoint.ConfidenceLevel,
            SetupSource = setupSource,
            ApplyMode = applyMode,
            AnchorTarget = selectedPoint.AnchorTarget,
            WorkingProjectBasePoint = workingProjectBasePoint is null
                ? null
                : new WorkingProjectBasePointReference
                {
                    ProjectCrs = selectedCrs.ToReference(),
                    Origin = new ProjectOrigin
                    {
                        Latitude = workingProjectBasePoint.Latitude,
                        Longitude = workingProjectBasePoint.Longitude,
                        ElevationMeters = 0d
                    },
                    ProjectedCoordinate = workingProjectBasePoint.ProjectedCoordinate,
                    Confidence = workingProjectBasePoint.ConfidenceLevel,
                    SetupSource = workingProjectBasePoint.SourceLabel
                }
        };
    }
}
