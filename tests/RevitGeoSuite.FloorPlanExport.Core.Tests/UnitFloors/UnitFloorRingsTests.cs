using NetTopologySuite.Geometries;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using Xunit;
using Polygon = NetTopologySuite.Geometries.Polygon;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.UnitFloors;

public sealed class UnitFloorRingsTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public void ShellComesFirstThenHoles_WithoutTheClosingPoint()
    {
        LinearRing shell = Factory.CreateLinearRing(new[]
        {
            new Coordinate(0, 0), new Coordinate(10, 0), new Coordinate(10, 10), new Coordinate(0, 10), new Coordinate(0, 0),
        });
        LinearRing hole = Factory.CreateLinearRing(new[]
        {
            new Coordinate(4, 4), new Coordinate(6, 4), new Coordinate(6, 6), new Coordinate(4, 6), new Coordinate(4, 4),
        });

        var rings = UnitFloorRings.FromPolygon(Factory.CreatePolygon(shell, new[] { hole }));

        Assert.Equal(2, rings.Count);
        Assert.Equal(4, rings[0].Count);
        Assert.Equal((0d, 0d), rings[0][0]);
        Assert.Equal(4, rings[1].Count);
    }

    [Fact]
    public void VerticesCloserThanTheMinimumSegmentAreDropped()
    {
        Polygon polygon = Factory.CreatePolygon(new[]
        {
            new Coordinate(0, 0), new Coordinate(10, 0), new Coordinate(10.0005, 0), new Coordinate(10, 10),
            new Coordinate(0, 10), new Coordinate(0, 0.0004), new Coordinate(0, 0),
        });

        var rings = UnitFloorRings.FromPolygon(polygon);

        Assert.Equal(4, Assert.Single(rings).Count);
    }

    [Fact]
    public void ARingThatCollapsesIsDropped()
    {
        Polygon tiny = Factory.CreatePolygon(new[]
        {
            new Coordinate(0, 0), new Coordinate(0.001, 0), new Coordinate(0.001, 0.001), new Coordinate(0, 0),
        });

        Assert.Empty(UnitFloorRings.FromPolygon(tiny));
    }
}
