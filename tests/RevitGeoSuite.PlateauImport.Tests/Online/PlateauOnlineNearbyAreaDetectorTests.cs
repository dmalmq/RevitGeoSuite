using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauOnlineNearbyAreaDetectorTests
{
    [Fact]
    public void GenerateSamplePoints_returns_three_rings_in_sixteen_directions()
    {
        PlateauOnlineSamplePoint[] points = PlateauOnlineNearbyAreaDetector.GenerateSamplePoints(35.6895, 139.6917);

        Assert.Equal(48, points.Length);
        Assert.Contains(points, point => point.DistanceMeters == 500.0);
        Assert.Contains(points, point => point.DistanceMeters == 1000.0);
        Assert.Contains(points, point => point.DistanceMeters == 1500.0);
    }

    [Fact]
    public void ResolveNearbyAreas_keeps_exact_area_first_and_filters_non_building_areas()
    {
        PlateauCatalog catalog = BuildCatalog(
            BuildDataset("13104", "bldg", "東京都", "東京都", "新宿区"),
            BuildDataset("13113", "bldg", "東京都", "東京都", "渋谷区"),
            BuildDataset("13105", "tran", "東京都", "東京都", "文京区"));
        var point = new PlateauOnlineProjectPoint(35.6895, 139.6917, "projectBasePoint", "Suggested from Project Base Point.");
        var samples = new[]
        {
            new PlateauOnlineSampleResult(new PlateauOnlineSamplePoint(35.68, 139.70, 500.0, 90.0), "13113"),
            new PlateauOnlineSampleResult(new PlateauOnlineSamplePoint(35.70, 139.69, 500.0, 0.0), "13105")
        };

        PlateauOnlineNearbyArea[] result = PlateauOnlineNearbyAreaDetector.ResolveNearbyAreas(catalog, "13104", samples, point);

        Assert.Equal(new[] { "13104", "13113" }, result.Select(area => area.Area.Code).ToArray());
        Assert.Equal(0.0, result[0].NearestDistanceMeters);
        Assert.Equal(500.0, result[1].NearestDistanceMeters);
    }

    [Fact]
    public void ResolveNearbyAreas_uses_closest_sample_for_duplicate_codes()
    {
        PlateauCatalog catalog = BuildCatalog(BuildDataset("13113", "bldg", "東京都", "東京都", "渋谷区"));
        var point = new PlateauOnlineProjectPoint(35.6895, 139.6917, "projectBasePoint", "Suggested from Project Base Point.");
        var samples = new[]
        {
            new PlateauOnlineSampleResult(new PlateauOnlineSamplePoint(35.68, 139.70, 1500.0, 90.0), "13113"),
            new PlateauOnlineSampleResult(new PlateauOnlineSamplePoint(35.68, 139.70, 500.0, 90.0), "13113")
        };

        PlateauOnlineNearbyArea[] result = PlateauOnlineNearbyAreaDetector.ResolveNearbyAreas(catalog, exactMunicipalityCode: null, samples, point);

        PlateauOnlineNearbyArea area = Assert.Single(result);
        Assert.Equal("13113", area.Area.Code);
        Assert.Equal(500.0, area.NearestDistanceMeters);
    }

    private static PlateauCatalog BuildCatalog(params PlateauDatasetEntry[] datasets)
    {
        return PlateauCatalog.Normalize(new PlateauCatalogResponse
        {
            LatestDatasets = new List<PlateauDatasetEntry>(datasets)
        });
    }

    private static PlateauDatasetEntry BuildDataset(string code, string typeEn, string pref, string city, string ward)
    {
        return new PlateauDatasetEntry
        {
            Format = "3D Tiles",
            TypeEn = typeEn,
            Url = $"https://example.test/{code}/{typeEn}/tileset.json",
            CityCode = city == ward ? null : code,
            WardCode = code,
            Pref = pref,
            City = city,
            Ward = ward,
            Lod = "2",
            Texture = false
        };
    }
}
