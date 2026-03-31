using System;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class ContextGeometryBuilderTests
{
    [Fact]
    public void BuildPlan_converts_projected_fixture_geometry_into_local_feet()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-origin-context.gml");
        PlateauCityModel model = new CityGmlParser().ParseFile(fixturePath);
        ContextGeometryBuilder builder = new ContextGeometryBuilder();
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            Title = "Working Project Base Point",
            Description = "Test reference",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorLatitude = 36d,
            AnchorLongitude = 139.833333333333d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext);
        ContextSolidPlan firstSolid = plan.Solids.First();
        var firstPoint = firstSolid.FootprintPointsFeet.First();

        Assert.Equal(2, plan.Solids.Count);
        Assert.Equal(328.084, firstPoint.XFeet, 3);
        Assert.Equal(492.126, firstPoint.YFeet, 3);
        Assert.Equal(32.808, firstSolid.HeightFeet, 3);
    }

    [Fact]
    public void BuildPlan_rejects_epsg_mismatch_between_file_and_project_reference()
    {
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 6677,
            Buildings = new[]
            {
                new PlateauBuildingFeature
                {
                    Id = "bldg-1",
                    Name = "Test",
                    ExteriorRing = new[]
                    {
                        new PlateauCoordinate3D(0, 0, 0),
                        new PlateauCoordinate3D(10, 0, 0),
                        new PlateauCoordinate3D(10, 10, 0),
                        new PlateauCoordinate3D(0, 10, 0),
                        new PlateauCoordinate3D(0, 0, 0)
                    }
                }
            }
        };
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6669, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS I" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d)
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new ContextGeometryBuilder().BuildPlan(model, referenceContext));

        Assert.Contains("EPSG:6677", ex.Message);
        Assert.Contains("EPSG:6669", ex.Message);
    }
}
