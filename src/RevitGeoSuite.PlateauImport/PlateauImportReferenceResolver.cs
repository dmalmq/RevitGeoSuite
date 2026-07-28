using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportReferenceResolver
{
    private const double FeetToMeters = 0.3048d;
    private readonly ICoordinateTransformer coordinateTransformer;
    private readonly IPlateauImportLocalBasisProvider localBasisProvider;

    public PlateauImportReferenceResolver(ICoordinateTransformer coordinateTransformer, IPlateauImportLocalBasisProvider? localBasisProvider = null)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
        this.localBasisProvider = localBasisProvider ?? new IdentityPlateauImportLocalBasisProvider();
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
            if (currentState.ProjectBasePoint.HasSharedPosition)
            {
                ProjectedCoordinate projectedCoordinate = new ProjectedCoordinate(
                    currentState.ProjectBasePoint.SharedEastWestFeet!.Value * FeetToMeters,
                    currentState.ProjectBasePoint.SharedNorthSouthFeet!.Value * FeetToMeters);
                GeographicCoordinate geographicCoordinate = coordinateTransformer.Unproject(projectedCoordinate, info.ProjectCrs);
                double anchorElevationMeters = currentState.ProjectBasePoint.SharedElevationFeet!.Value * FeetToMeters;

                return CreateContext(
                    title: "Revit Project Base Point",
                    description: "Uses the current Revit Project Base Point shared coordinates for PLATEAU placement. A saved Working Project Base Point is only used as a fallback when the active Revit Project Base Point location cannot be resolved.",
                    projectCrs: info.ProjectCrs,
                    anchorProjectedCoordinate: projectedCoordinate,
                    anchorLatitude: geographicCoordinate.Latitude,
                    anchorLongitude: geographicCoordinate.Longitude,
                    anchorElevationMeters: anchorElevationMeters,
                    anchorXFeet: currentState.ProjectBasePoint.XFeet,
                    anchorYFeet: currentState.ProjectBasePoint.YFeet,
                    anchorZFeet: currentState.ProjectBasePoint.ZFeet);
            }

            if (currentState.ProjectBasePoint.HasEstimatedLocation)
            {
                ProjectedCoordinate projectedCoordinate = coordinateTransformer.Project(
                    new GeographicCoordinate(
                        currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                        currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value),
                    info.ProjectCrs);
                double anchorElevationMeters = info.Origin.ElevationMeters + ((currentState.ProjectBasePoint.ZFeet - currentState.SurveyPoint.ZFeet) * FeetToMeters);

                return CreateContext(
                    title: "Revit Project Base Point",
                    description: "Uses the estimated current Revit Project Base Point location for PLATEAU placement because direct shared coordinates were not available.",
                    projectCrs: info.ProjectCrs,
                    anchorProjectedCoordinate: projectedCoordinate,
                    anchorLatitude: currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                    anchorLongitude: currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                    anchorElevationMeters: anchorElevationMeters,
                    anchorXFeet: currentState.ProjectBasePoint.XFeet,
                    anchorYFeet: currentState.ProjectBasePoint.YFeet,
                    anchorZFeet: currentState.ProjectBasePoint.ZFeet);
            }

            if (currentState.StoredWorkingProjectBasePoint?.IsValid == true)
            {
                return CreateContext(
                    title: "Saved Working Project Base Point",
                    description: "Uses the saved Working Project Base Point from georeference module state because the active Revit Project Base Point could not be resolved.",
                    projectCrs: currentState.StoredWorkingProjectBasePoint.ProjectCrs!,
                    anchorProjectedCoordinate: currentState.StoredWorkingProjectBasePoint.ProjectedCoordinate!.Value,
                    anchorLatitude: currentState.StoredWorkingProjectBasePoint.Origin!.Latitude,
                    anchorLongitude: currentState.StoredWorkingProjectBasePoint.Origin.Longitude,
                    anchorElevationMeters: currentState.StoredWorkingProjectBasePoint.Origin.ElevationMeters,
                    anchorXFeet: currentState.ProjectBasePoint.XFeet,
                    anchorYFeet: currentState.ProjectBasePoint.YFeet,
                    anchorZFeet: currentState.ProjectBasePoint.ZFeet);
            }
        }

        ProjectedCoordinate canonicalProjectedCoordinate = coordinateTransformer.Project(
            new GeographicCoordinate(info.Origin.Latitude, info.Origin.Longitude),
            info.ProjectCrs);

        return CreateContext(
            title: "Canonical Origin",
            description: "Uses the canonical stored origin from GeoProjectInfo. This is the stable fallback when no Project Base Point reference is available.",
            projectCrs: info.ProjectCrs,
            anchorProjectedCoordinate: canonicalProjectedCoordinate,
            anchorLatitude: info.Origin.Latitude,
            anchorLongitude: info.Origin.Longitude,
            anchorElevationMeters: info.Origin.ElevationMeters,
            anchorXFeet: currentState.SurveyPoint.XFeet,
            anchorYFeet: currentState.SurveyPoint.YFeet,
            anchorZFeet: currentState.SurveyPoint.ZFeet);
    }

    private PlateauImportReferenceContext CreateContext(
        string title,
        string description,
        CrsReference projectCrs,
        ProjectedCoordinate anchorProjectedCoordinate,
        double anchorLatitude,
        double anchorLongitude,
        double anchorElevationMeters,
        double anchorXFeet,
        double anchorYFeet,
        double anchorZFeet)
    {
        PlateauImportReferenceContext context = new PlateauImportReferenceContext
        {
            Title = title,
            Description = description,
            ProjectCrs = projectCrs,
            AnchorProjectedCoordinate = anchorProjectedCoordinate,
            AnchorLatitude = anchorLatitude,
            AnchorLongitude = anchorLongitude,
            AnchorElevationMeters = anchorElevationMeters,
            AnchorXFeet = anchorXFeet,
            AnchorYFeet = anchorYFeet,
            AnchorZFeet = anchorZFeet
        };

        localBasisProvider.Apply(context);
        return context;
    }
}
