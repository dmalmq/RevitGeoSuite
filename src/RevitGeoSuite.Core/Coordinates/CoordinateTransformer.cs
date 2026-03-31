using System;
using System.Collections.Generic;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace RevitGeoSuite.Core.Coordinates;

public sealed class CoordinateTransformer : ICoordinateTransformer
{
    private readonly object syncRoot = new object();
    private readonly ICrsRegistry crsRegistry;
    private readonly CoordinateSystemFactory coordinateSystemFactory;
    private readonly CoordinateTransformationFactory transformationFactory;
    private readonly GeographicCoordinateSystem geographicCoordinateSystem;
    private readonly Dictionary<int, MathTransform> forwardTransforms;
    private readonly Dictionary<int, MathTransform> reverseTransforms;

    public CoordinateTransformer(ICrsRegistry crsRegistry)
    {
        this.crsRegistry = crsRegistry ?? throw new ArgumentNullException(nameof(crsRegistry));
        coordinateSystemFactory = new CoordinateSystemFactory();
        transformationFactory = new CoordinateTransformationFactory();
        geographicCoordinateSystem = CreateJgd2011CoordinateSystem();
        forwardTransforms = new Dictionary<int, MathTransform>();
        reverseTransforms = new Dictionary<int, MathTransform>();
    }

    public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
    {
        if (targetCrs is null)
        {
            throw new ArgumentNullException(nameof(targetCrs));
        }

        MathTransform transform = GetTransform(targetCrs.EpsgCode, forwardTransforms, createForward: true);

        // Internal geographic coordinates stay in latitude/longitude order. ProjNet expects x/y,
        // so the library boundary is the only place where the values are reordered to longitude/latitude.
        double[] projected = transform.Transform(new[] { coordinate.Longitude, coordinate.Latitude });
        return new ProjectedCoordinate(projected[0], projected[1]);
    }

    public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
    {
        if (sourceCrs is null)
        {
            throw new ArgumentNullException(nameof(sourceCrs));
        }

        MathTransform transform = GetTransform(sourceCrs.EpsgCode, reverseTransforms, createForward: false);

        // Projected coordinates are normalized to Easting/Northing in metres throughout Core.
        double[] geographic = transform.Transform(new[] { coordinate.Easting, coordinate.Northing });
        return new GeographicCoordinate(geographic[1], geographic[0]);
    }

    private MathTransform GetTransform(int epsgCode, Dictionary<int, MathTransform> cache, bool createForward)
    {
        lock (syncRoot)
        {
            if (!cache.TryGetValue(epsgCode, out MathTransform? transform))
            {
                transform = CreateTransform(epsgCode, createForward);
                cache[epsgCode] = transform;
            }

            return transform;
        }
    }

    private MathTransform CreateTransform(int epsgCode, bool createForward)
    {
        if (!crsRegistry.TryGetByEpsgCode(epsgCode, out CrsDefinition? definition) || definition is null)
        {
            throw new ArgumentOutOfRangeException(nameof(epsgCode), epsgCode, "Unknown EPSG code.");
        }

        ProjectedCoordinateSystem projectedCoordinateSystem = CreateProjectedCoordinateSystem(definition);
        ICoordinateTransformation transformation = createForward
            ? transformationFactory.CreateFromCoordinateSystems(geographicCoordinateSystem, projectedCoordinateSystem)
            : transformationFactory.CreateFromCoordinateSystems(projectedCoordinateSystem, geographicCoordinateSystem);

        return transformation.MathTransform;
    }

    private GeographicCoordinateSystem CreateJgd2011CoordinateSystem()
    {
        HorizontalDatum datum = coordinateSystemFactory.CreateHorizontalDatum(
            "JGD2011",
            DatumType.HD_Geocentric,
            Ellipsoid.GRS80,
            null);

        return coordinateSystemFactory.CreateGeographicCoordinateSystem(
            "JGD2011",
            AngularUnit.Degrees,
            datum,
            PrimeMeridian.Greenwich,
            new AxisInfo("Longitude", AxisOrientationEnum.East),
            new AxisInfo("Latitude", AxisOrientationEnum.North));
    }

    private ProjectedCoordinateSystem CreateProjectedCoordinateSystem(CrsDefinition definition)
    {
        List<ProjectionParameter> parameters = new List<ProjectionParameter>
        {
            new ProjectionParameter("latitude_of_origin", definition.LatitudeOfOrigin),
            new ProjectionParameter("central_meridian", definition.CentralMeridian),
            new ProjectionParameter("scale_factor", definition.ScaleFactor),
            new ProjectionParameter("false_easting", definition.FalseEasting),
            new ProjectionParameter("false_northing", definition.FalseNorthing)
        };

        IProjection projection = coordinateSystemFactory.CreateProjection(
            $"Japan Plane Rectangular CS zone {definition.ZoneLabel}",
            "Transverse_Mercator",
            parameters);

        return coordinateSystemFactory.CreateProjectedCoordinateSystem(
            definition.Name,
            geographicCoordinateSystem,
            projection,
            LinearUnit.Metre,
            new AxisInfo("Easting", AxisOrientationEnum.East),
            new AxisInfo("Northing", AxisOrientationEnum.North));
    }
}
