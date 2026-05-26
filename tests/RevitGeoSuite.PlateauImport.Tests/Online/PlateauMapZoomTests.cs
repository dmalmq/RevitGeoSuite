using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauMapZoomTests
{
    [Theory]
    // Very large box (>1° on the bigger axis) — pan all the way out.
    [InlineData(139.0, 35.0, 141.5, 36.5, 9)]
    // ~0.7° wide — Sapporo-shi scale.
    [InlineData(141.0, 42.9, 141.7, 43.3, 10)]
    // ~0.3° wide.
    [InlineData(139.6, 35.5, 139.9, 35.7, 11)]
    // ~0.15° wide.
    [InlineData(139.6, 35.55, 139.75, 35.7, 12)]
    // ~0.07° wide — Tokyo ward scale.
    [InlineData(139.7, 35.65, 139.77, 35.72, 13)]
    // Tiny box (<0.05° on every axis) — small town / single block.
    [InlineData(139.700, 35.650, 139.715, 35.660, 14)]
    public void PickZoomForBounds_returns_expected_zoom_band(
        double westDeg, double southDeg, double eastDeg, double northDeg, int expectedZoom)
    {
        PlateauAreaBounds bounds = new PlateauAreaBounds(westDeg, southDeg, eastDeg, northDeg);
        Assert.Equal(expectedZoom, PlateauOnlineImportViewModel.PickZoomForBounds(bounds));
    }
}
