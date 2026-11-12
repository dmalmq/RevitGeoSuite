using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisFieldListTests
{
    private const string OperatorGuid = "5b0e2a51-7a43-4f0c-9d61-3a7e9a1f2c10";

    private static string StationList(string extraRows)
    {
        return "{\"schema\":\"revitgeosuite.gis-fields\",\"version\":1,\"fields\":[" + extraRows + "]}";
    }

    private static string OperatorRow(string guid = OperatorGuid, string name = "operator", string parameter = "gis_operator") =>
        "{\"layer\":\"unit\",\"name\":\"" + name + "\",\"aliasJa\":\"事業者\",\"type\":\"text\",\"length\":50," +
        "\"values\":[\"JR東日本\",\"東京メトロ\"],\"owner\":\"revit\"," +
        "\"source\":{\"kind\":\"parameter\",\"parameter\":\"" + parameter + "\",\"guid\":\"" + guid + "\",\"binding\":\"instance\",\"categories\":[\"OST_Floors\",\"OST_Rooms\"]}}";

    [Fact]
    public void TheStandardListReadsBackAndExportsTheDoorFieldsFromStandardParameters()
    {
        GisFieldList standard = GisFieldList.Parse(GisFieldList.CreateStandard().ToJson(), "standard");

        Assert.Equal(GisFieldList.CreateStandard().Fields.Count, standard.Fields.Count);
        Assert.Empty(standard.ExtraParameters());
        Assert.Equal(
            new[] { "Opening.door<-gis_door", "Opening.access_con<-gis_access_con", "Opening.name<-name" },
            standard.ToMappings().Select(mapping => $"{mapping.Layer}.{mapping.FieldName}<-{mapping.SourceKey}"));
        Assert.All(standard.ToMappings(), mapping => Assert.False(mapping.WarnWhenMissing));
        Assert.Equal(
            new[] { "fixture.anchor_id", "level.address_id", "level.build_id" },
            standard.Fields.Where(field => field.Owner == GisFieldOwner.Gdb).Select(field => field.Key));
        Assert.All(
            standard.Fields.Where(field => field.Owner == GisFieldOwner.Gdb),
            field => Assert.Equal(GisFieldSourceKind.None, field.Source.Kind));
        Assert.Equal(
            FloorTypeStandard.KnownCategories,
            standard.Fields.Single(field => field.Key == "unit.category").Values);
    }

    [Fact]
    public void ANewParameterIsOneRow()
    {
        GisFieldList list = GisFieldList.MergeOntoStandard(
            GisFieldList.CreateStandard(),
            GisFieldList.Parse(StationList(OperatorRow()), "station"),
            new List<string>());

        GisParameterDefinition parameter = Assert.Single(list.ExtraParameters());
        Assert.Equal("gis_operator", parameter.Name);
        Assert.Equal(new Guid(OperatorGuid), parameter.Guid);
        Assert.Equal(new[] { "OST_Floors", "OST_Rooms" }, parameter.Categories);

        CustomAttributeMapping mapping = Assert.Single(list.ToMappings(), candidate => candidate.Layer == SchemaLayerType.Unit);
        Assert.Equal(SchemaLayerType.Unit, mapping.Layer);
        Assert.Equal("operator", mapping.FieldName);
        Assert.Equal(CustomAttributeSourceKind.RevitParameter, mapping.SourceKind);
        Assert.Equal("gis_operator", mapping.SourceKey);
    }

    [Fact]
    public void AStationListCannotChangeOrAddCoreRows()
    {
        List<string> problems = new();
        string changedCore = "{\"layer\":\"unit\",\"name\":\"category\",\"aliasJa\":\"x\",\"source\":{\"kind\":\"none\"},\"owner\":\"gdb\"}";
        string fakeCore = "{\"layer\":\"unit\",\"name\":\"made_up\",\"source\":{\"kind\":\"core\"}}";

        GisFieldList list = GisFieldList.MergeOntoStandard(
            GisFieldList.CreateStandard(), GisFieldList.Parse(StationList(changedCore + "," + fakeCore), "station"), problems);

        Assert.Equal("カテゴリ", list.Fields.Single(field => field.Key == "unit.category").AliasJa);
        Assert.DoesNotContain(list.Fields, field => field.Name == "made_up");
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void AStationRowForAStandardDoorFieldIsLeftOutAndReported()
    {
        List<string> problems = new();
        string ownDoor = "{\"layer\":\"opening\",\"name\":\"door\",\"source\":{\"kind\":\"parameter\",\"parameter\":\"door_kind\",\"guid\":\"9a1d6c1e-2f0b-4b7e-9f3e-0c5d2a7b8e11\",\"binding\":\"instance\",\"categories\":[\"OST_Doors\"]}}";

        GisFieldList list = GisFieldList.MergeOntoStandard(
            GisFieldList.CreateStandard(), GisFieldList.Parse(StationList(ownDoor), "station"), problems);

        Assert.Equal("gis_door", list.Fields.Single(field => field.Key == "opening.door").Source.Parameter);
        Assert.Contains("opening.door", Assert.Single(problems));
    }

    [Fact]
    public void AStationCopyOfTheStandardMergesWithoutProblems()
    {
        List<string> problems = new();

        GisFieldList.MergeOntoStandard(
            GisFieldList.CreateStandard(), GisFieldList.Parse(GisFieldList.CreateStandard().ToJson(), "station"), problems);

        Assert.Empty(problems);
    }

    [Fact]
    public void GdbOwnedAndDerivedRowsMapAsTheyShould()
    {
        string gdbOnly = "{\"layer\":\"unit\",\"name\":\"checked_by\",\"source\":{\"kind\":\"none\"},\"owner\":\"gdb\"}";
        string derived = "{\"layer\":\"level\",\"name\":\"view_name\",\"source\":{\"kind\":\"derived\",\"derived\":\"ViewName\"}}";

        GisFieldList list = GisFieldList.Parse(StationList(gdbOnly + "," + derived), "station");

        CustomAttributeMapping mapping = Assert.Single(list.ToMappings());
        Assert.Equal(SchemaLayerType.Level, mapping.Layer);
        Assert.Equal(CustomAttributeSourceKind.DerivedValue, mapping.SourceKind);
        Assert.Empty(list.ExtraParameters());
    }

    [Theory]
    [InlineData("{\"layer\":\"room\",\"name\":\"a\",\"source\":{\"kind\":\"none\"}}", "layer 'room'")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"Bad Name\",\"source\":{\"kind\":\"none\"}}", "field name")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"source\":{\"kind\":\"parameter\",\"parameter\":\"gis_a\",\"binding\":\"instance\",\"categories\":[\"OST_Floors\"]}}", "needs a fixed 'guid'")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"source\":{\"kind\":\"parameter\",\"parameter\":\"gis_a\",\"guid\":\"" + OperatorGuid + "\",\"binding\":\"instance\"}}", "needs 'categories'")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"source\":{\"kind\":\"parameter\",\"parameter\":\"gis_category\",\"guid\":\"" + OperatorGuid + "\"}}", "is a standard parameter")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"source\":{\"kind\":\"derived\",\"derived\":\"Nope\"}}", "derived value 'Nope'")]
    public void ABadRowIsLeftOutWithTheReason(string row, string reason)
    {
        List<string> problems = new();

        GisFieldList list = GisFieldList.Parse(StationList(row), "station", problems);

        Assert.Empty(list.Fields);
        Assert.Contains(reason, Assert.Single(problems));
    }

    [Fact]
    public void TwoParametersCannotShareAGuidAndAFieldCannotRepeat()
    {
        List<string> problems = new();

        GisFieldList.Parse(
            StationList(OperatorRow() + "," + OperatorRow(name: "operator2", parameter: "gis_other") + "," + OperatorRow()),
            "station",
            problems);

        Assert.Equal(2, problems.Count);
        Assert.Contains("used by another parameter", problems[0]);
        Assert.Contains("appears twice", problems[1]);
    }

    [Theory]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"type\":\"double\",\"source\":{\"kind\":\"none\"}}")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"owner\":\"nobody\",\"source\":{\"kind\":\"none\"}}")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"source\":{\"kind\":\"parameter\",\"parameter\":\"gis_a\",\"guid\":\"not-a-guid\",\"categories\":[\"OST_Floors\"]}}")]
    [InlineData("{\"layer\":\"unit\",\"name\":\"a\",\"values\":\"x\",\"source\":{\"kind\":\"none\"}}")]
    [InlineData("\"just text\"")]
    public void ARowTheModelCannotReadIsLeftOutNotThrown(string row)
    {
        List<string> problems = new();
        string good = "{\"layer\":\"unit\",\"name\":\"checked_by\",\"source\":{\"kind\":\"none\"},\"owner\":\"gdb\"}";

        GisFieldList list = GisFieldList.Parse(StationList(row + "," + good), "station", problems);

        Assert.Equal("checked_by", Assert.Single(list.Fields).Name);
        Assert.Contains("row 1", Assert.Single(problems));
    }

    [Fact]
    public void AVersionThatIsNotANumberIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => GisFieldList.Parse("{\"schema\":\"revitgeosuite.gis-fields\",\"version\":\"one\",\"fields\":[]}", "station"));
    }

    [Fact]
    public void WithoutAProblemListABadRowThrows()
    {
        Assert.Throws<InvalidDataException>(() => GisFieldList.Parse(StationList("{\"layer\":\"x\",\"name\":\"a\"}"), "station"));
    }

    [Fact]
    public void ApplyToKeepsTheProfilesOwnMappings()
    {
        SchemaProfile profile = SchemaProfile.CreateCoreProfile();
        profile.Mappings.Add(new CustomAttributeMapping { Layer = SchemaLayerType.Unit, FieldName = "operator", SourceKind = CustomAttributeSourceKind.Constant, ConstantValue = "x" });
        GisFieldList list = GisFieldList.Parse(StationList(OperatorRow()), "station");

        SchemaProfile applied = list.ApplyTo(profile);

        Assert.Equal(CustomAttributeSourceKind.Constant, Assert.Single(applied.Mappings).SourceKind);
        Assert.Single(profile.Mappings);
    }
}
