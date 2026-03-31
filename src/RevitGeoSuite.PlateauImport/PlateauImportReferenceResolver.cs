using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportReferenceResolver
{
    private readonly ICoordinateTransformer coordinateTransformer;

    public PlateauImportReferenceResolver(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public PlateauImportReferenceContext? Resolve(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        PlateauImportReferenceSource preferredSource)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            return null;
        }

        if (preferredSource == PlateauImportReferenceSource.WorkingProjectBasePoint)
        {
            if (currentState.StoredWorkingProjectBasePoint?.IsValid == true)
            {
                return new PlateauImportReferenceContext
                {
                    Title = "Working Project Base Point",
                    Description = "Uses the saved Working Project Base Point from georeference module state. This is the preferred local reference for PLATEAU context import.",
                    ProjectCrs = currentState.StoredWorkingProjectBasePoint.ProjectCrs!,
                    AnchorProjectedCoordinate = currentState.StoredWorkingProjectBasePoint.ProjectedCoordinate!.Value,
                    AnchorLatitude = currentState.StoredWorkingProjectBasePoint.Origin!.Latitude,
                    AnchorLongitude = currentState.StoredWorkingProjectBasePoint.Origin.Longitude,
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

                return new PlateauImportReferenceContext
                {
                    Title = "Revit Project Base Point",
                    Description = "Uses the current Revit Project Base Point estimate because no saved Working Project Base Point is available yet.",
                    ProjectCrs = info.ProjectCrs,
                    AnchorProjectedCoordinate = projectedCoordinate,
                    AnchorLatitude = currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                    AnchorLongitude = currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                    AnchorXFeet = currentState.ProjectBasePoint.XFeet,
                    AnchorYFeet = currentState.ProjectBasePoint.YFeet,
                    AnchorZFeet = currentState.ProjectBasePoint.ZFeet
                };
            }
        }

        ProjectedCoordinate canonicalProjectedCoordinate = coordinateTransformer.Project(
            new GeographicCoordinate(info.Origin.Latitude, info.Origin.Longitude),
            info.ProjectCrs);

        return new PlateauImportReferenceContext
        {
            Title = "Canonical Origin",
            Description = "Uses the canonical stored origin from GeoProjectInfo. This is the stable fallback when no working Project Base Point reference is available.",
            ProjectCrs = info.ProjectCrs,
            AnchorProjectedCoordinate = canonicalProjectedCoordinate,
            AnchorLatitude = info.Origin.Latitude,
            AnchorLongitude = info.Origin.Longitude,
            AnchorXFeet = currentState.SurveyPoint.XFeet,
            AnchorYFeet = currentState.SurveyPoint.YFeet,
            AnchorZFeet = currentState.SurveyPoint.ZFeet
        };
    }
}
