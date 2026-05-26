using System;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Composes ECEF -> WGS84 geodetic -> project CRS conversion. The horizontal axes go through
/// the supplied <see cref="ICoordinateTransformer"/>; altitude passes through as the local Z
/// (relative to a project anchor passed in by the caller).
/// </summary>
public sealed class EcefToProjectTransformer
{
    private const double FeetToMeters = 0.3048d;

    private readonly ICoordinateTransformer transformer;
    private readonly CrsReference projectCrs;
    private readonly double altitudeAnchorMeters;
    private readonly bool useLocalPlacement;
    private readonly ProjectedCoordinate anchorProjectedCoordinate;
    private readonly double anchorXFeet;
    private readonly double anchorYFeet;
    private readonly double anchorZFeet;
    private readonly double sharedEastToLocalX;
    private readonly double sharedEastToLocalY;
    private readonly double sharedNorthToLocalX;
    private readonly double sharedNorthToLocalY;

    public EcefToProjectTransformer(ICoordinateTransformer transformer, CrsReference projectCrs, double altitudeAnchorMeters)
    {
        this.transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        this.projectCrs = projectCrs ?? throw new ArgumentNullException(nameof(projectCrs));
        this.altitudeAnchorMeters = altitudeAnchorMeters;
    }

    public EcefToProjectTransformer(
        ICoordinateTransformer transformer,
        CrsReference projectCrs,
        ProjectedCoordinate anchorProjectedCoordinate,
        double anchorElevationMeters,
        double anchorXFeet,
        double anchorYFeet,
        double anchorZFeet,
        double sharedEastToLocalX,
        double sharedEastToLocalY,
        double sharedNorthToLocalX,
        double sharedNorthToLocalY)
        : this(transformer, projectCrs, anchorElevationMeters)
    {
        useLocalPlacement = true;
        this.anchorProjectedCoordinate = anchorProjectedCoordinate;
        this.anchorXFeet = anchorXFeet;
        this.anchorYFeet = anchorYFeet;
        this.anchorZFeet = anchorZFeet;
        this.sharedEastToLocalX = sharedEastToLocalX;
        this.sharedEastToLocalY = sharedEastToLocalY;
        this.sharedNorthToLocalX = sharedNorthToLocalX;
        this.sharedNorthToLocalY = sharedNorthToLocalY;
    }

    /// <summary>
    /// Returns the input vertex expressed in project metres. When local placement is configured,
    /// X/Y/Z are Revit-local coordinates in metres; otherwise X/Y are absolute projected CRS metres.
    /// </summary>
    public Vector3d TransformEcefToProject(Vector3d ecef)
    {
        GeodeticCoordinate geodetic = EcefGeodeticConverter.ToGeodetic(ecef);
        ProjectedCoordinate projected = transformer.Project(
            new GeographicCoordinate(geodetic.LatitudeDegrees, geodetic.LongitudeDegrees),
            projectCrs);

        if (!useLocalPlacement)
        {
            return new Vector3d(projected.Easting, projected.Northing, geodetic.AltitudeMeters - altitudeAnchorMeters);
        }

        double deltaEastMeters = projected.Easting - anchorProjectedCoordinate.Easting;
        double deltaNorthMeters = projected.Northing - anchorProjectedCoordinate.Northing;
        double localXMeters =
            (anchorXFeet * FeetToMeters) +
            (deltaEastMeters * sharedEastToLocalX) +
            (deltaNorthMeters * sharedNorthToLocalX);
        double localYMeters =
            (anchorYFeet * FeetToMeters) +
            (deltaEastMeters * sharedEastToLocalY) +
            (deltaNorthMeters * sharedNorthToLocalY);
        double localZMeters = (anchorZFeet * FeetToMeters) + (geodetic.AltitudeMeters - altitudeAnchorMeters);
        return new Vector3d(localXMeters, localYMeters, localZMeters);
    }
}
