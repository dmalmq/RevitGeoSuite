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

    private static GisDriftShape Shape(string revitKey, string uniqueId) =>
        new(
            GisDrift.Deleted,
            revitKey,
            uniqueId,
            "unit",
            "改札",
            string.Empty,
            string.Empty,
            keeps: false,
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
    public void AKeyOrAHostWithoutAModelIsNotToldApart()
    {
        Assert.True(RevitKey.BelongsToModel(string.Empty, Host));
        Assert.True(RevitKey.BelongsToModel("u-1", Host));
        Assert.True(RevitKey.BelongsToModel(LinkedKey("u-1"), string.Empty));
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
    public void AModelWithoutAnIdIsTakenToBeTheOneTheFileWasWrittenFor()
    {
        const string document = @"doc:C:\MODELS\TOKYO.RVT";
        GisDriftFile file = File(
            new[]
            {
                Row(RevitKey.Create(document, "u-1")!, "u-1", "北改札"),
                Row(RevitKey.Create(RevitKey.QualifyForLinkInstance(document, LinkInstance), "u-2")!, "u-2", "南改札"),
            },
            model: document);

        GisDriftHostScope scope = GisDriftHostScope.Of(file, string.Empty);

        Assert.Equal("u-1", Assert.Single(scope.File.Rows).UniqueId);
        Assert.Single(scope.Problems);
    }

    [Fact]
    public void AFileOfOnlyTheHostsRowsIsReturnedAsItIs()
    {
        GisDriftFile file = File(
            new[] { Row(HostKey("u-1"), "u-1", "北改札"), Row(string.Empty, "u-3", "東改札") },
            new[] { Shape(string.Empty, string.Empty) });

        GisDriftHostScope scope = GisDriftHostScope.Of(file, Host);

        Assert.Same(file, scope.File);
        Assert.Empty(scope.Problems);
    }
}
