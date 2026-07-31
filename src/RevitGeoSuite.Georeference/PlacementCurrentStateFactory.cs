using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

internal static class PlacementCurrentStateFactory
{
    private const double FeetToMeters = 0.3048d;

    public static PlacementCurrentState Create(CurrentProjectStateSummary summary)
    {
        ProjectOrigin? currentOrigin = null;
        string currentOriginSource = string.Empty;

        if (summary.StoredOrigin is not null)
        {
            currentOrigin = summary.StoredOrigin;
            currentOriginSource = "stored shared geo metadata";
        }
        else if (summary.ProjectBasePoint.HasEstimatedLocation)
        {
            currentOrigin = new ProjectOrigin
            {
                Latitude = summary.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                Longitude = summary.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                ElevationMeters = summary.ProjectBasePoint.ZFeet * FeetToMeters
            };
            currentOriginSource = "estimated from the current Project Base Point and active project location";
        }
        else if (summary.SiteLatitudeDegrees.HasValue && summary.SiteLongitudeDegrees.HasValue)
        {
            currentOrigin = new ProjectOrigin
            {
                Latitude = summary.SiteLatitudeDegrees.Value,
                Longitude = summary.SiteLongitudeDegrees.Value,
                ElevationMeters = summary.ProjectPosition.ElevationFeet * FeetToMeters
            };
            currentOriginSource = "inferred from the Revit site location";
        }

        return new PlacementCurrentState
        {
            CurrentCrs = summary.StoredCrs,
            CurrentOrigin = currentOrigin,
            CurrentOriginSource = currentOriginSource,
            CurrentTrueNorthAngleDegrees = summary.StoredTrueNorthAngle ?? summary.ProjectPosition.AngleDegrees,
            CurrentConfidence = summary.StoredConfidence,
            CurrentSetupSource = summary.SetupSource,
            HasStoredGeoMetadata = summary.HasStoredGeoInfo,
            ExistingSetupDetected = summary.ExistingSetupDetected,
            CurrentWorkingProjectBasePoint = summary.StoredWorkingProjectBasePoint
        };
    }
}
