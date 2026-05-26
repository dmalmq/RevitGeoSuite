using System.Collections.Generic;
using RevitGeoSuite.Core.Plateau.Dem;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Dem;

public sealed class DemSamplerTests
{
    [Fact]
    public void TrySampleElevation_returns_interpolated_z_inside_triangle()
    {
        DemSampler sampler = BuildSampler();
        Assert.True(sampler.TrySampleElevation(2.5, 2.5, out double z));
        Assert.InRange(z, 4.99, 5.01);
    }

    [Fact]
    public void TrySampleElevation_returns_false_outside_hull()
    {
        DemSampler sampler = BuildSampler();
        Assert.False(sampler.TrySampleElevation(100, 100, out _));
    }

    [Fact]
    public void TrySampleElevation_interpolates_correctly_with_a_sloped_triangle()
    {
        var triangles = new List<PlateauTilesetTriangle>
        {
            new PlateauTilesetTriangle(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10), new Vector3d(0, 10, 0)),
            new PlateauTilesetTriangle(new Vector3d(10, 0, 10), new Vector3d(10, 10, 10), new Vector3d(0, 10, 0)),
        };
        DemSampler sampler = new DemSampler(BuildModel(triangles));
        Assert.True(sampler.TrySampleElevation(5, 0.1, out double z));
        Assert.InRange(z, 4.9, 5.1);
    }

    [Fact]
    public void SampleElevationOrNearest_falls_back_when_outside_hull()
    {
        DemSampler sampler = BuildSampler();
        double z = sampler.SampleElevationOrNearest(100, 100, out bool exact);
        Assert.False(exact);
        Assert.True(z >= 0 && z <= 10);
    }

    private static DemSampler BuildSampler()
    {
        var triangles = new List<PlateauTilesetTriangle>
        {
            new PlateauTilesetTriangle(new Vector3d(0, 0, 0), new Vector3d(5, 0, 0), new Vector3d(5, 5, 10)),
            new PlateauTilesetTriangle(new Vector3d(0, 0, 0), new Vector3d(5, 5, 10), new Vector3d(0, 5, 10)),
        };
        return new DemSampler(BuildModel(triangles));
    }

    private static PlateauTilesetModel BuildModel(List<PlateauTilesetTriangle> triangles)
    {
        var attrs = new Dictionary<string, object?>();
        var feature = new PlateauTilesetFeature("dem-0", attrs, triangles);
        return new PlateauTilesetModel(
            "https://example.com/dem", "dem", "1", false, "01100",
            new List<PlateauTilesetFeature> { feature });
    }
}
