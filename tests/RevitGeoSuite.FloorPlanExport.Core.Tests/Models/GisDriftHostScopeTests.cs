using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class GisDriftHostScopeTests
{
    private const string Host = "8d7f3c1e-0000-4000-8000-000000000001";
    private const string Linked = "5a5a5a5a-0000-4000-8000-000000000002";
    private const string LinkInstance = "link-platform-1";

    private static string HostKey(string uniqueId) => RevitKey.Create(Host, uniqueId)!;

    private static string LinkedKey(string uniqueId) =>
        RevitKey.Create(RevitKey.QualifyForLinkInstance(Linked, LinkInstance), uniqueId)!;

    private static GisDriftRow Row(string revitKey, string uniqueId, string gdb) =>
        new(revitKey, uniqueId, "unit", "name", string.Empty, string.Empty, string.Empty, "改札", "改札", gdb, "改札", conflict: false);

    private static GisDriftShape Shape(string revitKey, string uniqueId, string edit = GisDrift.Deleted, string group = "", bool keeps = false) =>
        new(
            edit,
            revitKey,
            uniqueId,
            "unit",
            "改札",
            string.Empty,
            group,
            keeps,
            Array.Empty<IReadOnlyList<IReadOnlyList<(double X, double Y)>>>());

    private static GisDriftFile File(IEnumerable<GisDriftRow> rows, IEnumerable<GisDriftShape>? shapes = null, string model = Host) =>
        new(model, "v/1", rows.ToList(), 6677, shapes?.ToList());

    /// <summary>The open model: parameter values by (unique id, parameter, binding).</summary>
    private static Func<string, string, string, string?> Model(params string[] uniqueIds) =>
        (uniqueId, parameter, binding) =>
            uniqueIds.Contains(uniqueId) && parameter == "name" && binding == GisDrift.Instance ? "改札" : null;

    [Theory]
    [InlineData("m|u|0", "m")]
    [InlineData("m@link-1|u|3", "m@link-1")]
    [InlineData(@"doc:C:\MODELS\A.RVT|u|0", @"doc:C:\MODELS\A.RVT")]
    [InlineData("u", "")]
    [InlineData("m|u", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheModelPartIsEverythingBeforeTheUniqueId(string? key, string expected)
    {
        Assert.Equal(expected, RevitKey.ModelPartOf(key));
    }

    [Fact]
    public void ALinkedKeyIsNotItsHostsEvenForTheSameModelId()
    {
        Assert.True(RevitKey.BelongsToModel(HostKey("u-1"), Host));
        Assert.True(RevitKey.BelongsToModel(HostKey("u-1"), Host.ToUpperInvariant()));
        Assert.False(RevitKey.BelongsToModel(LinkedKey("u-1"), Host));
        Assert.False(RevitKey.BelongsToModel(RevitKey.Create(RevitKey.QualifyForLinkInstance(Host, LinkInstance), "u-1"), Host));
        Assert.False(RevitKey.BelongsToModel(RevitKey.Create(Linked, "u-1"), Host));
        Assert.Equal(LinkInstance, RevitKey.LinkInstanceOf(RevitKey.ModelPartOf(LinkedKey("u-1"))));
        Assert.Equal(string.Empty, RevitKey.LinkInstanceOf(Host));
    }

    [Fact]
    public void AKeyOrAHostWithoutAModelIsNeverTakenForTheHosts()
    {
        Assert.False(RevitKey.BelongsToModel(string.Empty, Host));
        Assert.False(RevitKey.BelongsToModel(null, Host));
        Assert.False(RevitKey.BelongsToModel("u-1", Host));
        Assert.False(RevitKey.BelongsToModel("|u-1|0", Host));
        Assert.False(RevitKey.BelongsToModel(HostKey("u-1"), string.Empty));
        Assert.False(RevitKey.BelongsToModel(HostKey("u-1"), null));
    }

    [Fact]
    public void RowsAndShapesOfALinkedModelAreListedAndLeftOut()
    {
        GisDriftFile file = File(
            new[] { Row(HostKey("u-1"), "u-1", "北改札"), Row(LinkedKey("u-9"), "u-9", "南改札") },
            new[] { Shape(HostKey("u-2"), "u-2"), Shape(LinkedKey("u-8"), "u-8") });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Equal("u-1", Assert.Single(scope.File.Rows).UniqueId);
        Assert.Equal("u-2", Assert.Single(scope.File.Shapes).UniqueId);
        Assert.Equal(file.Model, scope.File.Model);
        Assert.Equal(file.AppliedVersion, scope.File.AppliedVersion);
        Assert.Equal(file.Epsg, scope.File.Epsg);
        Assert.Equal(2, scope.Problems.Count);
        Assert.All(scope.Problems, problem => Assert.Contains($"link instance {LinkInstance}", problem));
        Assert.All(scope.Problems, problem => Assert.Contains("Nothing is written here.", problem));
    }

    [Fact]
    public void AModelForkedFromItsLinkBySaveAsIsNotWrittenFromTheLinksRow()
    {
        // Save As leaves host and link with the same UniqueIds: the link's row names an element the host has too.
        GisDriftFile file = File(new[] { Row(LinkedKey("u-1"), "u-1", "南改札") });

        (IReadOnlyList<GisDriftChange> unscoped, _) = GisDrift.Plan(file, Model("u-1"), Standard);
        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);
        (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems) = GisDrift.Plan(scope.File, Model("u-1"), Standard);

        Assert.Equal("南改札", Assert.Single(unscoped).NewValue);
        Assert.Empty(changes);
        Assert.Empty(problems);
        Assert.Contains("linked model", Assert.Single(scope.Problems));
    }

    [Fact]
    public void TheHostsOwnRowStillPlansBesideALinkedRowOfTheSameUniqueId()
    {
        GisDriftFile file = File(new[] { Row(LinkedKey("u-1"), "u-1", "南改札"), Row(HostKey("u-1"), "u-1", "北改札") });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);
        (IReadOnlyList<GisDriftChange> changes, _) = GisDrift.Plan(scope.File, Model("u-1"), Standard);

        GisDriftChange change = Assert.Single(changes);
        Assert.Equal("北改札", change.NewValue);
        Assert.Equal(HostKey("u-1"), change.Row.RevitKey);
    }

    [Fact]
    public void ARowOfAnotherModelIsNamedByItsModel()
    {
        GisDriftFile file = File(new[] { Row(RevitKey.Create(Linked, "u-1")!, "u-1", "南改札") });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Empty(scope.File.Rows);
        string problem = Assert.Single(scope.Problems);
        Assert.Contains(Linked, problem);
        Assert.Contains(Host, problem);
    }

    [Fact]
    public void AModelWithoutAnIdIsScopedByItsDocumentKeyNotByTheFilesModel()
    {
        // Kanizu files such a model under its bare document key; the export keys its features doc:<document key>.
        const string documentKey = @"C:\MODELS\TOKYO.RVT";
        string host = RevitKey.CreateModelKey(null, documentKey);
        GisDriftFile file = File(
            new[]
            {
                Row(RevitKey.Create(host, "u-1")!, "u-1", "北改札"),
                Row(RevitKey.Create(RevitKey.QualifyForLinkInstance(host, LinkInstance), "u-2")!, "u-2", "南改札"),
                Row(RevitKey.Create(@"doc:C:\MODELS\OTHER.RVT", "u-3")!, "u-3", "東改札"),
            },
            model: documentKey);

        GisDriftHostScope scope = GisDriftHostScope.Of(file, host);

        Assert.Equal(@"doc:C:\MODELS\TOKYO.RVT", host);
        Assert.Equal("u-1", Assert.Single(scope.File.Rows).UniqueId);
        Assert.Equal(2, scope.Problems.Count);
        Assert.Contains("linked model", scope.Problems[0]);
        Assert.Contains("OTHER.RVT", scope.Problems[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void WithoutAHostKeyNothingIsTakenForTheHosts(string? host)
    {
        // Not even when the file's own model matches the row: another model's picked file reads the same.
        GisDriftFile file = File(
            new[] { Row(HostKey("u-1"), "u-1", "北改札") },
            new[] { Shape(HostKey("u-2"), "u-2"), Shape(string.Empty, string.Empty, GisDrift.HandDrawn) });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, host);

        Assert.Empty(scope.File.Rows);
        Assert.Empty(scope.File.Shapes);
        Assert.Equal(3, scope.Problems.Count);
        Assert.All(scope.Problems, problem => Assert.Contains("no key to compare", problem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("u-1")]
    [InlineData("|u-1|0")]
    [InlineData(Host + "|u-1")]
    [InlineData(Host + "||0")]
    [InlineData(Host + "| |0")]
    [InlineData(Host + "|u-1|x")]
    [InlineData(Host + "|u-1|")]
    [InlineData(Host + "|u-1|-1")]
    [InlineData(Host + "|u-1|+1")]
    [InlineData(Host + "|u-1|0|0")]
    [InlineData(Host + "||u-1|0")]
    public void ARowOrShapeWithoutAWholeKeyIsLeftOutEvenWithAUniqueId(string revitKey)
    {
        GisDriftFile file = File(
            new[] { Row(revitKey, "u-1", "北改札") },
            new[] { Shape(revitKey, "u-1"), Shape(revitKey, "u-1", GisDrift.Reshaped) });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);
        (IReadOnlyList<GisDriftChange> changes, _) = GisDrift.Plan(scope.File, Model("u-1"), Standard);

        Assert.Empty(scope.File.Rows);
        Assert.Empty(scope.File.Shapes);
        Assert.Empty(changes);
        Assert.Equal(3, scope.Problems.Count);
        Assert.All(scope.Problems, problem => Assert.Contains("no element key", problem));
    }

    [Fact]
    public void AUniqueIdOtherThanTheOneTheKeyNamesIsLeftOut()
    {
        // The lookup goes by the explicit uniqueId: a key that names u-A must not let u-B be written.
        GisDriftFile file = File(
            new[] { Row(HostKey("u-A"), "u-B", "北改札"), Row(HostKey("u-C"), "u-C", "南改札") },
            new[] { Shape(HostKey("u-A"), "u-B", GisDrift.Reshaped) });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);
        (IReadOnlyList<GisDriftChange> changes, _) = GisDrift.Plan(scope.File, Model("u-A", "u-B", "u-C"), Standard);

        Assert.Equal("u-C", Assert.Single(changes).UniqueId);
        Assert.Empty(scope.File.Shapes);
        Assert.Equal(2, scope.Problems.Count);
        Assert.All(scope.Problems, problem => Assert.Contains("uniqueId u-B is not the element its key names (u-A)", problem));
    }

    [Theory]
    [InlineData("m|u|0", true, "m", "u", 0)]
    [InlineData("m@link-1|u|12", true, "m@link-1", "u", 12)]
    [InlineData("m||0", false, "", "", 0)]
    [InlineData("|u|0", false, "", "", 0)]
    [InlineData("m|u|x", false, "", "", 0)]
    [InlineData("m|u|-1", false, "", "", 0)]
    [InlineData("m|u|1 ", false, "", "", 0)]
    [InlineData("m|u|0|0", false, "", "", 0)]
    [InlineData("m|u", false, "", "", 0)]
    [InlineData(null, false, "", "", 0)]
    public void AKeyReadsOnlyAsModelUniqueIdAndPart(string? key, bool reads, string model, string uniqueId, int part)
    {
        Assert.Equal(reads, RevitKey.TryParse(key, out string parsedModel, out string parsedUniqueId, out int parsedPart));
        Assert.Equal(model, parsedModel);
        Assert.Equal(uniqueId, parsedUniqueId);
        Assert.Equal(part, parsedPart);
    }

    [Fact]
    public void AUnitDrawnByHandInAnotherModelsFileIsLeftOut()
    {
        // No key says whose it is, so a picked file of another model must not make a floor here.
        GisDriftShape drawn = Shape(string.Empty, string.Empty, GisDrift.HandDrawn);

        GisDriftHostScope foreign = GisDriftHostScope.Of(File(Array.Empty<GisDriftRow>(), new[] { drawn }, model: Linked), Host);
        GisDriftHostScope unnamed = GisDriftHostScope.Of(File(Array.Empty<GisDriftRow>(), new[] { drawn }, model: " "), Host);
        GisDriftHostScope own = GisDriftHostScope.Of(File(Array.Empty<GisDriftRow>(), new[] { drawn }, model: Host.ToUpperInvariant()), Host);

        Assert.Empty(foreign.File.Shapes);
        Assert.Contains(Linked, Assert.Single(foreign.Problems));
        Assert.Empty(unnamed.File.Shapes);
        Assert.Contains("does not say which model", Assert.Single(unnamed.Problems));
        Assert.Single(own.File.Shapes);
        Assert.Empty(own.Problems);
    }

    [Fact]
    public void AUnitDrawnByHandForAModelWithoutAnIdGoesByItsBareDocumentKey()
    {
        const string documentKey = @"C:\MODELS\TOKYO.RVT";
        string host = RevitKey.CreateModelKey(null, documentKey);
        GisDriftShape drawn = Shape(string.Empty, string.Empty, GisDrift.HandDrawn);

        GisDriftHostScope own = GisDriftHostScope.Of(File(Array.Empty<GisDriftRow>(), new[] { drawn }, model: documentKey), host);
        GisDriftHostScope other = GisDriftHostScope.Of(File(Array.Empty<GisDriftRow>(), new[] { drawn }, model: @"C:\MODELS\OTHER.RVT"), host);

        Assert.Equal(documentKey, RevitKey.FilingKeyOf(host));
        Assert.Equal(Host, RevitKey.FilingKeyOf(Host));
        Assert.Single(own.File.Shapes);
        Assert.Empty(other.File.Shapes);
    }

    [Fact]
    public void OnlyAUnitDrawnByHandMayHaveNoKey()
    {
        GisDriftFile file = File(
            Array.Empty<GisDriftRow>(),
            new[]
            {
                Shape(string.Empty, string.Empty, GisDrift.HandDrawn),
                Shape(LinkedKey("u-9"), "u-9", GisDrift.HandDrawn),
                Shape(string.Empty, "u-4", GisDrift.Deleted),
            });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Equal(GisDrift.HandDrawn, Assert.Single(scope.File.Shapes).Edit);
        Assert.Equal(string.Empty, scope.File.Shapes[0].RevitKey);
        Assert.Equal(2, scope.Problems.Count);
    }

    [Fact]
    public void AMergeThatTakesInALinkedUnitIsLeftOutWhole()
    {
        string mixed = HostKey("u-1");
        string hostOnly = HostKey("u-5");
        GisDriftFile file = File(
            Array.Empty<GisDriftRow>(),
            new[]
            {
                Shape(mixed, "u-1", GisDrift.Merged, mixed, keeps: true),
                Shape(HostKey("u-2"), "u-2", GisDrift.Merged, mixed),
                Shape(LinkedKey("u-3"), "u-3", GisDrift.Merged, mixed),
                Shape(hostOnly, "u-5", GisDrift.Merged, hostOnly, keeps: true),
                Shape(HostKey("u-6"), "u-6", GisDrift.Merged, hostOnly),
                Shape(HostKey("u-7"), "u-7", GisDrift.Reshaped),
            });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        // The keeper would otherwise be redrawn to the merged outline, over the linked floor left in place.
        Assert.Equal(new[] { "u-5", "u-6", "u-7" }, scope.File.Shapes.Select(shape => shape.UniqueId));
        Assert.Equal(3, scope.Problems.Count);
        Assert.Contains("whole edit is left out", scope.Problems[0]);
        Assert.Contains("whole edit is left out", scope.Problems[1]);
        Assert.Contains($"link instance {LinkInstance}", scope.Problems[2]);
    }

    [Fact]
    public void ASplitWithAPieceOfAnotherModelIsLeftOutWhole()
    {
        string key = HostKey("u-1");
        GisDriftFile file = File(
            Array.Empty<GisDriftRow>(),
            new[]
            {
                Shape(key, "u-1", GisDrift.Split, key, keeps: true),
                Shape(RevitKey.Create(Linked, "u-1")!, "u-1", GisDrift.Split, key),
                Shape(HostKey("u-8"), "u-8", GisDrift.Split, HostKey("u-8"), keeps: true),
                Shape(HostKey("u-8"), "u-8", GisDrift.Split, HostKey("u-8")),
            });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Equal(new[] { "u-8", "u-8" }, scope.File.Shapes.Select(shape => shape.UniqueId));
        Assert.Equal(2, scope.Problems.Count);
    }

    [Fact]
    public void AFileOfOnlyTheHostsRowsIsReturnedAsItIs()
    {
        GisDriftFile file = File(
            new[] { Row(HostKey("u-1"), "u-1", "北改札") },
            new[] { Shape(HostKey("u-2"), "u-2"), Shape(string.Empty, string.Empty, GisDrift.HandDrawn) });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Same(file, scope.File);
        Assert.Empty(scope.Problems);
    }
}
