using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauContextExportPipelineTests
{
    [Fact]
    public void BuildOutlineDxfExportPackage_builds_context_package_and_preserves_dxf_anchoring()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauFolderScanResult scanResult = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(fixtureFolder);
        PlateauImportReferenceContext referenceContext = CreateReferenceContext();
        CurrentProjectStateSummary currentState = CreateProjectState();
        PlateauContextExportPipeline pipeline = new PlateauContextExportPipeline(
            new ContextGeometryBuilder(),
            new CoordinateTransformer(new CrsRegistry()),
            currentState,
            _ => new KibanScanResult(Array.Empty<KibanParsedFeature>(), Array.Empty<KibanParsedPolygonFeature>(), 0, 0));

        ShapefileExportRequest request = new ShapefileExportRequest(
            scanResult,
            referenceContext,
            new[] { PlateauFeatureType.Building, PlateauFeatureType.Road },
            new[] { "53394536" },
            string.Empty,
            kibanParsedFeatures: null,
            kibanParsedPolygonFeatures: null,
            selectedKibanLayerNames: Array.Empty<string>(),
            hasKibanLayerOptions: false);

        PlateauOutlineDxfExportPackage package = pipeline.BuildOutlineDxfExportPackage(
            request,
            acceptedLandUseClassNames: null,
            revitModelFeatures: Array.Empty<RevitModelFootprintFeature>(),
            progress: null,
            out IReadOnlyList<string> warnings);

        Assert.NotNull(warnings);
        Assert.Contains(package.Features, feature => string.Equals(feature.Layer, "PLATEAU_BUILDINGS", StringComparison.Ordinal));
        Assert.NotEmpty(package.RoadAreas);
        Assert.DoesNotContain(package.Features, feature => string.Equals(feature.Layer, "PLATEAU_ROADS", StringComparison.Ordinal));
        Assert.Equal(6677, package.ProjectCrs.EpsgCode);
        Assert.Equal(100d, package.OriginOffsetMetres.X, 6);
        Assert.Equal(200d, package.OriginOffsetMetres.Y, 6);
        Assert.Equal(110d, package.ProjectBasePointMarkerMetres.X, 6);
        Assert.Equal(220d, package.ProjectBasePointMarkerMetres.Y, 6);
    }

    private static PlateauImportReferenceContext CreateReferenceContext()
    {
        return new PlateauImportReferenceContext
        {
            Title = "Test reference",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorLatitude = 36d,
            AnchorLongitude = 139.833333333333d,
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d,
            SharedEastToLocalX = 1d,
            SharedEastToLocalY = 0d,
            SharedNorthToLocalX = 0d,
            SharedNorthToLocalY = 1d
        };
    }

    private static CurrentProjectStateSummary CreateProjectState()
    {
        return new CurrentProjectStateSummary
        {
            DocumentTitle = "Pipeline Test Project",
            IsSupportedDocument = true,
            HasStoredGeoInfo = true,
            SurveyPoint = new BasePointSnapshot
            {
                Name = "Survey Point",
                SharedEastWestFeet = 100d / 0.3048d,
                SharedNorthSouthFeet = 200d / 0.3048d,
                SharedElevationFeet = 0d
            },
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                SharedEastWestFeet = 110d / 0.3048d,
                SharedNorthSouthFeet = 220d / 0.3048d,
                SharedElevationFeet = 0d
            }
        };
    }
}
