using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class CoordinateTransformerTests
{
    [Fact]
    public void Transform_samples_match_gsi_fixtures_within_tolerance()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        List<TransformFixture> fixtures = LoadFixtures();

        foreach (TransformFixture fixture in fixtures)
        {
            CrsReference crsReference = new CrsReference { EpsgCode = fixture.EpsgCode, NameSnapshot = fixture.Name };
            GeographicCoordinate geographicCoordinate = new GeographicCoordinate(fixture.Latitude, fixture.Longitude);

            ProjectedCoordinate projectedCoordinate = transformer.Project(geographicCoordinate, crsReference);

            Assert.InRange(Math.Abs(projectedCoordinate.Easting - fixture.ExpectedEasting), 0.0, fixture.ToleranceMeters);
            Assert.InRange(Math.Abs(projectedCoordinate.Northing - fixture.ExpectedNorthing), 0.0, fixture.ToleranceMeters);

            GeographicCoordinate roundTripped = transformer.Unproject(projectedCoordinate, crsReference);
            Assert.InRange(Math.Abs(roundTripped.Latitude - fixture.Latitude), 0.0, 0.000000001);
            Assert.InRange(Math.Abs(roundTripped.Longitude - fixture.Longitude), 0.0, 0.000000001);
        }
    }

    [Fact]
    public void Project_normalizes_axis_order_to_easting_and_northing()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        TransformFixture tokyoFixture = LoadFixtures()[0];
        CrsReference crsReference = new CrsReference { EpsgCode = tokyoFixture.EpsgCode, NameSnapshot = tokyoFixture.Name };

        ProjectedCoordinate projectedCoordinate = transformer.Project(
            new GeographicCoordinate(tokyoFixture.Latitude, tokyoFixture.Longitude),
            crsReference);

        Assert.Equal(tokyoFixture.ExpectedEasting, Math.Round(projectedCoordinate.Easting, 4));
        Assert.Equal(tokyoFixture.ExpectedNorthing, Math.Round(projectedCoordinate.Northing, 4));
    }

    [Fact]
    public void Unknown_epsg_code_is_rejected_when_transforming()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);

        Assert.Throws<ArgumentOutOfRangeException>(() => transformer.Project(
            new GeographicCoordinate(35.681236, 139.767125),
            new CrsReference { EpsgCode = 999999, NameSnapshot = "Unknown" }));
    }

    private static List<TransformFixture> LoadFixtures()
    {
        string path = TestPaths.GetRepoPath("tests/Fixtures/Transforms/latlon-to-projected.json");
        return JsonConvert.DeserializeObject<List<TransformFixture>>(File.ReadAllText(path))!;
    }

    private sealed class TransformFixture
    {
        public string Name { get; set; } = string.Empty;

        public int EpsgCode { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public double ExpectedEasting { get; set; }

        public double ExpectedNorthing { get; set; }

        public double ToleranceMeters { get; set; }
    }
}
