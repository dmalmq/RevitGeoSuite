using System;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport;

internal sealed class PlateauDxfExportFrame
{
    private const double FeetToMeters = 0.3048d;
    private readonly PlateauImportReferenceContext referenceContext;
    private readonly double determinant;

    private PlateauDxfExportFrame(
        PlateauImportReferenceContext referenceContext,
        double determinant,
        BasePointSnapshot surveyPoint,
        BasePointSnapshot projectBasePoint)
    {
        this.referenceContext = referenceContext;
        this.determinant = determinant;
        SurveyPointSharedMetres = ToSharedMetres(surveyPoint);
        ProjectBasePointSharedMetres = ToSharedMetres(projectBasePoint);
    }

    public Vector3d SurveyPointSharedMetres { get; }

    public Vector3d ProjectBasePointSharedMetres { get; }

    public static PlateauDxfExportFrame Create(PlateauImportReferenceContext referenceContext, CurrentProjectStateSummary currentState)
    {
        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        double determinant = CalculatePlanDeterminant(referenceContext);
        if (Math.Abs(determinant) < 1e-9d)
        {
            throw new InvalidOperationException("The current Revit shared/local coordinate basis could not be inverted for DXF export.");
        }

        return new PlateauDxfExportFrame(
            referenceContext,
            determinant,
            currentState.SurveyPoint,
            currentState.ProjectBasePoint);
    }

    public (double EastingMetres, double NorthingMetres) ToSharedPlanMetres(double localXFeet, double localYFeet)
    {
        double localDeltaXFeet = localXFeet - referenceContext.AnchorXFeet;
        double localDeltaYFeet = localYFeet - referenceContext.AnchorYFeet;

        double eastFeet = ((localDeltaXFeet * referenceContext.SharedNorthToLocalY) - (referenceContext.SharedNorthToLocalX * localDeltaYFeet)) / determinant;
        double northFeet = ((referenceContext.SharedEastToLocalX * localDeltaYFeet) - (localDeltaXFeet * referenceContext.SharedEastToLocalY)) / determinant;

        return (
            referenceContext.AnchorProjectedCoordinate.Easting + (eastFeet * FeetToMeters),
            referenceContext.AnchorProjectedCoordinate.Northing + (northFeet * FeetToMeters));
    }

    private Vector3d ToSharedMetres(BasePointSnapshot point)
    {
        if (point.HasSharedPosition)
        {
            return new Vector3d(
                point.SharedEastWestFeet!.Value * FeetToMeters,
                point.SharedNorthSouthFeet!.Value * FeetToMeters,
                point.SharedElevationFeet!.Value * FeetToMeters);
        }

        (double eastingMetres, double northingMetres) = ToSharedPlanMetres(point.XFeet, point.YFeet);
        double elevationMetres = referenceContext.AnchorElevationMeters + ((point.ZFeet - referenceContext.AnchorZFeet) * FeetToMeters);
        return new Vector3d(eastingMetres, northingMetres, elevationMetres);
    }

    private static double CalculatePlanDeterminant(PlateauImportReferenceContext referenceContext)
    {
        return (referenceContext.SharedEastToLocalX * referenceContext.SharedNorthToLocalY)
            - (referenceContext.SharedNorthToLocalX * referenceContext.SharedEastToLocalY);
    }
}
