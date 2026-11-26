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

        // Zero or negative triangle counts must fail even in WithinBudget mode.
        List<ManifestFamilyEntry> zeroOrNegative = expected.Families
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
                    coarseTriangles: 0,
                    f.CoarseTriangleBudget,
                    mediumFineTriangles: 0,
                    f.MediumFineTriangleBudget,
                    f.SharedParameterGuids)
                : f.Id == "KNZ_COL_Round"
                    ? new ManifestFamilyEntry(
                        f.Id,
                        f.FamilyName,
                        f.Category,
                        f.SeedKind,
                        f.IsPolygonal,
                        f.HasCoarseKFootprint,
                        f.HasDownwardBottomFaceAtZero,
                        f.Subcategories,
                        coarseTriangles: -2,
                        f.CoarseTriangleBudget,
                        mediumFineTriangles: -4,
                        f.MediumFineTriangleBudget,
                        f.SharedParameterGuids)
                    : f)
            .ToList();

        ManifestComparisonResult zeroDiff = StarterKitManifestComparer.Compare(
            expected,
            CloneManifest(expected, families: zeroOrNegative),
            new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.WithinBudget });
        Assert.False(zeroDiff.IsMatch);
        Assert.Contains(zeroDiff.Mismatches, f => f.Key == "KNZ_COL_Rect" && f.Property == "coarseTriangles");
        Assert.Contains(zeroDiff.Mismatches, f => f.Key == "KNZ_COL_Rect" && f.Property == "mediumFineTriangles");
        Assert.Contains(zeroDiff.Mismatches, f => f.Key == "KNZ_COL_Round" && f.Property == "coarseTriangles");
        Assert.Contains(zeroDiff.Mismatches, f => f.Key == "KNZ_COL_Round" && f.Property == "mediumFineTriangles");
    }

    [Fact]
    public void FromJson_RejectsIncompatibleSchemaOrSchemaVersion()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());
        Newtonsoft.Json.Linq.JObject valid = Newtonsoft.Json.Linq.JObject.Parse(expected.ToCanonicalJson());

        Newtonsoft.Json.Linq.JObject wrongSchema = (Newtonsoft.Json.Linq.JObject)valid.DeepClone();
        wrongSchema["schema"] = "kanizu.other-manifest";
        Assert.Throws<System.IO.InvalidDataException>(() => ExpectedManifest.FromJson(wrongSchema.ToString()));

        Newtonsoft.Json.Linq.JObject wrongVersion = (Newtonsoft.Json.Linq.JObject)valid.DeepClone();
        wrongVersion["schemaVersion"] = 2;
        Assert.Throws<System.IO.InvalidDataException>(() => ExpectedManifest.FromJson(wrongVersion.ToString()));

        Newtonsoft.Json.Linq.JObject missingVersion = (Newtonsoft.Json.Linq.JObject)valid.DeepClone();
        missingVersion.Remove("schemaVersion");
        Assert.Throws<System.IO.InvalidDataException>(() => ExpectedManifest.FromJson(missingVersion.ToString()));
    }

    [Fact]
    public void Comparer_DetectsHideSubcategoriesAndHideNonGisLineStylesDrift()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        ManifestViewTemplateEntry export3d = Assert.Single(expected.ViewTemplates, v => v.Name == "Kanizu 3D Export");
        Assert.Contains("K_Footprint", export3d.HideSubcategories);
        ManifestViewTemplateEntry gisExport = Assert.Single(expected.ViewTemplates, v => v.Name == "GIS Export");
        Assert.True(gisExport.HideNonGisLineStyles);

        List<ManifestViewTemplateEntry> drifted = expected.ViewTemplates
            .Select(v => v.Name == "Kanizu 3D Export"
                ? new ManifestViewTemplateEntry(v.Name, v.ViewType, v.DetailLevel, v.IsCanonical2DExport, v.HasVisibilityHidingFilters, v.FilterNames, v.HideNonGisLineStyles, hideSubcategories: Array.Empty<string>())
                : v.Name == "GIS Export"
                    ? new ManifestViewTemplateEntry(v.Name, v.ViewType, v.DetailLevel, v.IsCanonical2DExport, v.HasVisibilityHidingFilters, v.FilterNames, hideNonGisLineStyles: false, v.HideSubcategories)
                    : v)
            .ToList();

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, CloneManifest(expected, viewTemplates: drifted));
        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Missing, f => f.Section == "viewTemplates" && f.Key == "Kanizu 3D Export" && f.Property == "hideSubcategories" && f.ExpectedValue == "K_Footprint");
        Assert.Contains(diff.Mismatches, f => f.Section == "viewTemplates" && f.Key == "GIS Export" && f.Property == "hideNonGisLineStyles");
    }

    [Fact]
    public void Comparer_RejectsDuplicateKeysInActualManifest()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        List<ManifestViewTemplateEntry> duplicatedTemplates = expected.ViewTemplates.ToList();
        ManifestViewTemplateEntry gisExport = expected.ViewTemplates.Single(v => v.Name == "GIS Export");
        duplicatedTemplates.Add(new ManifestViewTemplateEntry(
            gisExport.Name,
            gisExport.ViewType,
            gisExport.DetailLevel,
            gisExport.IsCanonical2DExport,
            hasVisibilityHidingFilters: true,
            gisExport.FilterNames,
            gisExport.HideNonGisLineStyles,
            gisExport.HideSubcategories));

        List<ManifestScheduleEntry> duplicatedSchedules = expected.Schedules.ToList();
        duplicatedSchedules.Add(expected.Schedules[0]);

        ExpectedManifest actual = CloneManifest(
            expected,
            viewTemplates: duplicatedTemplates,
            schedules: duplicatedSchedules);

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(expected, actual);
        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Unexpected, f => f.Section == "viewTemplates" && f.Key == "GIS Export" && f.Property == "entry");
        Assert.Contains(diff.Unexpected, f => f.Section == "schedules" && f.Key == expected.Schedules[0].Name && f.Property == "entry");
    }

    [Fact]
    public void Comparer_DetectsQaFilterPredicateAndGateSideValueColorDrift()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        ManifestQaFilterEntry gateSide = Assert.Single(expected.QaViewFilters, f => f.Name == "KNZ_QA_INFO_GateSide_Check");
        Assert.Equal("#FFF3CD", gateSide.SecondaryColorHex);
        Assert.NotEmpty(gateSide.Rules);
        Assert.Equal("#1E88E5", gateSide.ValueColors["rachi_nai"]);
        Assert.Equal("#FFF3CD", gateSide.ValueColors["rachi_gai"]);

        List<ManifestQaFilterEntry> driftedFilters = expected.QaViewFilters
            .Select(f => f.Name == "KNZ_QA_INFO_GateSide_Check"
                ? new ManifestQaFilterEntry(
                    f.Name,
                    f.Severity,
                    f.Visible,
                    f.OverrideColorHex,
                    f.Categories,
                    secondaryColorHex: "#FFFFFF",
                    rules: new[] { new ManifestFilterRuleEntry("gis_restriction", "equals", "rachi_gai") },
                    valueColors: new Dictionary<string, string> { ["rachi_nai"] = "#1E88E5", ["rachi_gai"] = "#000000" })
                : f)
            .ToList();

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(
            expected,
            CloneManifest(expected, qaViewFilters: driftedFilters));

        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Mismatches, f => f.Section == "qaViewFilters" && f.Key == "KNZ_QA_INFO_GateSide_Check" && f.Property == "secondaryColorHex");
        Assert.Contains(diff.Missing, f => f.Section == "qaViewFilters" && f.Key == "KNZ_QA_INFO_GateSide_Check" && f.Property == "rules");
        Assert.Contains(diff.Unexpected, f => f.Section == "qaViewFilters" && f.Key == "KNZ_QA_INFO_GateSide_Check" && f.Property == "rules");
        Assert.Contains(diff.Mismatches, f => f.Section == "qaViewFilters" && f.Key == "KNZ_QA_INFO_GateSide_Check" && f.Property == "valueColors[rachi_gai]");
    }

    [Fact]
    public void Comparer_DetectsScheduleFilterRuleDrift()
    {
        ExpectedManifest expected = ExpectedManifest.FromSpec(StarterKitSpecLoaderTests.LoadShippedSpec());

        ManifestScheduleEntry circException = Assert.Single(expected.Schedules, s => s.Id == "qa-exception-circulation-incomplete");
        Assert.Equal(2, circException.Filters.Count);

        List<ManifestScheduleEntry> driftedSchedules = expected.Schedules
            .Select(s => s.Id == "qa-exception-circulation-incomplete"
                ? new ManifestScheduleEntry(
                    s.Id,
                    s.Name,
                    s.Kind,
                    s.Category,
                    s.EmptyMeansPass,
                    s.Fields,
                    s.HasGisKindFilter,
                    // Drop the gis_direction isEmpty filter while keeping gis_kind hasValue!
                    filters: s.Filters.Where(r => r.Parameter == "gis_kind"))
                : s.Id == "qa-exception-doors-unmapped"
                    ? new ManifestScheduleEntry(
                        s.Id,
                        s.Name,
                        s.Kind,
                        s.Category,
                        s.EmptyMeansPass,
                        s.Fields,
                        s.HasGisKindFilter,
                        // Drop every filter from Doors unmapped!
                        filters: Array.Empty<ManifestFilterRuleEntry>())
                    : s)
            .ToList();

        ManifestComparisonResult diff = StarterKitManifestComparer.Compare(
            expected,
            CloneManifest(expected, schedules: driftedSchedules));

        Assert.False(diff.IsMatch);
        Assert.Contains(diff.Missing, f => f.Section == "schedules" && f.Key == circException.Name && f.Property == "filters" && f.ExpectedValue == "gis_direction|isEmpty|");
        Assert.Contains(diff.Missing, f => f.Section == "schedules" && f.Key == "✗ Doors unmapped" && f.Property == "filters" && f.ExpectedValue == "gis_door|isEmpty|");
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
