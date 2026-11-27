using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class T8TemplateBuilderManifestTests
{
    [Fact]
    public void TemplateBuildPlanBuilder_BuildsCompleteDeterministicPlanFromShippedSpec()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(spec);

        // Header & metric defaults
        Assert.Equal("KanizuStationStarter", plan.TemplateName);
        Assert.Equal(spec.Header.KitVersion, plan.KitVersion);
        Assert.Equal(3, plan.GisStandardVersion);
        Assert.Equal(TemplateStandardDefinitions.ExportPlanViewTemplateName, plan.CanonicalExportViewTemplate);
        Assert.Equal("GIS Export", plan.CanonicalExportViewTemplate);
        Assert.Equal("mm", plan.UnitsAndDefaults.LengthUnit);
        Assert.Equal("m2", plan.UnitsAndDefaults.AreaUnit);
        Assert.Equal("deg", plan.UnitsAndDefaults.AngleUnit);
        Assert.Equal(ShowcaseExportGuard.AuthoringModelRole, plan.UnitsAndDefaults.ModelRole);
        Assert.Equal(spec.Header.KitVersion, plan.UnitsAndDefaults.KitVersion);
        Assert.True(plan.UnitsAndDefaults.RequireZeroProjectIdentity);
        Assert.True(plan.UnitsAndDefaults.RequireZeroWorksets);

        // All 19 frozen shared parameters
        Assert.Equal(GisSharedParameters.All.Count, plan.SharedParameters.Count);
        Assert.Equal(19, plan.SharedParameters.Count);
        foreach (GisParameterDefinition def in GisSharedParameters.All)
        {
            TemplateSharedParameterPlan planned = Assert.Single(plan.SharedParameters, p => p.Name == def.Name);
            Assert.Equal(def.Guid, planned.Guid);
            Assert.Equal(def.Binding == GisParameterBinding.Type ? "type" : "instance", planned.Binding);
            Assert.Equal(def.DataType == GisParameterDataType.Integer ? "integer" : "text", planned.DataType);
            Assert.Equal(def.Categories.OrderBy(c => c, StringComparer.Ordinal), planned.Categories);
        }

        // Area scheme & frozen GIS_<area> color fill scheme
        Assert.Equal("GIS Area", plan.AreaColorScheme.AreaSchemeName);
        Assert.Equal("GIS Area Colors", plan.AreaColorScheme.ColorSchemeName);
        Assert.True(plan.AreaColorScheme.Entries.Count >= 6);
        Assert.Contains(plan.AreaColorScheme.Entries, e => e.MaterialName == "GIS_paid_area" && e.FillHex == "#F2F7FB");
        Assert.Contains(plan.AreaColorScheme.Entries, e => e.MaterialName == "GIS_free_area" && e.FillHex == "#FFFFFF");
        Assert.Contains(plan.AreaColorScheme.Entries, e => e.MaterialName == "GIS_facilities" && e.FillHex == "#DDEBEC");

        // Materials: all 11 K3D_*/M3D_* + all 7 frozen GIS_* materials
        Assert.Equal(spec.Materials3D.Materials.Count, plan.Materials3D.Count);
        Assert.Contains(plan.Materials3D, m => m.Name == "M3D_Stair_Monolithic" && m.Is3DStarterMaterial);
        Assert.Contains(plan.Materials3D, m => m.Name == "M3D_GlassGuard" && m.Transparency == 65 && m.AlphaMode == "blend");
        Assert.Contains(plan.Materials3D, m => m.Name == "K3D_Body" && m.Hex == "#ECEEF1");
        Assert.Contains(plan.Materials3D, m => m.Name == "K3D_Footprint");
        Assert.Equal(7, plan.Gis2DMaterials.Count);
        Assert.All(plan.Gis2DMaterials, m => Assert.StartsWith("GIS_", m.Name, StringComparison.Ordinal));

        // All 21 standard floor types
        Assert.Equal(FloorTypeStandard.LoadEmbedded().FloorTypes.Count, plan.StandardFloorTypes.Count);

        // Exact 4 GIS_* line styles from rte.json
        Assert.Equal(4, plan.LineStyles.Count);
        Assert.Equal(
            new[] { "GIS_curb", "GIS_detail_line", "GIS_fare_boundary", "GIS_platform_edge" },
            plan.LineStyles.Select(ls => ls.Name).ToArray());
        TemplateLineStylePlan fareBoundary = Assert.Single(plan.LineStyles, ls => ls.Name == "GIS_fare_boundary");
        Assert.Equal(5, fareBoundary.LineWeight);
        Assert.Equal("#1E88E5", fareBoundary.ColorHex);
        Assert.Equal("Dash", fareBoundary.LinePattern);

        // All 3 system family types from rte.json
        Assert.Equal(3, plan.SystemFamilies.Count);
        Assert.Contains(plan.SystemFamilies, sf => sf.TypeName == "KNZ_Stair_Monolithic" && sf.SystemKind == "Stair" && sf.MaterialRef == "M3D_Stair_Monolithic");
        Assert.Contains(plan.SystemFamilies, sf => sf.TypeName == "KNZ_Wall_GlassGuard_1100" && sf.SystemKind == "Wall" && sf.MaterialRef == "M3D_GlassGuard" && sf.ThicknessMm == 20 && sf.DefaultHeightMm == 1100);
        Assert.Contains(plan.SystemFamilies, sf => sf.TypeName == "opentobelow" && sf.SystemKind == "Floor" && sf.GisCategory == "opentobelow" && sf.MaterialRef == "GIS_stairs_escalators");

        // All 5 unified view templates
        Assert.Equal(5, plan.ViewTemplates.Count);
        Assert.Equal(
            new[] { "GIS Export", "GIS QA 3D", "GIS QA Plan", "GIS Working Plan", "Kanizu 3D Export" },
            plan.ViewTemplates.Select(vt => vt.Name).ToArray());
        Assert.All(plan.ViewTemplates, vt =>
        {
            Assert.False(vt.AllowVisibilityHidingFilters);
            Assert.False(vt.HasVisibilityHidingFilters);
        });
        TemplateViewTemplatePlan gisExport = Assert.Single(plan.ViewTemplates, vt => vt.Name == "GIS Export");
        Assert.True(gisExport.IsCanonical2DExport);
        Assert.Equal("FloorPlan", gisExport.ViewType);
        Assert.Equal("Coarse", gisExport.DetailLevel);
        Assert.True(gisExport.HideNonGisLineStyles);
        Assert.Empty(gisExport.FilterNames);

        TemplateViewTemplatePlan export3D = Assert.Single(plan.ViewTemplates, vt => vt.Name == "Kanizu 3D Export");
        Assert.Equal("ThreeD", export3D.ViewType);
        Assert.Equal("Medium", export3D.DetailLevel);
        Assert.Contains("K_Footprint", export3D.HideSubcategories);

        TemplateViewTemplatePlan qa3D = Assert.Single(plan.ViewTemplates, vt => vt.Name == "GIS QA 3D");
        Assert.Contains("K_Footprint", qa3D.HideSubcategories);
        Assert.Equal(8, qa3D.FilterNames.Count);

        // All 8 QA view filters including KNZ_QA_INFO_GateSide_Check
        Assert.Equal(8, plan.QaViewFilters.Count);
        Assert.All(plan.QaViewFilters, f => Assert.True(f.Visible));
        TemplateQaFilterPlan gateSide = Assert.Single(plan.QaViewFilters, f => f.Name == "KNZ_QA_INFO_GateSide_Check");
        Assert.Equal("info", gateSide.Severity);
        Assert.Equal("#1E88E5", gateSide.OverrideColorHex);
        Assert.Equal("#FFF3CD", gateSide.SecondaryColorHex);
        Assert.Equal("#1E88E5", gateSide.ValueColors["rachi_nai"]);
        Assert.Equal("#FFF3CD", gateSide.ValueColors["rachi_gai"]);
        Assert.Contains(gateSide.Rules, r => r.Parameter == GisSharedParameters.GisRestriction && r.Operator == "equals" && r.Value == "rachi_nai");

        // All 7 schedules (4 review + 3 exception) and strictly ZERO OST_Levels schedules
        Assert.Equal(7, plan.Schedules.Count);
        Assert.DoesNotContain(plan.Schedules, s => string.Equals(s.Category, "OST_Levels", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, plan.Schedules.Count(s => s.EmptyMeansPass));
        Assert.Equal(4, plan.Schedules.Count(s => !s.EmptyMeansPass));
        Assert.Contains(plan.Schedules, s => s.Name == "Kanizu GIS - Circulation" && s.HasGisKindFilter && s.Category == string.Empty);
        Assert.Contains(plan.Schedules, s => s.Name == "✗ Floors missing category" && s.EmptyMeansPass);
        Assert.Contains(plan.Schedules, s => s.Name == "✗ Doors unmapped" && s.EmptyMeansPass);
        Assert.Contains(plan.Schedules, s => s.Name == "✗ Circulation incomplete" && s.EmptyMeansPass);

        // Bilingual EN/JA 00_Start_Here Starting View with zero hardcoded personal paths
        Assert.Equal("00_Start_Here", plan.StartingView.Name);
        Assert.Equal("DraftingView", plan.StartingView.ViewType);
        Assert.True(plan.StartingView.Bilingual);
        Assert.Contains("en", plan.StartingView.Languages);
        Assert.Contains("ja", plan.StartingView.Languages);
        string startText = plan.StartingView.RenderBilingualText();
        Assert.Contains("Kanizu GIS Revit Starter Kit", startText, StringComparison.Ordinal);
        Assert.Contains("スターターキット", startText, StringComparison.Ordinal);
        Assert.Contains("OST_Levels", startText, StringComparison.Ordinal);
        AssertNoPersonalPaths(startText);

        // Loaded Starter Kit families (16 full catalog)
        Assert.Equal(16, plan.LoadedFamilies.Count);
        Assert.Equal(
            StarterKitCatalogSelector.AllCatalogFamilyIds,
            plan.LoadedFamilies.Select(f => f.Id).ToArray());
    }

    [Fact]
    public void TemplateBuildPlanBuilder_RejectsOstLevelsScheduleAndLegacyCanonicalTemplate()
    {
        StarterKitSpec shipped = StarterKitSpecLoaderTests.LoadShippedSpec();

        List<ScheduleSpec> schedulesWithLevels = shipped.Rte.Schedules.ToList();
        schedulesWithLevels.Add(new ScheduleSpec(
            "qa-schedule-levels",
            "Kanizu GIS - Levels",
            "review",
            "OST_Levels",
            emptyMeansPass: false,
            new[] { "ordinal", "short_name" },
            Array.Empty<FilterRuleSpec>()));

        RteTemplateSpec badScheduleRte = new(
            shipped.Rte.Schema,
            shipped.Rte.SchemaVersion,
            shipped.Rte.TemplateName,
            shipped.Rte.GisStandardVersion,
            shipped.Rte.CanonicalExportViewTemplate,
            shipped.Rte.StartingView,
            shipped.Rte.Hygiene,
            shipped.Rte.ViewTemplates,
            shipped.Rte.QaViewFilters,
            Array.AsReadOnly(schedulesWithLevels.ToArray()),
            shipped.Rte.LineStyles,
            shipped.Rte.SystemFamilies,
            shipped.Rte.IncludeAllStandardFloorTypes,
            shipped.Rte.IncludeAllStandardSharedParameters);

        StarterKitSpec badScheduleSpec = new(shipped.Header, badScheduleRte, shipped.Materials3D, shipped.Families);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => TemplateBuildPlanBuilder.Build(badScheduleSpec));
        Assert.Contains("OST_Levels", ex.Message, StringComparison.Ordinal);

        RteTemplateSpec badCanonicalRte = new(
            shipped.Rte.Schema,
            shipped.Rte.SchemaVersion,
            shipped.Rte.TemplateName,
            shipped.Rte.GisStandardVersion,
            TemplateStandardDefinitions.LegacyExportPlanViewTemplateName,
            shipped.Rte.StartingView,
            shipped.Rte.Hygiene,
            shipped.Rte.ViewTemplates,
            shipped.Rte.QaViewFilters,
            shipped.Rte.Schedules,
            shipped.Rte.LineStyles,
            shipped.Rte.SystemFamilies,
            shipped.Rte.IncludeAllStandardFloorTypes,
            shipped.Rte.IncludeAllStandardSharedParameters);

        StarterKitSpec badCanonicalSpec = new(shipped.Header, badCanonicalRte, shipped.Materials3D, shipped.Families);
        Assert.Throws<InvalidOperationException>(() => TemplateBuildPlanBuilder.Build(badCanonicalSpec));
    }

    [Theory]
    [InlineData(true, 16)]
    [InlineData(false, 15)]
    public void TemplateBuildPlan_RoundTripSimulatedManifestMatchesExpectedManifestWithZeroDiffs(
        bool includeOptionalFamilies,
        int expectedFamilyCount)
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(
            spec,
            new TemplateBuildPlanOptions { IncludeOptionalFamilies = includeOptionalFamilies });
        StarterKitCatalogPlanResult catalog = StarterKitCatalogSelector.CompileAndLintCatalog(
            spec,
            includeOptionalFamilies: includeOptionalFamilies);

        Assert.True(catalog.AllPassed);
        Assert.Equal(expectedFamilyCount, plan.LoadedFamilies.Count);
        Assert.Equal(expectedFamilyCount, catalog.ManifestEntries.Count);

        ExpectedManifest expected = StarterKitCatalogSelector.BuildExpectedManifestForSelection(
            spec,
            includeOptionalFamilies: includeOptionalFamilies);
        ExpectedManifest simulatedActual = plan.ToSimulatedManifest(catalog.ManifestEntries);

        ManifestComparisonResult exactComparison = StarterKitManifestComparer.Compare(
            expected,
            simulatedActual,
            new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.Exact });
        Assert.True(exactComparison.IsMatch, string.Join("\n", exactComparison.Findings.Select(f => f.ToString())));
        Assert.Empty(exactComparison.Findings);

        ManifestComparisonResult budgetComparison = StarterKitManifestComparer.Compare(
            expected,
            simulatedActual,
            new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.WithinBudget });
        Assert.True(budgetComparison.IsMatch);
        Assert.Equal(expected.ComputeSha256(), simulatedActual.ComputeSha256());

        // Round-trip through canonical JSON as well
        ExpectedManifest parsed = ExpectedManifest.FromJson(simulatedActual.ToCanonicalJson(indented: true));
        Assert.True(StarterKitManifestComparer.Compare(expected, parsed).IsMatch);
    }

    [Fact]
    public void ProjectTemplateResolver_ResolvesEnglishJapaneseAndNestedLocaleDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kanizu-t8-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            string enDir = Path.Combine(tempRoot, "en-flat");
            Directory.CreateDirectory(enDir);
            string defaultMetric = Path.Combine(enDir, "DefaultMetric.rte");
            File.WriteAllText(defaultMetric, "seed");
            Assert.Equal(
                Path.GetFullPath(defaultMetric),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(enDir));

            string jpDir = Path.Combine(tempRoot, "jp-flat");
            Directory.CreateDirectory(jpDir);
            string jpArch = Path.Combine(jpDir, "意匠(メートル単位).rte");
            File.WriteAllText(jpArch, "seed");
            Assert.Equal(
                Path.GetFullPath(jpArch),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(jpDir));
            Assert.True(StarterKitProjectTemplateResolver.IsJapaneseProjectTemplateFileName(jpArch));
            Assert.False(StarterKitProjectTemplateResolver.IsJapaneseProjectTemplateFileName(defaultMetric));

            // Nested Templates/Japanese/ and Templates/English/
            string nestedRoot = Path.Combine(tempRoot, "nested");
            string nestedEn = Path.Combine(nestedRoot, "English");
            string nestedJp = Path.Combine(nestedRoot, "Japanese");
            Directory.CreateDirectory(nestedEn);
            Directory.CreateDirectory(nestedJp);
            string nestedEnFile = Path.Combine(nestedEn, "Architectural-DefaultMetric.rte");
            string nestedJpFile = Path.Combine(nestedJp, "意匠(メートル単位).rte");
            File.WriteAllText(nestedEnFile, "seed");
            File.WriteAllText(nestedJpFile, "seed");

            Assert.Equal(
                Path.GetFullPath(nestedEnFile),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(nestedRoot, preferJapanese: false));
            Assert.Equal(
                Path.GetFullPath(nestedJpFile),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(nestedRoot, preferJapanese: true));

            // Standard Revit Windows US Metric and Japan template directories + Imperial English_I rejection
            string standardInstallRoot = Path.Combine(tempRoot, "revit-std-install");
            string imperialDir = Path.Combine(standardInstallRoot, "English_I");
            string usMetricDir = Path.Combine(standardInstallRoot, "US Metric");
            string japanDir = Path.Combine(standardInstallRoot, "Japan");
            Directory.CreateDirectory(imperialDir);
            Directory.CreateDirectory(usMetricDir);
            Directory.CreateDirectory(japanDir);
            string imperialSeed = Path.Combine(imperialDir, "Default.rte");
            string usMetricSeed = Path.Combine(usMetricDir, "DefaultMetric.rte");
            string japanSeed = Path.Combine(japanDir, "意匠(メートル単位).rte");
            File.WriteAllText(imperialSeed, "imperial");
            File.WriteAllText(usMetricSeed, "metric");
            File.WriteAllText(japanSeed, "japan-metric");

            Assert.Equal(
                Path.GetFullPath(usMetricSeed),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(standardInstallRoot, preferJapanese: false));
            Assert.Equal(
                Path.GetFullPath(japanSeed),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(standardInstallRoot, preferJapanese: true));

            // Imperial-only install must NEVER silently select English_I/Default.rte or US Imperial/Default.rte
            string imperialOnlyRoot = Path.Combine(tempRoot, "imperial-only");
            string imperialOnlyEnI = Path.Combine(imperialOnlyRoot, "English_I");
            string imperialOnlyUsImp = Path.Combine(imperialOnlyRoot, "US Imperial");
            Directory.CreateDirectory(imperialOnlyEnI);
            Directory.CreateDirectory(imperialOnlyUsImp);
            File.WriteAllText(Path.Combine(imperialOnlyEnI, "Default.rte"), "imperial");
            File.WriteAllText(Path.Combine(imperialOnlyUsImp, "Default.rte"), "imperial");
            File.WriteAllText(Path.Combine(imperialOnlyRoot, "Default.rte"), "ambiguous-imperial");

            Assert.Throws<FileNotFoundException>(() =>
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(imperialOnlyRoot, preferJapanese: false));
            Assert.Throws<FileNotFoundException>(() =>
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(imperialOnlyRoot, preferJapanese: true));

            // Explicit override takes precedence
            string explicitFile = Path.Combine(tempRoot, "CustomSeed.rte");
            File.WriteAllText(explicitFile, "seed");
            Assert.Equal(
                Path.GetFullPath(explicitFile),
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(nestedRoot, explicitSeedTemplatePath: explicitFile));

            // Non-.rte explicit file rejected
            string badExt = Path.Combine(tempRoot, "NotATemplate.rvt");
            File.WriteAllText(badExt, "rvt");
            Assert.Throws<InvalidOperationException>(() =>
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(null, explicitSeedTemplatePath: badExt));

            // Empty dir throws FileNotFoundException
            string emptyDir = Path.Combine(tempRoot, "empty");
            Directory.CreateDirectory(emptyDir);
            Assert.Throws<FileNotFoundException>(() =>
                StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(emptyDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TemplateManifestAuditHelpers_MissingLoadedFamilyProducesManifestDiff()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(spec);
        StarterKitCatalogPlanResult catalog = StarterKitCatalogSelector.CompileAndLintCatalog(spec, includeOptionalFamilies: true);
        ExpectedManifest expected = StarterKitCatalogSelector.BuildExpectedManifestForSelection(spec, includeOptionalFamilies: true);

        // Simulate a reopened .rte where one required family (KNZ_CIRC_Escalator) failed to load / was not saved.
        List<string> loadedNamesInDoc = catalog.ManifestEntries
            .Select(f => f.FamilyName)
            .Where(name => !string.Equals(name, "KNZ_CIRC_Escalator", StringComparison.Ordinal))
            .ToList();

        IReadOnlyList<ManifestFamilyEntry> filtered = TemplateManifestAuditHelpers.FilterLoadedFamilyEntries(
            catalog.ManifestEntries,
            loadedNamesInDoc);

        Assert.Equal(catalog.ManifestEntries.Count - 1, filtered.Count);
        Assert.DoesNotContain(filtered, f => f.FamilyName == "KNZ_CIRC_Escalator");

        ExpectedManifest actualWithMissingFamily = plan.ToSimulatedManifest(filtered);
        ManifestComparisonResult comparison = StarterKitManifestComparer.Compare(expected, actualWithMissingFamily);
        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.Findings,
            f => f.Kind == ManifestDiffKind.Missing &&
                 f.Section == "families" &&
                 string.Equals(f.Key, "KNZ_CIRC_Escalator", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateManifestAuditHelpers_PartialSubcategoryHidingFailsVerification()
    {
        // Hidden on 4 of 5 categories -> must NOT be reported as hidden
        SubcategoryVisibilityObservation[] partialObservations =
        {
            new("OST_GenericModel", "K_Footprint", isHidden: true),
            new("OST_SpecialityEquipment", "K_Footprint", isHidden: true),
            new("OST_Furniture", "K_Footprint", isHidden: true),
            new("OST_PlumbingFixtures", "K_Footprint", isHidden: true),
            new("OST_Doors", "K_Footprint", isHidden: false),
        };

        IReadOnlyList<string> partialResult = TemplateManifestAuditHelpers.ResolveFullyHiddenSubcategories(partialObservations);
        Assert.Empty(partialResult);

        // Hidden on all 5 of 5 categories -> reported as hidden
        SubcategoryVisibilityObservation[] fullObservations =
        {
            new("OST_GenericModel", "K_Footprint", isHidden: true),
            new("OST_SpecialityEquipment", "K_Footprint", isHidden: true),
            new("OST_Furniture", "K_Footprint", isHidden: true),
            new("OST_PlumbingFixtures", "K_Footprint", isHidden: true),
            new("OST_Doors", "K_Footprint", isHidden: true),
        };

        IReadOnlyList<string> fullResult = TemplateManifestAuditHelpers.ResolveFullyHiddenSubcategories(fullObservations);
        Assert.Equal(new[] { "K_Footprint" }, fullResult.ToArray());

        // Verify partial hiding surfaces as a manifest diff on 3D view templates
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(spec);
        StarterKitCatalogPlanResult catalog = StarterKitCatalogSelector.CompileAndLintCatalog(spec, includeOptionalFamilies: true);
        ExpectedManifest expected = StarterKitCatalogSelector.BuildExpectedManifestForSelection(spec, includeOptionalFamilies: true);
        ExpectedManifest baseline = plan.ToSimulatedManifest(catalog.ManifestEntries);

        List<ManifestViewTemplateEntry> patchedTemplates = baseline.ViewTemplates
            .Select(vt => vt.Name == "Kanizu 3D Export"
                ? new ManifestViewTemplateEntry(
                    vt.Name,
                    vt.ViewType,
                    vt.DetailLevel,
                    vt.IsCanonical2DExport,
                    vt.HasVisibilityHidingFilters,
                    vt.FilterNames,
                    vt.HideNonGisLineStyles,
                    partialResult)
                : vt)
            .ToList();

        ExpectedManifest partialManifest = new(
            baseline.KitVersion,
            baseline.GisStandardVersion,
            baseline.SupportedRevitVersions,
            baseline.CanonicalExportViewTemplate,
            baseline.StartingViewName,
            baseline.StartingViewType,
            baseline.StartingViewBilingual,
            baseline.ZeroIdentityEnforced,
            baseline.ZeroWorksetsEnforced,
            baseline.HasProjectIdentity,
            baseline.WorksetCount,
            patchedTemplates,
            baseline.QaViewFilters,
            baseline.Schedules,
            baseline.LineStyles,
            baseline.Materials,
            baseline.SystemFamilies,
            baseline.SharedParameters,
            baseline.Families);

        ManifestComparisonResult comparison = StarterKitManifestComparer.Compare(expected, partialManifest);
        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.Findings,
            f => f.Section == "viewTemplates" &&
                 f.Key == "Kanizu 3D Export" &&
                 f.Property == "hideSubcategories");
    }

    [Fact]
    public void TemplateManifestAuditHelpers_InspectsCompanionGateSideColorsAndMultiTemplateDrift()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(spec);
        TemplateQaFilterPlan gateSidePlan = Assert.Single(plan.QaViewFilters, f => f.Name == "KNZ_QA_INFO_GateSide_Check");

        // Companion descriptor expands rachi_gai (#FFF3CD) deterministically
        IReadOnlyList<CompanionQaFilterDescriptor> companions = TemplateManifestAuditHelpers.BuildCompanionFilters(gateSidePlan);
        CompanionQaFilterDescriptor rachiGai = Assert.Single(companions);
        Assert.Equal("KNZ_QA_INFO_GateSide_Check", rachiGai.BaseFilterName);
        Assert.Equal("KNZ_QA_INFO_GateSide_Check__rachi_gai", rachiGai.CompanionFilterName);
        Assert.Equal(GisSharedParameters.GisRestriction, rachiGai.ParameterName);
        Assert.Equal("rachi_gai", rachiGai.Value);
        Assert.Equal("#FFF3CD", rachiGai.OverrideColorHex);
        Assert.Equal(0xFF, rachiGai.Red);
        Assert.Equal(0xF3, rachiGai.Green);
        Assert.Equal(0xCD, rachiGai.Blue);

        Assert.True(TemplateManifestAuditHelpers.TryParseCompanionFilterName(
            rachiGai.CompanionFilterName,
            out string parsedBase,
            out string parsedValue));
        Assert.Equal("KNZ_QA_INFO_GateSide_Check", parsedBase);
        Assert.Equal("rachi_gai", parsedValue);
        Assert.False(TemplateManifestAuditHelpers.TryParseCompanionFilterName("KNZ_QA_INFO_GateSide_Check", out _, out _));

        // Reconstructing from valid live companion yields exact rachi_nai + rachi_gai ValueColors
        (string? secondaryHex, IReadOnlyDictionary<string, string> valueColors) =
            TemplateManifestAuditHelpers.ReconstructInspectedValueColors(
                gateSidePlan.Rules,
                "#1E88E5",
                new[] { ("rachi_gai", "#FFF3CD", true, true) });
        Assert.Equal("#FFF3CD", secondaryHex);
        Assert.Equal("#1E88E5", valueColors["rachi_nai"]);
        Assert.Equal("#FFF3CD", valueColors["rachi_gai"]);

        // Missing or unapplied companion MUST NOT echo spec SecondaryColorHex/ValueColors
        (string? missingSecondary, IReadOnlyDictionary<string, string> missingValueColors) =
            TemplateManifestAuditHelpers.ReconstructInspectedValueColors(
                gateSidePlan.Rules,
                "#1E88E5",
                Array.Empty<(string, string, bool, bool)>());
        Assert.Null(missingSecondary);
        Assert.Empty(missingValueColors);

        // Hidden companion or companion missing on one template must not be accepted
        (string? hiddenSecondary, IReadOnlyDictionary<string, string> hiddenValueColors) =
            TemplateManifestAuditHelpers.ReconstructInspectedValueColors(
                gateSidePlan.Rules,
                "#1E88E5",
                new[] { ("rachi_gai", "#FFF3CD", false, true) });
        Assert.Null(hiddenSecondary);
        Assert.DoesNotContain("rachi_gai", hiddenValueColors.Keys);

        // Multi-template aggregation catches visibility hiding or color drift on a second QA template
        AggregatedQaFilterObservation consistent = TemplateManifestAuditHelpers.AggregateFilterAcrossTemplates(new[]
        {
            new QaFilterTemplateObservation("GIS Working Plan", visible: true, overrideColorHex: "#1E88E5"),
            new QaFilterTemplateObservation("GIS QA Plan", visible: true, overrideColorHex: "#1E88E5"),
            new QaFilterTemplateObservation("GIS QA 3D", visible: true, overrideColorHex: "#1E88E5"),
        });
        Assert.True(consistent.Visible);
        Assert.False(consistent.HasColorDrift);
        Assert.Equal("#1E88E5", consistent.OverrideColorHex);

        AggregatedQaFilterObservation hiddenOnSecond = TemplateManifestAuditHelpers.AggregateFilterAcrossTemplates(new[]
        {
            new QaFilterTemplateObservation("GIS Working Plan", visible: true, overrideColorHex: "#1E88E5"),
            new QaFilterTemplateObservation("GIS QA Plan", visible: false, overrideColorHex: "#1E88E5"),
            new QaFilterTemplateObservation("GIS QA 3D", visible: true, overrideColorHex: "#1E88E5"),
        });
        Assert.False(hiddenOnSecond.Visible);

        AggregatedQaFilterObservation colorDrift = TemplateManifestAuditHelpers.AggregateFilterAcrossTemplates(new[]
        {
            new QaFilterTemplateObservation("GIS Working Plan", visible: true, overrideColorHex: "#1E88E5"),
            new QaFilterTemplateObservation("GIS QA Plan", visible: true, overrideColorHex: "#E53935"),
        });
        Assert.True(colorDrift.HasColorDrift);
        Assert.StartsWith("INCONSISTENT:", colorDrift.OverrideColorHex, StringComparison.Ordinal);
    }

    [Fact]
    public void StarterKitPackagePlanner_BuildsDeterministicZipLayoutAndGatesFailures()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        TemplateBuildPlan buildPlan = TemplateBuildPlanBuilder.Build(spec);
        StarterKitCatalogPlanResult catalog = StarterKitCatalogSelector.CompileAndLintCatalog(spec, includeOptionalFamilies: true);
        ExpectedManifest expected = ExpectedManifest.FromSpec(spec);
        ExpectedManifest actual = buildPlan.ToSimulatedManifest(catalog.ManifestEntries);
        ManifestComparisonResult okCompare = StarterKitManifestComparer.Compare(expected, actual);

        StarterKitPackagePlan plan2024 = StarterKitPackagePlanner.PlanPackage(spec, "2024", includeOptionalFamilies: true);
        StarterKitPackagePlan plan2027 = StarterKitPackagePlanner.PlanPackage(spec, "2027", includeOptionalFamilies: true);

        Assert.Equal("Kanizu-Revit-StarterKit-v1.0.0-Revit2024.zip", plan2024.ZipFileName);
        Assert.Equal("Kanizu-Revit-StarterKit-v1.0.0-Revit2027.zip", plan2027.ZipFileName);
        Assert.Equal("KanizuStationStarter.rte", plan2024.TemplateFileName);

        // 1 .rte + 16 .rfa + manifest.json + manifest-diff.json + SHA256SUMS.txt + README.md + START_HERE.txt = 22
        Assert.Equal(22, plan2024.Entries.Count);
        Assert.Equal(21, plan2024.ChecksumEntries.Count); // excludes SHA256SUMS.txt itself
        Assert.Equal(
            plan2024.Entries.Select(e => e.ArchiveRelativePath).OrderBy(p => p, StringComparer.Ordinal),
            plan2024.Entries.Select(e => e.ArchiveRelativePath));

        // Unsupported Revit year rejected
        Assert.Throws<ArgumentException>(() => StarterKitPackagePlanner.PlanPackage(spec, "2022"));

        // Valid staged artifacts + passing report => IsValid == true
        List<StarterKitFamilyVerificationSummary> famSummaries = catalog.LintReports
            .Select(r => new StarterKitFamilyVerificationSummary(
                r.FamilyId,
                r.FamilyName,
                $"Families/{r.FamilyName}.rfa",
                r.FileSizeBytes ?? 64 * 1024,
                r.Passed,
                r.Findings.Count(f => f.Severity == RuleSeverity.Error),
                r.Findings.Count(f => f.Severity == RuleSeverity.Warning),
                r.CoarseTriangles,
                r.MediumFineTriangles))
            .ToList();

        StarterKitBuildVerificationReport okReport = new(
            spec.Header.KitVersion,
            "2024",
            plan2024.TemplateFileName,
            templateFileSizeBytes: 8L * 1024 * 1024,
            spec.Header.Budgets.MaxRteFileSizeMb,
            expected.ComputeSha256(),
            actual.ComputeSha256(),
            okCompare,
            famSummaries,
            zeroIdentityVerified: true,
            zeroWorksetsVerified: true);

        Assert.True(okReport.Succeeded);
        StarterKitBuildVerificationReport parsedReport = StarterKitBuildVerificationReport.FromJson(okReport.ToCanonicalJson());
        Assert.True(parsedReport.Succeeded);
        Assert.Equal(0, parsedReport.DiffCount);
        Assert.Equal(0, parsedReport.RfaLintErrorCount);
        Assert.Equal(16, parsedReport.FamilySummaries.Count);

        Dictionary<string, long> stagedFiles = plan2024.Entries.ToDictionary(
            e => e.ArchiveRelativePath,
            e => e.Kind == StarterKitPackageEntryKind.TemplateRte ? 8L * 1024 * 1024 : 64L * 1024,
            StringComparer.Ordinal);

        StarterKitPackageValidationResult okValidation = StarterKitPackagePlanner.ValidateStagedArtifacts(
            plan2024,
            stagedFiles,
            okReport);
        Assert.True(okValidation.IsValid, string.Join("\n", okValidation.Errors));

        // Reject missing .rfa
        Dictionary<string, long> missingRfa = new(stagedFiles, StringComparer.Ordinal);
        missingRfa.Remove("Families/KNZ_CIRC_Escalator.rfa");
        StarterKitPackageValidationResult missingValidation = StarterKitPackagePlanner.ValidateStagedArtifacts(
            plan2024,
            missingRfa,
            okReport);
        Assert.False(missingValidation.IsValid);
        Assert.Contains(missingValidation.Errors, e => e.IndexOf("KNZ_CIRC_Escalator.rfa", StringComparison.Ordinal) >= 0);

        // Reject oversized .rte
        Dictionary<string, long> oversizedRte = new(stagedFiles, StringComparer.Ordinal)
        {
            ["KanizuStationStarter.rte"] = 30L * 1024 * 1024,
        };
        StarterKitPackageValidationResult sizeValidation = StarterKitPackagePlanner.ValidateStagedArtifacts(
            plan2024,
            oversizedRte,
            okReport);
        Assert.False(sizeValidation.IsValid);
        Assert.Contains(sizeValidation.Errors, e => e.IndexOf("maxRteFileSizeMb", StringComparison.Ordinal) >= 0);

        // Reject non-zero manifest diff
        ManifestComparisonResult badCompare = new(new[]
        {
            new ManifestDiffFinding(ManifestDiffKind.Missing, "schedules", "Kanizu GIS - Floors", "entry", "Kanizu GIS - Floors", string.Empty, "Missing schedule"),
        });
        StarterKitBuildVerificationReport diffReport = new(
            spec.Header.KitVersion,
            "2024",
            plan2024.TemplateFileName,
            8L * 1024 * 1024,
            spec.Header.Budgets.MaxRteFileSizeMb,
            expected.ComputeSha256(),
            "deadbeef",
            badCompare,
            famSummaries,
            zeroIdentityVerified: true,
            zeroWorksetsVerified: true);
        Assert.False(diffReport.Succeeded);
        StarterKitPackageValidationResult diffValidation = StarterKitPackagePlanner.ValidateStagedArtifacts(
            plan2024,
            stagedFiles,
            diffReport);
        Assert.False(diffValidation.IsValid);
        Assert.Contains(diffValidation.Errors, e => e.IndexOf("1 diff(s)", StringComparison.Ordinal) >= 0);

        // Reject non-zero RfaLinter error
        List<StarterKitFamilyVerificationSummary> failingFam = new(famSummaries)
        {
            [0] = new StarterKitFamilyVerificationSummary("KNZ_CIRC_Escalator", "KNZ_CIRC_Escalator", "Families/KNZ_CIRC_Escalator.rfa", 64 * 1024, false, errorCount: 2, warningCount: 0, 12, 120),
        };
        StarterKitBuildVerificationReport lintFailReport = new(
            spec.Header.KitVersion,
            "2024",
            plan2024.TemplateFileName,
            8L * 1024 * 1024,
            spec.Header.Budgets.MaxRteFileSizeMb,
            expected.ComputeSha256(),
            actual.ComputeSha256(),
            okCompare,
            failingFam,
            zeroIdentityVerified: true,
            zeroWorksetsVerified: true);
        Assert.False(lintFailReport.Succeeded);
        StarterKitPackageValidationResult lintValidation = StarterKitPackagePlanner.ValidateStagedArtifacts(
            plan2024,
            stagedFiles,
            lintFailReport);
        Assert.False(lintValidation.IsValid);
        Assert.Contains(lintValidation.Errors, e => e.IndexOf("2 blocking error(s)", StringComparison.Ordinal) >= 0);

        // Bilingual README.md & START_HERE.txt generation with zero personal paths
        string readme = StarterKitPackagePlanner.GenerateReadmeMarkdown(buildPlan, "2024");
        string startHere = StarterKitPackagePlanner.GenerateStartHereText(buildPlan);
        Assert.Contains("Kanizu Revit Starter Kit v1.0.0 (Revit 2024)", readme, StringComparison.Ordinal);
        Assert.Contains("収録内容", readme, StringComparison.Ordinal);
        Assert.Contains("GIS Export", readme, StringComparison.Ordinal);
        Assert.Contains("00_Start_Here", buildPlan.StartingView.Name, StringComparison.Ordinal);
        Assert.Contains("スターターキット", startHere, StringComparison.Ordinal);
        AssertNoPersonalPaths(readme);
        AssertNoPersonalPaths(startHere);
    }

    [Fact]
    public void StarterKitBuildConfig_RoundTripsJsonCleanly()
    {
        StarterKitBuildConfig original = new()
        {
            RevitYear = "2027",
            StarterKitSpecDirectory = "standards/starter-kit",
            StandardsDirectory = "standards",
            FamilySeedTemplatesDirectory = "templates/families",
            ProjectSeedTemplatesDirectory = "templates/project",
            OutputDirectory = "artifacts/starter-kit/2027",
            IncludeOptionalFamilies = false,
            OverwriteExisting = true,
            PreferJapaneseSeedTemplates = true,
            TriangleComparisonMode = "Exact",
        };

        StarterKitBuildConfig roundTripped = StarterKitBuildConfig.FromJson(original.ToJson());
        Assert.Equal("2027", roundTripped.RevitYear);
        Assert.Equal("standards/starter-kit", roundTripped.StarterKitSpecDirectory);
        Assert.Equal("standards", roundTripped.StandardsDirectory);
        Assert.Equal("templates/families", roundTripped.FamilySeedTemplatesDirectory);
        Assert.Equal("templates/project", roundTripped.ProjectSeedTemplatesDirectory);
        Assert.Equal("artifacts/starter-kit/2027", roundTripped.OutputDirectory);
        Assert.False(roundTripped.IncludeOptionalFamilies);
        Assert.True(roundTripped.PreferJapaneseSeedTemplates);
        Assert.Equal(ManifestTriangleComparisonMode.Exact, roundTripped.ResolveTriangleMode());
    }

    [Fact]
    public void ScopedSharedParametersFile_WritesUtf16LeBomEncoding()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"kanizu-sp-{Guid.NewGuid():N}.txt");
        try
        {
            string expectedContent = GisStandardFiles.ReadSharedParameterFile();
            ScopedSharedParametersFile.WriteUtf16LeBomIfChanged(tempPath, expectedContent);

            byte[] bytes = File.ReadAllBytes(tempPath);
            Assert.True(bytes.Length > 2);
            // UTF-16 LE BOM: 0xFF, 0xFE
            Assert.Equal((byte)0xFF, bytes[0]);
            Assert.Equal((byte)0xFE, bytes[1]);

            string decoded = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            Assert.Equal(expectedContent, decoded);

            // Idempotent re-write does not alter bytes
            DateTime firstWrite = File.GetLastWriteTimeUtc(tempPath);
            ScopedSharedParametersFile.WriteUtf16LeBomIfChanged(tempPath, expectedContent);
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(tempPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void BuildStarterKitScript_ExistsPassesStructuralAndHygieneChecks()
    {
        string repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", "Build-StarterKit.ps1");
        Assert.True(File.Exists(scriptPath), $"Expected PowerShell packaging script at '{scriptPath}'.");

        string rawContent = File.ReadAllText(scriptPath, Encoding.UTF8);
        string content = rawContent.Replace("\r\n", "\n");

        // Consistent line endings (respects .gitattributes *.ps1 text eol=crlf on Windows CI and LF in repo)
        Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
        Assert.Contains("Set-StrictMode -Version Latest", content, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = \"Stop\"", content, StringComparison.Ordinal);

        // Required Revit 2024 + 2027, dry-run/CI flags, verification gates, and versioned zip naming
        Assert.Contains("2024", content, StringComparison.Ordinal);
        Assert.Contains("2027", content, StringComparison.Ordinal);
        Assert.Contains("SkipRevitLaunch", content, StringComparison.Ordinal);
        Assert.Contains("ArtifactsDir", content, StringComparison.Ordinal);
        Assert.Contains("StandardsDir", content, StringComparison.Ordinal);
        Assert.Contains("Resolve-StandardsDirectory", content, StringComparison.Ordinal);
        Assert.Contains("RequirePerYearSubdirectory", content, StringComparison.Ordinal);
        Assert.Contains("Sort-StringsOrdinal", content, StringComparison.Ordinal);
        Assert.Contains("[System.StringComparer]::Ordinal", content, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $StagingDirectory", content, StringComparison.Ordinal);
        Assert.Contains("zeroIdentityVerified", content, StringComparison.Ordinal);
        Assert.Contains("zeroWorksetsVerified", content, StringComparison.Ordinal);
        Assert.Contains("fileSizeBudgetPassed", content, StringComparison.Ordinal);
        Assert.Contains("revitYear", content, StringComparison.Ordinal);
        Assert.Contains("GIS Export", content, StringComparison.Ordinal);
        Assert.Contains("OST_Levels", content, StringComparison.Ordinal);
        Assert.Contains("diffCount", content, StringComparison.Ordinal);
        Assert.Contains("rfaLintErrorCount", content, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", content, StringComparison.Ordinal);
        Assert.Contains("Kanizu-Revit-StarterKit-v$kitVersion-Revit$year.zip", content, StringComparison.Ordinal);
        Assert.Contains("KANIZU_STARTER_KIT_BUILD_CONFIG", content, StringComparison.Ordinal);

        // Zero hardcoded personal paths
        AssertNoPersonalPaths(content);

        // Balanced braces and parentheses (ignoring quoted strings and comments)
        AssertBalancedDelimiters(content);
    }

    private static void AssertNoPersonalPaths(string text)
    {
        Assert.DoesNotContain("/home/", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apollo", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBalancedDelimiters(string psScript)
    {
        int braceDepth = 0;
        int parenDepth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < psScript.Length; i++)
        {
            char c = psScript[i];
            char next = i + 1 < psScript.Length ? psScript[i + 1] : '\0';

            if (inBlockComment)
            {
                if (c == '#' && next == '>')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'')
                {
                    if (next == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (inDoubleQuote)
            {
                if (c == '`')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inDoubleQuote = false;
                }

                continue;
            }

            if (c == '<' && next == '#')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '#')
            {
                inLineComment = true;
                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (c == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (c == '{') braceDepth++;
            else if (c == '}') braceDepth--;
            else if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;

            Assert.True(braceDepth >= 0, $"Unmatched '}}' at index {i} in Build-StarterKit.ps1.");
            Assert.True(parenDepth >= 0, $"Unmatched ')' at index {i} in Build-StarterKit.ps1.");
        }

        Assert.False(inSingleQuote, "Unterminated single-quoted string in Build-StarterKit.ps1.");
        Assert.False(inDoubleQuote, "Unterminated double-quoted string in Build-StarterKit.ps1.");
        Assert.False(inBlockComment, "Unterminated block comment in Build-StarterKit.ps1.");
        Assert.Equal(0, braceDepth);
        Assert.Equal(0, parenDepth);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "standards", "starter-kit")) &&
                Directory.Exists(Path.Combine(dir.FullName, "scripts")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing standards/starter-kit and scripts.");
    }
}
