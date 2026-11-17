using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

/// <summary>G7: the 21 standard floor types are checked against floor-types.json and can be reset.</summary>
public sealed class StandardFloorTypeAuditTests
{
    private const string Platform = "j ホーム_床";

    [Fact]
    public void TheTwentyOneStandardTypesAsShippedAreClean()
    {
        Assert.Equal(21, FloorTypesAsStandard().Count);
        Assert.Empty(GisAudit.Run(ModelOnStandard(), Standard));
    }

    [Fact]
    public void ChangingTheCategoryOfTheStandardPlatformTypeIsAnErrorWithAReset()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard();
        FloorTypeInfo platform = types.Single(type => type.Name == Platform);
        platform.GisCategory = "walkway";

        GisAuditFinding finding = Assert.Single(GisAudit.Run(ModelOnStandard(floorTypes: types), Standard));

        Assert.Equal((GisAuditSeverity.Error, "floor types"), (finding.Severity, finding.Topic));
        Assert.Equal(platform.FloorIds, finding.ElementIds);
        Assert.Equal(
            "Floor type j ホーム_床 is a standard type, but its gis_category 'walkway' differs from the standard ('platform'). Its 1 floor exports as 'walkway', not as the name says. Reset it to the standard.",
            finding.Message);
        Assert.NotNull(finding.Fix);
        Assert.Equal((GisAuditFixKind.ResetFloorType, platform.ElementId), (finding.Fix!.Kind, finding.Fix.TargetId));
        Assert.Single(GisAudit.BlockingFindings(new[] { finding }));
    }

    [Fact]
    public void SeveralFloorsAreCountedInThePlural()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard()
            .Select(type => type.Name == Platform ? new FloorTypeInfo(type.ElementId, type.Name, "walkway", type.GisRestriction, type.Material, new long[] { 1, 2, 3 }) : type)
            .ToList();

        Assert.Contains("Its 3 floors export as 'walkway'", Assert.Single(GisAudit.Run(ModelOnStandard(floorTypes: types), Standard)).Message);
    }

    [Fact]
    public void AChangedRestrictionIsReportedTooAndBothFieldsAreNamed()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard();
        FloorTypeInfo platform = types.Single(type => type.Name == Platform);
        platform.GisCategory = "walkway";
        platform.GisRestriction = "rachi_gai";

        GisAuditFinding finding = Assert.Single(GisAudit.Run(ModelOnStandard(floorTypes: types), Standard));

        Assert.Contains("its gis_category 'walkway' and gis_restriction 'rachi_gai' differ from the standard ('platform', 'rachi_nai')", finding.Message);
    }

    [Fact]
    public void AnUnusedStandardTypeWithAnotherCategoryIsAWarning()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard()
            .Select(type => type.Name == Platform ? new FloorTypeInfo(type.ElementId, type.Name, "walkway", type.GisRestriction, type.Material) : type)
            .ToList();

        GisAuditFinding finding = Assert.Single(GisAudit.Run(ModelOnStandard(floorTypes: types), Standard));

        Assert.Equal(GisAuditSeverity.Warning, finding.Severity);
        Assert.Contains("No floor uses it yet", finding.Message);
        Assert.NotNull(finding.Fix);
    }

    [Fact]
    public void CaseSpacesAndTheMaterialAloneAreNotReported()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard();
        FloorTypeInfo platform = types.Single(type => type.Name == Platform);
        platform.GisCategory = " Platform ";
        platform.Material = "Concrete";

        Assert.Empty(GisAudit.Run(ModelOnStandard(floorTypes: types), Standard));
    }

    [Fact]
    public void TheResetWritesCategoryRestrictionAndMaterialOfTheStandardType()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard();
        FloorTypeInfo platform = types.Single(type => type.Name == Platform);
        platform.GisCategory = "walkway";
        platform.Material = "Concrete";

        FloorTypeSetupStep? step = SetupPlanner.ResetStep(ModelOnStandard(floorTypes: types), Standard, platform.ElementId);

        Assert.NotNull(step);
        Assert.Equal((SetupAction.Update, (long?)platform.ElementId), (step!.Action, step.ElementId));
        Assert.Equal(
            new[] { ("gis_category", "walkway", "platform"), ("material", "Concrete", Standard.FloorType(Platform)!.Material) },
            step.Changes.Select(change => (change.Field, change.OldValue, change.NewValue)));
    }

    [Fact]
    public void ThereIsNothingToResetOnAStandardTypeOrAnotherName()
    {
        List<FloorTypeInfo> types = FloorTypesAsStandard();
        types.Add(new FloorTypeInfo(999, "Generic 150mm", "walkway"));
        ModelSnapshot model = ModelOnStandard(floorTypes: types);

        long platformId = types.Single(type => type.Name == Platform).ElementId;
        Assert.Null(SetupPlanner.ResetStep(model, Standard, platformId));
        Assert.Null(SetupPlanner.ResetStep(model, Standard, 999));
        Assert.Null(SetupPlanner.ResetStep(model, Standard, 12345));

        // What the page says instead of "reset" (the handler returns it as the skipped line).
        Assert.Equal("Floor type j ホーム_床 already matches the standard.", SetupPlanner.NothingToReset(model, Standard, platformId));
        Assert.Equal("Floor type Generic 150mm no longer has a standard name.", SetupPlanner.NothingToReset(model, Standard, 999));
        Assert.Equal("Floor type 12345 is no longer in the model.", SetupPlanner.NothingToReset(model, Standard, 12345));
    }
}
