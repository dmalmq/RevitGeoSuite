using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// Inspects an open or reopened Starter Kit <c>.rte</c> <see cref="Document"/> plus the built
/// <see cref="ManifestFamilyEntry"/> records from <see cref="StarterKitFamilyCatalogBuilder"/> /
/// <see cref="RevitRfaInspector"/> and emits an actual <see cref="ExpectedManifest"/> matching
/// <see cref="ExpectedManifest.FromSpec"/> field-for-field, then gates it through
/// <see cref="StarterKitManifestComparer.Compare"/>.
/// </summary>
public static class ManifestDumper
{
    private const string QaFilterPrefix = "KNZ_QA_";
    private const string GisLineStylePrefix = "GIS_";

    /// <summary>
    /// Reopens a saved <c>.rte</c> template file from disk, dumps its semantic manifest, and closes it
    /// without saving in a <c>finally</c> block (verifying persisted post-save state).
    /// </summary>
    public static ExpectedManifest DumpFile(
        Application application,
        string rteFilePath,
        StarterKitSpec spec,
        IReadOnlyList<ManifestFamilyEntry> familyEntries,
        StarterKitReferenceStandards? standards = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrWhiteSpace(rteFilePath) || !File.Exists(rteFilePath))
        {
            throw new FileNotFoundException($"Starter Kit .rte file not found at '{rteFilePath}'.", rteFilePath);
        }

        string fullPath = Path.GetFullPath(rteFilePath);
        Document? doc = null;
        try
        {
            doc = application.OpenDocumentFile(fullPath);
            if (doc is null || doc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Document '{fullPath}' is not a valid Revit project/template document.");
            }

            return Dump(doc, spec, familyEntries, standards);
        }
        finally
        {
            if (doc != null && doc.IsValidObject)
            {
                try
                {
                    doc.Close(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of reopened template document.
                }
            }
        }
    }

