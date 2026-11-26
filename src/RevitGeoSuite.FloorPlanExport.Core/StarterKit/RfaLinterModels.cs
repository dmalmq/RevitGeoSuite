using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>One planar face extracted from a solid inside an <c>.rfa</c> family.</summary>
public sealed class RfaPlanarFaceSnapshot
{
    public RfaPlanarFaceSnapshot(
        double normalX,
        double normalY,
        double normalZ,
        double originZMm,
        double minZMm,
        double maxZMm,
        double areaSqMm = 0.0)
    {
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
        OriginZMm = originZMm;
        MinZMm = minZMm;
        MaxZMm = maxZMm;
        AreaSqMm = areaSqMm;
    }

    public double NormalX { get; }

    public double NormalY { get; }

    public double NormalZ { get; }

    public double OriginZMm { get; }

    public double MinZMm { get; }

    public double MaxZMm { get; }

    public double AreaSqMm { get; }
}

/// <summary>One solid geometry form inspected inside an <c>.rfa</c> family document.</summary>
public sealed class RfaSolidSnapshot
{
    public RfaSolidSnapshot(
        string name,
        string subcategory,
        string materialName,
        bool visibleInCoarse,
        bool visibleInMedium,
        bool visibleInFine,
        double minXMm,
        double maxXMm,
        double minYMm,
        double maxYMm,
        double minZMm,
        double maxZMm,
        int triangleCount,
        IEnumerable<RfaPlanarFaceSnapshot>? planarFaces = null)
    {
        Name = name ?? string.Empty;
        Subcategory = subcategory ?? string.Empty;
        MaterialName = materialName ?? string.Empty;
        VisibleInCoarse = visibleInCoarse;
        VisibleInMedium = visibleInMedium;
        VisibleInFine = visibleInFine;
        MinXMm = minXMm;
        MaxXMm = maxXMm;
        MinYMm = minYMm;
        MaxYMm = maxYMm;
        MinZMm = minZMm;
        MaxZMm = maxZMm;
        TriangleCount = triangleCount;
        PlanarFaces = Array.AsReadOnly((planarFaces ?? Array.Empty<RfaPlanarFaceSnapshot>()).ToArray());
    }

    public string Name { get; }

    public string Subcategory { get; }

    public string MaterialName { get; }

    public bool VisibleInCoarse { get; }

    public bool VisibleInMedium { get; }

    public bool VisibleInFine { get; }

    public double MinXMm { get; }

    public double MaxXMm { get; }

    public double MinYMm { get; }

    public double MaxYMm { get; }

    public double MinZMm { get; }

    public double MaxZMm { get; }

    public int TriangleCount { get; }

    public IReadOnlyList<RfaPlanarFaceSnapshot> PlanarFaces { get; }
}

/// <summary>One material definition inspected inside an <c>.rfa</c> family document.</summary>
public sealed class RfaMaterialSnapshot
{
    public RfaMaterialSnapshot(string name, int red, int green, int blue, int transparency = 0)
    {
        Name = name ?? string.Empty;
        Red = red;
        Green = green;
        Blue = blue;
        Transparency = transparency;
    }

    public string Name { get; }

    public int Red { get; }

    public int Green { get; }

    public int Blue { get; }

    public int Transparency { get; }
}

/// <summary>One family parameter (shared or family-local) inspected inside an <c>.rfa</c> document.</summary>
public sealed class RfaParameterSnapshot
{
    public RfaParameterSnapshot(
        string name,
        bool isShared,
        Guid? guid,
        bool isInstance,
        string storageKind = "Text",
        string? defaultValue = null,
        double? numericValueMm = null)
    {
        Name = name ?? string.Empty;
        IsShared = isShared;
        Guid = guid;
        IsInstance = isInstance;
        StorageKind = storageKind ?? string.Empty;
        DefaultValue = defaultValue ?? string.Empty;
        NumericValueMm = numericValueMm;
    }

    public string Name { get; }

    public bool IsShared { get; }

    public Guid? Guid { get; }

    public bool IsInstance { get; }

    public string StorageKind { get; }

    public string DefaultValue { get; }

    /// <summary>Length/dimensional value in millimetres when the parameter stores a length.</summary>
    public double? NumericValueMm { get; }
}

/// <summary>One nested family instance or loaded nested family inspected inside an <c>.rfa</c>.</summary>
public sealed class RfaNestedFamilySnapshot
{
    public RfaNestedFamilySnapshot(string familyName, int nestingDepth, string category = "")
    {
        FamilyName = familyName ?? string.Empty;
        NestingDepth = nestingDepth;
        Category = category ?? string.Empty;
    }

    public string FamilyName { get; }

    public int NestingDepth { get; }

    public string Category { get; }
}

