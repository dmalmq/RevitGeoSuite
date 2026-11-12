using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class SetupPlannerTests
{
    private static readonly Guid LegacyGuid = new("11111111-2222-3333-4444-555555555555");

    private static ParameterSetupStep Step(SetupPlan plan, string name) => plan.Parameters.Single(step => step.Name == name);

    [Fact]
    public void AModelOnTheStandardNeedsNothing()
    {
        SetupPlan plan = SetupPlanner.Plan(ModelOnStandard(), Standard);

        Assert.True(plan.IsEmpty);
        Assert.Empty(SetupPlanner.Describe(plan));
    }

    [Fact]
    public void AnEmptyModelGetsEverything()
    {
        SetupPlan plan = SetupPlanner.Plan(new ModelSnapshot(), Standard);

        Assert.Equal(12, plan.Parameters.Count(step => step.Action == ParameterSetupAction.Bind));
        Assert.Equal(Standard.Materials.Count, plan.Materials.Count);
        Assert.Equal(12, plan.FloorTypes.Count(step => step.Action == SetupAction.Create));
        Assert.True(plan.SetsVersion);
        Assert.Contains("Set gis_standard_version (none) to 1", SetupPlanner.Describe(plan));
    }

    [Fact]
    public void LegacyGuidIsKeptAndOnlyExtended()
    {
        List<BoundParameterInfo> bound = BoundAsStandard("category");
        bound.Add(new BoundParameterInfo("category", LegacyGuid, GisParameterBinding.Instance, new[] { "OST_Floors", "OST_Rooms" }));

        ParameterSetupStep step = Step(SetupPlanner.Plan(ModelOnStandard(boundParameters: bound), Standard), "category");

        Assert.Equal(ParameterSetupAction.ExtendLegacy, step.Action);
        Assert.Equal(new[] { "OST_Stairs", "OST_GenericModel", "OST_SpecialityEquipment" }, step.MissingCategories);
    }

    [Fact]
    public void LegacyGuidWithAllCategoriesIsKeptAsItIs()
    {
        List<BoundParameterInfo> bound = BoundAsStandard("category");
        bound.Add(new BoundParameterInfo("category", LegacyGuid, GisParameterBinding.Instance, Standard.Parameter("category")!.Categories));

        Assert.True(SetupPlanner.Plan(ModelOnStandard(boundParameters: bound), Standard).IsEmpty);
    }

    [Fact]
    public void BindingKindMismatchIsLeftAlone()
    {
        List<BoundParameterInfo> bound = BoundAsStandard("gis_category");
        bound.Add(new BoundParameterInfo("gis_category", null, GisParameterBinding.Instance, new[] { "OST_Floors" }));

        Assert.Equal(ParameterSetupAction.BindingMismatch, Step(SetupPlanner.Plan(ModelOnStandard(boundParameters: bound), Standard), "gis_category").Action);
    }

    [Fact]
    public void StandardGuidWithTheWrongBindingKindIsLeftAlone()
    {
        GisParameterDefinition standardKind = Standard.Parameter("gis_kind")!;
        List<BoundParameterInfo> bound = BoundAsStandard("gis_kind");
        bound.Add(new BoundParameterInfo("gis_kind", standardKind.Guid, GisParameterBinding.Instance, standardKind.Categories));

        Assert.Equal(ParameterSetupAction.BindingMismatch, Step(SetupPlanner.Plan(ModelOnStandard(boundParameters: bound), Standard), "gis_kind").Action);
    }

    [Fact]
    public void ExistingStandardTypeIsUpdatedInPlace()
    {
        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        floorTypes[0].GisCategory = "";
        floorTypes[0].Material = "Concrete";

        SetupPlan plan = SetupPlanner.Plan(ModelOnStandard(floorTypes: floorTypes), Standard);

        FloorTypeSetupStep step = Assert.Single(plan.FloorTypes);
        Assert.Equal(SetupAction.Update, step.Action);
        Assert.Equal(floorTypes[0].ElementId, step.ElementId);
        Assert.Equal(new[] { "gis_category", "material" }, step.Changes.Select(change => change.Field));
    }

    [Fact]
    public void RecoloursAMaterialThatDrifted()
    {
        List<MaterialInfo> materials = MaterialsAsStandard();
        materials[0].Color = "#000000";

        SetupPlan plan = SetupPlanner.Plan(ModelOnStandard(materials: materials), Standard);

        Assert.Equal(new[] { (SetupAction.Update, materials[0].Name) }, plan.Materials.Select(step => (step.Action, step.Name)));
    }

    [Fact]
    public void ClearsCategoryEchoesWhenTheVersionIsFirstStamped()
    {
        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        floorTypes.Add(new FloorTypeInfo(1, "j 改札内コンコース_床", floorIds: new long[] { 11, 12, 13 }));
        floorTypes.Add(new FloorTypeInfo(2, "j 東口通路_床", "walkway", floorIds: new long[] { 14, 15 }));
        FloorTypeInfo concourse = floorTypes[1]; // j ラチ内コンコース_床, walkway
        FloorInfo[] floors =
        {
            new(21, concourse.ElementId, "walkway"),   // echo of the type
            new(22, concourse.ElementId, "nonpublic"), // a real override
            new(11, 1, "Walkway"),                     // matches the name only: kept
            new(12, 1, "retail"),                      // a real override
            new(14, 2, "walkway"),                     // echo of the type's own gis_category
            new(15, 2, "retail"),                      // a real override
        };

        SetupPlan plan = SetupPlanner.Plan(ModelOnStandard(floorTypes: floorTypes, floors: floors, standardVersion: ""), Standard);

        Assert.Equal(new long[] { 21, 14 }, plan.EchoFloorIds);
        Assert.Contains("Clear the category of 2 floors that only repeats their type (written by an older export)", SetupPlanner.Describe(plan));
    }

    [Fact]
    public void LeavesFloorCategoriesAloneOnceTheVersionIsSet()
    {
        FloorTypeInfo concourse = FloorTypesAsStandard()[1];
        ModelSnapshot model = ModelOnStandard(floors: new[] { new FloorInfo(21, concourse.ElementId, "walkway") });

        Assert.Empty(SetupPlanner.Plan(model, Standard).EchoFloorIds);
    }
}
