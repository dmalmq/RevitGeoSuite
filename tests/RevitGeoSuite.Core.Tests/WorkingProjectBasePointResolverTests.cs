using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class WorkingProjectBasePointResolverTests
{
    [Fact]
    public void Resolve_prefers_explicit_working_project_base_point_when_present()
    {
        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 35.681236, Longitude = 139.767125, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(-5992.9196, -35363.2377),
            AnchorTarget = PlacementAnchorTarget.SurveyPoint,
            WorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 35.680910, Longitude = 139.768015, ElevationMeters = 0d },
                ProjectedCoordinate = new ProjectedCoordinate(120.0, 340.0),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Entered EPSG:6677 coordinates for Working Project Base Point"
            }
        };

        WorkingProjectBasePointReference? resolved = WorkingProjectBasePointResolver.Resolve(intent);

        Assert.NotNull(resolved);
        Assert.Equal(120.0, resolved!.ProjectedCoordinate!.Value.Easting, 3);
        Assert.Equal("Entered EPSG:6677 coordinates for Working Project Base Point", resolved.SetupSource);
    }

    [Fact]
    public void Resolve_derives_working_project_base_point_from_primary_project_base_point_anchor_when_needed()
    {
        PlacementIntent intent = new PlacementIntent
        {
            SelectedCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            SelectedOrigin = new ProjectOrigin { Latitude = 36.0, Longitude = 139.833333, ElevationMeters = 0d },
            SelectedProjectedCoordinate = new ProjectedCoordinate(0.0, 0.0),
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Entered EPSG:6677 coordinates for Project Base Point",
            AnchorTarget = PlacementAnchorTarget.ProjectBasePoint
        };

        WorkingProjectBasePointReference? resolved = WorkingProjectBasePointResolver.Resolve(intent);

        Assert.NotNull(resolved);
        Assert.Equal(6677, resolved!.ProjectCrs!.EpsgCode);
        Assert.Equal(0.0, resolved.ProjectedCoordinate!.Value.Easting, 3);
        Assert.Equal(36.0, resolved.Origin!.Latitude, 3);
    }
}
