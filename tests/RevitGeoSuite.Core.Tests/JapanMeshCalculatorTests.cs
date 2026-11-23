using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Mesh;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class JapanMeshCalculatorTests
{
    [Fact]
    public void Mesh_samples_match_expected_tertiary_codes()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        List<MeshFixture> fixtures = LoadFixtures();

        foreach (MeshFixture fixture in fixtures)
        {
            MeshCode meshCode = calculator.Calculate(fixture.Latitude, fixture.Longitude, JapanMeshLevel.Tertiary);
            Assert.Equal(fixture.MeshCode, meshCode.Value);
        }
    }

    [Fact]
    public void Bounds_match_documented_example_for_53394611()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();

        MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = "53394611" });

        Assert.Equal(35.675, bounds.SouthLatitude, 3);
        Assert.Equal(139.7625, bounds.WestLongitude, 4);
        Assert.Equal(35.68333333333333, bounds.NorthLatitude, 12);
        Assert.Equal(139.775, bounds.EastLongitude, 3);
    }

    [Fact]
    public void Neighbor_resolution_returns_the_eight_adjacent_meshes_in_display_order()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshNeighborResolver resolver = new MeshNeighborResolver(calculator);

        string[] actual = resolver
            .GetNeighbors(new MeshCode { Value = "53394611" })
            .Select(meshCode => meshCode.Value)
            .ToArray();

        string[] expected =
        {
            "53394620",
            "53394621",
            "53394622",
            "53394610",
            "53394612",
            "53394600",
            "53394601",
            "53394602"
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Neighbor_resolution_rolls_across_secondary_boundaries()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshNeighborResolver resolver = new MeshNeighborResolver(calculator);

        string[] actual = resolver
            .GetNeighbors(new MeshCode { Value = "53394699" })
            .Select(meshCode => meshCode.Value)
            .ToArray();

        Assert.Contains("53394790", actual);
        Assert.Contains("53395609", actual);
        Assert.Contains("53395700", actual);
    }

    [Theory]
    [InlineData(35.681236, 139.767125, true)]
    [InlineData(20.0, 122.0, true)]
    [InlineData(46.0, 154.0, true)]
    [InlineData(0.0, 0.0, false)]
    [InlineData(35.0, 100.0, false)]
    [InlineData(double.NaN, 139.0, false)]
    [InlineData(35.0, double.PositiveInfinity, false)]
    public void Japan_mesh_domain_validates_supported_coordinates(double latitude, double longitude, bool expected)
    {
        Assert.Equal(expected, JapanMeshDomain.IsSupportedCoordinate(latitude, longitude));
    }

    [Fact]
    public void Japan_mesh_domain_validates_tertiary_mesh_codes()
    {
        Assert.True(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "53394611" }));
        Assert.True(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "53397799" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(null));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = string.Empty }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "533946" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "5339461A" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "53398611" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "53394811" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "53399999" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "-3394611" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = " 3394611" }));
        Assert.False(JapanMeshDomain.IsValidTertiaryMeshCode(new MeshCode { Value = "５３３９４６１１" }));
    }

    [Theory]
    [InlineData("53394611")] // Tokyo Station (previously failed both lat & lon -> 53394600)
    [InlineData("64414277")] // Sapporo (previously failed lat -> 64414267)
    [InlineData("50303382")] // Fukuoka
    [InlineData("39272544")] // Naha
    [InlineData("53390000")] // Primary SW corner
    [InlineData("53397799")] // Primary NE corner
    [InlineData("53394600")] // Secondary SW corner
    [InlineData("53394699")] // Secondary NE corner
    public void Bounds_round_trip_exact_southwest_corner_center_and_interior_for_major_japan_cities(string code)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = code });

        Assert.Equal(code, calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Tertiary).Value);
        Assert.Equal(code, calculator.Calculate(bounds.CenterLatitude, bounds.CenterLongitude, JapanMeshLevel.Tertiary).Value);
        Assert.Equal(code, calculator.Calculate(bounds.NorthLatitude - 1e-6, bounds.EastLongitude - 1e-6, JapanMeshLevel.Tertiary).Value);
        Assert.Equal(code.Substring(0, 6), calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Secondary).Value);
        Assert.Equal(code.Substring(0, 4), calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Primary).Value);
    }

    [Fact]
    public void Primary_and_secondary_bounds_round_trip_at_southwest_corners()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();

        foreach (string primaryCode in new[] { "5339", "6441", "5030", "3927" })
        {
            MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = primaryCode });
            Assert.Equal(primaryCode, calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Primary).Value);
            Assert.Equal(primaryCode, calculator.Calculate(bounds.CenterLatitude, bounds.CenterLongitude, JapanMeshLevel.Primary).Value);
        }

        foreach (string secondaryCode in new[] { "533946", "644142", "503033", "392725" })
        {
            MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = secondaryCode });
            Assert.Equal(secondaryCode, calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Secondary).Value);
            Assert.Equal(secondaryCode, calculator.Calculate(bounds.CenterLatitude, bounds.CenterLongitude, JapanMeshLevel.Secondary).Value);
        }
    }

    [Fact]
    public void Every_japan_latitude_and_longitude_tertiary_band_round_trips_its_southwest_corner()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();

        // Sweep all 3,120 tertiary latitude bands across Japan [20 deg, 46 deg)
        for (int latCell = 20 * 120; latCell < 46 * 120; latCell++)
        {
            int pLat = latCell / 80;
            int rem = latCell % 80;
            int sLat = rem / 10;
            int tLat = rem % 10;
            string code = $"{pLat:00}39{sLat}6{tLat}1";

            MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = code });
            Assert.Equal(code, calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Tertiary).Value);
        }

        // Sweep all 2,560 tertiary longitude bands across Japan [122 deg, 154 deg)
        for (int lonCell = 22 * 80; lonCell < 54 * 80; lonCell++)
        {
            int pLon = lonCell / 80;
            int rem = lonCell % 80;
            int sLon = rem / 10;
            int tLon = rem % 10;
            string code = $"53{pLon:00}4{sLon}1{tLon}";

            MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = code });
            Assert.Equal(code, calculator.Calculate(bounds.SouthLatitude, bounds.WestLongitude, JapanMeshLevel.Tertiary).Value);
        }
    }

    [Theory]
    [InlineData("53398611")]
    [InlineData("53394811")]
    [InlineData("533999")]
    [InlineData("-1234567")]
    [InlineData("+1234567")]
    [InlineData(" 3394611")]
    [InlineData("５３３９４６１１")]
    public void GetBounds_rejects_malformed_or_out_of_range_mesh_codes(string invalidCode)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();

        Assert.Throws<ArgumentException>(() => calculator.GetBounds(new MeshCode { Value = invalidCode }));
    }

    [Theory]
    [InlineData(double.NaN, 139.767125)]
    [InlineData(35.681236, double.NaN)]
    [InlineData(double.PositiveInfinity, 139.767125)]
    [InlineData(35.681236, double.NegativeInfinity)]
    public void Calculate_rejects_non_finite_coordinates(double latitude, double longitude)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(latitude, longitude, JapanMeshLevel.Tertiary));
    }

    private static List<MeshFixture> LoadFixtures()
    {
        string path = TestPaths.GetRepoPath("tests/Fixtures/Mesh/japan-mesh-samples.json");
        return JsonConvert.DeserializeObject<List<MeshFixture>>(File.ReadAllText(path))!;
    }

    private sealed class MeshFixture
    {
        public string Name { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string MeshCode { get; set; } = string.Empty;
    }
}
