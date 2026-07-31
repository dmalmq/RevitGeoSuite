using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class CoordinateValidatorTests
{
    [Fact]
    public void Valid_tokyo_project_info_has_no_warnings()
    {
        CrsRegistry registry = new CrsRegistry();
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        CoordinateValidator validator = new CoordinateValidator(registry, new CoordinateTransformer(registry), meshCalculator);
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 12.5 },
            PrimaryMeshCode = meshCalculator.CalculatePrimaryMesh(35.681236, 139.767125)
        };

        Assert.Empty(validator.Validate(info));
    }

    [Fact]
    public void Missing_or_invalid_core_inputs_are_flagged()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateValidator validator = new CoordinateValidator(registry, new CoordinateTransformer(registry), new JapanMeshCalculator());
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 999999, NameSnapshot = "Unknown" }
        };

        string[] codes = validator.Validate(info).Select(result => result.Code).ToArray();

        Assert.Contains("invalid-crs", codes);
        Assert.Contains("missing-origin", codes);
    }

    [Fact]
    public void Suspicious_ranges_and_stale_mesh_are_flagged()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateValidator validator = new CoordinateValidator(registry, new CoordinateTransformer(registry), new JapanMeshCalculator());
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 10.0, Longitude = 200.0, ElevationMeters = 8000.0 },
            PrimaryMeshCode = new MeshCode { Value = "53394611" }
        };

        string[] codes = validator.Validate(info).Select(result => result.Code).ToArray();

        Assert.Contains("outside-japan-range", codes);
        Assert.Contains("suspicious-elevation", codes);
        Assert.Contains("stale-mesh", codes);
    }

    [Fact]
    public void Large_projected_offsets_are_soft_warnings()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateValidator validator = new CoordinateValidator(registry, new CoordinateTransformer(registry), new JapanMeshCalculator());
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6674, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS VI" },
            Origin = new ProjectOrigin { Latitude = 34.693725, Longitude = 135.502254, ElevationMeters = 20.0 }
        };

        ValidationResult result = Assert.Single(validator.Validate(info));
        Assert.Equal("large-offset", result.Code);
        Assert.Equal(ValidationSeverity.Warning, result.Severity);
    }
}
