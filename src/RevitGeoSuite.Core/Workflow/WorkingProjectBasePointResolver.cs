using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public static class WorkingProjectBasePointResolver
{
    public static WorkingProjectBasePointReference? Resolve(PlacementIntent intent)
    {
        if (intent is null)
        {
            return null;
        }

        if (intent.WorkingProjectBasePoint?.IsValid == true)
        {
            return intent.WorkingProjectBasePoint;
        }

        if (intent.AnchorTarget != PlacementAnchorTarget.ProjectBasePoint
            || intent.SelectedCrs is null
            || intent.SelectedOrigin is null
            || !intent.SelectedProjectedCoordinate.HasValue
            || !intent.SelectedProjectedCoordinate.Value.IsFinite)
        {
            return null;
        }

        return new WorkingProjectBasePointReference
        {
            ProjectCrs = intent.SelectedCrs,
            Origin = new ProjectOrigin
            {
                Latitude = intent.SelectedOrigin.Latitude,
                Longitude = intent.SelectedOrigin.Longitude,
                ElevationMeters = intent.SelectedOrigin.ElevationMeters
            },
            ProjectedCoordinate = intent.SelectedProjectedCoordinate,
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource
        };
    }
}
