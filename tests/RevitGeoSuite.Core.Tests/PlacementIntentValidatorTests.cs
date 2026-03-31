using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class PlacementIntentValidatorTests
{
    [Fact]
    public void Validate_requires_true_north_angle_for_project_location_and_angle_mode()
    {
        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            ApplyMode = PlacementApplyMode.ProjectLocationAndAngle,
            SetupSource = "Selected from OSM map"
        };

        PlacementIntentValidationResult result = new PlacementIntentValidator().Validate(intent);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.IndexOf("true north angle", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Validate_requires_projected_coordinate_before_preview_or_apply()
    {
        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            ApplyMode = PlacementApplyMode.MetadataOnly,
            SetupSource = "Selected from OSM map"
        };

        PlacementIntentValidationResult result = new PlacementIntentValidator().Validate(intent);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.IndexOf("Projected Easting / Northing", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Validate_accepts_metadata_only_intent_with_required_fields()
    {
        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            ApplyMode = PlacementApplyMode.MetadataOnly,
            SetupSource = "Selected from OSM map"
        };

        PlacementIntentValidationResult result = new PlacementIntentValidator().Validate(intent);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
