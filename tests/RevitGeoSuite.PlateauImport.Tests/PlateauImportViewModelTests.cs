using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauImportViewModelTests
{
    [Fact]
    public void Working_project_base_point_is_preferred_for_tile_suggestions_when_available()
    {
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Sample Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" },
                StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                    Origin = new ProjectOrigin { Latitude = 35.67916666666667, Longitude = 139.76875, ElevationMeters = 0d },
                    ProjectedCoordinate = new ProjectedCoordinate(150d, 200d),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Test"
                }
            },
            new GeoProjectInfo
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Test"
            });

        Assert.Equal(PlateauImportReferenceSource.WorkingProjectBasePoint, viewModel.SelectedReferenceSource);
        Assert.Equal("Working Project Base Point", viewModel.ReferenceSourceTitle);
        Assert.Contains(viewModel.SuggestedTiles, tile => tile.IsPrimary && tile.TileId == "53394611");
    }

    [Fact]
    public void Read_only_document_can_preview_but_cannot_import()
    {
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Read Only Project",
                IsSupportedDocument = true,
                IsReadOnly = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            new GeoProjectInfo
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Test"
            });
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFilePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-origin-context.gml");

        bool loaded = viewModel.TryLoadPreview();

        Assert.True(loaded);
        Assert.Equal(2, viewModel.PreparedSolidCount);
        Assert.False(viewModel.CanImport);
        Assert.Contains("read-only", viewModel.StatusMessage);
    }

    private static PlateauImportViewModel CreateViewModel(CurrentProjectStateSummary currentState, GeoProjectInfo info)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new PlateauImportViewModel(
            currentState,
            info,
            null,
            new PlateauImportReferenceResolver(coordinateTransformer),
            new RevitGeoSuite.Core.Plateau.Tiles.PlateauTileIndex(),
            new CityGmlParser(),
            new ContextGeometryBuilder());
    }
}
