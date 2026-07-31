using System;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public static class ProjectBasePointMoveMath
{
    public const double MaximumSupportedPlanMoveFeet = 10d * 5280d;

    public static bool TrySolvePlanOffset(
        double deltaEastFeet,
        double deltaNorthFeet,
        double xAxisEastFeet,
        double xAxisNorthFeet,
        double yAxisEastFeet,
        double yAxisNorthFeet,
        out double deltaXFeet,
        out double deltaYFeet)
    {
        double determinant = (xAxisEastFeet * yAxisNorthFeet) - (xAxisNorthFeet * yAxisEastFeet);
        if (Math.Abs(determinant) < 1e-9d)
        {
            deltaXFeet = 0d;
            deltaYFeet = 0d;
            return false;
        }

        deltaXFeet = ((deltaEastFeet * yAxisNorthFeet) - (deltaNorthFeet * yAxisEastFeet)) / determinant;
        deltaYFeet = ((xAxisEastFeet * deltaNorthFeet) - (xAxisNorthFeet * deltaEastFeet)) / determinant;
        return true;
    }

    public static double CalculatePlanDistance(double deltaEastFeet, double deltaNorthFeet)
    {
        return Math.Sqrt((deltaEastFeet * deltaEastFeet) + (deltaNorthFeet * deltaNorthFeet));
    }

    public static bool ExceedsMaximumSupportedPlanMove(double deltaEastFeet, double deltaNorthFeet, out double requiredPlanMoveFeet)
    {
        requiredPlanMoveFeet = CalculatePlanDistance(deltaEastFeet, deltaNorthFeet);
        return requiredPlanMoveFeet > MaximumSupportedPlanMoveFeet;
    }
}
