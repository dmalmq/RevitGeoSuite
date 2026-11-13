using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisAuditTests
{
    private static IReadOnlyList<GisAuditFinding> Audit(ModelSnapshot model) => GisAudit.Run(model, Standard);

    [Fact]
    public void AModelOnTheStandardIsClean()
    {
        Assert.Empty(Audit(ModelOnStandard()));
    }

    [Fact]
    public void FlagsAModelThatWasNeverSetUp()
    {
        Assert.Equal(new[] { "standard" }, Audit(ModelOnStandard(standardVersion: "")).Select(finding => finding.Topic));
    }

    [Fact]
    public void FlagsAnOlderStandard()
    {
        Assert.Contains("current one is 2", Audit(ModelOnStandard(standardVersion: "0"))[0].Message);
    }

    [Fact]
    public void FlagsAVersionThatIsNotANumber()
    {
        GisAuditFinding finding = Assert.Single(Audit(ModelOnStandard(standardVersion: "v1")));
        Assert.Equal(GisAuditSeverity.Error, finding.Severity);
    }

    [Fact]
    public void FlagsAMissingParameterAndALegacyGuid()
    {
        List<BoundParameterInfo> bound = BoundAsStandard("gis_kind", "name");
        bound.Add(new BoundParameterInfo("name", null, GisParameterBinding.Instance, new[] { "OST_Floors" }));

        List<string> messages = Audit(ModelOnStandard(boundParameters: bound)).Select(finding => finding.Message).ToList();

        Assert.Contains(messages, message => message.Contains("gis_kind is not bound"));
        Assert.Contains(messages, message => message.Contains("Parameter name uses its own GUID (project parameter)"));
    }

    [Fact]
    public void FlagsFloorTypesWithoutCategoryAndUnknownCategories()
    {
        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        floorTypes.Add(new FloorTypeInfo(1, "j 改札内コンコース_床", floorIds: new long[] { 11, 12 }));
        floorTypes.Add(new FloorTypeInfo(2, "j 謎_床", "concourse", floorIds: new long[] { 13 }));
        floorTypes.Add(new FloorTypeInfo(3, "j 未使用_床"));

        IReadOnlyList<GisAuditFinding> findings = Audit(ModelOnStandard(floorTypes: floorTypes));

        Assert.Equal(
            new[] { (GisAuditSeverity.Error, "13"), (GisAuditSeverity.Warning, "11,12") },
            findings.Select(finding => (finding.Severity, string.Join(",", finding.ElementIds))));
    }

    [Fact]
    public void FlagsFloorsWithAnUnknownInstanceCategory()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(ModelOnStandard(floors: new[] { new FloorInfo(5, 200, "lobby"), new FloorInfo(6, 200, "hall") }));

        Assert.Equal(new long[] { 6 }, Assert.Single(findings).ElementIds);
    }

    [Fact]
    public void FlagsLevelsWithFloorsButNoOrdinal()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(ModelOnStandard(levels: new[]
        {
            new LevelInfo(1, "ルミネ2_3FL", null, "3F"),
            new LevelInfo(3, "1FL", 0, "1F"),
            new LevelInfo(2, "Roof", null, "", hasFloors: false),
        }));

        Assert.Contains("no ordinal", Assert.Single(findings).Message);
    }

    [Fact]
    public void FlagsEscalatorsWithoutGisKindAndBadKinds()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(ModelOnStandard(familyTypes: new[]
        {
            new FamilyTypeInfo(1, "j エスカレータ-lightweight", "S1000", "", new long[] { 21 }),
            new FamilyTypeInfo(2, "Moving Walkway", "Type 1", "", new long[] { 22 }),
            new FamilyTypeInfo(3, "j エスカレーター-lightweight", "S1000", "escalator", new long[] { 23 }),
            new FamilyTypeInfo(4, "Bench", "1800", "", new long[] { 24 }),
            new FamilyTypeInfo(5, "Lift", "A", "lift", new long[] { 25 }),
            new FamilyTypeInfo(6, "j エスカレータ-other", "S800", "", new long[] { 26 }, gisCategory: "escalator"),
        }));

        Assert.Equal(
            new[] { (GisAuditSeverity.Error, 25L), (GisAuditSeverity.Warning, 21L), (GisAuditSeverity.Warning, 22L) },
            findings.Select(finding => (finding.Severity, finding.ElementIds.Single())));
        Assert.Contains("looks like an escalator", findings[1].Message);
        Assert.Contains("looks like a moving_walkway", findings[2].Message);
    }

    [Fact]
    public void OnlyErrorsBlockAndTheSummaryCountsEachSeverity()
    {
        GisAuditFinding[] findings =
        {
            new(GisAuditSeverity.Warning, "levels", "a"),
            new(GisAuditSeverity.Error, "floors", "b"),
            new(GisAuditSeverity.Info, "parameters", "c"),
        };

        Assert.Equal(new[] { "b" }, GisAudit.BlockingFindings(findings).Select(finding => finding.Message));
        Assert.Equal("1 errors, 1 warnings, 1 notes", GisAudit.Summary(findings));
    }

    [Fact]
    public void TheExportRecordNamesEachAcceptedErrorAndIsEmptyWithoutErrors()
    {
        GisAuditFinding[] errors =
        {
            new(GisAuditSeverity.Error, "floors", "b"),
            new(GisAuditSeverity.Error, "families", "d"),
        };

        Assert.Equal(
            new[]
            {
                "Exported with 2 GIS audit error(s) accepted; the output may be wrong where they apply.",
                "GIS audit error: b",
                "GIS audit error: d",
            },
            GisAudit.ExportRecord(errors));
        Assert.Empty(GisAudit.ExportRecord(System.Array.Empty<GisAuditFinding>()));
    }
}
