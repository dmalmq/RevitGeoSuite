using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using Xunit;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using Polygon = NetTopologySuite.Geometries.Polygon;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.UnitFloors;

public sealed class UnitFloorPlannerTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public void EachUnitTakesTheTypeOfTheFloorUnderMostOfIt()
    {
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 20, 10));
        HostFloorOutline concourse = Host("concourse", "j ラチ外コンコース_床", Box(20, 0, 40, 10));
        UnitFloorSource left = new("1", Box(0, 0, 10, 10));
        UnitFloorSource right = new("2", Box(10, 0, 20, 10));
        // Mostly on the concourse, a little over the shop floor.
        UnitFloorSource straddling = new("3", Box(18, 0, 30, 10));

        UnitFloorPlan plan = UnitFloorPlanner.Plan(new[] { left, right, straddling }, new[] { shops, concourse });

        Assert.All(plan.Units, unit => Assert.Equal(UnitFloorStatus.Matched, unit.Status));
        Assert.Same(shops, plan.Units[0].Host);
        Assert.Same(shops, plan.Units[1].Host);
        Assert.Same(concourse, plan.Units[2].Host);
        Assert.Equal(10d / 12d, plan.Units[2].HostShare, 6);
        Assert.Equal(3, plan.CreateCount);
    }

    [Fact]
    public void AFloorCoveredByTheNewFloorsIsReplaceable_APartlyCoveredOneIsNot()
    {
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 20, 10));
        HostFloorOutline concourse = Host("concourse", "j ラチ外コンコース_床", Box(20, 0, 40, 10));
        UnitFloorSource[] units =
        {
            new("1", Box(0, 0, 10, 10)),
            new("2", Box(10, 0, 20, 10)),
            new("3", Box(20, 0, 30, 10)),
        };

        UnitFloorPlan plan = UnitFloorPlanner.Plan(units, new[] { shops, concourse });

        HostFloorCoverage shopCoverage = plan.Hosts.Single(h => h.Host == shops);
        HostFloorCoverage concourseCoverage = plan.Hosts.Single(h => h.Host == concourse);
        Assert.True(shopCoverage.Replaceable);
        Assert.Equal(2, shopCoverage.UnitCount);
        Assert.Equal(1d, shopCoverage.CoveredShare, 6);
        Assert.False(concourseCoverage.Replaceable);
        Assert.Equal(0.5, concourseCoverage.CoveredShare, 6);
    }

    [Fact]
    public void AUnitWithNoFloorUnderItIsKeptWithoutAHost()
    {
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 10, 10));
        UnitFloorSource outside = new("9", Box(50, 50, 55, 55));

        PlannedUnitFloor planned = Assert.Single(UnitFloorPlanner.Plan(new[] { outside }, new[] { shops }).Units);

        Assert.Equal(UnitFloorStatus.NoHost, planned.Status);
        Assert.Null(planned.Host);
        Assert.True(planned.WillCreate);
    }

    [Fact]
    public void DuplicateKeysAndNonPolygonsGetNoFloor()
    {
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 10, 10));
        UnitFloorSource[] units =
        {
            new("a", Box(0, 0, 5, 5)),
            new("a", Box(5, 0, 10, 5)),
            new("b", Factory.CreatePoint(new Coordinate(1, 1))),
            new("c", null),
        };

        UnitFloorPlan plan = UnitFloorPlanner.Plan(units, new[] { shops });

        Assert.Equal(2, plan.CountOf(UnitFloorStatus.DuplicateKey));
        Assert.Equal(2, plan.CountOf(UnitFloorStatus.NotPolygon));
        Assert.Equal(0, plan.CreateCount);
    }

    [Fact]
    public void MultiPolygonUnitsKeepEveryPartAndDropSlivers()
    {
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 30, 10));
        MultiPolygon unit = Factory.CreateMultiPolygon(new[]
        {
            Box(0, 0, 10, 10),
            Box(20, 0, 30, 10),
            Box(15, 0, 15.1, 0.1),
        });

        PlannedUnitFloor planned = Assert.Single(UnitFloorPlanner.Plan(new[] { new UnitFloorSource("m", unit) }, new[] { shops }).Units);

        Assert.Equal(2, planned.Polygons.Count);
        Assert.Equal(200, planned.Area, 6);
    }

    [Fact]
    public void ASelfIntersectingUnitIsRepaired()
    {
        // A bow tie: two triangles that touch in the middle.
        Polygon bowTie = Factory.CreatePolygon(new[]
        {
            new Coordinate(0, 0), new Coordinate(10, 10), new Coordinate(10, 0), new Coordinate(0, 10), new Coordinate(0, 0),
        });
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Box(0, 0, 10, 10));

        PlannedUnitFloor planned = Assert.Single(UnitFloorPlanner.Plan(new[] { new UnitFloorSource("x", bowTie) }, new[] { shops }).Units);

        Assert.True(planned.WillCreate);
        Assert.All(planned.Polygons, polygon => Assert.True(polygon.IsValid));
    }

    [Fact]
    public void AFaceWithSeparateRegionsBecomesOnePolygonPerRegion()
    {
        // As Revit gives a floor whose one top face has two regions: the larger one with an
        // opening, and an island inside that opening.
        LinearRing[] rings =
        {
            Box(0, 0, 20, 20).Shell,
            Box(30, 0, 40, 10).Shell,
            Box(5, 5, 15, 15).Shell,
            Box(8, 8, 12, 12).Shell,
        };

        IReadOnlyList<Polygon> polygons = UnitFloorPlanner.PolygonsFromRings(rings, Factory);

        Assert.Equal(3, polygons.Count);
        Assert.Equal(400 - 100, polygons[0].Area, 6);
        Assert.Equal(100, polygons[1].Area, 6);
        Assert.Equal(16, polygons[2].Area, 6);

        // A unit in the second region takes this floor's type; one in the opening does not.
        HostFloorOutline shops = Host("shops", "j ラチ外店舗_床", Factory.CreateMultiPolygon(polygons.ToArray()));
        UnitFloorPlan plan = UnitFloorPlanner.Plan(
            new[] { new UnitFloorSource("in-second", Box(31, 1, 39, 9)), new UnitFloorSource("in-opening", Box(5.5, 5.5, 7.5, 14.5)) },
            new[] { shops });
        Assert.Equal(UnitFloorStatus.Matched, plan.Units[0].Status);
        Assert.Equal(UnitFloorStatus.NoHost, plan.Units[1].Status);
    }

    private static HostFloorOutline Host(string uniqueId, string typeName, NtsGeometry outline)
    {
        return new HostFloorOutline(uniqueId, uniqueId.Length, 1, typeName, outline);
    }

    internal static Polygon Box(double x0, double y0, double x1, double y1)
    {
        return Factory.CreatePolygon(new[]
        {
            new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0),
        });
    }
}
