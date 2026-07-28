using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Georeference;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class ManualProjectBasePointResolverTests
{
    [Fact]
    public void Resolve_preserves_exact_projected_project_base_point_coordinates()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        Assert.True(registry.TryGetByEpsgCode(6677, out CrsDefinition? crsDefinition));

        ManualProjectBasePointResolver resolver = new ManualProjectBasePointResolver(transformer);

        ManualProjectBasePointSelection result = resolver.Resolve(120.125, 340.875, crsDefinition!.ToReference());

        Assert.Equal(120.125, result.ProjectedCoordinate.Easting, 6);
        Assert.Equal(340.875, result.ProjectedCoordinate.Northing, 6);

        GeographicCoordinate expected = transformer.Unproject(
            new ProjectedCoordinate(120.125, 340.875),
            crsDefinition.ToReference());
        Assert.Equal(expected.Latitude, result.AnchorLatitude, 10);
        Assert.Equal(expected.Longitude, result.AnchorLongitude, 10);
    }

    [Fact]
    public void Resolve_rejects_non_finite_coordinates()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        Assert.True(registry.TryGetByEpsgCode(6677, out CrsDefinition? crsDefinition));

        ManualProjectBasePointResolver resolver = new ManualProjectBasePointResolver(transformer);

        Assert.Throws<System.InvalidOperationException>(() =>
            resolver.Resolve(double.NaN, 340.875, crsDefinition!.ToReference()));
    }
}
