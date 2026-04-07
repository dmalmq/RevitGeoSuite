using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class CityGmlExportViewModelTests
{
    [Fact]
    public void Previous_export_state_is_restored_on_startup()
    {
        CityGmlExportState exportState = new CityGmlExportState
        {
            LastExportPath = @"C:\\exports\\citygml",
            LastExportDateUtc = new DateTime(2026, 4, 3, 8, 0, 0, DateTimeKind.Utc),
            LastReferenceSource = CityGmlExportReferenceSource.CanonicalOrigin,
            LastExportedFeatureCount = 3,
            TargetSchemaVersion = CityGmlExportProfile.LightweightCityGml20
        };

        CityGmlExportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "CityGML Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            CreateGeoProjectInfo(),
            exportState);

        Assert.Equal(exportState.LastExportPath, viewModel.OutputDirectory);
        Assert.True(viewModel.HasLastExportRows);
        Assert.Equal(CityGmlExportReferenceSource.CanonicalOrigin, viewModel.SelectedReferenceSource);
    }

    private static CityGmlExportViewModel CreateViewModel(CurrentProjectStateSummary currentState, GeoProjectInfo info, CityGmlExportState? state = null)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new CityGmlExportViewModel(
            currentState,
            info,
            state,
            new CityGmlExportReferenceResolver(coordinateTransformer));
    }

    private static GeoProjectInfo CreateGeoProjectInfo()
    {
        return new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };
    }
}
