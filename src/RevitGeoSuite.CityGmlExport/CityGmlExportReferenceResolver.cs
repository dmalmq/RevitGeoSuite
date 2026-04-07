using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportReferenceResolver
{
    private const double FeetToMeters = 0.3048d;
    private readonly ICoordinateTransformer coordinateTransformer;

    public CityGmlExportReferenceResolver(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public CityGmlExportReferenceContext? Resolve(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        CityGmlExportReferenceSource preferredSource)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            return null;
        }

        if (preferredSource == CityGmlExportReferenceSource.WorkingProjectBasePoint)
        {
            if (currentState.StoredWorkingProjectBasePoint?.IsValid == true)
            {
                return new CityGmlExportReferenceContext
                {
                    Title = "Working Project Base Point",
                    Description = "Uses the saved Working Project Base Point from georeference module state. This is the preferred local reference when the model has a practical working anchor.",
                    ProjectCrs = currentState.StoredWorkingProjectBasePoint.ProjectCrs!,
                    AnchorProjectedCoordinate = currentState.StoredWorkingProjectBasePoint.ProjectedCoordinate!.Value,
                    AnchorLatitude = currentState.StoredWorkingProjectBasePoint.Origin!.Latitude,
                    AnchorLongitude = currentState.StoredWorkingProjectBasePoint.Origin.Longitude,
                    AnchorElevationMeters = currentState.StoredWorkingProjectBasePoint.Origin.ElevationMeters,
                    AnchorXFeet = currentState.ProjectBasePoint.XFeet,
                    AnchorYFeet = currentState.ProjectBasePoint.YFeet,
                    AnchorZFeet = currentState.ProjectBasePoint.ZFeet
                };
            }

            if (currentState.ProjectBasePoint.HasEstimatedLocation)
            {
                ProjectedCoordinate projectedCoordinate = coordinateTransformer.Project(
                    new GeographicCoordinate(
                        currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                        currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value),
                    info.ProjectCrs);

                double elevationMeters = info.Origin.ElevationMeters + ((currentState.ProjectBasePoint.ZFeet - currentState.SurveyPoint.ZFeet) * FeetToMeters);
                return new CityGmlExportReferenceContext
                {
                    Title = "Revit Project Base Point",
                    Description = "Uses the current Revit Project Base Point estimate because no saved Working Project Base Point is available yet.",
                    ProjectCrs = info.ProjectCrs,
                    AnchorProjectedCoordinate = projectedCoordinate,
                    AnchorLatitude = currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                    AnchorLongitude = currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                    AnchorElevationMeters = elevationMeters,
                    AnchorXFeet = currentState.ProjectBasePoint.XFeet,
                    AnchorYFeet = currentState.ProjectBasePoint.YFeet,
                    AnchorZFeet = currentState.ProjectBasePoint.ZFeet
                };
            }
        }

        ProjectedCoordinate canonicalProjectedCoordinate = coordinateTransformer.Project(
            new GeographicCoordinate(info.Origin.Latitude, info.Origin.Longitude),
            info.ProjectCrs);

        return new CityGmlExportReferenceContext
        {
            Title = "Canonical Origin",
            Description = "Uses the canonical stored origin from GeoProjectInfo. This is the stable fallback for CityGML export when no project-base-point reference is available.",
            ProjectCrs = info.ProjectCrs,
            AnchorProjectedCoordinate = canonicalProjectedCoordinate,
            AnchorLatitude = info.Origin.Latitude,
            AnchorLongitude = info.Origin.Longitude,
            AnchorElevationMeters = info.Origin.ElevationMeters,
            AnchorXFeet = currentState.SurveyPoint.XFeet,
            AnchorYFeet = currentState.SurveyPoint.YFeet,
            AnchorZFeet = currentState.SurveyPoint.ZFeet
        };
    }
}