/// <summary>
/// Pure-C# snapshot of an inspected Revit <c>.rfa</c> family (produced either by <c>RevitRfaInspector</c>
/// from a live Revit <c>Document</c> or by <see cref="FamilyGeometryPlanBuilder"/> for offline/Linux verification).
/// </summary>
public sealed class RfaInspectionSnapshot
{
    public RfaInspectionSnapshot(
        string familyName,
        string category,
        string? seedKind,
        long? fileSizeBytes,
        IEnumerable<string> subcategories,
        IEnumerable<RfaSolidSnapshot> solids,
        IEnumerable<RfaMaterialSnapshot> materials,
        IEnumerable<RfaParameterSnapshot> parameters,
        int coarseTriangles,
        int mediumFineTriangles,
        int importedCadCount = 0,
        int embeddedImageCount = 0,
        IEnumerable<RfaNestedFamilySnapshot>? nestedFamilies = null,
        int maxNestingDepth = 0,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? typeParameterValues = null)
    {
        FamilyName = familyName ?? string.Empty;
        Category = category ?? string.Empty;
        SeedKind = seedKind ?? string.Empty;
        FileSizeBytes = fileSizeBytes;
        Subcategories = Array.AsReadOnly(
            (subcategories ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray());
        Solids = Array.AsReadOnly((solids ?? Array.Empty<RfaSolidSnapshot>()).ToArray());
        Materials = Array.AsReadOnly((materials ?? Array.Empty<RfaMaterialSnapshot>()).ToArray());
        Parameters = Array.AsReadOnly((parameters ?? Array.Empty<RfaParameterSnapshot>()).ToArray());
        CoarseTriangles = coarseTriangles;
        MediumFineTriangles = mediumFineTriangles;
        ImportedCadCount = importedCadCount;
        EmbeddedImageCount = embeddedImageCount;
        NestedFamilies = Array.AsReadOnly((nestedFamilies ?? Array.Empty<RfaNestedFamilySnapshot>()).ToArray());
        MaxNestingDepth = NestedFamilies.Count == 0
            ? maxNestingDepth
            : maxNestingDepth < 0
                ? maxNestingDepth
                : Math.Max(maxNestingDepth, NestedFamilies.Max(n => n.NestingDepth));

        Dictionary<string, IReadOnlyDictionary<string, string>> typesCopy = new(StringComparer.Ordinal);
        if (typeParameterValues != null)
        {
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> kv in typeParameterValues)
            {
                Dictionary<string, string> inner = new(StringComparer.Ordinal);
                if (kv.Value != null)
                {
                    foreach (KeyValuePair<string, string> pv in kv.Value)
                    {
                        inner[pv.Key] = pv.Value ?? string.Empty;
                    }
                }

                typesCopy[kv.Key] = new ReadOnlyDictionary<string, string>(inner);
            }
        }

        TypeParameterValues = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(typesCopy);
    }

    public string FamilyName { get; }

    public string Category { get; }

    public string SeedKind { get; }

    public long? FileSizeBytes { get; }

    public IReadOnlyList<string> Subcategories { get; }

    public IReadOnlyList<RfaSolidSnapshot> Solids { get; }

    public IReadOnlyList<RfaMaterialSnapshot> Materials { get; }

    public IReadOnlyList<RfaParameterSnapshot> Parameters { get; }

    public int CoarseTriangles { get; }

    public int MediumFineTriangles { get; }

    public int ImportedCadCount { get; }

    public int EmbeddedImageCount { get; }

    public IReadOnlyList<RfaNestedFamilySnapshot> NestedFamilies { get; }

    public int MaxNestingDepth { get; }

    /// <summary>Optional per-family-type map of parameter name -> string value across all types in the .rfa.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TypeParameterValues { get; }

    /// <summary>Projects this inspected family snapshot into a canonical <see cref="ManifestFamilyEntry"/>.</summary>
    public ManifestFamilyEntry ToManifestFamilyEntry(FamilyGeometrySpec spec)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        List<RfaSolidSnapshot> coarseFootprints = Solids
            .Where(s =>
                s.VisibleInCoarse &&
                !s.VisibleInMedium &&
                !s.VisibleInFine &&
                string.Equals(s.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
            .ToList();

        bool hasCoarseKFootprint = coarseFootprints.Count > 0;
        bool requiresDownwardBottom = spec.Coarse != null && spec.Coarse.RequiresDownwardPlanarBottomAtZero;
        bool hasDownwardBottom = hasCoarseKFootprint &&
            requiresDownwardBottom &&
            coarseFootprints.All(s => s.PlanarFaces.Any(RfaLinter.IsValidDownwardBottomFace));

        IEnumerable<Guid> sharedGuids = Parameters
            .Where(p => p.IsShared && p.Guid.HasValue && p.Guid.Value != Guid.Empty)
            .Select(p => p.Guid!.Value);

        return new ManifestFamilyEntry(
            spec.Id,
            FamilyName,
            Category,
            string.IsNullOrWhiteSpace(SeedKind) ? spec.SeedKind : SeedKind,
            spec.IsPolygonal,
            hasCoarseKFootprint,
            hasDownwardBottom,
            Subcategories,
            CoarseTriangles,
            spec.TriangleBudget.CoarseMax,
            MediumFineTriangles,
            spec.TriangleBudget.MediumFineMax,
            sharedGuids);
    }
}

/// <summary>Pass/fail status of an <see cref="RfaLintReport"/> or individual rule result.</summary>
public enum RfaLintStatus
{
    Pass,
    Fail,
}

/// <summary>One structured bilingual finding reported by <see cref="RfaLinter"/>.</summary>
public sealed class RfaLintFinding
{
    public RfaLintFinding(
        string ruleId,
        RuleSeverity severity,
        string subject,
        string messageEn,
        string messageJa)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Severity = severity;
        Subject = subject ?? string.Empty;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
    }

