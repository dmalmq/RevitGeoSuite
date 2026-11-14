using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Coordinates;

public readonly struct AffineTransform2D
{
    public AffineTransform2D(Point2D origin, Point2D xBasis, Point2D yBasis)
    {
        Origin = origin;
        XBasis = xBasis;
        YBasis = yBasis;
    }

    public Point2D Origin { get; }

    public Point2D XBasis { get; }

    public Point2D YBasis { get; }

    public Point2D Transform(double x, double y)
    {
        return new Point2D(
            Origin.X + (XBasis.X * x) + (YBasis.X * y),
            Origin.Y + (XBasis.Y * x) + (YBasis.Y * y));
    }

    public Point2D Transform(Point2D point)
    {
        return Transform(point.X, point.Y);
    }

    public Point2D TransformVector(double x, double y)
    {
        return new Point2D(
            (XBasis.X * x) + (YBasis.X * y),
            (XBasis.Y * x) + (YBasis.Y * y));
    }

    public Point2D TransformVector(Point2D vector)
    {
        return TransformVector(vector.X, vector.Y);
    }

    /// <summary>The transform that undoes this one. Throws when the basis is singular.</summary>
    public AffineTransform2D Inverse()
    {
        double determinant = (XBasis.X * YBasis.Y) - (YBasis.X * XBasis.Y);
        if (System.Math.Abs(determinant) < 1e-15)
        {
            throw new System.InvalidOperationException("The transform has no inverse: its axes are parallel.");
        }

        Point2D xBasis = new(YBasis.Y / determinant, -XBasis.Y / determinant);
        Point2D yBasis = new(-YBasis.X / determinant, XBasis.X / determinant);
        Point2D origin = new(
            -((xBasis.X * Origin.X) + (yBasis.X * Origin.Y)),
            -((xBasis.Y * Origin.X) + (yBasis.Y * Origin.Y)));
        return new AffineTransform2D(origin, xBasis, yBasis);
    }
}
