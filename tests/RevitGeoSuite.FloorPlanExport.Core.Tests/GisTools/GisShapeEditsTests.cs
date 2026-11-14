using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisShapeEditsTests
{
    private const double Feet = 1 / 0.3048;

    // The export's transform: model feet to project metres, rotated 30° and moved to JGD2011 IX-like coordinates.
    private static readonly AffineTransform2D ModelToProject = Rotated(30, -5800, -35000);

    private static AffineTransform2D Rotated(double degrees, double east, double north)
    {
        double angle = degrees * Math.PI / 180;
        double scale = 0.3048;
        return new AffineTransform2D(
            new Point2D(east, north),
            new Point2D(Math.Cos(angle) * scale, Math.Sin(angle) * scale),
            new Point2D(-Math.Sin(angle) * scale, Math.Cos(angle) * scale));
    }

    // A rectangle in model feet, given in metres.
    private static Polygon2D Rect(double minX, double minY, double maxX, double maxY) => new(new[]
    {
        new Point2D(minX * Feet, minY * Feet),
        new Point2D(maxX * Feet, minY * Feet),
        new Point2D(maxX * Feet, maxY * Feet),
        new Point2D(minX * Feet, maxY * Feet),
    });

    // The same rectangle as the GDB holds it: in project metres, counter-clockwise, closed.
    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> Gdb(params Polygon2D[] polygons) =>
        polygons.Select(polygon => (IReadOnlyList<IReadOnlyList<(double X, double Y)>>)new[]
        {
            (IReadOnlyList<(double X, double Y)>)polygon.ExteriorRing
                .Concat(new[] { polygon.ExteriorRing[0] })
                .Select(point => ModelToProject.Transform(point))
                .Select(point => (point.X, point.Y))
                .ToList(),
        }).ToList();

    private static GisDriftShape Shape(string edit, string uniqueId, params Polygon2D[] outline) =>
        new(edit, "m|" + uniqueId + "|0", uniqueId, "unit", "ショップ", "lvl-1f", string.Empty, false, Gdb(outline));

    private static (IReadOnlyList<ShapeEdit> Edits, IReadOnlyList<string> Problems) Plan(GisDriftShape[] shapes, ShapeFloor[] floors, int fileEpsg = 6677, int modelEpsg = 6677) =>
        GisShapeEdits.Plan(
            new GisDriftFile("m", "v1", Array.Empty<GisDriftRow>(), fileEpsg, shapes),
            floors.ToDictionary(floor => floor.UniqueId),
            ModelToProject,
            modelEpsg);

    [Fact]
    public void TheInverseUndoesTheExportTransform()
    {
        AffineTransform2D back = ModelToProject.Inverse();
        Point2D model = new(123.4, -56.7);

        Point2D roundTrip = back.Transform(ModelToProject.Transform(model));

        Assert.Equal(model.X, roundTrip.X, 9);
        Assert.Equal(model.Y, roundTrip.Y, 9);
    }

    [Fact]
    public void AReshapedFloorGetsTheGdbOutlineInModelFeet()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 12, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });

        Assert.Empty(problems);
        ShapeEdit edit = Assert.Single(edits);
        Assert.Equal(ShapeEditKind.Reshape, edit.Kind);
        Assert.Equal(101, edit.Floor.ElementId);
        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 12, 10) }, edit.Outline));
        Assert.Equal("Reshape floor 101 (ショップ) to the GDB outline", edit.Label());
    }

    private static Polygon2D Holed(Polygon2D outline, Polygon2D hole) => new(outline.ExteriorRing, new[] { hole.ExteriorRing });

    private static double Area(IReadOnlyList<Polygon2D> outline) =>
        outline.Sum(polygon => Math.Abs(RingArea(polygon.ExteriorRing)) - polygon.InteriorRings.Sum(ring => Math.Abs(RingArea(ring)))) * 0.3048 * 0.3048;

    private static double RingArea(IReadOnlyList<Point2D> ring) =>
        Enumerable.Range(0, ring.Count).Sum(index => (ring[index].X * ring[(index + 1) % ring.Count].Y) - (ring[(index + 1) % ring.Count].X * ring[index].Y)) / 2;

    [Fact]
    public void APlainFloorIsSketchedAsTheGdbOutline()
    {
        ShapeFloor floor = new("a", 101, new[] { Rect(0, 0, 10, 10) });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { Rect(0, 0, 11, 10) });

        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 11, 10) }, sketch));
    }

    [Fact]
    public void AnOpeningThatCutsTheFloorStaysOutOfItsSketch()
    {
        // The top face has the opening's hole, the sketch does not; the GDB grows the floor 1 m east around the same hole.
        ShapeFloor floor = new("a", 101, new[] { Holed(Rect(0, 0, 10, 10), Rect(4, 4, 5, 5)) }, sketch: new[] { Rect(0, 0, 10, 10) });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { Holed(Rect(0, 0, 11, 10), Rect(4, 4, 5, 5)) });

        Polygon2D only = Assert.Single(sketch);
        Assert.Empty(only.InteriorRings);
        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 11, 10) }, sketch));
    }

    [Fact]
    public void AFloorWhoseSketchAlreadyGivesTheGdbOutlineIsAProblemNotACard()
    {
        // Reshaped once: the sketch has the GDB outline, an opening still cuts the top face where the GDB has none.
        ShapeFloor floor = new("a", 101, new[] { Holed(Rect(0, 0, 11, 10), Rect(4, 4, 5, 5)) }, sketch: new[] { Rect(0, 0, 11, 10) });

        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 11, 10)) }, new[] { floor });

        Assert.Empty(edits);
        Assert.Contains("already has the GDB outline in its sketch", Assert.Single(problems));
    }

    [Fact]
    public void ARoundingDifferenceAlongAnEdgeIsNotAnEdit()
    {
        ShapeFloor floor = new("a", 101, new[] { Rect(0, 0, 10, 10) });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { Rect(0, 0.002, 11, 10) });

        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 11, 10) }, sketch));
        Assert.Equal(110, Area(sketch), 1);
    }

    [Fact]
    public void ASketchEdgeAFractionOfAMillimetreFromTheTopFaceLeavesNoSliverHole()
    {
        // The sketch's east edge and the top face's round to grid lines 1 mm apart; the GDB grows the top face 1 m east.
        ShapeFloor floor = new("a", 101, new[] { Rect(0, 0, 10.0005001, 10) }, sketch: new[] { Rect(0, 0, 10.0004999, 10) });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { Rect(0, 0, 11.0005001, 10) });

        Polygon2D only = Assert.Single(sketch);
        Assert.Empty(only.InteriorRings);
        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 11, 10) }, sketch));
    }

    [Fact]
    public void ASketchCornerAFractionOfAMillimetreFromTheTopFaceLeavesNoSliverSpike()
    {
        // The GDB removes a notch whose edge sits a hair inside the sketch's: no 1 mm wall is left standing.
        ShapeFloor floor = new("a", 101, new[] { Rect(0, 0, 10, 10) }, sketch: new[] { Rect(0, 0, 10.0004999, 10) });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { new Polygon2D(new[]
        {
            new Point2D(0, 0), new Point2D(10 * Feet, 0), new Point2D(10 * Feet, 8 * Feet), new Point2D(9 * Feet, 8 * Feet),
            new Point2D(9 * Feet, 10 * Feet), new Point2D(0, 10 * Feet),
        }) });

        Assert.Single(sketch);
        Assert.Equal(98, Area(sketch), 2);
        Assert.True(sketch[0].ExteriorRing.Count <= 7, string.Join(" ", sketch[0].ExteriorRing.Select(point => $"({point.X * 0.3048:F4},{point.Y * 0.3048:F4})")));
    }

    // LUMINE floor 6481239 in model feet, from the c3a run on 0108caf: its sketch; its outline as the
    // export writes it, with a 1.7 m x 1.5 m rectangle trimmed where a vertical unit overlaps it; and
    // the GDB outline, the export's with one east corner moved 1 m.
    private static readonly Polygon2D LumineSketch = new(new[]
        {
            new Point2D(-939.5462, 5373.4361),
            new Point2D(-936.6566, 5413.0508),
            new Point2D(-929.5232, 5412.5304),
            new Point2D(-929.4373, 5413.7078),
            new Point2D(-909.4915, 5412.2529),
            new Point2D(-909.3399, 5414.3306),
            new Point2D(-907.7038, 5414.2113),
            new Point2D(-907.1401, 5422.2855),
            new Point2D(-884.0631, 5420.6022),
            new Point2D(-885.1966, 5405.0632),
            new Point2D(-878.5419, 5404.5778),
            new Point2D(-881.1243, 5369.1746),
        });

    private static readonly Polygon2D LumineExported = new(new[]
        {
            new Point2D(-939.5468, 5373.4281),
            new Point2D(-936.6572, 5413.0428),
            new Point2D(-929.5238, 5412.5225),
            new Point2D(-929.4379, 5413.6998),
            new Point2D(-909.4921, 5412.2449),
            new Point2D(-909.3405, 5414.3226),
            new Point2D(-907.7044, 5414.2033),
            new Point2D(-907.1407, 5422.2775),
            new Point2D(-884.0630, 5420.6020),
            new Point2D(-884.1866, 5418.9092),
            new Point2D(-889.7601, 5419.3158),
            new Point2D(-890.1181, 5414.4075),
            new Point2D(-884.5446, 5414.0010),
            new Point2D(-884.6626, 5412.3833),
            new Point2D(-885.7342, 5412.4614),
            new Point2D(-886.2687, 5405.1333),
            new Point2D(-887.2361, 5391.8709),
            new Point2D(-879.5099, 5391.3073),
            new Point2D(-879.5740, 5390.4284),
            new Point2D(-879.9611, 5385.1211),
            new Point2D(-881.1249, 5369.1667),
        });

    private static readonly Polygon2D LumineGdb = new(new[]
        {
            new Point2D(-939.5468, 5373.4281),
            new Point2D(-936.6572, 5413.0428),
            new Point2D(-929.5238, 5412.5225),
            new Point2D(-929.4379, 5413.6998),
            new Point2D(-909.4921, 5412.2449),
            new Point2D(-909.3405, 5414.3226),
            new Point2D(-907.7044, 5414.2033),
            new Point2D(-907.1407, 5422.2775),
            new Point2D(-883.4103, 5423.8093),
            new Point2D(-884.1866, 5418.9092),
            new Point2D(-889.7601, 5419.3158),
            new Point2D(-890.1181, 5414.4075),
            new Point2D(-884.5446, 5414.0010),
            new Point2D(-884.6626, 5412.3833),
            new Point2D(-885.7342, 5412.4614),
            new Point2D(-886.2687, 5405.1333),
            new Point2D(-887.2361, 5391.8709),
            new Point2D(-879.5099, 5391.3073),
            new Point2D(-879.5740, 5390.4284),
            new Point2D(-879.9611, 5385.1211),
            new Point2D(-881.1249, 5369.1667),
        });

    [Fact]
    public void AnExportTrimIsNotAGdbEditOnlyTheMovedCornerIs()
    {
        ShapeFloor floor = new("f", 6481239, new[] { LumineExported }, sketch: new[] { LumineSketch });

        List<Polygon2D> sketch = GisShapeEdits.SketchFor(floor, new[] { LumineGdb });

        Polygon2D only = Assert.Single(sketch);
        Assert.Empty(only.InteriorRings);
        Assert.True(GisShapeEdits.CanSketch(sketch));

        // The sketch only grows, by the wedge the moved corner adds (about 7 m x 1 m / 2): the trimmed rectangle stays in it.
        double grown = Area(sketch) - Area(new[] { LumineSketch });
        Assert.InRange(grown, 3, 4);
        Assert.InRange(GisShapeEdits.GapMetres(sketch, new[] { LumineSketch }), 0.9, 1.1);
    }

    [Fact]
    public void AFloorWhoseExportOutlineIsTheGdbOutlineIsSettled()
    {
        ShapeFloor floor = new("f", 6481239, new[] { LumineExported }, sketch: new[] { LumineSketch });

        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(new[] { Shape(GisDrift.Reshaped, "f", LumineExported) }, new[] { floor });

        Assert.Empty(edits);
        Assert.Empty(problems);
    }

    [Fact]
    public void SketchLoopsFollowTheEvenOddRule()
    {
        List<Polygon2D> region = GisShapeEdits.FromLoops(new[] { Rect(0, 0, 10, 10).ExteriorRing, Rect(4, 4, 5, 5).ExteriorRing, Rect(20, 0, 22, 2).ExteriorRing });

        Assert.Equal(2, region.Count);
        Assert.Equal(99 + 4, Area(region), 3);
    }

    [Fact]
    public void TheGapBetweenTwoOutlinesIsInMetres()
    {
        Assert.Equal(0.25, GisShapeEdits.GapMetres(new[] { Rect(0, 0, 10, 10) }, new[] { Rect(0.25, 0, 10.25, 10) }), 6);
        Assert.Equal(double.PositiveInfinity, GisShapeEdits.GapMetres(new[] { Rect(0, 0, 10, 10) }, Array.Empty<Polygon2D>()));
    }

    [Fact]
    public void AFloorThatAlreadyHasTheOutlineIsLeftOut()
    {
        // Within 5 mm: what a GDB round trip leaves.
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 10.005, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });

        Assert.Empty(edits);
        Assert.Empty(problems);
    }

    [Fact]
    public void PointsCloserThanRevitCanDrawAreMerged()
    {
        Polygon2D withStutter = new(new[]
        {
            new Point2D(0, 0),
            new Point2D(10 * Feet, 0),
            new Point2D((10 * Feet) + 0.001, 0.001),
            new Point2D(10 * Feet, 10 * Feet),
            new Point2D(0, 10 * Feet),
        });

        ShapeEdit edit = Assert.Single(Plan(
            new[] { Shape(GisDrift.Reshaped, "a", withStutter) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 5) }) }).Edits);

        // Four corners, closed.
        Assert.Equal(5, edit.Outline[0].ExteriorRing.Count);
    }

    [Fact]
    public void AUnitThatIsNotAFloorIsListedForAChangeByHand()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "room-1", Rect(0, 0, 12, 10)) },
            Array.Empty<ShapeFloor>());

        Assert.Empty(edits);
        Assert.Contains("no floor in this model has unique id room-1", Assert.Single(problems));
    }

    [Fact]
    public void AFloorWhoseSketchCannotBeRedrawnIsListed()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 12, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, "has a slope arrow") });

        Assert.Empty(edits);
        Assert.Equal("unit ショップ reshaped: floor 101 has a slope arrow, so its sketch is not redrawn. Reshape it in Revit by hand.", Assert.Single(problems));
    }

    [Fact]
    public void AFloorWithCurvedEdgesIsListedRatherThanFlattened()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 12, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, GisShapeEdits.CurvedEdges) });

        Assert.Empty(edits);
        Assert.Equal(
            "unit ショップ reshaped: floor 101 has curved edges, which the GDB's straight-segment outline would replace with short lines, so its sketch is not redrawn. Reshape it in Revit by hand.",
            Assert.Single(problems));
    }

    [Fact]
    public void AFloorInSeveralPartsIsListedRatherThanRedrawnToOnePart()
    {
        // The export writes parts m|a|0 and m|a|1 as two units; redrawing to one would drop the other.
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(20, 0, 32, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10), Rect(20, 0, 30, 10) }) });
        Assert.Empty(edits);
        Assert.Contains("is in 2 separate parts", Assert.Single(problems));
    }

    [Fact]
    public void AnOutlineThatCrossesItselfIsListedRatherThanSketched()
    {
        // The east corner moved past the notch beside it: the ring crosses itself.
        Polygon2D bowtie = new(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(0, 10), new Point2D(10, 10) }, Array.Empty<IReadOnlyList<Point2D>>());
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", bowtie) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });
        Assert.Empty(edits);
        Assert.EndsWith(GisShapeEdits.CrossesItself, Assert.Single(problems));
    }

    [Fact]
    public void TwoEditsForOneFloorAreBothListed()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 12, 10)), Shape(GisDrift.Reshaped, "a", Rect(0, 0, 14, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });
        Assert.Empty(edits);
        Assert.Contains("more than one way", Assert.Single(problems));
    }

    [Fact]
    public void OutlinesInAnotherCrsAreNotApplied()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 12, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) },
            fileEpsg: 4326);

        Assert.Empty(edits);
        Assert.Contains("EPSG:4326", Assert.Single(problems));
    }

    [Fact]
    public void EditsAppliedInALaterStepAreListed()
    {
        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = Plan(
            new[] { Shape(GisDrift.Split, "a", Rect(0, 0, 5, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });

        Assert.Empty(edits);
        Assert.Contains("not applied by Review GDB edits yet", Assert.Single(problems));
    }

    [Fact]
    public void TheFixtureWrittenByKanizuPlansWithoutError()
    {
        GisDriftFile file = GisDrift.Read(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "Fixtures", "Conformance", "gis-drift", "8d7f3c1e-0000.gis-drift.json"));

        (IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems) = GisShapeEdits.Plan(
            file,
            new Dictionary<string, ShapeFloor>(),
            ModelToProject,
            file.Epsg);

        Assert.Empty(edits);
        Assert.Equal(file.Shapes.Count, problems.Count);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
