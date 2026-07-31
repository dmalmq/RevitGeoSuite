using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ProjectLocationWriter
{
    private const double MetersToFeet = 1.0 / 0.3048d;

    public void Apply(Document document, PlacementIntent intent, double resolvedTrueNorthAngleDegrees)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (intent.ApplyMode == PlacementApplyMode.MetadataOnly)
        {
            return;
        }

        if (intent.SelectedOrigin is null || !intent.SelectedProjectedCoordinate.HasValue)
        {
            throw new InvalidOperationException("Placement intent is missing the origin or projected coordinates required for Revit project location updates.");
        }

        SiteLocation siteLocation = document.SiteLocation
            ?? throw new InvalidOperationException("The active Revit document does not expose a writable site location.");
        siteLocation.Latitude = DegreesToRadians(intent.SelectedOrigin.Latitude);
        siteLocation.Longitude = DegreesToRadians(intent.SelectedOrigin.Longitude);

        XYZ anchorPoint = ResolveAnchorPoint(document, intent.AnchorTarget);
        ProjectPosition projectPosition = new ProjectPosition(
            intent.SelectedProjectedCoordinate.Value.Easting * MetersToFeet,
            intent.SelectedProjectedCoordinate.Value.Northing * MetersToFeet,
            intent.SelectedOrigin.ElevationMeters * MetersToFeet,
            DegreesToRadians(resolvedTrueNorthAngleDegrees));

        document.ActiveProjectLocation.SetProjectPosition(anchorPoint, projectPosition);
    }

    public static PlacementAnchorTarget ResolveAnchorTarget(PlacementAnchorTarget anchorTarget)
    {
        return anchorTarget == PlacementAnchorTarget.Unspecified
            ? PlacementAnchorTarget.SurveyPoint
            : anchorTarget;
    }

    private static XYZ ResolveAnchorPoint(Document document, PlacementAnchorTarget anchorTarget)
    {
        return ResolveAnchorTarget(anchorTarget) switch
        {
            PlacementAnchorTarget.ProjectBasePoint => BasePoint.GetProjectBasePoint(document).Position,
            _ => BasePoint.GetSurveyPoint(document).Position
        };
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0d);
    }
}
