using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Category of a structured difference between expected and actual Starter Kit manifests.</summary>
public enum ManifestDiffKind
{
    Missing,
    Unexpected,
    Mismatch,
}

/// <summary>How triangle counts are compared between <see cref="ExpectedManifest"/> and a dumped manifest.</summary>
public enum ManifestTriangleComparisonMode
{
    /// <summary>Actual triangle counts must exactly match the deterministic procedural estimate.</summary>
    Exact,

    /// <summary>Actual triangle counts must not exceed the family's Coarse and Medium/Fine budgets.</summary>
    WithinBudget,
}

/// <summary>Options controlling <see cref="StarterKitManifestComparer.Compare"/>.</summary>
public sealed class ManifestCompareOptions
{
    public static ManifestCompareOptions Default { get; } = new();

    public ManifestTriangleComparisonMode TriangleMode { get; set; } = ManifestTriangleComparisonMode.Exact;
}

/// <summary>One structured difference returned by <see cref="StarterKitManifestComparer"/>.</summary>
public sealed class ManifestDiffFinding
{
    public ManifestDiffFinding(
        ManifestDiffKind kind,
        string section,
        string key,
        string property,
        string expectedValue,
        string actualValue,
        string message)
    {
        Kind = kind;
        Section = section ?? string.Empty;
        Key = key ?? string.Empty;
        Property = property ?? string.Empty;
        ExpectedValue = expectedValue ?? string.Empty;
        ActualValue = actualValue ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public ManifestDiffKind Kind { get; }

    public string Section { get; }

    public string Key { get; }

    public string Property { get; }

    public string ExpectedValue { get; }

    public string ActualValue { get; }

    public string Message { get; }

    public override string ToString() => $"[{Kind}] {Section}.{Key}.{Property}: {Message}";
}

/// <summary>Deterministic comparison result between an expected and an actual Starter Kit manifest.</summary>
public sealed class ManifestComparisonResult
{
    public ManifestComparisonResult(IEnumerable<ManifestDiffFinding> findings)
    {
        List<ManifestDiffFinding> sorted = (findings ?? Array.Empty<ManifestDiffFinding>())
            .OrderBy(f => f.Section, StringComparer.Ordinal)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .ThenBy(f => f.Property, StringComparer.Ordinal)
            .ThenBy(f => f.Kind)
            .ToList();
        Findings = Array.AsReadOnly(sorted.ToArray());
    }

    public IReadOnlyList<ManifestDiffFinding> Findings { get; }

    public bool IsMatch => Findings.Count == 0;

    public IEnumerable<ManifestDiffFinding> Missing => Findings.Where(f => f.Kind == ManifestDiffKind.Missing);

    public IEnumerable<ManifestDiffFinding> Unexpected => Findings.Where(f => f.Kind == ManifestDiffKind.Unexpected);

    public IEnumerable<ManifestDiffFinding> Mismatches => Findings.Where(f => f.Kind == ManifestDiffKind.Mismatch);
}

/// <summary>
/// Deterministic semantic manifest comparer used by T6/T8 <c>ManifestDumper</c> build gates.
/// Returns structured <see cref="ManifestDiffFinding"/> items (<c>Missing</c>, <c>Unexpected</c>, <c>Mismatch</c>)
/// and enforces Starter Kit hard invariants (canonical <c>GIS Export</c> template, zero <c>OST_Levels</c> schedules,
/// zero project identity &amp; worksets, and <c>K_Footprint</c> coarse geometry on polygonal families).
/// </summary>
public static class StarterKitManifestComparer
{
    private const string ForbiddenScheduleCategory = "OST_Levels";
    private const string CanonicalExportTemplateName = "GIS Export";

