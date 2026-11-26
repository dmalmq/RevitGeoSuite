using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class ExpectedManifestTests
{
    [Fact]
    public void Generation_IsDeterministicAcrossRunsAndCanonicalizesOrdering()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        ExpectedManifest first = ExpectedManifest.FromSpec(spec);
        ExpectedManifest second = ExpectedManifest.FromSpec(spec);

        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.Equal(first.ComputeSha256(), second.ComputeSha256());

        // Reversed input order must yield identical canonical JSON and hash.
        StarterKitSpec reversedSpec = new(
            spec.Header,
            new RteTemplateSpec(
                spec.Rte.Schema,
                spec.Rte.SchemaVersion,
                spec.Rte.TemplateName,
                spec.Rte.GisStandardVersion,
                spec.Rte.CanonicalExportViewTemplate,
                spec.Rte.StartingView,
                spec.Rte.Hygiene,
                spec.Rte.ViewTemplates.Reverse().ToArray(),
                spec.Rte.QaViewFilters.Reverse().ToArray(),
                spec.Rte.Schedules.Reverse().ToArray(),
                spec.Rte.LineStyles.Reverse().ToArray(),
                spec.Rte.SystemFamilies.Reverse().ToArray(),
                spec.Rte.IncludeAllStandardFloorTypes,
                spec.Rte.IncludeAllStandardSharedParameters),
            new Materials3DSpec(
                spec.Materials3D.Schema,
                spec.Materials3D.SchemaVersion,
                spec.Materials3D.AlignmentNotes,
                spec.Materials3D.Materials.Reverse().ToArray()),
            spec.Families.Reverse().ToArray());

        ExpectedManifest fromReversed = ExpectedManifest.FromSpec(reversedSpec);
        Assert.Equal(first.ToCanonicalJson(), fromReversed.ToCanonicalJson());
        Assert.Equal(first.ComputeSha256(), fromReversed.ComputeSha256());
        Assert.True(StarterKitManifestComparer.Compare(first, fromReversed).IsMatch);
    }

    [Fact]
    public void Manifest_RoundTripsThroughJsonWithZeroDiff()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        ExpectedManifest expected = ExpectedManifest.FromSpec(spec);

        string json = expected.ToCanonicalJson();
        ExpectedManifest roundTripped = ExpectedManifest.FromJson(json);

        Assert.Equal(expected.ComputeSha256(), roundTripped.ComputeSha256());
        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, roundTripped);
        Assert.True(diff.IsMatch);
        Assert.Empty(diff.Findings);
        Assert.Equal(19, expected.SharedParameters.Count);
        Assert.Equal(16, expected.Families.Count);
    }

    [Fact]
    public void Comparer_CatchesMissingTemplateFilterMaterialAndFamily()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        ExpectedManifest actual = CloneManifest(
            expected,
            viewTemplates: expected.ViewTemplates.Where(v => v.Name != "Kanizu 3D Export"),
            qaViewFilters: expected.QaViewFilters.Where(f => f.Name != "KNZ_QA_INFO_GateSide_Check"),
            materials: expected.Materials.Where(m => m.Name != "K3D_Glass"),
            families: expected.Families.Where(f => f.Id != "KNZ_CIRC_Escalator"));

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, actual);
        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Missing, f => f.Section == "viewTemplates" && f.Key == "Kanizu 3D Export");
        Assert.Contains(diff.Missing, f => f.Section == "qaViewFilters" && f.Key == "KNZ_QA_INFO_GateSide_Check");
        Assert.Contains(diff.Missing, f => f.Section == "materials" && f.Key == "K3D_Glass");
        Assert.Contains(diff.Missing, f => f.Section == "families" && f.Key == "KNZ_CIRC_Escalator");
    }

    [Fact]
    public void Comparer_CatchesForbiddenOstLevelsScheduleAndNonCanonicalExportTemplate()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        List<ManifestScheduleEntry> schedulesWithLevels = expected.Schedules.ToList();
        schedulesWithLevels.Add(new ManifestScheduleEntry(
            "qa-schedule-levels",
            "Kanizu GIS - Levels",
            "review",
            "OST_Levels",
            emptyMeansPass: false,
            new[] { "short_name", "ordinal" },
            hasGisKindFilter: false));

        List<ManifestViewTemplateEntry> wrongExportTemplates = expected.ViewTemplates
            .Select(v => v.Name == "GIS Export"
                ? new ManifestViewTemplateEntry("Kanizu GIS Export Plan", v.ViewType, v.DetailLevel, true, false, v.FilterNames)
                : v)
            .ToList();

        ExpectedManifest actual = CloneManifest(
            expected,
            canonicalExportViewTemplate: "Kanizu GIS Export Plan",
            viewTemplates: wrongExportTemplates,
            schedules: schedulesWithLevels);

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, actual);
        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Mismatches, f => f.Section == "header" && f.Property == "canonicalExportViewTemplate");
        Assert.Contains(diff.Missing, f => f.Section == "viewTemplates" && f.Key == "GIS Export");
        Assert.Contains(diff.Unexpected, f => f.Section == "viewTemplates" && f.Key == "Kanizu GIS Export Plan");
        Assert.Contains(diff.Unexpected, f => f.Section == "schedules" && f.Key == "Kanizu GIS - Levels");
    }

    [Fact]
    public void Comparer_CatchesMissingKFootprintAndNonZeroIdentityOrWorksets()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        List<ManifestFamilyEntry> missingFootprint = expected.Families
            .Select(f => f.Id == "KNZ_FIX_TicketGateBank"
                ? new ManifestFamilyEntry(
                    f.Id,
                    f.FamilyName,
                    f.Category,
                    f.SeedKind,
                    f.IsPolygonal,
                    hasCoarseKFootprint: false,
                    hasDownwardBottomFaceAtZero: false,
                    f.Subcategories.Where(s => s != "K_Footprint"),
                    0,
                    f.CoarseTriangleBudget,
                    f.MediumFineTriangles,
                    f.MediumFineTriangleBudget,
                    f.SharedParameterGuids)
                : f)
            .ToList();

        ExpectedManifest actual = CloneManifest(
            expected,
            hasProjectIdentity: true,
            worksetCount: 2,
            families: missingFootprint);

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, actual);
        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Mismatches, f => f.Section == "hygiene" && f.Property == "hasProjectIdentity");
        Assert.Contains(diff.Mismatches, f => f.Section == "hygiene" && f.Property == "worksetCount");
        Assert.Contains(diff.Mismatches, f => f.Section == "families" && f.Key == "KNZ_FIX_TicketGateBank" && f.Property == "hasCoarseKFootprint");
        Assert.Contains(diff.Mismatches, f => f.Section == "families" && f.Key == "KNZ_FIX_TicketGateBank" && f.Property == "hasDownwardBottomFaceAtZero");
        Assert.Contains(diff.Missing, f => f.Section == "families" && f.Key == "KNZ_FIX_TicketGateBank" && f.Property == "subcategories" && f.ExpectedValue == "K_Footprint");
    }

    [Fact]
    public void Comparer_SupportsWithinBudgetTriangleModeForTolerantGating()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        List<ManifestFamilyEntry> slightlyDifferentWithinBudget = expected.Families
            .Select(f => f.Id == "KNZ_COL_Rect"
                ? new ManifestFamilyEntry(
                    f.Id,
                    f.FamilyName,
                    f.Category,
                    f.SeedKind,
                    f.IsPolygonal,
                    f.HasCoarseKFootprint,
                    f.HasDownwardBottomFaceAtZero,
                    f.Subcategories,
                    f.CoarseTriangles,
                    f.CoarseTriangleBudget,
                    f.MediumFineTriangles + 4, // still <= 48 budget
                    f.MediumFineTriangleBudget,
                    f.SharedParameterGuids)
                : f)
            .ToList();

        ExpectedManifest actual = CloneManifest(expected, families: slightlyDifferentWithinBudget);

        ManifestComparisonResult exactDiff = StarterKitManifestComparer.Compare(expected, actual);
        Assert.False(exactDiff.IsMatch);
        Assert.Contains(exactDiff.Mismatches, f => f.Key == "KNZ_COL_Rect" && f.Property == "mediumFineTriangles");

        ManifestComparisonResult budgetDiff = StarterKitManifestComparer.Compare(
            expected,
            actual,
            new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.WithinBudget });
        Assert.True(budgetDiff.IsMatch);

        // Exceeding budget must fail even in WithinBudget mode.
        List<ManifestFamilyEntry> overBudget = expected.Families
            .Select(f => f.Id == "KNZ_COL_Rect"
                ? new ManifestFamilyEntry(
                    f.Id,
                    f.FamilyName,
                    f.Category,
                    f.SeedKind,
                    f.IsPolygonal,
                    f.HasCoarseKFootprint,
                    f.HasDownwardBottomFaceAtZero,
                    f.Subcategories,
                    f.CoarseTriangles,
                    f.CoarseTriangleBudget,
                    f.MediumFineTriangleBudget + 1,
                    f.MediumFineTriangleBudget,
                    f.SharedParameterGuids)
                : f)
            .ToList();

        ManifestComparisonResult overBudgetDiff = StarterKitManifestComparer.Compare(
            expected,
            CloneManifest(expected, families: overBudget),
            new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.WithinBudget });
        Assert.False(overBudgetDiff.IsMatch);
        Assert.Contains(overBudgetDiff.Mismatches, f => f.Key == "KNZ_COL_Rect" && f.Property == "mediumFineTriangles");
    }

    private static ExpectedManifest CloneManifest(
        ExpectedManifest orig,
        string? canonicalExportViewTemplate = null,
        bool? hasProjectIdentity = null,
        int? worksetCount = null,
        IEnumerable<ManifestViewTemplateEntry>? viewTemplates = null,
        IEnumerable<ManifestQaFilterEntry>? qaViewFilters = null,
        IEnumerable<ManifestScheduleEntry>? schedules = null,
        IEnumerable<ManifestMaterialEntry>? materials = null,
        IEnumerable<ManifestFamilyEntry>? families = null)
    {
        return new ExpectedManifest(
            orig.KitVersion,
            orig.GisStandardVersion,
            orig.SupportedRevitVersions,
            canonicalExportViewTemplate ?? orig.CanonicalExportViewTemplate,
            orig.StartingViewName,
            orig.StartingViewType,
            orig.StartingViewBilingual,
            orig.ZeroIdentityEnforced,
            orig.ZeroWorksetsEnforced,
            hasProjectIdentity ?? orig.HasProjectIdentity,
            worksetCount ?? orig.WorksetCount,
            viewTemplates ?? orig.ViewTemplates,
            qaViewFilters ?? orig.QaViewFilters,
            schedules ?? orig.Schedules,
            orig.LineStyles,
            materials ?? orig.Materials,
            orig.SystemFamilies,
            orig.SharedParameters,
            families ?? orig.Families);
    }
}
