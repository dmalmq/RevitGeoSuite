using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class FloorTypeMatcherTests
{
    private static FloorTypeInfo FloorType(string name, string category = "", int floors = 3, long elementId = 900) =>
        new(elementId, name, category, "", "", Enumerable.Range(1, floors).Select(id => (long)id));

    private static FloorTypeProposal? Propose(string name) => FloorTypeMatcher.ProposeOne(FloorType(name), Standard);

    [Fact]
    public void ZoneNameStripsPrefixAndSuffix()
    {
        Assert.Equal("ラチ内コンコース", FloorTypeMatcher.ZoneName(" j ラチ内コンコース_床 "));
        Assert.Equal("Concrete 150", FloorTypeMatcher.ZoneName("Concrete 150"));
    }

    [Theory]
    [InlineData("j 改札内コンコース_床", "j ラチ内コンコース_床", "rachi_nai")]
    [InlineData("j 改札外通路コンコース_床", "j ラチ外コンコース_床", "rachi_gai")]
    [InlineData("j 新幹線ホーム(JR東)_床", "j 新幹線ホーム_床", "rachi_nai")]
    [InlineData("j 在来線ホーム_床", "j ホーム_床", "rachi_nai")]
    [InlineData("j ラチ内コンコース(JR東海新幹線)_床", "j 新幹線コンコース_床", "rachi_nai")]
    [InlineData("j 緑の窓口_床", "j みどりの窓口_床", "")]
    [InlineData("j 駅務室_床", "j 駅事務室_床", "")]
    [InlineData("j 男子トイレ2_床", "j 男子トイレ_床", "")]
    public void MatchesByWords(string name, string expected, string restriction)
    {
        FloorTypeProposal? proposal = Propose(name);

        Assert.NotNull(proposal);
        Assert.Equal(expected, proposal!.StandardType.Name);
        Assert.Equal(restriction, proposal.GisRestriction);
    }

    [Fact]
    public void ShopRestrictionComesFromTheName()
    {
        Assert.Equal("rachi_nai", Propose("j ラチ内店舗_床")!.GisRestriction);
        Assert.Equal("rachi_gai", Propose("j 改札外店舗_床")!.GisRestriction);
        Assert.Equal("", Propose("j 店舗_床 古い")!.GisRestriction);
    }

    [Fact]
    public void UnknownNamesAreLeftForAPerson()
    {
        Assert.Null(Propose("Generic 150mm"));
    }

    [Fact]
    public void OnlyTypesInUseWithoutACategoryNeedAFix()
    {
        ModelSnapshot model = new(floorTypes: new[]
        {
            FloorType("j 改札内コンコース_床", elementId: 1),
            FloorType("j 改札外コンコース_床", category: "walkway", elementId: 2),
            FloorType("j 待合室2_床", floors: 0, elementId: 3),
            FloorType("j ラチ内コンコース_床", elementId: 4),
            FloorType("Generic 150mm", elementId: 5),
            FloorType("j 謎_床", category: "concourse", elementId: 6),
        });

        Assert.Equal(new long[] { 1 }, FloorTypeMatcher.Propose(model, Standard).Select(proposal => proposal.FloorType.ElementId));
        Assert.Equal(new long[] { 5, 6 }, FloorTypeMatcher.Unmatched(model, Standard).Select(entry => entry.ElementId));
    }

    [Fact]
    public void ProposalsComeMostFloorsFirst()
    {
        ModelSnapshot model = new(floorTypes: new[]
        {
            FloorType("j 改札内コンコース_床", floors: 2, elementId: 1),
            FloorType("j 改札外コンコース_床", floors: 5, elementId: 2),
            FloorType("j 駅務室_床", floors: 2, elementId: 3),
        });

        Assert.Equal(new long[] { 2, 1, 3 }, FloorTypeMatcher.Propose(model, Standard).Select(proposal => proposal.FloorType.ElementId));
    }

    [Fact]
    public void LabelNamesTheCountAndTarget()
    {
        string label = Propose("j 改札内コンコース_床")!.Label();

        Assert.Contains("(3 floors)", label);
        Assert.Contains("j ラチ内コンコース_床", label);
    }

    [Fact]
    public void EchoFloorIdsAreFloorsRepeatingTheProposedCategory()
    {
        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        floorTypes.Add(new FloorTypeInfo(1, "j 改札内コンコース_床", floorIds: new long[] { 11, 12 }));
        floorTypes.Add(new FloorTypeInfo(2, "j 謎の部屋_床", floorIds: new long[] { 13 }));
        ModelSnapshot model = ModelOnStandard(
            floorTypes: floorTypes,
            floors: new[] { new FloorInfo(11, 1, " Walkway"), new FloorInfo(12, 1, "retail"), new FloorInfo(13, 2, "walkway") });

        Assert.Equal(new long[] { 11 }, FloorTypeMatcher.EchoFloorIds(model, FloorTypeMatcher.Propose(model, Standard)));
    }

    [Fact]
    public void AcceptAppliesOnlyAcceptedTypesAndReportsTheOnesThatNoLongerApply()
    {
        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        floorTypes.Add(new FloorTypeInfo(1, "j 改札内コンコース_床", floorIds: new long[] { 11, 12 }));
        floorTypes.Add(new FloorTypeInfo(2, "j 店舗_床 (old)", floorIds: new long[] { 13 }));
        floorTypes.Add(new FloorTypeInfo(3, "j 謎の部屋_床", floorIds: new long[] { 14 }));
        floorTypes.Add(new FloorTypeInfo(4, "j 待合_床 (fixed)", "walkway", floorIds: new long[] { 15 }));
        ModelSnapshot model = ModelOnStandard(floorTypes: floorTypes, floors: new[] { new FloorInfo(11, 1, "walkway"), new FloorInfo(13, 2, "walkway") });

        FloorTypeFix fix = FloorTypeMatcher.Accept(model, Standard, new long[] { 1, 1, 3, 4, 999 });

        Assert.Equal(new long[] { 1 }, fix.Proposals.Select(proposal => proposal.FloorType.ElementId));
        Assert.Equal(new long[] { 11 }, fix.EchoFloorIds);
        Assert.Equal(
            new[]
            {
                "Floor type j 謎の部屋_床 no longer matches a standard type.",
                "Floor type j 待合_床 (fixed) no longer needs a fix.",
                "Floor type 999 is no longer in the model.",
            },
            fix.Skipped);
    }

    [Fact]
    public void WritingNeedsBothTypeParametersBoundToTypes()
    {
        Assert.Equal("", FloorTypeMatcher.WriteBlocker(ModelOnStandard()));
        Assert.StartsWith("gis_restriction is not bound", FloorTypeMatcher.WriteBlocker(ModelOnStandard(BoundAsStandard("gis_restriction"))));

        List<BoundParameterInfo> instance = BoundAsStandard("gis_category");
        instance.Add(new BoundParameterInfo("gis_category", null, GisParameterBinding.Instance, new[] { "OST_Floors" }));
        Assert.StartsWith("gis_category is bound as an instance parameter", FloorTypeMatcher.WriteBlocker(ModelOnStandard(instance)));
    }
}
