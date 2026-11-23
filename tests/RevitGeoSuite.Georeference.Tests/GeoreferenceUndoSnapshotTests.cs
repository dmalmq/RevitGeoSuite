using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.Storage;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class GeoreferenceUndoSnapshotTests
{
    [Fact]
    public void Serialize_and_deserialize_round_trips_full_undo_snapshot()
    {
        DateTime capturedAt = new DateTime(2026, 11, 23, 7, 15, 0, DateTimeKind.Utc);
        GeoreferenceUndoSnapshot original = new GeoreferenceUndoSnapshot
        {
            CapturedAtUtc = capturedAt,
            SiteLatitudeRadians = 35.681236 * (Math.PI / 180.0),
            SiteLongitudeRadians = 139.767125 * (Math.PI / 180.0),
            EastWestFeet = -19661.81,
            NorthSouthFeet = -116021.12,
            ElevationFeet = 40.35,
            AngleRadians = 0.25,
            SurveyPointXFeet = 12.5,
            SurveyPointYFeet = -34.25,
            SurveyPointZFeet = 3.0,
            SurveyPointClipped = true,
            SurveyPointPinned = false,
            PreviousGeoInfo = new GeoProjectInfo
            {
                SchemaVersion = 1,
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 36.0, Longitude = 139.83333333333334, ElevationMeters = 12.3 },
                TrueNorthAngle = 14.323944878,
                PrimaryMeshCode = new MeshCode { Value = "53394611" },
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Prior setup",
                ModelId = "model-123",
                GisStationFolder = "station-a"
            },
            PreviousModuleState = new GeoreferenceModuleState
            {
                WorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                    Origin = new ProjectOrigin { Latitude = 35.675, Longitude = 139.7625, ElevationMeters = 12.3 },
                    ProjectedCoordinate = new ProjectedCoordinate(-6412.5, -36055.25),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Prior setup"
                },
                LastUpdatedUtc = capturedAt
            },
            HasPreviousModuleStateSnapshot = true,
            Summary = "Before applying JGD2011 / Japan Plane Rectangular CS IX"
        };

        string json = GeoreferenceUndoSnapshotSerializer.Serialize(original);
        bool parsed = GeoreferenceUndoSnapshotSerializer.TryDeserialize(json, out GeoreferenceUndoSnapshot? restored);

        Assert.True(parsed);
        Assert.NotNull(restored);
        Assert.True(restored!.HasPreviousModuleStateSnapshot);
        Assert.Equal(GeoreferenceModuleStateRevertAction.Restore, GeoreferenceUndoStorage.ResolveModuleStateRevertAction(restored));
        Assert.Equal(original.CapturedAtUtc, restored.CapturedAtUtc);
        Assert.Equal(original.SiteLatitudeRadians, restored.SiteLatitudeRadians, 12);
        Assert.Equal(original.SiteLongitudeRadians, restored.SiteLongitudeRadians, 12);
        Assert.Equal(original.EastWestFeet, restored.EastWestFeet, 10);
        Assert.Equal(original.NorthSouthFeet, restored.NorthSouthFeet, 10);
        Assert.Equal(original.ElevationFeet, restored.ElevationFeet, 10);
        Assert.Equal(original.AngleRadians, restored.AngleRadians, 12);
        Assert.True(restored.HasSurveyPointPosition());
        Assert.Equal(12.5, restored.SurveyPointXFeet!.Value, 10);
        Assert.Equal(-34.25, restored.SurveyPointYFeet!.Value, 10);
        Assert.Equal(3.0, restored.SurveyPointZFeet!.Value, 10);
        Assert.True(restored.SurveyPointClipped);
        Assert.False(restored.SurveyPointPinned);
        Assert.NotNull(restored.PreviousGeoInfo);
        Assert.Equal(6677, restored.PreviousGeoInfo!.ProjectCrs!.EpsgCode);
        Assert.Equal("53394611", restored.PreviousGeoInfo.PrimaryMeshCode!.Value);
        Assert.Equal("model-123", restored.PreviousGeoInfo.ModelId);
        Assert.NotNull(restored.PreviousModuleState);
        Assert.Equal(6677, restored.PreviousModuleState!.WorkingProjectBasePoint!.ProjectCrs!.EpsgCode);
        Assert.Equal(-6412.5, restored.PreviousModuleState.WorkingProjectBasePoint.ProjectedCoordinate!.Value.Easting, 6);
        Assert.Equal(original.Summary, restored.Summary);
    }

    [Fact]
    public void TryDeserialize_handles_legacy_snapshot_without_survey_point_or_module_state()
    {
        const string legacyJson =
            "{\"CapturedAtUtc\":\"2026-01-01T00:00:00Z\",\"SiteLatitudeRadians\":0.62,\"SiteLongitudeRadians\":2.44," +
            "\"EastWestFeet\":10.0,\"NorthSouthFeet\":20.0,\"ElevationFeet\":5.0,\"AngleRadians\":0.0," +
            "\"PreviousGeoInfo\":null,\"Summary\":\"Legacy snapshot\"}";

        bool parsed = GeoreferenceUndoSnapshotSerializer.TryDeserialize(legacyJson, out GeoreferenceUndoSnapshot? restored);

        Assert.True(parsed);
        Assert.NotNull(restored);
        Assert.False(restored!.HasSurveyPointPosition());
        Assert.False(restored.HasPreviousModuleStateSnapshot);
        Assert.Null(restored.PreviousGeoInfo);
        Assert.Null(restored.PreviousModuleState);
        Assert.Equal(
            GeoreferenceModuleStateRevertAction.LeaveUnchanged,
            GeoreferenceUndoStorage.ResolveModuleStateRevertAction(restored));
        Assert.Equal(10.0, restored.EastWestFeet);
        Assert.Equal("Legacy snapshot", restored.Summary);
        GeoreferenceUndoStorage.ValidateForRestore(restored);
    }

    [Fact]
    public void Snapshot_captured_with_absent_module_state_removes_module_state_on_revert()
    {
        GeoreferenceUndoSnapshot capturedWithoutModuleState = new GeoreferenceUndoSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            HasPreviousModuleStateSnapshot = true,
            PreviousModuleState = null,
            Summary = "Fresh model before first georeference"
        };

        string json = GeoreferenceUndoSnapshotSerializer.Serialize(capturedWithoutModuleState);
        bool parsed = GeoreferenceUndoSnapshotSerializer.TryDeserialize(json, out GeoreferenceUndoSnapshot? restored);

        Assert.True(parsed);
        Assert.NotNull(restored);
        Assert.True(restored!.HasPreviousModuleStateSnapshot);
        Assert.Null(restored.PreviousModuleState);
        Assert.Equal(
            GeoreferenceModuleStateRevertAction.Remove,
            GeoreferenceUndoStorage.ResolveModuleStateRevertAction(restored));
    }

    [Fact]
    public void TryDeserialize_migrates_legacy_epsg_codes_in_geo_info_and_module_state()
    {
        GeoreferenceUndoSnapshot legacy = new GeoreferenceUndoSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            PreviousGeoInfo = new GeoProjectInfo
            {
                ProjectCrs = new CrsReference { EpsgCode = 10275, NameSnapshot = "JGD2024 / Japan Plane Rectangular CS IX" }
            },
            PreviousModuleState = new GeoreferenceModuleState
            {
                WorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 10275, NameSnapshot = "JGD2024 / Japan Plane Rectangular CS IX" }
                }
            }
        };

        string json = GeoreferenceUndoSnapshotSerializer.Serialize(legacy);
        bool parsed = GeoreferenceUndoSnapshotSerializer.TryDeserialize(json, out GeoreferenceUndoSnapshot? restored);

        Assert.True(parsed);
        Assert.NotNull(restored);
        Assert.Equal(6677, restored!.PreviousGeoInfo!.ProjectCrs!.EpsgCode);
        Assert.Equal(6677, restored.PreviousModuleState!.WorkingProjectBasePoint!.ProjectCrs!.EpsgCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ corrupt json")]
    public void TryDeserialize_returns_false_for_blank_or_corrupt_payloads(string? payload)
    {
        bool parsed = GeoreferenceUndoSnapshotSerializer.TryDeserialize(payload, out GeoreferenceUndoSnapshot? restored);

        Assert.False(parsed);
        Assert.Null(restored);
    }

    [Fact]
    public void ValidateForRestore_rejects_non_finite_and_incomplete_snapshots()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot { EastWestFeet = double.NaN }));

        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot { ElevationFeet = double.PositiveInfinity }));

        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot { SiteLatitudeRadians = 2.0 }));

        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot { SiteLongitudeRadians = 4.0 }));

        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot { SurveyPointXFeet = 10.0 }));

        Assert.Throws<InvalidOperationException>(() =>
            GeoreferenceUndoStorage.ValidateForRestore(new GeoreferenceUndoSnapshot
            {
                SurveyPointXFeet = 10.0,
                SurveyPointYFeet = double.NaN,
                SurveyPointZFeet = 0.0
            }));
    }

    [Fact]
    public void NormalizeAngleRadians_preserves_in_range_angles_and_wraps_out_of_range_angles()
    {
        const double quarterTurn = Math.PI / 2.0;
        Assert.Equal(quarterTurn, GeoreferenceUndoStorage.NormalizeAngleRadians(quarterTurn));
        Assert.Equal(-Math.PI, GeoreferenceUndoStorage.NormalizeAngleRadians(-Math.PI));
        Assert.Equal(Math.PI, GeoreferenceUndoStorage.NormalizeAngleRadians(Math.PI));

        double wrapped = GeoreferenceUndoStorage.NormalizeAngleRadians(3.0 * Math.PI / 2.0);
        Assert.Equal(-Math.PI / 2.0, wrapped, 12);
    }

    [Fact]
    public void ModuleStateStorageSerializer_RemoveEntry_removes_only_target_module()
    {
        Dictionary<string, StorageEnvelope<string>> entries = new Dictionary<string, StorageEnvelope<string>>(StringComparer.Ordinal)
        {
            [ModuleStateIds.Georeference] = new StorageEnvelope<string>
            {
                SchemaVersion = ModuleStateStorage.CurrentSchemaVersion,
                PayloadType = ModuleStatePayloadTypes.Georeference,
                Payload = "{}"
            },
            [ModuleStateIds.PlateauImport] = new StorageEnvelope<string>
            {
                SchemaVersion = ModuleStateStorage.CurrentSchemaVersion,
                PayloadType = ModuleStatePayloadTypes.PlateauImport,
                Payload = "{}"
            }
        };

        bool removed = ModuleStateStorageSerializer.RemoveEntry(entries, ModuleStateIds.Georeference);

        Assert.True(removed);
        Assert.False(entries.ContainsKey(ModuleStateIds.Georeference));
        Assert.True(entries.ContainsKey(ModuleStateIds.PlateauImport));
        Assert.False(ModuleStateStorageSerializer.RemoveEntry(entries, ModuleStateIds.Georeference));
    }
}
