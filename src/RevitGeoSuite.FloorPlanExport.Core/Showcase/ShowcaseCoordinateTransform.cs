using System;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Revit-API-free 3D point in Revit internal coordinates (feet).</summary>
public readonly struct ShowcasePoint3D
{
    public ShowcasePoint3D(double xFeet, double yFeet, double zFeet)
    {
        XFeet = xFeet;
        YFeet = yFeet;
        ZFeet = zFeet;
    }

    public double XFeet { get; }

    public double YFeet { get; }

    public double ZFeet { get; }
}

/// <summary>
/// Pure-C# inverse shared-coordinate transform mapping projected CRS coordinates (easting, northing, elevation
/// in metres) into Revit internal coordinates (feet), accounting for shared-coordinate origin translation and
/// True North rotation.
/// </summary>
public sealed class ShowcaseCoordinateTransform
{
    public const double FeetToMetersFactor = CrsTransformer.FeetToMetersFactor;
    public const double MetersToFeetFactor = 1d / FeetToMetersFactor;

    private ShowcaseCoordinateTransform(
        AffineTransform2D forwardInternalFeetToCrsMeters,
        AffineTransform2D inverseCrsMetersToInternalFeet,
        double originElevationMeters)
    {
        ForwardInternalFeetToCrsMeters = forwardInternalFeetToCrsMeters;
        InverseCrsMetersToInternalFeet = inverseCrsMetersToInternalFeet;
        OriginElevationMeters = originElevationMeters;
    }

    /// <summary>Forward 2D affine transform mapping Revit internal feet to projected CRS metres.</summary>
    public AffineTransform2D ForwardInternalFeetToCrsMeters { get; }

    /// <summary>Inverse 2D affine transform mapping projected CRS metres to Revit internal feet.</summary>
    public AffineTransform2D InverseCrsMetersToInternalFeet { get; }

    /// <summary>Shared datum elevation (metres, e.g. Tokyo Peil) at Revit internal <c>Z = 0</c>.</summary>
    public double OriginElevationMeters { get; }

    /// <summary>Identity transform at shared origin <c>(0, 0, 0)</c> with zero rotation (metres &lt;-&gt; feet).</summary>
    public static ShowcaseCoordinateTransform Identity { get; } =
        FromSharedOrigin(0d, 0d, 0d, 0d);