    public static ManifestComparisonResult Compare(
        ExpectedManifest expected,
        ExpectedManifest actual,
        ManifestCompareOptions? options = null)
    {
        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (actual is null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        ManifestCompareOptions opts = options ?? ManifestCompareOptions.Default;
        List<ManifestDiffFinding> findings = new();

        CompareHeader(expected, actual, findings);
        CompareHygiene(expected, actual, findings);
        CompareKeyed(
            "viewTemplates",
            expected.ViewTemplates,
            actual.ViewTemplates,
            v => v.Name,
            CompareViewTemplate,
            findings);
        CompareKeyed(
            "qaViewFilters",
            expected.QaViewFilters,
            actual.QaViewFilters,
            f => f.Name,
            CompareQaFilter,
            findings);
        CompareKeyed(
            "schedules",
            expected.Schedules,
            actual.Schedules,
            s => s.Name,
            CompareSchedule,
            findings);
        CompareKeyed(
            "lineStyles",
            expected.LineStyles,
            actual.LineStyles,
            l => l.Name,
            CompareLineStyle,
            findings);
        CompareKeyed(
            "materials",
            expected.Materials,
            actual.Materials,
            m => m.Name,
            CompareMaterial,
            findings);
        CompareKeyed(
            "systemFamilies",
            expected.SystemFamilies,
            actual.SystemFamilies,
            sf => sf.Id,
            CompareSystemFamily,
            findings);
        CompareKeyed(
            "sharedParameters",
            expected.SharedParameters,
            actual.SharedParameters,
            p => p.Name,
            CompareSharedParameter,
            findings);
        CompareKeyed(
            "families",
            expected.Families,
            actual.Families,
            f => f.Id,
            (e, a, list) => CompareFamily(e, a, opts, list),
            findings);

        // Enforce hard semantic invariants on actual regardless of unexpected entries
        foreach (ManifestScheduleEntry sched in actual.Schedules)
        {
            if (string.Equals(sched.Category, ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase))
            {
                bool alreadyReported = findings.Any(f =>
                    string.Equals(f.Section, "schedules", StringComparison.Ordinal) &&
                    string.Equals(f.Key, sched.Name, StringComparison.Ordinal) &&
                    string.Equals(f.Property, "category", StringComparison.Ordinal));
                if (!alreadyReported)
                {
                    findings.Add(new ManifestDiffFinding(
                        ManifestDiffKind.Unexpected,
                        "schedules",
                        sched.Name,
                        "category",
                        "not OST_Levels",
                        sched.Category,
                        $"Schedule '{sched.Name}' uses forbidden unschedulable category '{ForbiddenScheduleCategory}'."));
                }
            }
        }

        return new ManifestComparisonResult(findings);
    }

    private static void CompareHeader(ExpectedManifest expected, ExpectedManifest actual, List<ManifestDiffFinding> findings)
    {
        CompareScalar("header", "kit", "kitVersion", expected.KitVersion, actual.KitVersion, findings);
        CompareScalar("header", "kit", "gisStandardVersion", expected.GisStandardVersion.ToString(CultureInfo.InvariantCulture), actual.GisStandardVersion.ToString(CultureInfo.InvariantCulture), findings);
        CompareStringList("header", "kit", "supportedRevitVersions", expected.SupportedRevitVersions, actual.SupportedRevitVersions, findings);
        CompareScalar("header", "kit", "canonicalExportViewTemplate", expected.CanonicalExportViewTemplate, actual.CanonicalExportViewTemplate, findings);
        if (!string.Equals(actual.CanonicalExportViewTemplate, CanonicalExportTemplateName, StringComparison.Ordinal) &&
            string.Equals(expected.CanonicalExportViewTemplate, actual.CanonicalExportViewTemplate, StringComparison.Ordinal))
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Mismatch,
                "header",
                "kit",
                "canonicalExportViewTemplate",
                CanonicalExportTemplateName,
                actual.CanonicalExportViewTemplate,
                $"Canonical 2D export view template must be '{CanonicalExportTemplateName}'."));
        }

