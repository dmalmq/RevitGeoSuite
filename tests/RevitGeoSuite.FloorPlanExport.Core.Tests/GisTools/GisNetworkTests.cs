using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisNetworkTests
{
    private const double Foot = 0.3048;

    // Model feet to EPSG:6677 metres: the model origin sits at (-7130, -34580).
    private static readonly AffineTransform2D ModelToProject = new(new Point2D(-7130, -34580), new Point2D(Foot, 0), new Point2D(0, Foot));

    private static readonly Dictionary<string, NetworkLevel> Levels = new()
    {
        ["lvl-b1"] = new NetworkLevel(101, -5 / Foot),
        ["lvl-1f"] = new NetworkLevel(102, 0),
        ["lvl-2f"] = new NetworkLevel(103, 6 / Foot),
    };

    private static string SamplePath() =>
        Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "Fixtures", "Conformance", "gis-network", "3f2a9c1e-0000-4000-8000-000000000001.gis-network.json");

    private static GisNetworkFile Sample() => GisNetwork.Read(SamplePath());

    [Fact]
    public void ReadsTheAgreedSample()
    {
        GisNetworkFile file = Sample();
        Assert.Equal("3f2a9c1e-0000-4000-8000-000000000001", file.Model);
        Assert.Equal(GisNetwork.FileName(file.Model), Path.GetFileName(SamplePath()));
        Assert.Equal("revit/sample-station-3f2a9c1e/20261113T050000Z-ab12cd34", file.BasedOn);
        Assert.Equal("2026-11-13T05:30:00Z", file.WrittenUtc);
        Assert.Equal(6677, file.Epsg);
        Assert.Equal(new[] { "B1", "1F", "2F" }, file.Levels.Select(level => level.ShortName));
        Assert.Equal(4, file.Links.Count);
        Assert.Equal(3, file.Links[1].Line.Count);
        Assert.True(file.Links[1].OneWay);
        Assert.False(file.Links[3].StepFree);
        Assert.Equal(new[] { "stairs", "escalator", "elevator", "ramp", "movingwalkway" }, file.Connectors.Select(connector => connector.Kind));
        Assert.Equal("stair-uid", file.Connectors[0].UniqueId);
        Assert.Equal("", file.Connectors[4].UniqueId);
        Assert.Equal(21, file.Connectors[1].PassageType);
        Assert.Equal(2, file.Findings.Count);
        Assert.Equal(new Point2D(-7090, -34540), file.Findings[0].At);
        Assert.Null(file.Findings[1].At);
        Assert.Equal("", file.Findings[1].LevelId);
        Assert.Equal(0, file.NotShown);
    }

    [Fact]
    public void RefusesANewerVersionAndReadsOneWithout()
    {
        string text = File.ReadAllText(SamplePath());
        FormatException error = Assert.Throws<FormatException>(() => GisNetwork.Parse(text.Replace("\"version\": 1", "\"version\": 2"), "x.gis-network.json"));
        Assert.Contains("x.gis-network.json is version 2; this tool reads version 1", error.Message);
        Assert.Equal(4, GisNetwork.Parse(text.Replace("\"version\": 1,", string.Empty)).Links.Count);
        Assert.Throws<FormatException>(() => GisNetwork.Parse(text.Replace("kanizu.gis-network", "kanizu.gis-drift")));
        Assert.Throws<FormatException>(() => GisNetwork.Parse("{"));
    }

    [Fact]
    public void DrawsEachPositionBackInModelFeetAboveItsLevel()
    {
        NetworkPicture picture = GisNetwork.Draw(Sample(), ModelToProject, 6677, Levels);
        Assert.Equal("", picture.Refused);
        Assert.Equal((4, 5, 1, 0), (picture.Links, picture.Connectors, picture.Findings, picture.UnknownLevel));

        // Link 1 on 1F runs from (-7130, -34560) to (-7120, -34560): 20 m and 30 m from the model origin's corner.
        NetworkStroke first = picture.Strokes[0];
        Assert.Equal(NetworkInk.Walk, first.Ink);
        Assert.Equal(102, first.PlanLevel);
        Assert.Equal(0, first.X1, 6);
        Assert.Equal(20 / Foot, first.Y1, 6);
        Assert.Equal(10 / Foot, first.X2, 6);
        Assert.Equal(GisNetwork.LiftFeet, first.Z1, 6);

        // The B1 link is not step-free.
        Assert.Contains(picture.Strokes, stroke => stroke.Ink == NetworkInk.WalkNotStepFree && stroke.PlanLevel == 101 && Math.Abs(stroke.Z1 - ((-5 / Foot) + GisNetwork.LiftFeet)) < 1e-9);
    }

    [Fact]
    public void DrawsAConnectorFromOneLevelToTheOtherAndMarksEachEndInItsPlan()
    {
        NetworkPicture picture = GisNetwork.Draw(Sample(), ModelToProject, 6677, Levels);
        NetworkStroke stairs = picture.Strokes.Single(stroke => stroke.Ink == NetworkInk.Stairs && stroke.PlanLevel == 0);
        Assert.Equal(GisNetwork.LiftFeet, stairs.Z1, 6);
        Assert.Equal((6 / Foot) + GisNetwork.LiftFeet, stairs.Z2, 6);
        Assert.Equal(4, picture.Strokes.Count(stroke => stroke.Ink == NetworkInk.Stairs && stroke.PlanLevel == 102));
        Assert.Equal(4, picture.Strokes.Count(stroke => stroke.Ink == NetworkInk.Stairs && stroke.PlanLevel == 103));

        // A one-way escalator gets an arrow on its "from" floor; a two-way stair does not.
        Assert.Equal(6, picture.Strokes.Count(stroke => stroke.Ink == NetworkInk.Escalator && stroke.PlanLevel == 102));
        Assert.Single(picture.Strokes, stroke => stroke.Ink == NetworkInk.MovingWalkway && stroke.PlanLevel == 0);

        // The finding with a place is a red cross on 2F; the one without goes to the audit only.
        Assert.Equal(2, picture.Strokes.Count(stroke => stroke.Ink == NetworkInk.Warning && stroke.PlanLevel == 103));
    }

    [Fact]
    public void DrawsAConnectorKindItDoesNotKnowGenerically()
    {
        string text = File.ReadAllText(SamplePath()).Replace("\"movingwalkway\"", "\"funicular\"").Replace("\"kanizu\": \"0.1.12\",", "\"kanizu\": \"0.1.12\", \"later\": {\"a\": 1},");
        NetworkPicture picture = GisNetwork.Draw(GisNetwork.Parse(text), ModelToProject, 6677, Levels);
        Assert.Equal(5, picture.Connectors);
        Assert.Contains(picture.Strokes, stroke => stroke.Ink == NetworkInk.OtherConnector);
    }

    [Fact]
    public void DrawsNothingWhenTheFileIsInAnotherCrs()
    {
        NetworkPicture other = GisNetwork.Draw(Sample(), ModelToProject, 6668, Levels);
        Assert.Contains("EPSG:6677", other.Refused);
        Assert.Contains("EPSG:6668", other.Refused);
        Assert.Empty(other.Strokes);
        Assert.Contains("no project CRS", GisNetwork.Draw(Sample(), ModelToProject, 0, Levels).Refused);
    }

    [Fact]
    public void CountsWhatSitsOnALevelThisModelDoesNotHave()
    {
        Dictionary<string, NetworkLevel> noBasement = Levels.Where(pair => pair.Key != "lvl-b1").ToDictionary(pair => pair.Key, pair => pair.Value);
        NetworkPicture picture = GisNetwork.Draw(Sample(), ModelToProject, 6677, noBasement);
        // The B1 link, the elevator and the ramp from B1.
        Assert.Equal(3, picture.UnknownLevel);
        Assert.Equal((3, 3), (picture.Links, picture.Connectors));
        Assert.DoesNotContain(picture.Strokes, stroke => stroke.PlanLevel == 101);
    }

    [Fact]
    public void AuditsEachFindingInItsOwnWordsWithTheElement()
    {
        string text = File.ReadAllText(SamplePath()).Replace("\"floorWithoutConnector\"", "\"somethingNew\"");
        GisNetworkFile file = GisNetwork.Parse(text);
        IReadOnlyList<GisAuditFinding> audit = GisNetwork.Audit(file, uniqueId => uniqueId == "stair2-uid" ? 42 : null);
        Assert.Equal(2, audit.Count);
        Assert.Equal(GisAuditSeverity.Warning, audit[0].Severity);
        Assert.Equal("network", audit[0].Topic);
        Assert.StartsWith("A stair, escalator or elevator on 2F reaches no unit on the next floor", audit[0].Message);
        Assert.EndsWith("(Kanizu: stairs on 2F match no unit on the next floor)", audit[0].Message);
        Assert.Equal(new long[] { 42 }, audit[0].ElementIds);
        Assert.Equal(GisAuditSeverity.Info, audit[1].Severity);
        Assert.Equal("Kanizu reports somethingNew. (Kanizu: floor 3F has links but no level connector reaches it)", audit[1].Message);
        Assert.Empty(audit[1].ElementIds);

        Assert.Equal(GisAuditSeverity.Info, GisNetwork.SeverityOf("doorWithoutLink"));
        Assert.Equal(GisAuditSeverity.Warning, GisNetwork.SeverityOf("linkThroughWall"));
        Assert.Equal(GisAuditSeverity.Warning, GisNetwork.SeverityOf("floorWithoutConnector"));
    }

    [Fact]
    public void SaysOnceWhichKindsKanizuDidNotCheck()
    {
        Assert.Empty(Sample().NotChecked);
        string text = File.ReadAllText(SamplePath()).Replace("\"notChecked\": []", "\"notChecked\": [\"linkThroughWall\", \"later\"]");
        GisNetworkFile file = GisNetwork.Parse(text);
        Assert.Equal(new[] { "linkThroughWall", "later" }, file.NotChecked);
        GisAuditFinding note = GisNetwork.Audit(file, _ => null).Last();
        Assert.Equal(GisAuditSeverity.Info, note.Severity);
        Assert.Equal("Kanizu did not check the walking network for: links through walls, later. Generate the network in Kanizu and save it to check them.", note.Message);
        Assert.Empty(GisNetwork.Parse(text.Replace("\"notChecked\": [\"linkThroughWall\", \"later\"],", string.Empty)).NotChecked);
    }

    [Fact]
    public void FileNameMatchesTheDriftFiles()
    {
        Assert.Equal("a_b_c.gis-network.json", GisNetwork.FileName("a b|c"));
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