    /// <summary>Inspects an open Revit <c>.rte</c> document and emits an actual semantic <see cref="ExpectedManifest"/>.</summary>
    public static ExpectedManifest Dump(
        Document doc,
        StarterKitSpec spec,
        IReadOnlyList<ManifestFamilyEntry> familyEntries,
        StarterKitReferenceStandards? standards = null)
    {
        if (doc is null)
        {
            throw new ArgumentNullException(nameof(doc));
        }

        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();

        string kitVersion = ReadProjectInfoSharedText(doc, GisSharedParameters.GisKitVersion);
        int gisStandardVersion = GisStandardStateReader.ReadStandardVersion(doc) ?? 0;

        (string startingViewName, string startingViewType, bool startingViewBilingual) = InspectStartingView(doc);
        bool hasProjectIdentity = DetectProjectIdentity(doc);
        int worksetCount = CountUserWorksets(doc);

        List<ManifestViewTemplateEntry> viewTemplates = InspectViewTemplates(doc, spec);
        string canonicalTemplate = viewTemplates
            .FirstOrDefault(v => v.IsCanonical2DExport)?.Name
            ?? string.Empty;

        Dictionary<long, string> sharedParamNameById = MapSharedParameterNamesById(doc);
        List<ManifestQaFilterEntry> qaFilters = InspectQaFilters(doc, spec, sharedParamNameById);
        List<ManifestScheduleEntry> schedules = InspectSchedules(doc, spec);
        List<ManifestLineStyleEntry> lineStyles = InspectLineStyles(doc);
        List<ManifestMaterialEntry> materials = InspectMaterials3D(doc, spec);
        List<ManifestSystemFamilyEntry> systemFamilies = InspectSystemFamilies(doc, spec);
        List<ManifestSharedParameterEntry> sharedParameters = InspectSharedParameters(doc, refStandards);

        // Cross-check familyEntries against the Family elements ACTUALLY loaded in the reopened .rte
        // so missing preloaded families always surface as ManifestDiffKind.Missing.
        List<string> loadedFamilyNamesInDoc = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Select(f => f.Name ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList();
        IReadOnlyList<ManifestFamilyEntry> verifiedLoadedFamilies = TemplateManifestAuditHelpers.FilterLoadedFamilyEntries(
            familyEntries,
            loadedFamilyNamesInDoc);

        return new ExpectedManifest(
            kitVersion,
            gisStandardVersion,
            spec.Header.SupportedRevitVersions,
            canonicalTemplate,
            startingViewName,
            startingViewType,
            startingViewBilingual,
            spec.Rte.Hygiene.RequireZeroProjectIdentity,
            spec.Rte.Hygiene.RequireZeroWorksets,
            hasProjectIdentity,
            worksetCount,
            viewTemplates,
            qaFilters,
            schedules,
            lineStyles,
            materials,
            systemFamilies,
            sharedParameters,
            verifiedLoadedFamilies);
    }

    /// <summary>
    /// Compares <paramref name="actual"/> against the expected manifest built from <paramref name="spec"/>,
    /// aggregates <c>RfaLinter</c> results and file-size budgets, and returns a structured
    /// <see cref="StarterKitBuildVerificationReport"/>.
    /// </summary>
    public static StarterKitBuildVerificationReport Verify(
        ExpectedManifest actual,
        StarterKitSpec spec,
        string revitYear,
        string templateFilePath,
        bool includeOptionalFamilies = true,
        ManifestCompareOptions? compareOptions = null,
        StarterKitCatalogBuildResult? catalogResult = null,
        StarterKitReferenceStandards? standards = null,
        IReadOnlyList<string>? extraErrors = null,
        IReadOnlyList<string>? log = null)
    {
        if (actual is null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        ExpectedManifest expected = StarterKitCatalogSelector.BuildExpectedManifestForSelection(
            spec,
            includeOptionalFamilies,
            standards);
        ManifestComparisonResult comparison = StarterKitManifestComparer.Compare(
            expected,
            actual,
            compareOptions ?? new ManifestCompareOptions { TriangleMode = ManifestTriangleComparisonMode.WithinBudget });

        long rteSizeBytes = !string.IsNullOrWhiteSpace(templateFilePath) && File.Exists(templateFilePath)
            ? new FileInfo(templateFilePath).Length
            : 0L;
        string templateFileName = !string.IsNullOrWhiteSpace(templateFilePath)
            ? Path.GetFileName(templateFilePath)
            : StarterKitPackagePlanner.GetTemplateFileName(spec);

        List<StarterKitFamilyVerificationSummary> familySummaries = new();
        if (catalogResult != null)
        {
            foreach (FamilyBuildResult fam in catalogResult.Results)
            {
                int errors = fam.LintReport.Findings.Count(f => f.Severity == RuleSeverity.Error);
                int warnings = fam.LintReport.Findings.Count(f => f.Severity == RuleSeverity.Warning);
                familySummaries.Add(new StarterKitFamilyVerificationSummary(
                    fam.FamilyId,
                    fam.FamilyName,
                    $"{StarterKitPackagePlanner.FamiliesFolderName}/{fam.FamilyName}.rfa",
                    fam.FileSizeBytes,
                    fam.Succeeded,
                    errors,
                    warnings,
                    fam.Snapshot.CoarseTriangles,
                    fam.Snapshot.MediumFineTriangles));
            }
        }

        List<string> errorsList = new(extraErrors ?? Array.Empty<string>());
        if (!comparison.IsMatch)
        {
            errorsList.Add($"Manifest comparison reported {comparison.Findings.Count} difference(s).");
        }

        int totalLintErrors = familySummaries.Sum(f => f.ErrorCount);
        if (totalLintErrors > 0)
        {
            errorsList.Add($"RfaLinter reported {totalLintErrors} error(s) across built families.");
        }

        bool zeroIdentityVerified = !actual.HasProjectIdentity;
        bool zeroWorksetsVerified = actual.WorksetCount == 0;

        return new StarterKitBuildVerificationReport(
            spec.Header.KitVersion,
            revitYear,
            templateFileName,
            rteSizeBytes,
            spec.Header.Budgets.MaxRteFileSizeMb,
            expected.ComputeSha256(),
            actual.ComputeSha256(),
            comparison,
            familySummaries,
            zeroIdentityVerified,
            zeroWorksetsVerified,
            errorsList,
            log);
    }

    private static string ReadProjectInfoSharedText(Document doc, string paramName)
    {
        Parameter? param = GisStandardStateReader.FindSharedParameter(doc.ProjectInformation, paramName, allowNameFallback: true);
        if (param is null || !param.HasValue)
        {
            return string.Empty;
        }

        return (param.StorageType == StorageType.String ? param.AsString() : param.AsValueString())?.Trim() ?? string.Empty;
    }

    private static (string Name, string ViewType, bool Bilingual) InspectStartingView(Document doc)
    {
        StartingViewSettings settings = StartingViewSettings.GetStartingViewSettings(doc);
        if (settings.ViewId == ElementId.InvalidElementId || doc.GetElement(settings.ViewId) is not View startView)
        {
            return (string.Empty, string.Empty, false);
        }

        string viewType = startView.ViewType == ViewType.DraftingView
            ? "DraftingView"
            : startView.ViewType.ToString();

        bool hasAsciiLetters = false;
        bool hasJapaneseChars = false;
        foreach (TextNote note in new FilteredElementCollector(doc, startView.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
        {
            string text = note.Text ?? string.Empty;
            foreach (char ch in text)
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    hasAsciiLetters = true;
                }
                else if ((ch >= '\u3040' && ch <= '\u30FF') || (ch >= '\u4E00' && ch <= '\u9FFF'))
                {
                    hasJapaneseChars = true;
                }
            }
        }

        return (startView.Name ?? string.Empty, viewType, hasAsciiLetters && hasJapaneseChars);
    }

    private static bool DetectProjectIdentity(Document doc)
    {
        try
        {
            GeoProjectInfo? geo = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(doc));
            if (!string.IsNullOrWhiteSpace(geo?.ModelId) || !string.IsNullOrWhiteSpace(geo?.GisStationFolder))
            {
                return true;
            }
        }
        catch (Exception)
        {
            // No GeoProjectInfo schema stored yet — expected for a clean template.
        }

        foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
        {
            Parameter? levelIdParam = GisStandardStateReader.FindSharedParameter(level, GisSharedParameters.LevelId);
            if (levelIdParam != null && !string.IsNullOrWhiteSpace(levelIdParam.AsString()))
            {
                return true;
            }
        }

        HashSet<BuiltInCategory> idCategories = new();
        foreach (string name in new[] { GisSharedParameters.Id, GisSharedParameters.GisKey })
        {
            if (GisSharedParameters.TryGet(name, out GisParameterDefinition def))
            {
                foreach (string catName in def.Categories)
                {
                    if (Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
                    {
                        idCategories.Add(bic);
                    }
                }
            }
        }

        if (idCategories.Count > 0)
        {
            foreach (Element elem in new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(idCategories))
                .WhereElementIsNotElementType())
            {
                Parameter? idParam = GisStandardStateReader.FindSharedParameter(elem, GisSharedParameters.Id);
                Parameter? keyParam = GisStandardStateReader.FindSharedParameter(elem, GisSharedParameters.GisKey);
                if ((idParam != null && !string.IsNullOrWhiteSpace(idParam.AsString())) ||
                    (keyParam != null && !string.IsNullOrWhiteSpace(keyParam.AsString())))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountUserWorksets(Document doc)
    {
        if (!doc.IsWorkshared)
        {
            return 0;
        }

        return new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .Count();
    }

    private static List<ManifestViewTemplateEntry> InspectViewTemplates(Document doc, StarterKitSpec spec)
    {
        HashSet<string> expectedNames = new(spec.Rte.ViewTemplates.Select(v => v.Name), StringComparer.Ordinal);
        List<ManifestViewTemplateEntry> entries = new();

        foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (!view.IsTemplate)
            {
                continue;
            }

            string name = view.Name ?? string.Empty;
            bool isRelevant = expectedNames.Contains(name)
                || name.StartsWith("GIS ", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Kanizu ", StringComparison.OrdinalIgnoreCase);
            if (!isRelevant)
            {
                continue;
            }

            string viewType = view.ViewType == ViewType.ThreeD
                ? "ThreeD"
                : view.ViewType == ViewType.FloorPlan
                    ? "FloorPlan"
                    : view.ViewType.ToString();

            string detailLevel = view.DetailLevel.ToString();
            bool isCanonical = string.Equals(name, TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.Ordinal);

            List<string> filterNames = new();
            bool hasHideFilter = false;
            foreach (ElementId fid in view.GetFilters())
            {
                if (doc.GetElement(fid) is ParameterFilterElement pfe)
                {
                    string filterName = pfe.Name ?? string.Empty;
                    if (view.GetIsFilterEnabled(fid) && !view.GetFilterVisibility(fid))
                    {
                        hasHideFilter = true;
                    }

                    // Companion value-color filters (<base>__<value>) are inspected as part of their
                    // base filter's ValueColors; exclude them from top-level FilterNames so the
                    // canonical template filter list stays aligned with ExpectedManifest.
                    if (!TemplateManifestAuditHelpers.TryParseCompanionFilterName(filterName, out _, out _))
                    {
                        filterNames.Add(filterName);
                    }
                }
            }

            bool hideNonGisLineStyles = AreNonGisLineStylesHidden(doc, view);
            List<string> hiddenSubcategories = ReadHiddenKanizuSubcategories(doc, view);

            entries.Add(new ManifestViewTemplateEntry(
                name,
                viewType,
                detailLevel,
                isCanonical,
                hasHideFilter,
                filterNames,
                hideNonGisLineStyles,
                hiddenSubcategories));
        }

        return entries;
    }

    private static bool AreNonGisLineStylesHidden(Document doc, View view)
    {
        Category? lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        if (lines?.SubCategories is null)
        {
            return false;
        }

        int nonGisCount = 0;
        int nonGisHidden = 0;
        foreach (Category style in lines.SubCategories)
        {
            if (style is null || !view.CanCategoryBeHidden(style.Id))
            {
                continue;
            }

            if (!(style.Name ?? string.Empty).StartsWith(GisLineStylePrefix, StringComparison.Ordinal))
            {
                nonGisCount++;
                if (view.GetCategoryHidden(style.Id))
                {
                    nonGisHidden++;
                }
            }
        }

        return nonGisCount > 0 && nonGisHidden == nonGisCount;
    }

    private static List<string> ReadHiddenKanizuSubcategories(Document doc, View view)
    {
        List<SubcategoryVisibilityObservation> observations = new();
        foreach (Category parent in doc.Settings.Categories)
        {
            if (parent?.SubCategories is null)
            {
                continue;
            }

            foreach (Category sub in parent.SubCategories)
            {
                if (sub != null &&
                    string.Equals(sub.Name, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal) &&
                    view.CanCategoryBeHidden(sub.Id))
                {
                    observations.Add(new SubcategoryVisibilityObservation(
                        parent.Name ?? string.Empty,
                        sub.Name,
                        view.GetCategoryHidden(sub.Id)));
                }
            }
        }

        return TemplateManifestAuditHelpers.ResolveFullyHiddenSubcategories(observations).ToList();
    }

    private static List<ManifestQaFilterEntry> InspectQaFilters(
        Document doc,
        StarterKitSpec spec,
        IReadOnlyDictionary<long, string> sharedParamNameById)
    {
        Dictionary<string, QaViewFilterSpec> specByName = spec.Rte.QaViewFilters
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        // Inspect filter visibility and overrides across ALL QA view templates (never stopping at the first).
        List<View> qaTemplates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && (string.Equals(v.Name, "GIS QA Plan", StringComparison.Ordinal)
                                      || string.Equals(v.Name, "GIS QA 3D", StringComparison.Ordinal)
                                      || string.Equals(v.Name, "GIS Working Plan", StringComparison.Ordinal)))
            .OrderBy(v => v.Name ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        List<ParameterFilterElement> allFilters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .ToList();

        // Group companion value-color filters (<base>__<value>) under their base filter name.
        Dictionary<string, List<(string ValueKey, ParameterFilterElement Filter)>> companionsByBase = new(StringComparer.Ordinal);
        List<ParameterFilterElement> baseFilters = new();

        foreach (ParameterFilterElement filter in allFilters)
        {
            string name = filter.Name ?? string.Empty;
            if (!name.StartsWith(QaFilterPrefix, StringComparison.Ordinal) && !specByName.ContainsKey(name))
            {
                continue;
            }

            if (TemplateManifestAuditHelpers.TryParseCompanionFilterName(name, out string baseName, out string valueKey))
            {
                if (!companionsByBase.TryGetValue(baseName, out List<(string ValueKey, ParameterFilterElement Filter)>? list))
                {
                    list = new List<(string ValueKey, ParameterFilterElement Filter)>();
                    companionsByBase[baseName] = list;
                }

                list.Add((valueKey, filter));
            }
            else
            {
                baseFilters.Add(filter);
            }
        }

        List<ManifestQaFilterEntry> entries = new();
        foreach (ParameterFilterElement filter in baseFilters)
        {
            string name = filter.Name ?? string.Empty;

            List<string> categories = new();
            foreach (ElementId catId in filter.GetCategories())
            {
                string catName = ResolveBuiltInCategoryName(catId);
                if (catName.Length > 0)
                {
                    categories.Add(catName);
                }
            }

            List<ManifestFilterRuleEntry> rules = ExtractFilterRules(filter.GetElementFilter(), sharedParamNameById);
            (AggregatedQaFilterObservation aggregated, HashSet<long> baseTemplateIds) =
                InspectFilterAcrossTemplates(filter.Id, qaTemplates);

            List<(string ValueKey, string OverrideHex, bool Visible, bool AppliedOnAllBaseTemplates)> inspectedCompanions = new();
            if (companionsByBase.TryGetValue(name, out List<(string ValueKey, ParameterFilterElement Filter)>? compList))
            {
                foreach ((string valueKey, ParameterFilterElement compFilter) in compList)
                {
                    List<ManifestFilterRuleEntry> compRules = ExtractFilterRules(compFilter.GetElementFilter(), sharedParamNameById);
                    string actualValueKey = compRules.FirstOrDefault(r =>
                        string.Equals(r.Operator, "equals", StringComparison.Ordinal))?.Value ?? valueKey;

                    (AggregatedQaFilterObservation compAgg, HashSet<long> compTemplateIds) =
                        InspectFilterAcrossTemplates(compFilter.Id, qaTemplates);
                    bool appliedOnAll = baseTemplateIds.Count > 0 && baseTemplateIds.All(id => compTemplateIds.Contains(id));

                    inspectedCompanions.Add((actualValueKey, compAgg.OverrideColorHex, compAgg.Visible, appliedOnAll));
                }
            }

            (string? secondaryHex, IReadOnlyDictionary<string, string> valueColors) =
                TemplateManifestAuditHelpers.ReconstructInspectedValueColors(
                    rules,
                    aggregated.OverrideColorHex,
                    inspectedCompanions);

            specByName.TryGetValue(name, out QaViewFilterSpec? specFilter);
            string severity = specFilter?.Severity ?? InferSeverityFromName(name);

            entries.Add(new ManifestQaFilterEntry(
                name,
                severity,
                aggregated.Visible,
                aggregated.OverrideColorHex,
                categories,
                secondaryHex,
                rules,
                valueColors));
        }

        return entries;
    }

    private static (AggregatedQaFilterObservation Aggregated, HashSet<long> AppliedTemplateIds) InspectFilterAcrossTemplates(
        ElementId filterId,
        IReadOnlyList<View> qaTemplates)
    {
        List<QaFilterTemplateObservation> observations = new();
        HashSet<long> appliedIds = new();

        foreach (View vt in qaTemplates)
        {
            if (!vt.IsFilterApplied(filterId))
            {
                continue;
            }

            appliedIds.Add(vt.Id.Value);
            bool visible = vt.GetFilterVisibility(filterId);
            OverrideGraphicSettings ogs = vt.GetFilterOverrides(filterId);
            Color? color = ogs.SurfaceForegroundPatternColor != null && ogs.SurfaceForegroundPatternColor.IsValid
                ? ogs.SurfaceForegroundPatternColor
                : (ogs.ProjectionLineColor != null && ogs.ProjectionLineColor.IsValid ? ogs.ProjectionLineColor : null);
            string hex = color != null && color.IsValid
                ? GisToolStandard.RgbToHex(color.Red, color.Green, color.Blue)
                : string.Empty;

            observations.Add(new QaFilterTemplateObservation(vt.Name ?? string.Empty, visible, hex));
        }

        return (TemplateManifestAuditHelpers.AggregateFilterAcrossTemplates(observations), appliedIds);
    }

    private static List<ManifestFilterRuleEntry> ExtractFilterRules(
        ElementFilter? filter,
        IReadOnlyDictionary<long, string> sharedParamNameById)
    {
        List<ManifestFilterRuleEntry> results = new();
        if (filter is null)
        {
            return results;
        }

        WalkElementFilter(filter, sharedParamNameById, results);
        return results;
    }

    private static void WalkElementFilter(
        ElementFilter filter,
        IReadOnlyDictionary<long, string> sharedParamNameById,
        List<ManifestFilterRuleEntry> output)
    {
        if (filter is ElementLogicalFilter logical)
        {
            foreach (ElementFilter child in logical.GetFilters())
            {
                WalkElementFilter(child, sharedParamNameById, output);
            }

            return;
        }

        if (filter is ElementParameterFilter paramFilter)
        {
            foreach (FilterRule rule in paramFilter.GetRules())
            {
                ManifestFilterRuleEntry? mapped = MapRevitFilterRule(rule, sharedParamNameById);
                if (mapped != null)
                {
                    output.Add(mapped);
                }
            }
        }
    }

    private static ManifestFilterRuleEntry? MapRevitFilterRule(
        FilterRule rule,
        IReadOnlyDictionary<long, string> sharedParamNameById)
    {
        if (rule is FilterInverseRule inverse)
        {
            FilterRule? inner = inverse.GetInnerRule();
            if (inner is FilterStringRule innerStr && innerStr.GetEvaluator() is FilterStringEquals)
            {
                string paramName = ResolveRuleParameterName(innerStr.GetRuleParameter(), sharedParamNameById);
                return new ManifestFilterRuleEntry(paramName, "notEquals", innerStr.RuleString ?? string.Empty);
            }

            return null;
        }

        if (rule is HasNoValueFilterRule)
        {
            string paramName = ResolveRuleParameterName(rule.GetRuleParameter(), sharedParamNameById);
            return new ManifestFilterRuleEntry(paramName, "isEmpty", string.Empty);
        }

        if (rule is HasValueFilterRule)
        {
            string paramName = ResolveRuleParameterName(rule.GetRuleParameter(), sharedParamNameById);
            return new ManifestFilterRuleEntry(paramName, "hasValue", string.Empty);
        }

        if (rule is FilterStringRule strRule)
        {
            string paramName = ResolveRuleParameterName(strRule.GetRuleParameter(), sharedParamNameById);
            FilterStringRuleEvaluator eval = strRule.GetEvaluator();
            string op = eval is FilterStringEquals
                ? "equals"
                : eval is FilterStringContains
                    ? "contains"
                    : eval.GetType().Name;
            return new ManifestFilterRuleEntry(paramName, op, strRule.RuleString ?? string.Empty);
        }

        return null;
    }

    private static string ResolveRuleParameterName(ElementId paramId, IReadOnlyDictionary<long, string> sharedParamNameById)
    {
        if (paramId != ElementId.InvalidElementId && sharedParamNameById.TryGetValue(paramId.Value, out string? name) && name != null)
        {
            return name;
        }

        return paramId.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string InferSeverityFromName(string name)
    {
        if (name.IndexOf("_ERR_", StringComparison.OrdinalIgnoreCase) >= 0) return "error";
        if (name.IndexOf("_WARN_", StringComparison.OrdinalIgnoreCase) >= 0) return "warning";
        if (name.IndexOf("_OK_", StringComparison.OrdinalIgnoreCase) >= 0) return "ok";
        return "info";
    }

    private static List<ManifestScheduleEntry> InspectSchedules(Document doc, StarterKitSpec spec)
    {
        Dictionary<string, ScheduleSpec> specByName = spec.Rte.Schedules
            .ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);

        List<ManifestScheduleEntry> entries = new();
        foreach (ViewSchedule schedule in new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>())
        {
            if (schedule.IsTemplate)
            {
                continue;
            }

            string name = schedule.Name ?? string.Empty;
            string categoryName = ResolveBuiltInCategoryName(schedule.Definition.CategoryId);
            bool isKanizuSchedule = specByName.ContainsKey(name)
                || name.StartsWith("Kanizu GIS", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("✗", StringComparison.Ordinal)
                || string.Equals(categoryName, TemplateBuildPlanBuilder.ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase);

            if (!isKanizuSchedule)
            {
                continue;
            }

            ScheduleDefinition def = schedule.Definition;
            Dictionary<ScheduleFieldId, string> sharedFieldNamesById = new();
            List<string> fields = new();
            int fieldCount = def.GetFieldCount();
            for (int i = 0; i < fieldCount; i++)
            {
                ScheduleField field = def.GetField(i);
                if (field.ParameterId != ElementId.InvalidElementId &&
                    doc.GetElement(field.ParameterId) is SharedParameterElement shared)
                {
                    string paramName = (shared.GetDefinition()?.Name ?? shared.Name ?? string.Empty).Trim();
                    if (paramName.Length > 0)
                    {
                        fields.Add(paramName);
                        sharedFieldNamesById[field.FieldId] = paramName;
                    }
                }
            }

            List<ManifestFilterRuleEntry> filters = new();
            int filterCount = def.GetFilterCount();
            for (int i = 0; i < filterCount; i++)
            {
                ScheduleFilter sf = def.GetFilter(i);
                if (!sharedFieldNamesById.TryGetValue(sf.FieldId, out string? paramName) || paramName is null)
                {
                    continue;
                }

                (string op, string val) = MapScheduleFilter(sf);
                filters.Add(new ManifestFilterRuleEntry(paramName, op, val));
            }

            bool hasGisKindFilter = filters.Any(r =>
                string.Equals(r.Parameter, GisSharedParameters.GisKind, StringComparison.Ordinal) &&
                string.Equals(r.Operator, "hasValue", StringComparison.Ordinal));

            specByName.TryGetValue(name, out ScheduleSpec? specSched);
            bool isException = specSched?.EmptyMeansPass ?? name.StartsWith("✗", StringComparison.Ordinal);
            string kind = specSched?.Kind ?? (isException ? "exception" : "review");
            string id = specSched?.Id ?? name;

            entries.Add(new ManifestScheduleEntry(
                id,
                name,
                kind,
                categoryName,
                isException,
                fields,
                hasGisKindFilter,
                filters));
        }

        return entries;
    }

    private static (string Op, string Value) MapScheduleFilter(ScheduleFilter sf)
    {
        switch (sf.FilterType)
        {
            case ScheduleFilterType.HasNoValue:
                return ("isEmpty", string.Empty);
            case ScheduleFilterType.HasValue:
                return ("hasValue", string.Empty);
            case ScheduleFilterType.Equal:
                return ("equals", sf.IsStringValue ? sf.GetStringValue() ?? string.Empty : string.Empty);
            case ScheduleFilterType.NotEqual:
                return ("notEquals", sf.IsStringValue ? sf.GetStringValue() ?? string.Empty : string.Empty);
            case ScheduleFilterType.Contains:
                return ("contains", sf.IsStringValue ? sf.GetStringValue() ?? string.Empty : string.Empty);
            default:
                return (sf.FilterType.ToString(), string.Empty);
        }
    }

    private static List<ManifestLineStyleEntry> InspectLineStyles(Document doc)
    {
        List<ManifestLineStyleEntry> entries = new();
        Category? lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        if (lines?.SubCategories is null)
        {
            return entries;
        }

        ElementId solidPatternId = LinePatternElement.GetSolidPatternId();

        foreach (Category style in lines.SubCategories)
        {
            string name = style?.Name ?? string.Empty;
            if (!name.StartsWith(GisLineStylePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            int weight = style!.GetLineWeight(GraphicsStyleType.Projection) ?? 0;
            Color? color = style.LineColor;
            string hex = color != null && color.IsValid
                ? GisToolStandard.RgbToHex(color.Red, color.Green, color.Blue)
                : string.Empty;

            ElementId patternId = style.GetLinePatternId(GraphicsStyleType.Projection);
            string patternName = patternId == solidPatternId || patternId == ElementId.InvalidElementId
                ? "Solid"
                : (doc.GetElement(patternId) as LinePatternElement)?.Name ?? string.Empty;

            entries.Add(new ManifestLineStyleEntry(name, weight, hex, patternName));
        }

        return entries;
    }

    private static List<ManifestMaterialEntry> InspectMaterials3D(Document doc, StarterKitSpec spec)
    {
        Dictionary<string, Material3DSpec> specByName = spec.Materials3D.Materials
            .ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);

        List<ManifestMaterialEntry> entries = new();
        foreach (Material mat in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
        {
            string name = mat.Name ?? string.Empty;
            bool isStarter3D = specByName.ContainsKey(name)
                || name.StartsWith("K3D_", StringComparison.Ordinal)
                || name.StartsWith("M3D_", StringComparison.Ordinal);
            if (!isStarter3D)
            {
                continue;
            }

            Color? color = mat.Color;
            string hex = color != null && color.IsValid
                ? GisToolStandard.RgbToHex(color.Red, color.Green, color.Blue)
                : string.Empty;
            int transparency = mat.Transparency;
            string alphaMode = transparency > 0 ? "blend" : "opaque";
            string role = specByName.TryGetValue(name, out Material3DSpec? specMat) && specMat != null
                ? specMat.Role
                : string.Empty;

            entries.Add(new ManifestMaterialEntry(name, role, hex, transparency, alphaMode));
        }

        return entries;
    }

    private static List<ManifestSystemFamilyEntry> InspectSystemFamilies(Document doc, StarterKitSpec spec)
    {
        List<ManifestSystemFamilyEntry> entries = new();

        foreach (SystemFamilySpec sf in spec.Rte.SystemFamilies)
        {
            if (string.Equals(sf.SystemKind, "Stair", StringComparison.OrdinalIgnoreCase))
            {
                StairsType? stair = new FilteredElementCollector(doc)
                    .OfClass(typeof(StairsType))
                    .Cast<StairsType>()
                    .FirstOrDefault(s => string.Equals(s.Name, sf.TypeName, StringComparison.Ordinal));
                if (stair != null)
                {
                    string matName = ReadStairMaterialName(doc, stair);
                    entries.Add(new ManifestSystemFamilyEntry(sf.Id, stair.Name, "Stair", "OST_Stairs", matName));
                }
            }
            else if (string.Equals(sf.SystemKind, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                WallType? wall = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(w => string.Equals(w.Name, sf.TypeName, StringComparison.Ordinal));
                if (wall != null)
                {
                    string matName = ReadCompoundTopMaterialName(doc, wall.GetCompoundStructure());
                    entries.Add(new ManifestSystemFamilyEntry(sf.Id, wall.Name, "Wall", "OST_Walls", matName));
                }
            }
            else if (string.Equals(sf.SystemKind, "Floor", StringComparison.OrdinalIgnoreCase))
            {
                FloorType? floor = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(f => string.Equals(f.Name, sf.TypeName, StringComparison.Ordinal));
                if (floor != null)
                {
                    string matName = ReadCompoundTopMaterialName(doc, floor.GetCompoundStructure());
                    entries.Add(new ManifestSystemFamilyEntry(sf.Id, floor.Name, "Floor", "OST_Floors", matName));
                }
            }
        }

        return entries;
    }

    private static string ReadStairMaterialName(Document doc, StairsType stair)
    {
        if (stair.RunType != ElementId.InvalidElementId && doc.GetElement(stair.RunType) is StairsRunType run)
        {
            if (run.IsMonolithic)
            {
                try
                {
                    ElementId matId = run.MaterialId;
                    if (matId != ElementId.InvalidElementId)
                    {
                        string? name = doc.GetElement(matId)?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name!;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Fall through to built-in material parameters when MaterialId is unavailable.
                }

                Parameter? structParam = run.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (structParam != null && structParam.StorageType == StorageType.ElementId)
                {
                    ElementId id = structParam.AsElementId();
                    if (id != ElementId.InvalidElementId)
                    {
                        string? name = doc.GetElement(id)?.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name!;
                        }
                    }
                }
            }

            Parameter? treadParam = run.get_Parameter(BuiltInParameter.STAIRS_TRISERTYPE_TREAD_MATERIAL);
            if (treadParam != null && treadParam.StorageType == StorageType.ElementId)
            {
                ElementId id = treadParam.AsElementId();
                if (id != ElementId.InvalidElementId)
                {
                    return doc.GetElement(id)?.Name ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static string ReadCompoundTopMaterialName(Document doc, CompoundStructure? cs)
    {
        if (cs is null || cs.LayerCount == 0)
        {
            return string.Empty;
        }

        ElementId matId = cs.GetMaterialId(0);
        return matId != ElementId.InvalidElementId ? doc.GetElement(matId)?.Name ?? string.Empty : string.Empty;
    }

    private static List<ManifestSharedParameterEntry> InspectSharedParameters(
        Document doc,
        StarterKitReferenceStandards refStandards)
    {
        List<ManifestSharedParameterEntry> entries = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        DefinitionBindingMapIterator iter = doc.ParameterBindings.ForwardIterator();
        iter.Reset();
        while (iter.MoveNext())
        {
            if (iter.Key is not InternalDefinition def || iter.Current is not ElementBinding binding)
            {
                continue;
            }

            string name = def.Name ?? string.Empty;
            if (!refStandards.ParametersByName.ContainsKey(name) || !seen.Add(name))
            {
                continue;
            }

            Guid guid = doc.GetElement(def.Id) is SharedParameterElement shared
                ? shared.GuidValue
                : Guid.Empty;
            string bindingKind = binding is TypeBinding ? "type" : "instance";
            ForgeTypeId dataTypeId = def.GetDataType();
            string dataType = MapSharedParameterDataType(dataTypeId);

            List<string> categories = new();
            foreach (Category cat in binding.Categories)
            {
                if (cat != null && cat.BuiltInCategory != BuiltInCategory.INVALID)
                {
                    categories.Add(cat.BuiltInCategory.ToString());
                }
            }

            entries.Add(new ManifestSharedParameterEntry(name, guid, bindingKind, dataType, categories));
        }

        return entries;
    }

    private static string MapSharedParameterDataType(ForgeTypeId? dataTypeId)
    {
        if (dataTypeId is null)
        {
            return "unknown";
        }

        if (dataTypeId == SpecTypeId.Int.Integer)
        {
            return "integer";
        }

        if (dataTypeId == SpecTypeId.String.Text)
        {
            return "text";
        }

        if (dataTypeId == SpecTypeId.Boolean.YesNo)
        {
            return "yesno";
        }

        return string.IsNullOrWhiteSpace(dataTypeId.TypeId) ? "unknown" : dataTypeId.TypeId;
    }

    private static Dictionary<long, string> MapSharedParameterNamesById(Document doc)
    {
        Dictionary<long, string> map = new();
        foreach (SharedParameterElement shared in new FilteredElementCollector(doc)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>())
        {
            string name = (shared.GetDefinition()?.Name ?? shared.Name ?? string.Empty).Trim();
            if (name.Length > 0 && !map.ContainsKey(shared.Id.Value))
            {
                map[shared.Id.Value] = name;
            }
        }

        return map;
    }

    private static string ResolveBuiltInCategoryName(ElementId categoryId)
    {
        if (categoryId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        long raw = categoryId.Value;
        if (raw >= int.MinValue && raw <= int.MaxValue)
        {
            BuiltInCategory bic = (BuiltInCategory)(int)raw;
            if (Enum.IsDefined(typeof(BuiltInCategory), bic) && bic != BuiltInCategory.INVALID)
            {
                return bic.ToString();
            }
        }

        return string.Empty;
    }
}
