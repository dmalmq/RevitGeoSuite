using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class PlacementPreviewServiceTests
{
    [Fact]
    public void CreatePreview_for_metadata_only_keeps_true_north_unchanged_and_warns_when_current_metadata_is_missing()
    {
        PlacementPreviewService service = CreateService();

        PlacementCurrentState currentState = new PlacementCurrentState
        {
            CurrentOrigin = new ProjectOrigin { Latitude = 35.680000, Longitude = 139.760000, ElevationMeters = 12.0 },
            CurrentOriginSource = "inferred from the Revit site location",
            CurrentTrueNorthAngleDegrees = 12.5,
            ExistingSetupDetected = true,
            HasStoredGeoMetadata = false
        };

        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            ApplyMode = PlacementApplyMode.MetadataOnly,
            Confidence = GeoConfidenceLevel.Approximate,
            SetupSource = "Selected from OSM map",
            AnchorTarget = PlacementAnchorTarget.Unspecified
        };

        PlacementPreview preview = service.CreatePreview(currentState, intent);

        Assert.True(preview.IsReadyToApply);
        Assert.Contains(preview.Fields, field => field.Label == "True North Angle" && field.CurrentValue == "12.500°" && field.ProposedValue == "12.500°");
        Assert.Contains(preview.Fields, field => field.Label == "Projected Coordinate" && field.ProposedValue.Contains("E -5992.9196 m"));
        Assert.Contains(preview.Fields, field => field.Label == "Anchor Target" && field.ProposedValue == "Survey Point (default)");
        Assert.Contains(preview.WhatWillNotChange, line => line.IndexOf("Project location offsets", System.StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(preview.Warnings, line => line.IndexOf("not stored yet", System.StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(preview.Warnings, line => line.IndexOf("Existing coordinate setup", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void CreatePreview_for_known_survey_point_coordinates_includes_anchor_target()
    {
        PlacementPreviewService service = CreateService();

        PlacementCurrentState currentState = new PlacementCurrentState
        {
            CurrentTrueNorthAngleDegrees = 0.0,
            HasStoredGeoMetadata = true,
            CurrentOrigin = new ProjectOrigin { Latitude = 35.680000, Longitude = 139.760000, ElevationMeters = 12.0 }
        };

        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 36.0, Longitude = 139.833333, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(0.0, 0.0),
            ApplyMode = PlacementApplyMode.ProjectLocation,
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Entered EPSG:6677 coordinates for Survey Point",
            AnchorTarget = PlacementAnchorTarget.SurveyPoint
        };

        PlacementPreview preview = service.CreatePreview(currentState, intent);

        Assert.True(preview.IsReadyToApply);
        Assert.Contains(preview.Fields, field => field.Label == "Anchor Target" && field.ProposedValue == "Survey Point");
        Assert.Contains(preview.WhatWillChange, line => line.IndexOf("Survey Point", System.StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains("audit log", preview.PersistenceSummary);
    }

    [Fact]
    public void CreatePreview_includes_working_project_base_point_when_defined()
    {
        PlacementPreviewService service = CreateService();

        PlacementCurrentState currentState = new PlacementCurrentState
        {
            CurrentCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            CurrentOrigin = new ProjectOrigin { Latitude = 35.680000, Longitude = 139.760000, ElevationMeters = 12.0 },
            CurrentWorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 35.680910, Longitude = 139.768015, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(100.0, 200.0),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Existing working point"
            },
            HasStoredGeoMetadata = true
        };

        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            ApplyMode = PlacementApplyMode.ProjectLocation,
            Confidence = GeoConfidenceLevel.Approximate,
            SetupSource = "Selected from OSM map",
            WorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 35.681000, Longitude = 139.767900, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(320.0, 480.0),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Entered EPSG:6677 coordinates for Working Project Base Point"
            }
        };

        PlacementPreview preview = service.CreatePreview(currentState, intent);

        Assert.Contains(preview.Fields, field => field.Label == "Working Project Base Point" && field.ProposedValue.Contains("E 320.0000 m"));
        Assert.Contains(preview.WhatWillChange, line => line.IndexOf("working Project Base Point", System.StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(preview.WhatWillChange, line => line.IndexOf("working Project Base Point", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static PlacementPreviewService CreateService()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        CoordinateValidator validator = new CoordinateValidator(registry, transformer, new JapanMeshCalculator());
        return new PlacementPreviewService(validator);
    }
}