    public string RuleId { get; }

    public RuleSeverity Severity { get; }

    public string Subject { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }

    public override string ToString() => $"[{RuleId}] ({Subject}) {MessageEn}";
}

/// <summary>Per-rule outcome inside an <see cref="RfaLintReport"/>.</summary>
public sealed class RfaLintRuleResult
{
    public RfaLintRuleResult(string ruleId, RfaLintStatus status, IEnumerable<RfaLintFinding> findings)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Status = status;
        Findings = Array.AsReadOnly((findings ?? Array.Empty<RfaLintFinding>()).ToArray());
    }

    public string RuleId { get; }

    public RfaLintStatus Status { get; }

    public IReadOnlyList<RfaLintFinding> Findings { get; }
}

/// <summary>Complete structured report produced by <see cref="RfaLinter.Lint"/>.</summary>
public sealed class RfaLintReport
{
    public RfaLintReport(
        string familyId,
        string familyName,
        IEnumerable<RfaLintRuleResult> ruleResults,
        int coarseTriangles,
        int mediumFineTriangles,
        int estimatedCoarseTriangles,
        int estimatedMediumFineTriangles,
        long? fileSizeBytes)
    {
        FamilyId = familyId ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        RuleResults = Array.AsReadOnly((ruleResults ?? Array.Empty<RfaLintRuleResult>()).ToArray());
        List<RfaLintFinding> allFindings = RuleResults.SelectMany(r => r.Findings).ToList();
        Findings = Array.AsReadOnly(allFindings.ToArray());

        Dictionary<string, IReadOnlyList<RfaLintFinding>> byRule = new(StringComparer.Ordinal);
        foreach (RfaLintRuleResult result in RuleResults)
        {
            byRule[result.RuleId] = result.Findings;
        }

        FindingsByRule = new ReadOnlyDictionary<string, IReadOnlyList<RfaLintFinding>>(byRule);
        Status = allFindings.Any(f => f.Severity == RuleSeverity.Error)
            ? RfaLintStatus.Fail
            : RfaLintStatus.Pass;
        CoarseTriangles = coarseTriangles;
        MediumFineTriangles = mediumFineTriangles;
        EstimatedCoarseTriangles = estimatedCoarseTriangles;
        EstimatedMediumFineTriangles = estimatedMediumFineTriangles;
        FileSizeBytes = fileSizeBytes;

        int failedRules = RuleResults.Count(r => r.Status == RfaLintStatus.Fail);
        SummaryEn = Status == RfaLintStatus.Pass
            ? $"{FamilyName}: PASS ({RuleResults.Count}/{RuleResults.Count} rules passed; Coarse={CoarseTriangles} tris, Medium/Fine={MediumFineTriangles} tris)."
            : $"{FamilyName}: FAIL ({failedRules}/{RuleResults.Count} rules failed, {Findings.Count} finding(s)).";
        SummaryJa = Status == RfaLintStatus.Pass
            ? $"{FamilyName}: 合格（{RuleResults.Count}/{RuleResults.Count} ルール合格、簡略={CoarseTriangles} 三角形、標準・詳細={MediumFineTriangles} 三角形）。"
            : $"{FamilyName}: 不合格（{failedRules}/{RuleResults.Count} ルール不合格、指摘 {Findings.Count} 件）。";
    }

    public string FamilyId { get; }

    public string FamilyName { get; }

    public RfaLintStatus Status { get; }

    public bool Passed => Status == RfaLintStatus.Pass;

    public IReadOnlyList<RfaLintRuleResult> RuleResults { get; }

    public IReadOnlyList<RfaLintFinding> Findings { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<RfaLintFinding>> FindingsByRule { get; }

    public int CoarseTriangles { get; }

    public int MediumFineTriangles { get; }

    public int EstimatedCoarseTriangles { get; }

    public int EstimatedMediumFineTriangles { get; }

    public long? FileSizeBytes { get; }

    public string SummaryEn { get; }

    public string SummaryJa { get; }

    public bool HasFinding(string ruleId) =>
        FindingsByRule.TryGetValue(ruleId, out IReadOnlyList<RfaLintFinding>? list) && list != null && list.Count > 0;
}
