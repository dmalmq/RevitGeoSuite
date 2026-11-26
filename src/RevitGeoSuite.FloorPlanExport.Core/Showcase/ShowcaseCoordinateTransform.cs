using System;
using System.Collections.Generic;
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

    /// <summary>Shared CRS easting (metres) corresponding to Revit internal <c>(0, 0, 0)</c>.</summary>
    public double OriginEastingMeters => ForwardInternalFeetToCrsMeters.Origin.X;

    /// <summary>Shared CRS northing (metres) corresponding to Revit internal <c>(0, 0, 0)</c>.</summary>
    public double OriginNorthingMeters => ForwardInternalFeetToCrsMeters.Origin.Y;

    /// <summary>Rotation angle (radians) of the forward X basis relative to CRS East.</summary>
    public double RotationRadians => Math.Atan2(ForwardInternalFeetToCrsMeters.XBasis.Y, ForwardInternalFeetToCrsMeters.XBasis.X);

    /// <summary>Identity transform at shared origin <c>(0, 0, 0)</c> with zero rotation (metres &lt;-&gt; feet).</summary>
    public static ShowcaseCoordinateTransform Identity { get; } =
        FromSharedOrigin(0d, 0d, 0d, 0d);

    /// <summary>
    /// True when this transform represents an unconfigured Revit default project location at
    /// <c>(EastWest = 0, NorthSouth = 0, Elevation = 0, Angle = 0)</c>.
    /// </summary>
    public bool IsDefaultZeroOrigin(double toleranceMeters = 1e-6d) =>
        Math.Abs(OriginEastingMeters) <= toleranceMeters &&
        Math.Abs(OriginNorthingMeters) <= toleranceMeters &&
        Math.Abs(OriginElevationMeters) <= toleranceMeters &&
        Math.Abs(ForwardInternalFeetToCrsMeters.XBasis.X - FeetToMetersFactor) <= 1e-9d &&
        Math.Abs(ForwardInternalFeetToCrsMeters.XBasis.Y) <= 1e-9d &&
        Math.Abs(ForwardInternalFeetToCrsMeters.YBasis.X) <= 1e-9d &&
        Math.Abs(ForwardInternalFeetToCrsMeters.YBasis.Y - FeetToMetersFactor) <= 1e-9d;

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

    /// <summary>
    /// Computes a localized shared-coordinate transform centered on the planar bounding box of
    /// <paramref name="snapshot"/> (and its lowest surveyed/model elevation) so newly created companion
    /// geometry sits near Revit's internal origin (well within Revit's 10-mile radius) while preserving
    /// exact projected CRS shared coordinates.
    /// </summary>
    public static ShowcaseCoordinateTransform CreateLocalizedForSnapshot(ShowcaseSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity;
        bool hasPlanarCoords = false;

        foreach (ShowcaseLevel level in snapshot.Levels)
        {
            if (level.TpM.HasValue && IsFinite(level.TpM.Value) && level.TpM.Value < minZ)
            {
                minZ = level.TpM.Value;
            }
        }

        foreach (ShowcaseLayer layer in snapshot.Layers)
        {
            foreach (ShowcaseFeature feature in layer.Features)
            {
                AccumulateGeometryBounds(feature.Geometry, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref hasPlanarCoords);
            }
        }

        double originEasting = hasPlanarCoords ? RoundMillimeter((minX + maxX) * 0.5d) : 0d;
        double originNorthing = hasPlanarCoords ? RoundMillimeter((minY + maxY) * 0.5d) : 0d;
        double originElevation = !double.IsPositiveInfinity(minZ) ? RoundMillimeter(minZ) : 0d;

        return FromSharedOrigin(originEasting, originNorthing, originElevation, rotationDegrees: 0d);
    }

    /// <summary>
    /// Resolves the effective transform for a companion build and reports whether the target document's
    /// <c>ActiveProjectLocation</c> should be configured to match it.
    /// When building a fresh blank companion (<paramref name="isFreshNewDocument"/> = <c>true</c>) whose
    /// project location is still at <c>(0, 0, 0, 0)</c>, automatically localizes the internal origin to the
    /// snapshot's bounding-box center so projected EPSG coordinates never sit tens/thousands of kilometres away.
    /// </summary>
    public static ShowcaseCoordinateTransform ResolveTransformForCompanion(
        ShowcaseSnapshot snapshot,
        ShowcaseCoordinateTransform? explicitOverride,
        ShowcaseCoordinateTransform? documentLocationTransform,
        bool isFreshNewDocument,
        out bool shouldConfigureDocumentProjectPosition)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        bool docIsUnconfiguredZero = documentLocationTransform == null || documentLocationTransform.IsDefaultZeroOrigin();

        if (explicitOverride != null)
        {
            shouldConfigureDocumentProjectPosition = isFreshNewDocument && docIsUnconfiguredZero && !explicitOverride.IsDefaultZeroOrigin();
            return explicitOverride;
        }

        if (isFreshNewDocument && docIsUnconfiguredZero)
        {
            ShowcaseCoordinateTransform localized = CreateLocalizedForSnapshot(snapshot);
            shouldConfigureDocumentProjectPosition = !localized.IsDefaultZeroOrigin();
            return localized;
        }

        shouldConfigureDocumentProjectPosition = false;
        return documentLocationTransform ?? Identity;
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

    private static void AccumulateGeometryBounds(
        ShowcaseGeometry geometry,
        ref double minX,
        ref double maxX,
        ref double minY,
        ref double maxY,
        ref double minZ,
        ref bool hasPlanarCoords)
    {
        switch (geometry)
        {
            case ShowcasePointGeometry pt:
                IncludePoint(pt.Xy, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                if (pt.Z.HasValue && IsFinite(pt.Z.Value) && pt.Z.Value < minZ)
                {
                    minZ = pt.Z.Value;
                }

                break;

            case ShowcaseLineStringGeometry line:
                IncludePoints(line.Coords, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                break;

            case ShowcaseMultiLineStringGeometry multiLine:
                foreach (IReadOnlyList<Point2D> l in multiLine.Lines)
                {
                    IncludePoints(l, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                }

                break;

            case ShowcasePolygonGeometry poly:
                foreach (IReadOnlyList<Point2D> ring in poly.Rings)
                {
                    IncludePoints(ring, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                }

                break;

            case ShowcaseMultiPolygonGeometry multiPoly:
                foreach (ShowcasePolygonGeometry poly in multiPoly.Polygons)
                {
                    foreach (IReadOnlyList<Point2D> ring in poly.Rings)
                    {
                        IncludePoints(ring, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                    }
                }

                break;

            case ShowcaseConnector3DGeometry conn:
                IncludePoint(conn.From.Xy, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                IncludePoint(conn.To.Xy, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                IncludePoints(conn.Path, ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
                if (conn.From.TpM.HasValue && IsFinite(conn.From.TpM.Value) && conn.From.TpM.Value < minZ)
                {
                    minZ = conn.From.TpM.Value;
                }

                if (conn.To.TpM.HasValue && IsFinite(conn.To.TpM.Value) && conn.To.TpM.Value < minZ)
                {
                    minZ = conn.To.TpM.Value;
                }

                break;
        }
    }

    private static void IncludePoints(
        IReadOnlyList<Point2D> points,
        ref double minX,
        ref double maxX,
        ref double minY,
        ref double maxY,
        ref bool hasPlanarCoords)
    {
        for (int i = 0; i < points.Count; i++)
        {
            IncludePoint(points[i], ref minX, ref maxX, ref minY, ref maxY, ref hasPlanarCoords);
        }
    }

    private static void IncludePoint(
        Point2D pt,
        ref double minX,
        ref double maxX,
        ref double minY,
        ref double maxY,
        ref bool hasPlanarCoords)
    {
        if (!IsFinite(pt.X) || !IsFinite(pt.Y))
        {
            return;
        }

        if (pt.X < minX) minX = pt.X;
        if (pt.X > maxX) maxX = pt.X;
        if (pt.Y < minY) minY = pt.Y;
        if (pt.Y > maxY) maxY = pt.Y;
        hasPlanarCoords = true;
    }

    private static double RoundMillimeter(double meters)
    {
        double rounded = Math.Round(meters * 1000d, MidpointRounding.AwayFromZero) / 1000d;
        return rounded == 0d ? 0d : rounded;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void RequireFinite(double value, string paramName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, $"Coordinate value '{value}' must be finite.");
        }
    }
}
