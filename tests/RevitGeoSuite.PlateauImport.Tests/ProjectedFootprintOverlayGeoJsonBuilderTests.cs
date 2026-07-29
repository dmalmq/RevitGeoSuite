using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class ProjectedFootprintOverlayGeoJsonBuilderTests
{
    [Fact]
    public void Create_geojson_builds_closed_polygon_from_projected_hull()
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        ProjectedFootprintOverlayGeoJsonBuilder builder = new ProjectedFootprintOverlayGeoJsonBuilder(coordinateTransformer);
        CrsReference crs = new CrsReference
        {
            EpsgCode = 6677,
            NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX"
        };

        string geoJson = builder.CreateGeoJson(
            new[]
            {
                new ProjectedCoordinate(0d, 0d),
                new ProjectedCoordinate(100d, 0d),
                new ProjectedCoordinate(100d, 50d),
                new ProjectedCoordinate(0d, 50d),
                new ProjectedCoordinate(50d, 25d)
            },
            crs,
            "host-footprint",
            "Host Model",
            42);

        JObject featureCollection = JObject.Parse(geoJson);
        JToken feature = featureCollection["features"]!.Single()!;
        JArray coordinates = (JArray)feature["geometry"]!["coordinates"]![0]!;

        Assert.Equal("host-footprint", (string?)feature["properties"]!["featureId"]);
        Assert.Equal(42, (int?)feature["properties"]!["elementCount"]);
        Assert.Equal(5, coordinates.Count);
        Assert.Equal(coordinates[0]!.ToString(), coordinates[coordinates.Count - 1]!.ToString());
    }

    [Fact]
    public void Create_geojson_returns_empty_for_fewer_than_three_unique_points()
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        ProjectedFootprintOverlayGeoJsonBuilder builder = new ProjectedFootprintOverlayGeoJsonBuilder(coordinateTransformer);
        CrsReference crs = new CrsReference
        {
            EpsgCode = 6677,
            NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX"
        };

        string geoJson = builder.CreateGeoJson(
            new[]
            {
                new ProjectedCoordinate(0d, 0d),
                new ProjectedCoordinate(0d, 0d),
                new ProjectedCoordinate(100d, 0d)
            },
            crs,
            "host-footprint",
            "Host Model",
            2);

        Assert.Equal(string.Empty, geoJson);
    }
}

