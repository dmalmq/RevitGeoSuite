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

    private static IReadOnlyList<FloorTypeProposal> Candidates(string name, ModelSnapshot? model = null) =>
        FloorTypeMatcher.Candidates(FloorType(name), model ?? new ModelSnapshot(), Standard);

    private static FloorTypeProposal? Propose(string name) => Candidates(name).SingleOrDefault();

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
    public void ShopsAreSplitByTheGateSideTheNameStates()
    {
        Assert.Equal("j ラチ内店舗_床", Propose("j ラチ内店舗_床")!.StandardType.Name);
        Assert.Equal("name", Propose("j ラチ内店舗_床")!.Reason);
        Assert.Equal("j ラチ外店舗_床", Propose("j 改札外店舗_床")!.StandardType.Name);
        Assert.Equal("words in the name", Propose("j 改札外店舗_床")!.Reason);
        Assert.Equal("j ラチ内スロープ_床", Propose("j 改札内スロープ(東)_床")!.StandardType.Name);
    }

    [Fact]
    public void AWordWithoutASideOffersBothTypes()
    {
        IReadOnlyList<FloorTypeProposal> candidates = Candidates("j 店舗_床 古い");

        Assert.Equal(new[] { "j ラチ外店舗_床", "j ラチ内店舗_床" }, candidates.Select(proposal => proposal.StandardType.Name));
        Assert.All(candidates, proposal => Assert.Equal("words in the name", proposal.Reason));
        Assert.All(candidates, proposal => Assert.Null(proposal.NewName));
    }

    [Fact]
    public void AnOldNameOffersEveryCurrentNameAndRenamesToIt()
    {
        IReadOnlyList<FloorTypeProposal> shops = Candidates("j 店舗_床");

        Assert.Equal(new[] { "j ラチ外店舗_床", "j ラチ内店舗_床" }, shops.Select(proposal => proposal.StandardType.Name));
        Assert.Equal(new[] { "j ラチ外店舗_床", "j ラチ内店舗_床" }, shops.Select(proposal => proposal.NewName));
        Assert.All(shops, proposal => Assert.Equal("old name", proposal.Reason));

        FloorTypeProposal smoking = Propose("smoking room")!;
        Assert.Equal("j 喫煙所_床", smoking.StandardType.Name);
        Assert.Equal("j 喫煙所_床", smoking.NewName);
        Assert.Equal("j 屋外_床", Propose("ROOF")!.NewName);
        Assert.Equal("j その他_床", Propose("Other")!.StandardType.Name);
    }

    [Fact]
    public void AnOldNameTakesTheSideFromTheTypeOrItsWords()
    {
        FloorTypeInfo inside = new(1, "j 店舗_床", "retail", "rachi_nai", "", new long[] { 1 });
        FloorTypeInfo outside = new(2, "j 店舗_床", "retail", "rachi_gai", "", new long[] { 1 });
        FloorTypeInfo worded = new(3, "ラチ外店舗", floorIds: new long[] { 1 });

        Assert.Equal("j ラチ内店舗_床", Assert.Single(FloorTypeMatcher.Candidates(inside, new ModelSnapshot(), Standard)).StandardType.Name);
        Assert.Equal("j ラチ外店舗_床", Assert.Single(FloorTypeMatcher.Candidates(outside, new ModelSnapshot(), Standard)).StandardType.Name);
        Assert.Equal("j ラチ外店舗_床", Assert.Single(FloorTypeMatcher.Candidates(worded, new ModelSnapshot(), Standard)).StandardType.Name);
    }

    [Fact]
    public void ATakenCurrentNameIsNotARenameTarget()
    {
        ModelSnapshot model = new(floorTypes: new[]
        {
            new FloorTypeInfo(1, "j ラチ外店舗_床", "retail", "rachi_gai", "", new long[] { 7 }),
            FloorType("j 店舗_床", "retail", elementId: 2),
        });

        FloorTypeProposal[] shops = FloorTypeMatcher.Candidates(model.FloorTypes[1], model, Standard).ToArray();

        Assert.Null(shops[0].NewName);
        Assert.Equal("j ラチ内店舗_床", shops[1].NewName);
    }

    [Fact]
    public void ARenamedTypeStopsNeedingAFixAndAnOldNameWithNoFreeTargetDoesNotWithACategory()
    {
        ModelSnapshot old = new(floorTypes: new[] { FloorType("smoking room", "smokingarea", elementId: 1) });
        Assert.True(FloorTypeMatcher.NeedsFix(old.FloorTypes[0], old, Standard));

        ModelSnapshot renamed = new(floorTypes: new[] { FloorType("j 喫煙所_床", "smokingarea", elementId: 1) });
        Assert.False(FloorTypeMatcher.NeedsFix(renamed.FloorTypes[0], renamed, Standard));

        ModelSnapshot taken = new(floorTypes: new[]
        {
            FloorType("smoking room", "smokingarea", elementId: 1),
            FloorType("j 喫煙所_床", "smokingarea", elementId: 2),
        });
        Assert.False(FloorTypeMatcher.NeedsFix(taken.FloorTypes[0], taken, Standard));

        ModelSnapshot takenNoCategory = new(floorTypes: new[]
        {
            FloorType("smoking room", elementId: 1),
            FloorType("j 喫煙所_床", "smokingarea", elementId: 2),
        });
        Assert.True(FloorTypeMatcher.NeedsFix(takenNoCategory.FloorTypes[0], takenNoCategory, Standard));
        Assert.Null(Assert.Single(FloorTypeMatcher.Propose(takenNoCategory, Standard)).NewName);
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
        Assert.Contains("renames to j 喫煙所_床", Propose("smoking room")!.Label());
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
        floorTypes.Add(new FloorTypeInfo(2, "j ラチ外店舗_床 (old)", floorIds: new long[] { 13 }));
        floorTypes.Add(new FloorTypeInfo(3, "j 謎の部屋_床", floorIds: new long[] { 14 }));
        floorTypes.Add(new FloorTypeInfo(4, "j 待合_床 (fixed)", "walkway", floorIds: new long[] { 15 }));
        ModelSnapshot model = ModelOnStandard(floorTypes: floorTypes, floors: new[] { new FloorInfo(11, 1, "walkway"), new FloorInfo(13, 2, "walkway") });

        FloorTypeFix fix = FloorTypeMatcher.Accept(
            model,
            Standard,
            new[] { (1L, "j ラチ内コンコース_床"), (1L, "j ラチ内コンコース_床"), (3L, "j 待合室_床"), (4L, "j 待合室_床"), (999L, "j 待合室_床") });

        Assert.Equal(new long[] { 1 }, fix.Proposals.Select(proposal => proposal.FloorType.ElementId));
        Assert.Equal(new long[] { 11 }, fix.EchoFloorIds);
        Assert.Equal(
            new[]
            {
                "Floor type j 謎の部屋_床 no longer matches j 待合室_床.",
                "Floor type j 待合_床 (fixed) no longer needs a fix.",
                "Floor type 999 is no longer in the model.",
            },
            fix.Skipped);
    }

    [Fact]
    public void AcceptSkipsATypeWithTwoAcceptedCandidates()
    {
        ModelSnapshot model = new(floorTypes: new[] { FloorType("j 店舗_床", "retail", elementId: 1), FloorType("smoking room", elementId: 2) });

        FloorTypeFix fix = FloorTypeMatcher.Accept(
            model,
            Standard,
            new[] { (1L, "j ラチ外店舗_床"), (1L, "j ラチ内店舗_床"), (2L, "j 喫煙所_床") });

        Assert.Equal(new[] { "j 喫煙所_床" }, fix.Proposals.Select(proposal => proposal.StandardType.Name));
        Assert.Equal(new[] { "Floor type j 店舗_床 has more than one accepted standard type. Choose one." }, fix.Skipped);
    }

    [Fact]
    public void AcceptMatchesTheChosenCandidateAndKeepsItsRename()
    {
        ModelSnapshot model = new(floorTypes: new[] { FloorType("j 店舗_床", "retail", elementId: 1) });

        FloorTypeProposal proposal = Assert.Single(FloorTypeMatcher.Accept(model, Standard, new[] { (1L, "j ラチ内店舗_床") }).Proposals);

        Assert.Equal("j ラチ内店舗_床", proposal.StandardType.Name);
        Assert.Equal("j ラチ内店舗_床", proposal.NewName);
        Assert.Equal("rachi_nai", proposal.GisRestriction);
    }

    [Fact]
    public void AcceptLetsOnlyTheTypeWithMoreFloorsTakeAContestedName()
    {
        ModelSnapshot model = new(floorTypes: new[]
        {
            FloorType("店舗", floors: 2, elementId: 1),
            FloorType("j 店舗_床", floors: 5, elementId: 2),
        });

        FloorTypeFix fix = FloorTypeMatcher.Accept(model, Standard, new[] { (1L, "j ラチ内店舗_床"), (2L, "j ラチ内店舗_床") });

        Assert.Equal(2, fix.Proposals.Count);
        Assert.Equal("j ラチ内店舗_床", fix.Proposals.Single(proposal => proposal.FloorType.ElementId == 2).NewName);
        Assert.Null(fix.Proposals.Single(proposal => proposal.FloorType.ElementId == 1).NewName);
        Assert.Equal("j ラチ内店舗_床", fix.Proposals.Single(proposal => proposal.FloorType.ElementId == 1).StandardType.Name);
        Assert.Equal(new[] { "Floor type 店舗 keeps its name: j 店舗_床 is renamed to j ラチ内店舗_床." }, fix.Skipped);
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

    [Fact]
    public void Audit_says_a_two_sided_old_name_moves_all_its_floors()
    {
        ModelSnapshot model = new(floorTypes: new[]
        {
            FloorType("j 店舗_床", "retail", floors: 6, elementId: 1),
            FloorType("smoking room", "smokingarea", floors: 2, elementId: 2),
        });
        List<string> messages = GisAudit.Run(model, Standard).Select(finding => finding.Message).Where(message => message.Contains("uses an old name")).ToList();

        Assert.Equal(
            new[]
            {
                "Floor type j 店舗_床 (6 floors) uses an old name. Run Fix floor types to rename it to j ラチ外店舗_床 or j ラチ内店舗_床. All 6 floors of this type get the name you choose. If some belong to the other one, duplicate the type and move those floors to the copy first.",
                "Floor type smoking room (2 floors) uses an old name. Run Fix floor types to rename it to j 喫煙所_床.",
            },
            messages);
    }
}
