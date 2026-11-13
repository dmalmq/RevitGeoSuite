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

    private static ShapePlan Plan(GisDriftShape[] shapes, ShapeFloor[] floors, int fileEpsg = 6677, int modelEpsg = 6677) =>
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
    public void OnlyFloorsThatAlreadyHaveTheGdbOutlineStopWaiting()
    {
        ShapePlan plan = Plan(
            new[]
            {
                Shape(GisDrift.Reshaped, "same", Rect(0, 0, 10, 10)),
                Shape(GisDrift.Reshaped, "wider", Rect(0, 0, 12, 10)),
                Shape(GisDrift.Reshaped, "no-floor", Rect(0, 0, 12, 10)),
                Shape(GisDrift.Moved, "moved", Rect(0, 0, 5, 10)),
            },
            new[]
            {
                new ShapeFloor("same", 101, new[] { Rect(0, 0, 10, 10) }),
                new ShapeFloor("wider", 102, new[] { Rect(0, 0, 10, 10) }),
            });

        Assert.Equal(3, plan.Waiting);
    }

    [Fact]
    public void ShapesInAnotherCrsAllWait()
    {
        ShapePlan plan = Plan(
            new[] { Shape(GisDrift.Reshaped, "a", Rect(0, 0, 10, 10)), Shape(GisDrift.Reshaped, "b", Rect(0, 0, 10, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) },
            fileEpsg: 4326);

        Assert.Equal(2, plan.Waiting);
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
            new[] { Shape(GisDrift.Moved, "a", Rect(0, 0, 5, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });

        Assert.Empty(edits);
        Assert.Contains("not applied by Review GDB edits yet", Assert.Single(problems));
    }

    private static GisDriftShape Piece(string uniqueId, bool keeps, Polygon2D outline) =>
        new(GisDrift.Split, "m|" + uniqueId + "|0", uniqueId, "unit", "ショップ", "lvl-1f", "m|" + uniqueId + "|0", keeps, Gdb(outline));

    [Fact]
    public void ASplitRedrawsTheKeptFloorAndAddsTheOtherPieces()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, levelId: 7) });

        Assert.Empty(plan.Problems);
        ShapeEdit edit = Assert.Single(plan.Edits);
        Assert.Equal(ShapeEditKind.Split, edit.Kind);
        Assert.True(edit.RedrawFloor);
        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(0, 0, 5, 10) }, edit.Outline));
        Assert.True(GisShapeEdits.SameOutline(new[] { Rect(5, 0, 10, 10) }, Assert.Single(edit.NewFloors).Outline));
        Assert.Equal("Split floor 101 (ショップ) into 2 floors by the GDB", edit.Label());
        Assert.Equal(2, plan.Waiting);
    }

    [Fact]
    public void ASplitTheModelAlreadyHasOffersNothing()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[]
            {
                new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 10) }, levelId: 7),
                new ShapeFloor("b", 102, new[] { Rect(5, 0, 10, 10) }, levelId: 7),
            });

        Assert.Empty(plan.Edits);
        Assert.Empty(plan.Problems);
        Assert.Equal(0, plan.Waiting);
    }

    [Fact]
    public void AFloorWithThePieceOnAnotherLevelIsNotThePiece()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[]
            {
                new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 10) }, levelId: 7),
                new ShapeFloor("b", 102, new[] { Rect(5, 0, 10, 10) }, levelId: 8),
            });

        ShapeEdit edit = Assert.Single(plan.Edits);
        Assert.False(edit.RedrawFloor);
        Assert.Single(edit.NewFloors);
        Assert.Equal(1, plan.Waiting);
    }

    [Fact]
    public void ASplitWithoutOneKeptPieceIsListed()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", false, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, levelId: 7) });

        Assert.Empty(plan.Edits);
        Assert.Contains("0 pieces of this split that keep the floor", Assert.Single(plan.Problems));
        Assert.Equal(2, plan.Waiting);
    }

    [Fact]
    public void ASplitOfAFloorThatCannotBeRedrawnIsListed()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, "has a slope arrow", levelId: 7) });

        Assert.Empty(plan.Edits);
        Assert.Equal("unit ショップ split: floor 101 has a slope arrow, so its sketch is not redrawn. Split it in Revit by hand.", Assert.Single(plan.Problems));
    }

    [Fact]
    public void ASplitPieceInSeveralPartsIsListed()
    {
        GisDriftShape twoParts = new(GisDrift.Split, "m|a|0", "a", "unit", "ショップ", "lvl-1f", "m|a|0", false, Gdb(Rect(5, 0, 7, 10), Rect(8, 0, 10, 10)));
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), twoParts },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }, levelId: 7) });

        Assert.Empty(plan.Edits);
        Assert.Contains("several separate parts in the GDB", Assert.Single(plan.Problems));
    }

    [Fact]
    public void ASplitOfAFloorInSeveralPartsIsListed()
    {
        ShapePlan plan = Plan(
            new[] { Piece("a", true, Rect(0, 0, 5, 10)), Piece("a", false, Rect(5, 0, 10, 10)) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10), Rect(20, 0, 30, 10) }, levelId: 7) });

        Assert.Empty(plan.Edits);
        Assert.Contains("is in 2 separate parts", Assert.Single(plan.Problems));
    }

    [Fact]
    public void AMergeThatWouldDeleteAFloorInSeveralPartsIsListed()
    {
        ShapePlan plan = Plan(
            new[] { Merged("a", true, Rect(0, 0, 10, 10)), Merged("b", false, null) },
            new[]
            {
                new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 10) }),
                new ShapeFloor("b", 102, new[] { Rect(5, 0, 10, 10), Rect(20, 0, 30, 10) }),
            });

        Assert.Empty(plan.Edits);
        Assert.Contains("floor 102 is in 2 separate parts", Assert.Single(plan.Problems));
    }

    private static GisDriftShape Merged(string uniqueId, bool keeps, Polygon2D? outline) =>
        new(GisDrift.Merged, "m|" + uniqueId + "|0", uniqueId, "unit", "ショップ", "lvl-1f", "m|a|0", keeps, outline == null ? Array.Empty<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>() : Gdb(outline));

    [Fact]
    public void AMergeRedrawsTheKeptFloorAndDeletesTheOthers()
    {
        ShapePlan plan = Plan(
            new[] { Merged("a", true, Rect(0, 0, 10, 10)), Merged("b", false, null) },
            new[]
            {
                new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 10) }),
                new ShapeFloor("b", 102, new[] { Rect(5, 0, 10, 10) }),
            });

        Assert.Empty(plan.Problems);
        ShapeEdit edit = Assert.Single(plan.Edits);
        Assert.Equal(ShapeEditKind.Merge, edit.Kind);
        Assert.True(edit.RedrawFloor);
        Assert.Equal(102, Assert.Single(edit.Absorbed).ElementId);
        Assert.Equal("Merge floor(s) 102 into floor 101 (ショップ) by the GDB", edit.Label());
        Assert.Equal(2, plan.Waiting);
    }

    [Fact]
    public void AMergeTheModelAlreadyHasOffersNothing()
    {
        ShapePlan plan = Plan(
            new[] { Merged("a", true, Rect(0, 0, 10, 10)), Merged("b", false, null) },
            new[] { new ShapeFloor("a", 101, new[] { Rect(0, 0, 10, 10) }) });

        Assert.Empty(plan.Edits);
        Assert.Empty(plan.Problems);
        Assert.Equal(0, plan.Waiting);
    }

    [Fact]
    public void AMergeLeavesAnElementThatIsNotAFloorToBeDoneByHand()
    {
        ShapePlan plan = Plan(
            new[] { Merged("a", true, Rect(0, 0, 10, 10)), Merged("b", false, null) },
            new[]
            {
                new ShapeFloor("a", 101, new[] { Rect(0, 0, 5, 10) }),
                new ShapeFloor("b", 102, Array.Empty<Polygon2D>(), GisShapeEdits.NotAFloor),
            });

        Assert.Empty(plan.Edits);
        Assert.Equal("unit ショップ merged: element 102 is not a floor, so the merge does not delete it. Merge it in Revit by hand.", Assert.Single(plan.Problems));
        Assert.Equal(2, plan.Waiting);
    }

    [Fact]
    public void ANewFloorTakesThePiecesOwnGdbValuesAndStandardType()
    {
        GisDriftShape piece = new(
            GisDrift.Split, "m|a|0", "a", "unit", "ショップ", "lvl-1f", "m|a|0", false, Gdb(Rect(5, 0, 10, 10)),
            gdbName: "カフェ", gdbAltName: "カ", gdbCategory: "retail", gdbRestriction: "rachi_nai");
        List<string> problems = new();

        NewFloor floor = GisShapeEdits.NewFloorOf(piece, new[] { Rect(5, 0, 10, 10) }, GisToolStandard.LoadEmbedded(), problems);

        Assert.Empty(problems);
        Assert.Equal("カフェ", floor.Values["name"]);
        Assert.Equal("カ", floor.Values["short_name"]);
        Assert.Equal("retail", floor.Values["category"]);
        Assert.Equal("j ラチ内店舗_床", floor.TypeName);
    }

    [Fact]
    public void ANewFloorWithoutGdbValuesTakesTheKeptFloors()
    {
        NewFloor floor = GisShapeEdits.NewFloorOf(Piece("a", false, Rect(5, 0, 10, 10)), new[] { Rect(5, 0, 10, 10) }, GisToolStandard.LoadEmbedded(), new List<string>());

        Assert.Empty(floor.Values);
        Assert.Null(floor.TypeName);
    }

    [Fact]
    public void ReadsThePiecesGdbValuesKanizuWrites()
    {
        GisDriftFile file = GisDrift.Parse(@"{""schema"":""kanizu.gis-drift"",""version"":1,""model"":""m"",""appliedVersion"":""v"",""rows"":[],""epsg"":6677,
            ""shapes"":[{""edit"":""split"",""revitKey"":""m|a|0"",""uniqueId"":""a"",""layer"":""unit"",""label"":""x"",""levelId"":"""",""group"":""m|a|0"",""keeps"":false,
            ""gdbName"":""カフェ"",""gdbAltName"":""カ"",""gdbCategory"":""retail"",""gdbRestriction"":""rachi_gai"",
            ""outline"":{""type"":""Polygon"",""coordinates"":[[[0,0],[1,0],[1,1],[0,0]]]}}]}");

        GisDriftShape shape = Assert.Single(file.Shapes);
        Assert.Equal(("カフェ", "カ", "retail", "rachi_gai"), (shape.GdbName, shape.GdbAltName, shape.GdbCategory, shape.GdbRestriction));
    }

    [Fact]
    public void NewFloorsTakeTheFloorsGisValuesButNotItsIds()
    {
        Assert.Contains("name", GisShapeEdits.CopiedToNewFloors);
        Assert.Contains("category", GisShapeEdits.CopiedToNewFloors);
        Assert.Contains("short_name", GisShapeEdits.CopiedToNewFloors);
        Assert.DoesNotContain("id", GisShapeEdits.CopiedToNewFloors);
        Assert.DoesNotContain("gis_key", GisShapeEdits.CopiedToNewFloors);
    }

    [Fact]
    public void TheFixtureWrittenByKanizuPlansWithoutError()
    {
        GisDriftFile file = GisDrift.Read(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "Fixtures", "Conformance", "gis-drift", "8d7f3c1e-0000.gis-drift.json"));

        ShapePlan plan = GisShapeEdits.Plan(
            file,
            new Dictionary<string, ShapeFloor>(),
            ModelToProject,
            file.Epsg);

        // One line per shape, except the split's two pieces and the merge's two units, which share one each.
        Assert.Empty(plan.Edits);
        Assert.Equal(file.Shapes.Count - 2, plan.Problems.Count);
        Assert.Equal(file.Shapes.Count, plan.Waiting);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
