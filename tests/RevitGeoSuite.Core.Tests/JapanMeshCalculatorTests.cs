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
