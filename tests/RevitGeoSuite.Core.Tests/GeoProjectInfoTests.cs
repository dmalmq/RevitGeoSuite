using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class GeoProjectInfoTests
{
    [Fact]
    public void EnsureModelId_assigns_once_and_keeps_existing_id()
    {
        GeoProjectInfo info = new GeoProjectInfo();
        Guid first = new Guid("3f2b8c1d-6a4e-4b7f-9c2d-1e5a7b9c0d3f");

        Assert.True(info.EnsureModelId(() => first));
        Assert.Equal("3f2b8c1d-6a4e-4b7f-9c2d-1e5a7b9c0d3f", info.ModelId);
        Assert.False(info.EnsureModelId(() => Guid.NewGuid()));
        Assert.Equal("3f2b8c1d-6a4e-4b7f-9c2d-1e5a7b9c0d3f", info.ModelId);
    }

    [Fact]
    public void ModelId_round_trips_through_json()
    {
        GeoProjectInfo info = new GeoProjectInfo { ModelId = "model-1" };

        GeoProjectInfo? reloaded = JsonConvert.DeserializeObject<GeoProjectInfo>(JsonConvert.SerializeObject(info));

        Assert.Equal("model-1", reloaded?.ModelId);
    }

    [Fact]
    public void Defaults_are_safe_for_phase1_scaffolding()
    {
        GeoProjectInfo info = new GeoProjectInfo();

        Assert.Equal(1, info.SchemaVersion);
        Assert.Equal(GeoConfidenceLevel.Unknown, info.Confidence);
        Assert.Null(info.ProjectCrs);
        Assert.Null(info.Origin);
        Assert.Null(info.PrimaryMeshCode);
        Assert.Equal(string.Empty, info.SetupSource);
    }

    [Fact]
    public void Serialization_uses_expected_canonical_fields()
    {
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 12.5 },
            PrimaryMeshCode = new MeshCode { Value = "53394611" },
            Confidence = GeoConfidenceLevel.Approximate,
            SetupSource = "Phase1Test"
        };

        JObject json = JObject.Parse(JsonConvert.SerializeObject(info));

        Assert.Equal(6677, (int?)json["ProjectCrs"]?["EpsgCode"]);
        Assert.Equal("53394611", (string?)json["PrimaryMeshCode"]?["Value"]);
        Assert.Equal("Phase1Test", (string?)json["SetupSource"]);
        Assert.NotNull(json["Origin"]);
        Assert.Null(json["Warnings"]);
    }

    [Fact]
    public void Applying_changed_canonical_location_invalidates_primary_mesh_code()
    {
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 12.5 },
            TrueNorthAngle = 0.0,
            PrimaryMeshCode = new MeshCode { Value = "53394611" }
        };

        info.ApplyCanonicalLocation(
            new CrsReference { EpsgCode = 6674, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS VI" },
            new ProjectOrigin { Latitude = 34.693725, Longitude = 135.502254, ElevationMeters = 20.0 },
            0.1);

        Assert.Null(info.PrimaryMeshCode);
        Assert.Equal(6674, info.ProjectCrs!.EpsgCode);
        Assert.Equal(34.693725, info.Origin!.Latitude);
        Assert.Equal(0.1, info.TrueNorthAngle);
    }

    [Fact]
    public void Applying_same_canonical_location_keeps_primary_mesh_code()
    {
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 12.5 },
            TrueNorthAngle = 0.0,
            PrimaryMeshCode = new MeshCode { Value = "53394611" }
        };

        info.ApplyCanonicalLocation(
            new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 12.5 },
            0.0);

        Assert.Equal("53394611", info.PrimaryMeshCode!.Value);
    }
}