        CompareScalar("header", "startingView", "name", expected.StartingViewName, actual.StartingViewName, findings);
        CompareScalar("header", "startingView", "viewType", expected.StartingViewType, actual.StartingViewType, findings);
        CompareScalar("header", "startingView", "bilingual", expected.StartingViewBilingual.ToString(), actual.StartingViewBilingual.ToString(), findings);
    }

    private static void CompareHygiene(ExpectedManifest expected, ExpectedManifest actual, List<ManifestDiffFinding> findings)
    {
        CompareScalar("hygiene", "rte", "zeroIdentityEnforced", expected.ZeroIdentityEnforced.ToString(), actual.ZeroIdentityEnforced.ToString(), findings);
        CompareScalar("hygiene", "rte", "zeroWorksetsEnforced", expected.ZeroWorksetsEnforced.ToString(), actual.ZeroWorksetsEnforced.ToString(), findings);

        if (expected.ZeroIdentityEnforced && actual.HasProjectIdentity)
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Mismatch,
                "hygiene",
                "rte",
                "hasProjectIdentity",
                "False",
                "True",
                "Starter Kit .rte must carry zero project identity (ModelId, GisStationFolder, level_id, element id/gis_key absent)."));
        }

        if (expected.ZeroWorksetsEnforced && actual.WorksetCount > 0)
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Mismatch,
                "hygiene",
                "rte",
                "worksetCount",
                "0",
                actual.WorksetCount.ToString(CultureInfo.InvariantCulture),
                $"Starter Kit .rte must contain zero worksets, found {actual.WorksetCount}."));
        }
    }

    private static void CompareViewTemplate(
        ManifestViewTemplateEntry expected,
        ManifestViewTemplateEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("viewTemplates", expected.Name, "viewType", expected.ViewType, actual.ViewType, findings);
        CompareScalar("viewTemplates", expected.Name, "detailLevel", expected.DetailLevel, actual.DetailLevel, findings);
        CompareScalar("viewTemplates", expected.Name, "isCanonical2DExport", expected.IsCanonical2DExport.ToString(), actual.IsCanonical2DExport.ToString(), findings);
        CompareScalar("viewTemplates", expected.Name, "hasVisibilityHidingFilters", expected.HasVisibilityHidingFilters.ToString(), actual.HasVisibilityHidingFilters.ToString(), findings);
        CompareScalar("viewTemplates", expected.Name, "hideNonGisLineStyles", expected.HideNonGisLineStyles.ToString(), actual.HideNonGisLineStyles.ToString(), findings);
        CompareStringList("viewTemplates", expected.Name, "filterNames", expected.FilterNames, actual.FilterNames, findings);
        CompareStringList("viewTemplates", expected.Name, "hideSubcategories", expected.HideSubcategories, actual.HideSubcategories, findings);
    }

    private static void CompareQaFilter(
        ManifestQaFilterEntry expected,
        ManifestQaFilterEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("qaViewFilters", expected.Name, "severity", expected.Severity, actual.Severity, findings);
        CompareScalar("qaViewFilters", expected.Name, "visible", expected.Visible.ToString(), actual.Visible.ToString(), findings);
        CompareScalar("qaViewFilters", expected.Name, "overrideColorHex", expected.OverrideColorHex, actual.OverrideColorHex, findings);
        CompareStringList("qaViewFilters", expected.Name, "categories", expected.Categories, actual.Categories, findings);
    }

    private static void CompareSchedule(
        ManifestScheduleEntry expected,
        ManifestScheduleEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("schedules", expected.Name, "id", expected.Id, actual.Id, findings);
        CompareScalar("schedules", expected.Name, "kind", expected.Kind, actual.Kind, findings);
        CompareScalar("schedules", expected.Name, "category", expected.Category, actual.Category, findings);
        CompareScalar("schedules", expected.Name, "emptyMeansPass", expected.EmptyMeansPass.ToString(), actual.EmptyMeansPass.ToString(), findings);
        CompareScalar("schedules", expected.Name, "hasGisKindFilter", expected.HasGisKindFilter.ToString(), actual.HasGisKindFilter.ToString(), findings);
        CompareStringList("schedules", expected.Name, "fields", expected.Fields, actual.Fields, findings);
    }

    private static void CompareLineStyle(
        ManifestLineStyleEntry expected,
        ManifestLineStyleEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("lineStyles", expected.Name, "lineWeight", expected.LineWeight.ToString(CultureInfo.InvariantCulture), actual.LineWeight.ToString(CultureInfo.InvariantCulture), findings);
        CompareScalar("lineStyles", expected.Name, "colorHex", expected.ColorHex, actual.ColorHex, findings);
        CompareScalar("lineStyles", expected.Name, "linePattern", expected.LinePattern, actual.LinePattern, findings);
    }

    private static void CompareMaterial(
        ManifestMaterialEntry expected,
        ManifestMaterialEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("materials", expected.Name, "role", expected.Role, actual.Role, findings);
        CompareScalar("materials", expected.Name, "hex", expected.Hex, actual.Hex, findings);
        CompareScalar("materials", expected.Name, "transparency", expected.Transparency.ToString(CultureInfo.InvariantCulture), actual.Transparency.ToString(CultureInfo.InvariantCulture), findings);
        CompareScalar("materials", expected.Name, "alphaMode", expected.AlphaMode, actual.AlphaMode, findings);
    }

    private static void CompareSystemFamily(
        ManifestSystemFamilyEntry expected,
        ManifestSystemFamilyEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("systemFamilies", expected.Id, "typeName", expected.TypeName, actual.TypeName, findings);
        CompareScalar("systemFamilies", expected.Id, "systemKind", expected.SystemKind, actual.SystemKind, findings);
        CompareScalar("systemFamilies", expected.Id, "category", expected.Category, actual.Category, findings);
        CompareScalar("systemFamilies", expected.Id, "materialRef", expected.MaterialRef, actual.MaterialRef, findings);
    }

    private static void CompareSharedParameter(
        ManifestSharedParameterEntry expected,
        ManifestSharedParameterEntry actual,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("sharedParameters", expected.Name, "guid", expected.Guid.ToString("D"), actual.Guid.ToString("D"), findings);
        CompareScalar("sharedParameters", expected.Name, "binding", expected.Binding, actual.Binding, findings);
        CompareScalar("sharedParameters", expected.Name, "dataType", expected.DataType, actual.DataType, findings);
        CompareStringList("sharedParameters", expected.Name, "categories", expected.Categories, actual.Categories, findings);
    }

    private static void CompareFamily(
        ManifestFamilyEntry expected,
        ManifestFamilyEntry actual,
        ManifestCompareOptions options,
        List<ManifestDiffFinding> findings)
    {
        CompareScalar("families", expected.Id, "familyName", expected.FamilyName, actual.FamilyName, findings);
        CompareScalar("families", expected.Id, "category", expected.Category, actual.Category, findings);
        CompareScalar("families", expected.Id, "seedKind", expected.SeedKind, actual.SeedKind, findings);
        CompareScalar("families", expected.Id, "isPolygonal", expected.IsPolygonal.ToString(), actual.IsPolygonal.ToString(), findings);
        CompareScalar("families", expected.Id, "hasCoarseKFootprint", expected.HasCoarseKFootprint.ToString(), actual.HasCoarseKFootprint.ToString(), findings);
        CompareScalar("families", expected.Id, "hasDownwardBottomFaceAtZero", expected.HasDownwardBottomFaceAtZero.ToString(), actual.HasDownwardBottomFaceAtZero.ToString(), findings);
        CompareStringList("families", expected.Id, "subcategories", expected.Subcategories, actual.Subcategories, findings);

        if (options.TriangleMode == ManifestTriangleComparisonMode.Exact)
        {
            CompareScalar("families", expected.Id, "coarseTriangles", expected.CoarseTriangles.ToString(CultureInfo.InvariantCulture), actual.CoarseTriangles.ToString(CultureInfo.InvariantCulture), findings);
            CompareScalar("families", expected.Id, "mediumFineTriangles", expected.MediumFineTriangles.ToString(CultureInfo.InvariantCulture), actual.MediumFineTriangles.ToString(CultureInfo.InvariantCulture), findings);
        }
        else
        {
            if (actual.CoarseTriangles < 0 || (expected.HasCoarseKFootprint && actual.CoarseTriangles <= 0))
            {
                string expectedRange = expected.HasCoarseKFootprint
                    ? $"1..{expected.CoarseTriangleBudget}"
                    : $"0..{expected.CoarseTriangleBudget}";
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Mismatch,
                    "families",
                    expected.Id,
                    "coarseTriangles",
                    expectedRange,
                    actual.CoarseTriangles.ToString(CultureInfo.InvariantCulture),
                    $"Family '{expected.Id}' coarse triangles ({actual.CoarseTriangles}) must be in {expectedRange}."));
            }
            else if (actual.CoarseTriangles > expected.CoarseTriangleBudget)
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Mismatch,
                    "families",
                    expected.Id,
                    "coarseTriangles",
                    $"<= {expected.CoarseTriangleBudget}",
                    actual.CoarseTriangles.ToString(CultureInfo.InvariantCulture),
                    $"Family '{expected.Id}' coarse triangles ({actual.CoarseTriangles}) exceed budget ({expected.CoarseTriangleBudget})."));
            }

            if (actual.MediumFineTriangles <= 0)
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Mismatch,
                    "families",
                    expected.Id,
                    "mediumFineTriangles",
                    $"1..{expected.MediumFineTriangleBudget}",
                    actual.MediumFineTriangles.ToString(CultureInfo.InvariantCulture),
                    $"Family '{expected.Id}' mediumFine triangles ({actual.MediumFineTriangles}) must be > 0 and <= {expected.MediumFineTriangleBudget}."));
            }
            else if (actual.MediumFineTriangles > expected.MediumFineTriangleBudget)
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Mismatch,
                    "families",
                    expected.Id,
                    "mediumFineTriangles",
                    $"<= {expected.MediumFineTriangleBudget}",
                    actual.MediumFineTriangles.ToString(CultureInfo.InvariantCulture),
                    $"Family '{expected.Id}' mediumFine triangles ({actual.MediumFineTriangles}) exceed budget ({expected.MediumFineTriangleBudget})."));
            }
        }

        IReadOnlyList<string> expectedGuids = Array.AsReadOnly(expected.SharedParameterGuids.Select(g => g.ToString("D")).ToArray());
        IReadOnlyList<string> actualGuids = Array.AsReadOnly(actual.SharedParameterGuids.Select(g => g.ToString("D")).ToArray());
        CompareStringList("families", expected.Id, "sharedParameterGuids", expectedGuids, actualGuids, findings);
    }

    private static void CompareKeyed<T>(
        string section,
        IReadOnlyList<T> expectedItems,
        IReadOnlyList<T> actualItems,
        Func<T, string> keySelector,
        Action<T, T, List<ManifestDiffFinding>> compareItem,
        List<ManifestDiffFinding> findings)
    {
        Dictionary<string, T> expectedMap = new(StringComparer.Ordinal);
        foreach (T item in expectedItems)
        {
            string key = keySelector(item);
            if (!expectedMap.ContainsKey(key))
            {
                expectedMap[key] = item;
            }
            else
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Unexpected,
                    section,
                    key,
                    "entry",
                    "unique key",
                    key,
                    $"Duplicate entry '{key}' in expected manifest section '{section}'."));
            }
        }

        Dictionary<string, T> actualMap = new(StringComparer.Ordinal);
        foreach (T item in actualItems)
        {
            string key = keySelector(item);
            if (!actualMap.ContainsKey(key))
            {
                actualMap[key] = item;
            }
            else
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Unexpected,
                    section,
                    key,
                    "entry",
                    "unique key",
                    key,
                    $"Duplicate entry '{key}' in actual manifest section '{section}'."));
            }
        }

        foreach (KeyValuePair<string, T> entry in expectedMap.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!actualMap.TryGetValue(entry.Key, out T? actualItem))
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Missing,
                    section,
                    entry.Key,
                    "entry",
                    entry.Key,
                    string.Empty,
                    $"Missing expected entry '{entry.Key}' in section '{section}'."));
            }
            else
            {
                compareItem(entry.Value, actualItem, findings);
            }
        }

        foreach (KeyValuePair<string, T> entry in actualMap.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!expectedMap.ContainsKey(entry.Key))
            {
                findings.Add(new ManifestDiffFinding(
                    ManifestDiffKind.Unexpected,
                    section,
                    entry.Key,
                    "entry",
                    string.Empty,
                    entry.Key,
                    $"Unexpected entry '{entry.Key}' in section '{section}'."));
            }
        }
    }

    private static void CompareScalar(
        string section,
        string key,
        string property,
        string expected,
        string actual,
        List<ManifestDiffFinding> findings)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Mismatch,
                section,
                key,
                property,
                expected,
                actual,
                $"Expected '{expected}' for {section}.{key}.{property}, found '{actual}'."));
        }
    }

    private static void CompareStringList(
        string section,
        string key,
        string property,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual,
        List<ManifestDiffFinding> findings)
    {
        HashSet<string> expectedSet = new(expected, StringComparer.Ordinal);
        HashSet<string> actualSet = new(actual, StringComparer.Ordinal);

        foreach (string item in expected.Where(e => !actualSet.Contains(e)).OrderBy(e => e, StringComparer.Ordinal))
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Missing,
                section,
                key,
                property,
                item,
                string.Empty,
                $"Missing expected value '{item}' in {section}.{key}.{property}."));
        }

        foreach (string item in actual.Where(a => !expectedSet.Contains(a)).OrderBy(a => a, StringComparer.Ordinal))
        {
            findings.Add(new ManifestDiffFinding(
                ManifestDiffKind.Unexpected,
                section,
                key,
                property,
                string.Empty,
                item,
                $"Unexpected value '{item}' in {section}.{key}.{property}."));
        }
    }
}
