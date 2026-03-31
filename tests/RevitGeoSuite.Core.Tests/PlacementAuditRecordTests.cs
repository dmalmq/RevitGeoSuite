using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class PlacementAuditRecordTests
{
    [Fact]
    public void Audit_record_serializes_expected_phase5_fields()
    {
        PlacementAuditRecord record = new PlacementAuditRecord
        {
            AppliedAtUtc = new System.DateTime(2026, 3, 31, 1, 2, 3, System.DateTimeKind.Utc),
            DocumentTitle = "Sample Project",
            ApplyMode = PlacementApplyMode.ProjectLocationAndAngle,
            AnchorTarget = PlacementAnchorTarget.ProjectBasePoint,
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            ProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            WorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 35.680910, Longitude = 139.768015, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(120.0, 340.0),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Entered EPSG:6677 coordinates for Working Project Base Point"
            },
            TrueNorthAngle = 22.5,
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Entered EPSG:6677 coordinates for Project Base Point",
            Summary = "Applied sample audit summary"
        };

        JObject json = JObject.Parse(JsonConvert.SerializeObject(record));
        double easting = json["ProjectedCoordinate"]?["Easting"]?.Value<double>() ?? double.NaN;
        double workingEasting = json["WorkingProjectBasePoint"]?["ProjectedCoordinate"]?["Easting"]?.Value<double>() ?? double.NaN;

        Assert.Equal("Sample Project", (string?)json["DocumentTitle"]);
        Assert.Equal("ProjectLocationAndAngle", (string?)json["ApplyMode"]);
        Assert.Equal("ProjectBasePoint", (string?)json["AnchorTarget"]);
        Assert.Equal(6677, (int?)json["ProjectCrs"]?["EpsgCode"]);
        Assert.True(System.Math.Abs(easting - (-5992.9196)) < 0.0001);
        Assert.True(System.Math.Abs(workingEasting - 120.0) < 0.0001);
        Assert.Equal("Applied sample audit summary", (string?)json["Summary"]);
    }
}