    /// <summary>
    /// Creates an inverse transform directly from a measured Revit forward affine transform
    /// (such as <c>SharedCoordinateProjector.Transform</c>, mapping internal feet to CRS metres).
    /// </summary>
    public static ShowcaseCoordinateTransform FromForwardAffine(
        AffineTransform2D forwardInternalFeetToCrsMeters,
        double originElevationMeters = 0d)
    {
        RequireFinite(forwardInternalFeetToCrsMeters.Origin.X, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(forwardInternalFeetToCrsMeters.Origin.Y, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(forwardInternalFeetToCrsMeters.XBasis.X, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(forwardInternalFeetToCrsMeters.XBasis.Y, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(forwardInternalFeetToCrsMeters.YBasis.X, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(forwardInternalFeetToCrsMeters.YBasis.Y, nameof(forwardInternalFeetToCrsMeters));
        RequireFinite(originElevationMeters, nameof(originElevationMeters));

        AffineTransform2D inverse = forwardInternalFeetToCrsMeters.Inverse();
        return new ShowcaseCoordinateTransform(forwardInternalFeetToCrsMeters, inverse, originElevationMeters);
    }

    /// <summary>
    /// Creates a transform matching <see cref="CrsTransformer.TransformFromRevitFeet(double, double, double, double, double)"/>:
    /// forward rotates Revit internal coordinates by <paramref name="rotationDegrees"/> (CCW in plan) and translates
    /// by (<paramref name="originEastingMeters"/>, <paramref name="originNorthingMeters"/>, <paramref name="originElevationMeters"/>).
    /// </summary>
    public static ShowcaseCoordinateTransform FromSharedOrigin(
        double originEastingMeters,
        double originNorthingMeters,
        double originElevationMeters = 0d,
        double rotationDegrees = 0d)
    {
        RequireFinite(originEastingMeters, nameof(originEastingMeters));
        RequireFinite(originNorthingMeters, nameof(originNorthingMeters));
        RequireFinite(originElevationMeters, nameof(originElevationMeters));
        RequireFinite(rotationDegrees, nameof(rotationDegrees));

        double radians = rotationDegrees * (Math.PI / 180d);
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        AffineTransform2D forward = new(
            new Point2D(originEastingMeters, originNorthingMeters),
            new Point2D(FeetToMetersFactor * cos, FeetToMetersFactor * sin),
            new Point2D(-FeetToMetersFactor * sin, FeetToMetersFactor * cos));

        return FromForwardAffine(forward, originElevationMeters);
    }

    /// <summary>Maps projected CRS planar coordinates (metres) into Revit internal XY coordinates (feet).</summary>
    public Point2D ToRevitInternal2D(double eastingMeters, double northingMeters)
    {
        RequireFinite(eastingMeters, nameof(eastingMeters));
        RequireFinite(northingMeters, nameof(northingMeters));
        return InverseCrsMetersToInternalFeet.Transform(eastingMeters, northingMeters);
    }

    /// <summary>Maps projected CRS planar coordinates (metres) into Revit internal XY coordinates (feet).</summary>
    public Point2D ToRevitInternal2D(Point2D crsMeters) =>
        ToRevitInternal2D(crsMeters.X, crsMeters.Y);

    /// <summary>Maps a shared elevation (metres, e.g. TP) into Revit internal Z (feet).</summary>
    public double ToRevitInternalZFeet(double elevationMeters)
    {
        RequireFinite(elevationMeters, nameof(elevationMeters));
        return (elevationMeters - OriginElevationMeters) * MetersToFeetFactor;
    }

    /// <summary>Maps projected CRS 3D coordinates (metres) into Revit internal 3D coordinates (feet).</summary>
    public ShowcasePoint3D ToRevitInternal(double eastingMeters, double northingMeters, double elevationMeters)
    {
        Point2D xyFeet = ToRevitInternal2D(eastingMeters, northingMeters);
        double zFeet = ToRevitInternalZFeet(elevationMeters);
        return new ShowcasePoint3D(xyFeet.X, xyFeet.Y, zFeet);
    }

    /// <summary>Forward-maps Revit internal 3D coordinates (feet) back into projected CRS coordinates (metres).</summary>
    public (double EastingMeters, double NorthingMeters, double ElevationMeters) ToCrsMeters(ShowcasePoint3D internalFeet)
    {
        RequireFinite(internalFeet.XFeet, nameof(internalFeet));
        RequireFinite(internalFeet.YFeet, nameof(internalFeet));
        RequireFinite(internalFeet.ZFeet, nameof(internalFeet));

        Point2D xyMeters = ForwardInternalFeetToCrsMeters.Transform(internalFeet.XFeet, internalFeet.YFeet);
        double elevationMeters = OriginElevationMeters + (internalFeet.ZFeet * FeetToMetersFactor);
        return (xyMeters.X, xyMeters.Y, elevationMeters);
    }

    /// <summary>Converts a length/height in metres into Revit internal feet.</summary>
    public static double MetersToFeet(double meters)
    {
        RequireFinite(meters, nameof(meters));
        return meters * MetersToFeetFactor;
    }

    /// <summary>Converts a length/height in Revit internal feet into metres.</summary>
    public static double FeetToMeters(double feet)
    {
        RequireFinite(feet, nameof(feet));
        return feet * FeetToMetersFactor;
    }

    private static void RequireFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, $"Coordinate value '{value}' must be finite.");
        }
    }
}
