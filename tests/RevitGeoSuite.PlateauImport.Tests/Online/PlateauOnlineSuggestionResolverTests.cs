using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.PlateauImport.Online;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauOnlineSuggestionResolverTests
{
    [Fact]
    public void ResolveProjectPoint_prefers_shared_project_base_point_when_crs_is_available()
    {
        var transformer = new RecordingCoordinateTransformer(new GeographicCoordinate(35.6895, 139.6917));
        var state = new CurrentProjectStateSummary
        {
            ProjectBasePoint = new BasePointSnapshot
            {
                SharedEastWestFeet = 3280.839895,
                SharedNorthSouthFeet = 6561.67979,
                SharedElevationFeet = 0,
                EstimatedLatitudeDegrees = 34.0,
                EstimatedLongitudeDegrees = 135.0
            },
            StoredWorkingProjectBasePoint = BuildStoredWorkingProjectBasePoint(33.0, 132.0)
        };
        var info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" }
        };

        PlateauOnlineProjectPoint? point = PlateauOnlineSuggestionResolver.ResolveProjectPoint(state, info, transformer);

        Assert.NotNull(point);
        Assert.Equal("projectBasePoint", point.Source);
        Assert.Equal(35.6895, point.Latitude);
        Assert.Equal(139.6917, point.Longitude);
        Assert.True(transformer.LastUnprojected.HasValue);
        Assert.Equal(1000.0, transformer.LastUnprojected.Value.Easting, 6);
        Assert.Equal(2000.0, transformer.LastUnprojected.Value.Northing, 6);
    }

    [Fact]
    public void ResolveProjectPoint_uses_estimated_project_base_point_when_shared_crs_is_unavailable()
    {
        var state = new CurrentProjectStateSummary
        {
            ProjectBasePoint = new BasePointSnapshot
            {
                EstimatedLatitudeDegrees = 35.6895,
                EstimatedLongitudeDegrees = 139.6917
            },
            StoredWorkingProjectBasePoint = BuildStoredWorkingProjectBasePoint(33.0, 132.0)
        };

        PlateauOnlineProjectPoint? point = PlateauOnlineSuggestionResolver.ResolveProjectPoint(
            state,
            info: null,
            new RecordingCoordinateTransformer(new GeographicCoordinate(0, 0)));

        Assert.NotNull(point);
        Assert.Equal("projectBasePoint", point.Source);
        Assert.Equal(35.6895, point.Latitude);
        Assert.Equal(139.6917, point.Longitude);
    }

    [Fact]
    public void ResolveProjectPoint_falls_back_to_saved_working_project_base_point()
    {
        var state = new CurrentProjectStateSummary
        {
            ProjectBasePoint = new BasePointSnapshot(),
            StoredWorkingProjectBasePoint = BuildStoredWorkingProjectBasePoint(35.0, 138.0)
        };

        PlateauOnlineProjectPoint? point = PlateauOnlineSuggestionResolver.ResolveProjectPoint(
            state,
            info: null,
            new RecordingCoordinateTransformer(new GeographicCoordinate(0, 0)));

        Assert.NotNull(point);
        Assert.Equal("workingProjectBasePoint", point.Source);
        Assert.Equal(35.0, point.Latitude);
        Assert.Equal(138.0, point.Longitude);
    }

    [Fact]
    public void ResolveProjectPoint_returns_null_when_no_project_reference_exists()
    {
        var state = new CurrentProjectStateSummary
        {
            ProjectBasePoint = new BasePointSnapshot()
        };

        PlateauOnlineProjectPoint? point = PlateauOnlineSuggestionResolver.ResolveProjectPoint(
            state,
            info: null,
            new RecordingCoordinateTransformer(new GeographicCoordinate(0, 0)));

        Assert.Null(point);
    }

    [Fact]
    public void ResolveSuggestedArea_matches_reverse_geocode_code_and_keeps_project_point_coordinates()
    {
        PlateauCatalog catalog = BuildCatalog(
            BuildDataset("13104", "bldg", "東京都", "東京都", "新宿区"),
            BuildDataset("13113", "bldg", "東京都", "東京都", "渋谷区"));
        var point = new PlateauOnlineProjectPoint(35.6895, 139.6917, "projectBasePoint", "Suggested from Project Base Point.");

        PlateauOnlineSuggestedArea? suggestion = PlateauOnlineSuggestionResolver.ResolveSuggestedArea(catalog, "13104", point);

        Assert.NotNull(suggestion);
        Assert.Equal("13104", suggestion.Area.Code);
        Assert.Contains("新宿区", suggestion.DisplayLabel);
        Assert.Equal("13104", suggestion.CodeLabel);
        Assert.Equal(35.6895, suggestion.Latitude);
        Assert.Equal(139.6917, suggestion.Longitude);
    }

    [Fact]
    public void ResolveSuggestedArea_returns_null_when_area_has_no_building_dataset()
    {
        PlateauCatalog catalog = BuildCatalog(BuildDataset("13104", "tran", "東京都", "東京都", "新宿区"));
        var point = new PlateauOnlineProjectPoint(35.6895, 139.6917, "projectBasePoint", "Suggested from Project Base Point.");

        PlateauOnlineSuggestedArea? suggestion = PlateauOnlineSuggestionResolver.ResolveSuggestedArea(catalog, "13104", point);

        Assert.Null(suggestion);
    }

    private static WorkingProjectBasePointReference BuildStoredWorkingProjectBasePoint(double latitude, double longitude)
    {
        return new WorkingProjectBasePointReference
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = latitude, Longitude = longitude, ElevationMeters = 0 },
            ProjectedCoordinate = new ProjectedCoordinate(0, 0)
        };
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

    private sealed class RecordingCoordinateTransformer : ICoordinateTransformer
    {
        private readonly GeographicCoordinate unprojectResult;

        public RecordingCoordinateTransformer(GeographicCoordinate unprojectResult)
        {
            this.unprojectResult = unprojectResult;
        }

        public ProjectedCoordinate? LastUnprojected { get; private set; }

        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
        {
            return new ProjectedCoordinate(coordinate.Longitude, coordinate.Latitude);
        }

        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
        {
            LastUnprojected = coordinate;
            return unprojectResult;
        }
    }
}
