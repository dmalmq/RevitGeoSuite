using System;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Explicit vertical datum tag for heights entering or leaving <see cref="EcefToProjectTransformer"/>.
/// </summary>
public enum VerticalDatum
{
    /// <summary>
    /// Orthometric height above the geoid (<c>H = h - N</c>). Matches Revit project elevations,
    /// local PLATEAU CityGML/DEM files, and 3D Tiles export anchors.
    /// </summary>
    Orthometric = 0,

    /// <summary>
    /// Ellipsoidal height above the WGS84 reference ellipsoid (<c>h</c>), as encoded directly in
    /// WGS84 ECEF 3D Tiles and Cesium quantized-mesh terrain tiles.
    /// </summary>
    Ellipsoidal = 1
}

/// <summary>
/// Provides geoid undulation <c>N(lat, lon)</c> in metres (<c>H_orthometric = h_ellipsoidal - N</c>).
/// Serves as the vertical-datum seam shared across online 3D Tiles, OSM Buildings, and online terrain:
/// defaults to the embedded EGM2008 5-arcminute grid (<see cref="Egm2008GeoidModel"/>) and can be
/// backed by JPGEO2024 once the GSI JPGEO2024 grid asset is bundled.
/// </summary>
public interface IGeoidModel
{
    string Name { get; }

    double GetUndulationMeters(double latitudeDegrees, double longitudeDegrees);
}

/// <summary>
/// Built-in geoid model backed by the embedded EGM2008 5-arcminute grid (<see cref="Egm2008Geoid"/>),
/// matching <c>Tiles3DExport</c> globally (and ~36.71 m in Tokyo, within ~0.1 m of JPGEO2024).
/// </summary>
public sealed class Egm2008GeoidModel : IGeoidModel
{
    public static Egm2008GeoidModel Instance { get; } = new Egm2008GeoidModel();

    public string Name => "EGM2008";

    public double GetUndulationMeters(double latitudeDegrees, double longitudeDegrees)
    {
        return Egm2008Geoid.GetUndulationMeters(latitudeDegrees, longitudeDegrees);
    }
}

/// <summary>
/// Constant-offset geoid model for explicit fixed-offset overrides or zero-offset ellipsoidal tests.
/// </summary>
public sealed class ConstantGeoidModel : IGeoidModel
{
    public static ConstantGeoidModel Zero { get; } = new ConstantGeoidModel(0d, "Ellipsoidal");

    public ConstantGeoidModel(double undulationMeters, string name = "Constant")
    {
        if (double.IsNaN(undulationMeters) || double.IsInfinity(undulationMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(undulationMeters), undulationMeters, "Geoid undulation must be finite.");
        }

        UndulationMeters = undulationMeters;
        Name = string.IsNullOrWhiteSpace(name) ? "Constant" : name.Trim();
    }

    public double UndulationMeters { get; }

    public string Name { get; }

    public double GetUndulationMeters(double latitudeDegrees, double longitudeDegrees) => UndulationMeters;
}

/// <summary>
/// Composes ECEF -> WGS84 geodetic -> project CRS conversion with an explicit vertical datum.
/// Horizontal axes go through the supplied <see cref="ICoordinateTransformer"/>; WGS84 ECEF
/// ellipsoidal heights are converted to orthometric project heights (<c>H = h - N</c>) by default
/// using <see cref="GeoidModel"/> so online PLATEAU and OSM buildings sit flush with orthometric
/// project anchors and ground surfaces.
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

    public EcefToProjectTransformer(
        ICoordinateTransformer transformer,
        CrsReference projectCrs,
        double altitudeAnchorMeters,
        VerticalDatum outputVerticalDatum = VerticalDatum.Orthometric,
        IGeoidModel? geoidModel = null)
    {
        this.transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        this.projectCrs = projectCrs ?? throw new ArgumentNullException(nameof(projectCrs));
        this.altitudeAnchorMeters = altitudeAnchorMeters;
        OutputVerticalDatum = outputVerticalDatum;
        GeoidModel = geoidModel ?? Egm2008GeoidModel.Instance;
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
        double sharedNorthToLocalY,
        VerticalDatum outputVerticalDatum = VerticalDatum.Orthometric,
        IGeoidModel? geoidModel = null)
        : this(transformer, projectCrs, anchorElevationMeters, outputVerticalDatum, geoidModel)
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

    public VerticalDatum OutputVerticalDatum { get; }

    public IGeoidModel GeoidModel { get; }

    /// <summary>
    /// Returns the input WGS84 ECEF vertex expressed in project metres. ECEF altitudes are
    /// ellipsoidal and are converted to <see cref="OutputVerticalDatum"/> via <see cref="GeoidModel"/>.
    /// When local placement is configured, X/Y/Z are Revit-local coordinates in metres; otherwise
    /// X/Y are absolute projected CRS metres.
    /// </summary>
    public Vector3d TransformEcefToProject(Vector3d ecef)
    {
        GeodeticCoordinate geodetic = EcefGeodeticConverter.ToGeodetic(ecef);
        return TransformGeographicToProject(
            geodetic.LatitudeDegrees,
            geodetic.LongitudeDegrees,
            geodetic.AltitudeMeters,
            VerticalDatum.Ellipsoidal);
    }

    /// <summary>
    /// Returns a WGS84 lon/lat (and optional altitude in <paramref name="inputDatum"/>) expressed in
    /// project metres, using the same projection + local-placement basis as <see cref="TransformEcefToProject"/>.
    /// Lets vector sources that already carry lon/lat (e.g. MVT tiles) land in the model's internal
    /// frame exactly like the 3D-Tiles building meshes.
    /// </summary>
    public Vector3d TransformGeographicToProject(
        double latitudeDegrees,
        double longitudeDegrees,
        double altitudeMeters = 0d,
        VerticalDatum inputDatum = VerticalDatum.Orthometric)
    {
        ProjectedCoordinate projected = transformer.Project(
            new GeographicCoordinate(latitudeDegrees, longitudeDegrees),
            projectCrs);

        double adjustedAltitudeMeters = ConvertAltitudeDatum(latitudeDegrees, longitudeDegrees, altitudeMeters, inputDatum);

        if (!useLocalPlacement)
        {
            return new Vector3d(projected.Easting, projected.Northing, adjustedAltitudeMeters - altitudeAnchorMeters);
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
        double localZMeters = (anchorZFeet * FeetToMeters) + (adjustedAltitudeMeters - altitudeAnchorMeters);
        return new Vector3d(localXMeters, localYMeters, localZMeters);
    }

    private double ConvertAltitudeDatum(double latitudeDegrees, double longitudeDegrees, double altitudeMeters, VerticalDatum inputDatum)
    {
        if (inputDatum == OutputVerticalDatum)
        {
            return altitudeMeters;
        }

        double undulationMeters = GeoidModel.GetUndulationMeters(latitudeDegrees, longitudeDegrees);
        return inputDatum == VerticalDatum.Ellipsoidal
            ? altitudeMeters - undulationMeters
            : altitudeMeters + undulationMeters;
    }
}
