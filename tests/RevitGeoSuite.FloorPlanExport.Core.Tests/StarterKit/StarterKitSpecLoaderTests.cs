using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class StarterKitSpecLoaderTests
{
    [Fact]
    public void ShippedStarterKitOnDisk_LoadsAndPassesAllCrossReferenceValidations()
    {
        string standardsDir = GetStandardsDirectory();
        string kitDir = Path.Combine(standardsDir, "starter-kit");

        StarterKitLoadResult result = StarterKitSpecLoader.LoadFromDirectory(kitDir, standardsDir);

        Assert.True(
            result.IsValid,
            "Expected shipped standards/starter-kit/ to pass validation, but got errors:\n" +
            string.Join("\n", result.Validation.Errors.Select(e => e.ToString())));
        Assert.NotNull(result.Spec);

        StarterKitSpec spec = result.Spec!;
        Assert.Equal("1.0.0", spec.Header.KitVersion);
        Assert.Equal(3, spec.Header.GisStandardVersion);
        Assert.Contains("2024", spec.Header.SupportedRevitVersions);
        Assert.Contains("2027", spec.Header.SupportedRevitVersions);
        Assert.Equal(16, spec.Families.Count);
        Assert.Equal("GIS Export", spec.Rte.CanonicalExportViewTemplate);
        Assert.Equal("00_Start_Here", spec.Rte.StartingView.Name);
        Assert.True(spec.Rte.StartingView.Bilingual);
        Assert.True(spec.Rte.Hygiene.RequireZeroProjectIdentity);
        Assert.True(spec.Rte.Hygiene.RequireZeroWorksets);
        Assert.Equal(5, spec.Rte.ViewTemplates.Count);
        Assert.Equal(8, spec.Rte.QaViewFilters.Count);
        Assert.Equal(7, spec.Rte.Schedules.Count);
        Assert.DoesNotContain(spec.Rte.Schedules, s => string.Equals(s.Category, "OST_Levels", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, spec.Rte.LineStyles.Count);
        Assert.Equal(3, spec.Rte.SystemFamilies.Count);
    }

    [Fact]
    public void EmbeddedStarterKit_LoadsIdenticallyToDiskSpecs()
    {
        string standardsDir = GetStandardsDirectory();
        StarterKitLoadResult disk = StarterKitSpecLoader.LoadFromDirectory(Path.Combine(standardsDir, "starter-kit"), standardsDir);
        StarterKitLoadResult embedded = StarterKitSpecLoader.LoadEmbedded();

        Assert.True(disk.IsValid);
        Assert.True(embedded.IsValid, string.Join("\n", embedded.Validation.Errors.Select(e => e.ToString())));

        ExpectedManifest diskManifest = ExpectedManifest.FromSpec(disk.Spec!, StarterKitReferenceStandards.LoadFromDirectory(standardsDir));
        ExpectedManifest embeddedManifest = ExpectedManifest.FromSpec(embedded.Spec!, StarterKitReferenceStandards.LoadEmbedded());

        Assert.Equal(diskManifest.ToCanonicalJson(), embeddedManifest.ToCanonicalJson());
        Assert.Equal(diskManifest.ComputeSha256(), embeddedManifest.ComputeSha256());
    }

    [Fact]
    public void ShippedStarterKit_CoversAllCatalogFamiliesAndContracts()
    {
        StarterKitSpec spec = LoadShippedSpec();

        string[] expectedIds =
        {
            "KNZ_CIRC_Escalator",
            "KNZ_CIRC_MovingWalkway",
            "KNZ_CIRC_Elevator",
            "KNZ_CIRC_ElevatorDoor",
            "KNZ_OPEN_DoorSwing",
            "KNZ_OPEN_DoorSlide",
            "KNZ_OPEN_DoorAuto",
            "KNZ_OPEN_Shutter",
            "KNZ_OPEN_Opening",
            "KNZ_FIX_TicketGateBank",
            "KNZ_FIX_BarrierFence",
            "KNZ_FIX_Kiosk",
            "KNZ_FIX_Equipment",
            "KNZ_COL_Rect",
            "KNZ_COL_Round",
            "KNZ_POI_FacilityMarker",
        };

        Assert.Equal(
            expectedIds.OrderBy(x => x, StringComparer.Ordinal),
            spec.Families.Select(f => f.Id).OrderBy(x => x, StringComparer.Ordinal));

        // Wall-hosted doors use wallHostedDoor seed; all others are 100% procedural.
        foreach (FamilyGeometrySpec family in spec.Families)
        {
            if (string.Equals(family.Category, "OST_Doors", StringComparison.Ordinal))
            {
                Assert.Equal(FamilyGeometrySpec.SeedWallHostedDoor, family.SeedKind);
            }
            else
            {
                Assert.Equal(FamilyGeometrySpec.SeedProcedural, family.SeedKind);
            }

            if (family.IsPolygonal)
            {
                Assert.NotNull(family.Coarse);
                Assert.Equal("K_Footprint", family.Coarse!.Subcategory);
                Assert.True(family.Coarse.RequiresDownwardPlanarBottomAtZero);
                Assert.NotEmpty(family.Coarse.Ops);
            }
        }

        // Canonical 2D export template must be GIS Export at Coarse with zero filters (KZ-VIEW-004).
        ViewTemplateSpec gisExport = Assert.Single(spec.Rte.ViewTemplates, v => v.Name == "GIS Export");
        Assert.True(gisExport.IsCanonical2DExport);
        Assert.Equal("Coarse", gisExport.DetailLevel);
        Assert.False(gisExport.AllowVisibilityHidingFilters);
        Assert.Empty(gisExport.Filters);
        Assert.DoesNotContain(spec.Rte.ViewTemplates, v => string.Equals(v.Name, "Kanizu GIS Export Plan", StringComparison.OrdinalIgnoreCase));

        // Kanizu 3D Export must be Medium and hide K_Footprint.
        ViewTemplateSpec export3d = Assert.Single(spec.Rte.ViewTemplates, v => v.Name == "Kanizu 3D Export");
        Assert.Equal("Medium", export3d.DetailLevel);
        Assert.Contains("K_Footprint", export3d.HideSubcategories);

        // GateSide check filter must contrast rachi_nai and rachi_gai.
        QaViewFilterSpec gateSide = Assert.Single(spec.Rte.QaViewFilters, f => f.Name == "KNZ_QA_INFO_GateSide_Check");
        Assert.True(gateSide.Visible);
        Assert.True(gateSide.ValueColors.ContainsKey("rachi_nai"));
        Assert.True(gateSide.ValueColors.ContainsKey("rachi_gai"));

        // Circulation schedule must filter on gis_kind hasValue.
        ScheduleSpec circSchedule = Assert.Single(spec.Rte.Schedules, s => s.Id == "qa-schedule-circulation");
        Assert.Contains(circSchedule.Filters, f => f.Parameter == "gis_kind" && f.Operator == "hasValue");

        // All 3 exception schedules must use emptyMeansPass=true.
        ScheduleSpec[] exceptions = spec.Rte.Schedules.Where(s => s.Kind == "exception").ToArray();
        Assert.Equal(3, exceptions.Length);
        Assert.All(exceptions, s => Assert.True(s.EmptyMeansPass));
    }

    [Fact]
    public void Validation_RejectsForbiddenOstLevelsSchedule()
    {
        StarterKitSpec shipped = LoadShippedSpec();
        List<ScheduleSpec> mutatedSchedules = shipped.Rte.Schedules.ToList();
        mutatedSchedules.Add(new ScheduleSpec(
            "qa-schedule-levels",
            "Kanizu GIS - Levels",
            "review",
            "OST_Levels",
            emptyMeansPass: false,
            new[] { "short_name", "ordinal" },
            Array.Empty<FilterRuleSpec>()));

        RteTemplateSpec badRte = CloneRte(shipped.Rte, schedules: mutatedSchedules);
        StarterKitSpec badKit = new(shipped.Header, badRte, shipped.Materials3D, shipped.Families);

        StarterKitValidationResult result = StarterKitSpecLoader.Validate(badKit);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-030" && e.Message.Contains("OST_Levels"));
    }

    [Fact]
    public void Validation_RejectsLegacyExportTemplateNameAndHideFilterOnGisExport()
    {
        StarterKitSpec shipped = LoadShippedSpec();
        List<ViewTemplateSpec> badTemplates = shipped.Rte.ViewTemplates
            .Select(vt => vt.Name == "GIS Export"
                ? new ViewTemplateSpec(
                    "Kanizu GIS Export Plan",
                    vt.ViewType,
                    vt.DetailLevel,
                    vt.IsCanonical2DExport,
                    allowVisibilityHidingFilters: true,
                    filters: new[] { "KNZ_QA_ERR_Floor_MissingCategory" },
                    vt.VisibleCategories,
                    vt.VisibleLineStylePrefixes,
                    vt.HideNonGisLineStyles,
                    vt.HideSubcategories)
                : vt)
            .ToList();

        RteTemplateSpec badRte = CloneRte(shipped.Rte, canonicalExportTemplate: "Kanizu GIS Export Plan", viewTemplates: badTemplates);
        StarterKitSpec badKit = new(shipped.Header, badRte, shipped.Materials3D, shipped.Families);

        StarterKitValidationResult result = StarterKitSpecLoader.Validate(badKit);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-003");
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-020");
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-023");
    }

    [Fact]
    public void Validation_RejectsFilterOnCanonicalGisExportTemplate()
    {
        StarterKitSpec shipped = LoadShippedSpec();
        List<ViewTemplateSpec> badTemplates = shipped.Rte.ViewTemplates
            .Select(vt => vt.Name == "GIS Export"
                ? new ViewTemplateSpec(
                    vt.Name,
                    vt.ViewType,
                    vt.DetailLevel,
                    vt.IsCanonical2DExport,
                    allowVisibilityHidingFilters: false,
                    filters: new[] { "KNZ_QA_INFO_GateSide_Check" },
                    vt.VisibleCategories,
                    vt.VisibleLineStylePrefixes,
                    vt.HideNonGisLineStyles,
                    vt.HideSubcategories)
                : vt)
            .ToList();

        RteTemplateSpec badRte = CloneRte(shipped.Rte, viewTemplates: badTemplates);
        StarterKitValidationResult result = StarterKitSpecLoader.Validate(
            new StarterKitSpec(shipped.Header, badRte, shipped.Materials3D, shipped.Families));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-025");
    }

    [Fact]
    public void Validation_RejectsZeroIdentityOrZeroWorksetRelaxation()
    {
        StarterKitSpec shipped = LoadShippedSpec();
        RteHygieneSpec badHygiene = new(
            requireZeroProjectIdentity: false,
            requireZeroWorksets: false,
            shipped.Rte.Hygiene.ForbiddenIdentityFields,
            Array.Empty<string>());

        RteTemplateSpec badRte = CloneRte(shipped.Rte, hygiene: badHygiene);
        StarterKitValidationResult result = StarterKitSpecLoader.Validate(
            new StarterKitSpec(shipped.Header, badRte, shipped.Materials3D, shipped.Families));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-004");
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-005");
        Assert.Contains(result.Errors, e => e.Code == "SK-RTE-006");
    }

    [Fact]
    public void Validation_RejectsMissingKFootprintUnknownMaterialBadBindingAndInvalidDomainValue()
    {
        StarterKitSpec shipped = LoadShippedSpec();
        FamilyGeometrySpec orig = shipped.FindFamily("KNZ_OPEN_DoorSwing")!;

        FamilyGeometrySpec broken = new(
            orig.Schema,
            orig.SchemaVersion,
            orig.Id,
            orig.FamilyName,
            orig.DisplayNameEn,
            orig.DisplayNameJa,
            orig.Group,
            orig.Category,
            orig.SeedKind,
            isPolygonal: true,
            orig.Subcategories,
            new AntiZFightingClearances(2.0, 8.0, 8.0), // below 4mm minimum
            orig.DefaultDimensionsMm,
            new[]
            {
                new FamilySharedParameterSpec("gis_door", "type", true, "revolving_invalid"), // out of opening-types.json domain
                new FamilySharedParameterSpec("gis_kind", "type", true, "escalator"), // not bound to OST_Doors!
            },
            orig.TriangleBudget,
            coarse: null, // missing K_Footprint!
            new MediumFineGeometrySpec(new[]
            {
                new GeometryOpSpec(
                    GeometryOpSpec.OpBox,
                    "bad_leaf",
                    subcategory: "K_Body",
                    materialRef: "K3D_DoesNotExist",
                    originMm: new[] { 0.0, 0.0, 0.0 }, // violates slabClearMm
                    sizeMm: new[] { 900.0, 44.0, 2000.0 }),
            }));

        List<FamilyGeometrySpec> mutatedFamilies = shipped.Families
            .Select(f => f.Id == broken.Id ? broken : f)
            .ToList();

        StarterKitValidationResult result = StarterKitSpecLoader.Validate(
            new StarterKitSpec(shipped.Header, shipped.Rte, shipped.Materials3D, mutatedFamilies));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SK-CLR-001");
        Assert.Contains(result.Errors, e => e.Code == "SK-DOM-005");
        Assert.Contains(result.Errors, e => e.Code == "SK-FAM-013");
        Assert.Contains(result.Errors, e => e.Code == "SK-GEO-001");
        Assert.Contains(result.Errors, e => e.Code == "SK-GEO-019");
        Assert.Contains(result.Errors, e => e.Code == "SK-GEO-020");
    }

    [Fact]
    public void Validation_RejectsMissingFamilySpecFileOnDisk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "kanizu-sk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceDir = Path.Combine(GetStandardsDirectory(), "starter-kit");
            File.Copy(Path.Combine(sourceDir, "kit.json"), Path.Combine(tempDir, "kit.json"));
            File.Copy(Path.Combine(sourceDir, "rte.json"), Path.Combine(tempDir, "rte.json"));
            File.Copy(Path.Combine(sourceDir, "materials-3d.json"), Path.Combine(tempDir, "materials-3d.json"));

            StarterKitLoadResult result = StarterKitSpecLoader.LoadFromDirectory(tempDir, GetStandardsDirectory());
            Assert.False(result.IsValid);
            Assert.Contains(result.Validation.Errors, e => e.Code == "SK-LOAD-004");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ShippedStarterKitFiles_ContainZeroPrivateStationNamesOrMachinePaths()
    {
        string kitDir = Path.Combine(GetStandardsDirectory(), "starter-kit");
        string[] files = Directory.GetFiles(kitDir, "*.json", SearchOption.AllDirectories);
        Assert.True(files.Length >= 19, $"Expected at least 19 starter-kit spec JSON files, found {files.Length}.");

        Regex forbiddenPatterns = new(
            @"(?:C:\\Users\\|/home/|\\\\[A-Za-z0-9_.-]+\\|lumine|shinjuku|ikebukuro|shibuya|tokyo\s*station|kitte|shinagawa)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (string file in files)
        {
            string content = File.ReadAllText(file, Encoding.UTF8);
            Assert.False(
                forbiddenPatterns.IsMatch(content),
                $"Tracked starter-kit spec '{Path.GetFileName(file)}' matched forbidden private path or client station name pattern.");
            Assert.DoesNotContain("\r", content);
            // Ensure every JSON file parses cleanly
            JObject.Parse(content);
        }
    }

    internal static StarterKitSpec LoadShippedSpec()
    {
        string standardsDir = GetStandardsDirectory();
        return StarterKitSpecLoader.LoadValidFromDirectory(Path.Combine(standardsDir, "starter-kit"), standardsDir);
    }

    internal static string GetStandardsDirectory([System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        List<string> starts = new() { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        string? callerDir = string.IsNullOrWhiteSpace(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDir))
        {
            starts.Add(callerDir!);
        }

        foreach (string start in starts)
        {
            DirectoryInfo? current = new(start);
            while (current != null && !File.Exists(Path.Combine(current.FullName, "RevitGeoSuite.sln")))
            {
                current = current.Parent;
            }

            if (current != null)
            {
                return Path.Combine(current.FullName, "standards");
            }
        }

        throw new DirectoryNotFoundException("Could not locate RevitGeoSuite.sln above BaseDirectory, current directory, or source directory.");
    }

    private static RteTemplateSpec CloneRte(
        RteTemplateSpec orig,
        string? canonicalExportTemplate = null,
        RteHygieneSpec? hygiene = null,
        IReadOnlyList<ViewTemplateSpec>? viewTemplates = null,
        IReadOnlyList<ScheduleSpec>? schedules = null)
    {
        return new RteTemplateSpec(
            orig.Schema,
            orig.SchemaVersion,
            orig.TemplateName,
            orig.GisStandardVersion,
            canonicalExportTemplate ?? orig.CanonicalExportViewTemplate,
            orig.StartingView,
            hygiene ?? orig.Hygiene,
            viewTemplates ?? orig.ViewTemplates,
            orig.QaViewFilters,
            schedules ?? orig.Schedules,
            orig.LineStyles,
            orig.SystemFamilies,
            orig.IncludeAllStandardFloorTypes,
            orig.IncludeAllStandardSharedParameters);
    }
}
