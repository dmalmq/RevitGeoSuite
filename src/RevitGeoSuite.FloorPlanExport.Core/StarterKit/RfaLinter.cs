using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Deep pure-C# linter evaluating <c>KZ-RFA-001</c> through <c>KZ-RFA-013</c> against an
/// <see cref="RfaInspectionSnapshot"/>, its declarative <see cref="FamilyGeometrySpec"/>,
/// and the surrounding <see cref="StarterKitSpec"/>.
/// </summary>
public static class RfaLinter
{
    public const string RuleFamilyIdentity = "KZ-RFA-001";
    public const string RuleSharedParameters = "KZ-RFA-002";
    public const string RuleSubcategoryVisibility = "KZ-RFA-003";
    public const string RuleCoarseDownwardBottomFace = "KZ-RFA-004";
    public const string RuleAntiZFightingClearances = "KZ-RFA-005";
    public const string RuleTriangleBudgets = "KZ-RFA-006";
    public const string RuleNoImportedCadOrImages = "KZ-RFA-007";
    public const string RuleNoUnapprovedNestedFamilies = "KZ-RFA-008";
    public const string RuleApprovedMaterials = "KZ-RFA-009";
    public const string RuleNoBakedIdentity = "KZ-RFA-010";
    public const string RuleFileSizeBudget = "KZ-RFA-011";
    public const string RuleValidDomainDefaults = "KZ-RFA-012";
    public const string RuleCirculationContract = "KZ-RFA-013";

    public const double BottomFaceZToleranceMm = 1.0;
    public const double DownwardNormalZThreshold = -0.9;
    public const double ClearanceEpsilonMm = 0.05;
    public const int MaxAllowedNestingDepth = 1;

    public static readonly IReadOnlyList<string> AllRuleIds = Array.AsReadOnly(new[]
    {
        RuleFamilyIdentity,
        RuleSharedParameters,
        RuleSubcategoryVisibility,
        RuleCoarseDownwardBottomFace,
        RuleAntiZFightingClearances,
        RuleTriangleBudgets,
        RuleNoImportedCadOrImages,
        RuleNoUnapprovedNestedFamilies,
        RuleApprovedMaterials,
        RuleNoBakedIdentity,
        RuleFileSizeBudget,
        RuleValidDomainDefaults,
        RuleCirculationContract,
    });

    public static readonly IReadOnlyList<string> ApprovedMediumFineSubcategories = Array.AsReadOnly(new[]
    {
        "K_Body",
        "K_Glass",
        "K_Accent",
        "K_Frame",
    });

    private static readonly HashSet<string> ApprovedMediumFineSubcategorySet =
        new(ApprovedMediumFineSubcategories, StringComparer.Ordinal);

    private static readonly string[] ForbiddenIdentityParameters =
    {
        GisSharedParameters.Id,
        GisSharedParameters.LevelId,
        GisSharedParameters.GisKey,
        "ModelId",
        "GisStationFolder",
    };

    /// <summary>Evaluates all 13 deep <c>KZ-RFA-001…013</c> rules on an inspected family snapshot.</summary>
    public static RfaLintReport Lint(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec familySpec,
        StarterKitSpec kitSpec,
        StarterKitReferenceStandards? standards = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        FamilyTriangleEstimate estimate = TriangleEstimator.EstimateFamily(familySpec);

        Dictionary<string, List<RfaLintFinding>> bucket = AllRuleIds.ToDictionary(
            id => id,
            _ => new List<RfaLintFinding>(),
            StringComparer.Ordinal);

        EvaluateRule001Identity(snapshot, familySpec, refStandards, bucket[RuleFamilyIdentity]);
        EvaluateRule002SharedParameters(snapshot, familySpec, refStandards, bucket[RuleSharedParameters]);
        EvaluateRule003Subcategories(snapshot, familySpec, bucket[RuleSubcategoryVisibility]);
        EvaluateRule004DownwardBottomFace(snapshot, familySpec, bucket[RuleCoarseDownwardBottomFace]);
        EvaluateRule005Clearances(snapshot, familySpec, bucket[RuleAntiZFightingClearances]);
        EvaluateRule006Triangles(snapshot, familySpec, kitSpec, estimate, bucket[RuleTriangleBudgets]);
        EvaluateRule007NoCadOrImages(snapshot, bucket[RuleNoImportedCadOrImages]);
        EvaluateRule008NoUnapprovedNesting(snapshot, kitSpec, bucket[RuleNoUnapprovedNestedFamilies]);
        EvaluateRule009Materials(snapshot, kitSpec, bucket[RuleApprovedMaterials]);
        EvaluateRule010NoBakedIdentity(snapshot, bucket[RuleNoBakedIdentity]);
        EvaluateRule011FileSize(snapshot, kitSpec, bucket[RuleFileSizeBudget]);
        EvaluateRule012DomainDefaults(snapshot, familySpec, refStandards, bucket[RuleValidDomainDefaults]);
        EvaluateRule013CirculationContract(snapshot, familySpec, refStandards, bucket[RuleCirculationContract]);

        List<RfaLintRuleResult> ruleResults = new(AllRuleIds.Count);
        foreach (string ruleId in AllRuleIds)
        {
            List<RfaLintFinding> findings = bucket[ruleId];
            RfaLintStatus status = findings.Any(f => f.Severity == RuleSeverity.Error)
                ? RfaLintStatus.Fail
                : RfaLintStatus.Pass;
            ruleResults.Add(new RfaLintRuleResult(ruleId, status, findings));
        }

        return new RfaLintReport(
            familySpec.Id,
            snapshot.FamilyName,
            ruleResults,
            snapshot.CoarseTriangles,
            snapshot.MediumFineTriangles,
            estimate.CoarseTriangles,
            estimate.MediumFineTriangles,
            snapshot.FileSizeBytes);
    }

    private static void EvaluateRule001Identity(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(snapshot.FamilyName) ||
            !string.Equals(snapshot.FamilyName, spec.FamilyName, StringComparison.Ordinal))
        {
            AddError(
                findings,
                RuleFamilyIdentity,
                "familyName",
                $"Family name '{snapshot.FamilyName}' does not match expected spec familyName '{spec.FamilyName}'.",
                $"ファミリ名 '{snapshot.FamilyName}' が仕様の familyName '{spec.FamilyName}' と一致しません。");
        }

        if (!snapshot.FamilyName.StartsWith("KNZ_", StringComparison.Ordinal))
        {
            AddError(
                findings,
                RuleFamilyIdentity,
                "familyName",
                $"Starter Kit family name '{snapshot.FamilyName}' must start with 'KNZ_'.",
                $"Starter Kit ファミリ名 '{snapshot.FamilyName}' は 'KNZ_' で始まる必要があります。");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Category) ||
            !snapshot.Category.StartsWith("OST_", StringComparison.Ordinal))
        {
            AddError(
                findings,
                RuleFamilyIdentity,
                "category",
                $"Family category '{snapshot.Category}' must be a valid Revit BuiltInCategory (OST_*).",
                $"ファミリカテゴリ '{snapshot.Category}' は有効な BuiltInCategory（OST_*）である必要があります。");
        }
        else if (!string.Equals(snapshot.Category, spec.Category, StringComparison.Ordinal))
        {
            AddError(
                findings,
                RuleFamilyIdentity,
                "category",
                $"Family category '{snapshot.Category}' does not match expected spec category '{spec.Category}'.",
                $"ファミリカテゴリ '{snapshot.Category}' が仕様カテゴリ '{spec.Category}' と一致しません。");
        }

        string? gisKind = GetEffectiveDefaultValue(snapshot, spec, GisSharedParameters.GisKind);
        if (!string.IsNullOrWhiteSpace(gisKind) &&
            standards.FamiliesByGisKind.TryGetValue(gisKind!, out IReadOnlySetLike? registered) &&
            registered != null &&
            !registered.Contains(snapshot.FamilyName))
        {
            AddError(
                findings,
                RuleFamilyIdentity,
                "families.json",
                $"Family '{snapshot.FamilyName}' declares gis_kind '{gisKind}' but is not registered under '{gisKind}' in standards/families.json.",
                $"ファミリ '{snapshot.FamilyName}' は gis_kind '{gisKind}' を宣言していますが、standards/families.json の '{gisKind}' に登録されていません。");
        }
    }

    private static void EvaluateRule002SharedParameters(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        Dictionary<string, RfaParameterSnapshot> byName = BuildParameterMap(snapshot);

        foreach (FamilySharedParameterSpec requiredSpec in spec.SharedParameters)
        {
            if (!standards.ParametersByName.TryGetValue(requiredSpec.Name, out GisParameterDefinition? stdDef) || stdDef == null)
            {
                AddError(
                    findings,
                    RuleSharedParameters,
                    requiredSpec.Name,
                    $"Spec parameter '{requiredSpec.Name}' is not defined in Kanizu GisSharedParameters.",
                    $"仕様パラメータ '{requiredSpec.Name}' は Kanizu 共有パラメータ標準に定義されていません。");
                continue;
            }

            if (!byName.TryGetValue(requiredSpec.Name, out RfaParameterSnapshot? actual))
            {
                if (requiredSpec.Required)
                {
                    AddError(
                        findings,
                        RuleSharedParameters,
                        requiredSpec.Name,
                        $"Missing required shared parameter '{requiredSpec.Name}' (GUID {stdDef.Guid:D}, {stdDef.Binding}).",
                        $"必須共有パラメータ '{requiredSpec.Name}'（GUID {stdDef.Guid:D}、{stdDef.Binding}）がバインドされていません。");
                }

                continue;
            }

            ValidateParameterBinding(actual, stdDef, findings);
        }

        // Also validate any additional Kanizu-named parameter that IS present in the family (e.g. optional id/gis_key bound with wrong GUID)
        HashSet<string> checkedNames = new(spec.SharedParameters.Select(p => p.Name), StringComparer.Ordinal);
        foreach (RfaParameterSnapshot param in snapshot.Parameters)
        {
            if (checkedNames.Contains(param.Name))
            {
                continue;
            }

            if (standards.ParametersByName.TryGetValue(param.Name, out GisParameterDefinition? stdDef) && stdDef != null)
            {
                ValidateParameterBinding(param, stdDef, findings);
            }
        }
    }

    private static void ValidateParameterBinding(
        RfaParameterSnapshot actual,
        GisParameterDefinition expected,
        List<RfaLintFinding> findings)
    {
        if (!actual.IsShared || !actual.Guid.HasValue || actual.Guid.Value != expected.Guid)
        {
            string foundGuid = actual.IsShared && actual.Guid.HasValue ? actual.Guid.Value.ToString("D") : "(not shared)";
            AddError(
                findings,
                RuleSharedParameters,
                actual.Name,
                $"Shared parameter '{actual.Name}' has GUID '{foundGuid}', expected frozen GUID '{expected.Guid:D}'.",
                $"共有パラメータ '{actual.Name}' の GUID が '{foundGuid}' です（標準固定 GUID: '{expected.Guid:D}'）。");
        }

        bool expectedInstance = expected.Binding == GisParameterBinding.Instance;
        if (actual.IsInstance != expectedInstance)
        {
            string expectedKind = expectedInstance ? "instance" : "type";
            string actualKind = actual.IsInstance ? "instance" : "type";
            AddError(
                findings,
                RuleSharedParameters,
                actual.Name,
                $"Shared parameter '{actual.Name}' is bound as {actualKind}, expected {expectedKind}.",
                $"共有パラメータ '{actual.Name}' が {actualKind} バインドになっています（期待: {expectedKind}）。");
        }
    }

    private static void EvaluateRule003Subcategories(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        List<RfaLintFinding> findings)
    {
        bool requireCoarse = spec.IsPolygonal || spec.Coarse != null;
        HashSet<string> declaredSubcategories = new(spec.Subcategories, StringComparer.Ordinal);

        if (requireCoarse && !snapshot.Subcategories.Contains(CoarseGeometrySpec.FootprintSubcategory, StringComparer.Ordinal))
        {
            AddError(
                findings,
                RuleSubcategoryVisibility,
                CoarseGeometrySpec.FootprintSubcategory,
                $"Family '{snapshot.FamilyName}' is missing required subcategory '{CoarseGeometrySpec.FootprintSubcategory}'.",
                $"ファミリ '{snapshot.FamilyName}' に必須サブカテゴリ '{CoarseGeometrySpec.FootprintSubcategory}' がありません。");
        }

        int coarseFootprintSolids = 0;
        int mediumFineSolids = 0;

        foreach (RfaSolidSnapshot solid in snapshot.Solids)
        {
            bool isFootprint = string.Equals(solid.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal);
            if (isFootprint)
            {
                if (!solid.VisibleInCoarse || solid.VisibleInMedium || solid.VisibleInFine)
                {
                    AddError(
                        findings,
                        RuleSubcategoryVisibility,
                        solid.Name,
                        $"K_Footprint solid '{solid.Name}' must be Coarse-only (Coarse=true, Medium=false, Fine=false), found Coarse={solid.VisibleInCoarse}, Medium={solid.VisibleInMedium}, Fine={solid.VisibleInFine}.",
                        $"K_Footprint ソリッド '{solid.Name}' は簡略のみ表示である必要があります（現在: Coarse={solid.VisibleInCoarse}, Medium={solid.VisibleInMedium}, Fine={solid.VisibleInFine}）。");
                }
                else
                {
                    coarseFootprintSolids++;
                }

                continue;
            }

            if (solid.VisibleInCoarse)
            {
                AddError(
                    findings,
                    RuleSubcategoryVisibility,
                    solid.Name,
                    $"Solid '{solid.Name}' on subcategory '{solid.Subcategory}' must NOT be visible in Coarse detail level (Coarse is reserved for K_Footprint).",
                    $"サブカテゴリ '{solid.Subcategory}' のソリッド '{solid.Name}' は簡略表示で非表示にする必要があります（簡略表示は K_Footprint 専用）。");
            }

            if (string.IsNullOrWhiteSpace(solid.Subcategory) || !ApprovedMediumFineSubcategorySet.Contains(solid.Subcategory))
            {
                AddError(
                    findings,
                    RuleSubcategoryVisibility,
                    solid.Name,
                    $"Medium/Fine solid '{solid.Name}' uses unapproved subcategory '{solid.Subcategory}'. Allowed: {string.Join(", ", ApprovedMediumFineSubcategories)}.",
                    $"標準・詳細ソリッド '{solid.Name}' に未承認のサブカテゴリ '{solid.Subcategory}' が設定されています（許可: {string.Join(", ", ApprovedMediumFineSubcategories)}）。");
            }
            else if (declaredSubcategories.Count > 0 && !declaredSubcategories.Contains(solid.Subcategory))
            {
                AddError(
                    findings,
                    RuleSubcategoryVisibility,
                    solid.Name,
                    $"Medium/Fine solid '{solid.Name}' uses subcategory '{solid.Subcategory}' which is not declared in spec subcategories.",
                    $"標準・詳細ソリッド '{solid.Name}' のサブカテゴリ '{solid.Subcategory}' は仕様のサブカテゴリ一覧に宣言されていません。");
            }

            if (!solid.VisibleInMedium && !solid.VisibleInFine)
            {
                AddError(
                    findings,
                    RuleSubcategoryVisibility,
                    solid.Name,
                    $"Medium/Fine solid '{solid.Name}' is hidden in both Medium and Fine detail levels.",
                    $"標準・詳細ソリッド '{solid.Name}' が標準・詳細の両方で非表示になっています。");
            }
            else
            {
                mediumFineSolids++;
            }
        }

        if (requireCoarse && coarseFootprintSolids == 0)
        {
            AddError(
                findings,
                RuleSubcategoryVisibility,
                CoarseGeometrySpec.FootprintSubcategory,
                $"Family '{snapshot.FamilyName}' has no Coarse-only K_Footprint solid.",
                $"ファミリ '{snapshot.FamilyName}' に簡略専用の K_Footprint ソリッドが存在しません。");
        }

        if (mediumFineSolids == 0)
        {
            AddError(
                findings,
                RuleSubcategoryVisibility,
                "mediumFine",
                $"Family '{snapshot.FamilyName}' has no Medium/Fine massing solids.",
                $"ファミリ '{snapshot.FamilyName}' に標準・詳細用の 3D ソリッドが存在しません。");
        }
    }

    private static void EvaluateRule004DownwardBottomFace(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        List<RfaLintFinding> findings)
    {
        bool requireDownwardBottom = spec.IsPolygonal ||
            (spec.Coarse != null && spec.Coarse.RequiresDownwardPlanarBottomAtZero);
        if (!requireDownwardBottom)
        {
            return;
        }

        List<RfaSolidSnapshot> coarseFootprints = snapshot.Solids
            .Where(s => s.VisibleInCoarse &&
                        string.Equals(s.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
            .ToList();

        if (coarseFootprints.Count == 0)
        {
            AddError(
                findings,
                RuleCoarseDownwardBottomFace,
                CoarseGeometrySpec.FootprintSubcategory,
                $"Polygonal family '{snapshot.FamilyName}' has no Coarse K_Footprint solid to verify downward PlanarFace at Z=0.",
                $"ポリゴン対象ファミリ '{snapshot.FamilyName}' に Z=0 下向き PlanarFace を検証できる簡略 K_Footprint ソリッドがありません。");
            return;
        }

        foreach (RfaSolidSnapshot solid in coarseFootprints)
        {
            bool hasValidBottom = solid.PlanarFaces.Any(face =>
                face.NormalZ < DownwardNormalZThreshold &&
                Math.Abs(face.OriginZMm) <= BottomFaceZToleranceMm &&
                Math.Abs(face.MinZMm) <= BottomFaceZToleranceMm &&
                Math.Abs(face.MaxZMm) <= BottomFaceZToleranceMm);

            if (!hasValidBottom)
            {
                RfaPlanarFaceSnapshot? closestFace = solid.PlanarFaces
                    .OrderBy(f => f.NormalZ)
                    .ThenBy(f => Math.Abs(f.OriginZMm))
                    .FirstOrDefault();

                string detailEn = closestFace == null
                    ? "no PlanarFaces were found."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "best candidate face had Normal.Z={0:0.###} (requires < -0.9) and Z={1:0.##} mm (requires |Z| <= 1 mm).",
                        closestFace.NormalZ,
                        closestFace.OriginZMm);

                string detailJa = closestFace == null
                    ? "PlanarFace が見つかりません。"
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "最良候補面の Normal.Z={0:0.###}（条件: < -0.9）、Z={1:0.##} mm（条件: |Z| <= 1 mm）。",
                        closestFace.NormalZ,
                        closestFace.OriginZMm);

                AddError(
                    findings,
                    RuleCoarseDownwardBottomFace,
                    solid.Name,
                    $"Coarse K_Footprint solid '{solid.Name}' violates the Z=0 downward PlanarFace contract: {detailEn}",
                    $"簡略 K_Footprint ソリッド '{solid.Name}' が Z=0 下向き PlanarFace 契約に違反しています: {detailJa}");
            }
        }
    }

    private static void EvaluateRule005Clearances(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        List<RfaLintFinding> findings)
    {
        double slabClearMm = Math.Max(AntiZFightingClearances.MinimumSlabClearMm, spec.ClearancesMm.SlabClearMm);
        double sideClearMm = Math.Max(AntiZFightingClearances.MinimumSideClearMm, spec.ClearancesMm.SideClearMm);
        double glassTuckMm = Math.Max(AntiZFightingClearances.MinimumGlassTuckMm, spec.ClearancesMm.GlassTuckMm);

        List<RfaSolidSnapshot> coarseFootprints = snapshot.Solids
            .Where(s => s.VisibleInCoarse &&
                        string.Equals(s.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
            .ToList();

        double envMinX = coarseFootprints.Count > 0 ? coarseFootprints.Min(s => s.MinXMm) : 0.0;
        double envMaxX = coarseFootprints.Count > 0 ? coarseFootprints.Max(s => s.MaxXMm) : spec.DefaultDimensionsMm.WidthMm;
        double envMinY = coarseFootprints.Count > 0 ? coarseFootprints.Min(s => s.MinYMm) : 0.0;
        double envMaxY = coarseFootprints.Count > 0 ? coarseFootprints.Max(s => s.MaxYMm) : spec.DefaultDimensionsMm.DepthMm;

        List<RfaSolidSnapshot> mediumFineSolids = snapshot.Solids
            .Where(s => !string.Equals(s.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
            .ToList();

        foreach (RfaSolidSnapshot solid in mediumFineSolids)
        {
            if (solid.MinZMm + ClearanceEpsilonMm < slabClearMm)
            {
                AddError(
                    findings,
                    RuleAntiZFightingClearances,
                    solid.Name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Medium/Fine solid '{0}' bottom Z ({1:0.##} mm) violates slab anti-Z-fighting clearance ({2:0.##} mm).",
                        solid.Name,
                        solid.MinZMm,
                        slabClearMm),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "標準・詳細ソリッド '{0}' の底面 Z（{1:0.##} mm）がスラブ Z ファイティング防止クリアランス（{2:0.##} mm）を下回っています。",
                        solid.Name,
                        solid.MinZMm,
                        slabClearMm));
            }

            bool isGlass = string.Equals(solid.Subcategory, "K_Glass", StringComparison.Ordinal);
            double requiredEdgeInset = isGlass ? sideClearMm + glassTuckMm : sideClearMm;

            double minClearX = solid.MinXMm - envMinX;
            double maxClearX = envMaxX - solid.MaxXMm;
            double minClearY = solid.MinYMm - envMinY;
            double maxClearY = envMaxY - solid.MaxYMm;
            double worstSideClear = Math.Min(Math.Min(minClearX, maxClearX), Math.Min(minClearY, maxClearY));

            if (worstSideClear + ClearanceEpsilonMm < requiredEdgeInset)
            {
                if (isGlass)
                {
                    AddError(
                        findings,
                        RuleAntiZFightingClearances,
                        solid.Name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "K_Glass solid '{0}' edge inset ({1:0.##} mm) violates side+glass-tuck anti-Z-fighting clearance ({2:0.##} mm = {3:0.##} mm side + {4:0.##} mm tuck).",
                            solid.Name,
                            worstSideClear,
                            requiredEdgeInset,
                            sideClearMm,
                            glassTuckMm),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "K_Glass ソリッド '{0}' の外周クリアランス（{1:0.##} mm）が側面＋ガラス引き込み基準（{2:0.##} mm）を満たしていません。",
                            solid.Name,
                            worstSideClear,
                            requiredEdgeInset));
                }
                else
                {
                    AddError(
                        findings,
                        RuleAntiZFightingClearances,
                        solid.Name,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Medium/Fine solid '{0}' side inset ({1:0.##} mm) violates side anti-Z-fighting clearance ({2:0.##} mm).",
                            solid.Name,
                            worstSideClear,
                            sideClearMm),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "標準・詳細ソリッド '{0}' の側面クリアランス（{1:0.##} mm）が側面 Z ファイティング防止クリアランス（{2:0.##} mm）を下回っています。",
                            solid.Name,
                            worstSideClear,
                            sideClearMm));
                }
            }
        }
    }

    private static void EvaluateRule006Triangles(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        StarterKitSpec kitSpec,
        FamilyTriangleEstimate estimate,
        List<RfaLintFinding> findings)
    {
        bool requireCoarse = spec.IsPolygonal || spec.Coarse != null;
        int coarseCeiling = Math.Min(spec.TriangleBudget.CoarseMax, kitSpec.Header.Budgets.MaxCoarseTrianglesPerFamily);
        int mediumFineCeiling = Math.Min(spec.TriangleBudget.MediumFineMax, kitSpec.Header.Budgets.MaxMediumTrianglesPerFamily);

        if (!estimate.CoarseWithinBudget || estimate.CoarseTriangles > kitSpec.Header.Budgets.MaxCoarseTrianglesPerFamily)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "spec.coarseTriangles",
                $"Spec estimated Coarse triangles ({estimate.CoarseTriangles}) exceed ceiling ({coarseCeiling}).",
                $"仕様の簡略推定三角形数（{estimate.CoarseTriangles}）が上限（{coarseCeiling}）を超えています。");
        }

        if (!estimate.MediumFineWithinBudget || estimate.MediumFineTriangles > kitSpec.Header.Budgets.MaxMediumTrianglesPerFamily)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "spec.mediumFineTriangles",
                $"Spec estimated Medium/Fine triangles ({estimate.MediumFineTriangles}) exceed ceiling ({mediumFineCeiling}).",
                $"仕様の標準・詳細推定三角形数（{estimate.MediumFineTriangles}）が上限（{mediumFineCeiling}）を超えています。");
        }

        if (requireCoarse && snapshot.CoarseTriangles <= 0)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "coarseTriangles",
                $"Coarse triangle count ({snapshot.CoarseTriangles}) must be > 0 when Coarse footprint geometry is required.",
                $"簡略フットプリントが必要なファミリの簡略三角形数（{snapshot.CoarseTriangles}）は 0 より大きい必要があります。");
        }
        else if (snapshot.CoarseTriangles < 0 || snapshot.CoarseTriangles > coarseCeiling)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "coarseTriangles",
                $"Actual Coarse triangle count ({snapshot.CoarseTriangles}) exceeds budget ({coarseCeiling}).",
                $"実測の簡略三角形数（{snapshot.CoarseTriangles}）が上限（{coarseCeiling}）を超えています。");
        }

        if (snapshot.MediumFineTriangles <= 0)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "mediumFineTriangles",
                $"Medium/Fine triangle count ({snapshot.MediumFineTriangles}) must be > 0.",
                $"標準・詳細三角形数（{snapshot.MediumFineTriangles}）は 0 より大きい必要があります。");
        }
        else if (snapshot.MediumFineTriangles > mediumFineCeiling)
        {
            AddError(
                findings,
                RuleTriangleBudgets,
                "mediumFineTriangles",
                $"Actual Medium/Fine triangle count ({snapshot.MediumFineTriangles}) exceeds budget ({mediumFineCeiling}).",
                $"実測の標準・詳細三角形数（{snapshot.MediumFineTriangles}）が上限（{mediumFineCeiling}）を超えています。");
        }
    }

    private static void EvaluateRule007NoCadOrImages(
        RfaInspectionSnapshot snapshot,
        List<RfaLintFinding> findings)
    {
        if (snapshot.ImportedCadCount > 0)
        {
            AddError(
                findings,
                RuleNoImportedCadOrImages,
                "ImportInstance",
                $"Family '{snapshot.FamilyName}' contains {snapshot.ImportedCadCount} imported CAD/DWG/mesh instance(s); zero allowed.",
                $"ファミリ '{snapshot.FamilyName}' に {snapshot.ImportedCadCount} 件のインポート CAD/DWG/メッシュが含まれています（0 件必須）。");
        }

        if (snapshot.EmbeddedImageCount > 0)
        {
            AddError(
                findings,
                RuleNoImportedCadOrImages,
                "ImageType",
                $"Family '{snapshot.FamilyName}' contains {snapshot.EmbeddedImageCount} embedded raster image(s); zero allowed.",
                $"ファミリ '{snapshot.FamilyName}' に {snapshot.EmbeddedImageCount} 件の埋め込みラスター画像が含まれています（0 件必須）。");
        }
    }

    private static void EvaluateRule008NoUnapprovedNesting(
        RfaInspectionSnapshot snapshot,
        StarterKitSpec kitSpec,
        List<RfaLintFinding> findings)
    {
        HashSet<string> approvedStarterFamilies = new(
            kitSpec.Header.Families.Select(f => f.Id),
            StringComparer.Ordinal);

        if (snapshot.MaxNestingDepth > MaxAllowedNestingDepth)
        {
            AddError(
                findings,
                RuleNoUnapprovedNestedFamilies,
                "maxNestingDepth",
                $"Family '{snapshot.FamilyName}' nesting depth ({snapshot.MaxNestingDepth}) exceeds maximum allowed depth ({MaxAllowedNestingDepth}).",
                $"ファミリ '{snapshot.FamilyName}' のネスト階層（{snapshot.MaxNestingDepth}）が許容上限（{MaxAllowedNestingDepth}）を超えています。");
        }

        foreach (RfaNestedFamilySnapshot nested in snapshot.NestedFamilies)
        {
            bool isStarter = nested.FamilyName.StartsWith("KNZ_", StringComparison.Ordinal) &&
                             approvedStarterFamilies.Contains(nested.FamilyName);
            if (!isStarter)
            {
                AddError(
                    findings,
                    RuleNoUnapprovedNestedFamilies,
                    nested.FamilyName,
                    $"Family '{snapshot.FamilyName}' embeds unapproved non-starter nested family '{nested.FamilyName}'.",
                    $"ファミリ '{snapshot.FamilyName}' に未承認の非 Starter Kit ネストファミリ '{nested.FamilyName}' が埋め込まれています。");
            }

            if (nested.NestingDepth > MaxAllowedNestingDepth)
            {
                AddError(
                    findings,
                    RuleNoUnapprovedNestedFamilies,
                    nested.FamilyName,
                    $"Nested family '{nested.FamilyName}' is at nesting depth {nested.NestingDepth} (max allowed: {MaxAllowedNestingDepth}).",
                    $"ネストファミリ '{nested.FamilyName}' の階層 {nested.NestingDepth} が許容上限（{MaxAllowedNestingDepth}）を超えています。");
            }
        }
    }

    private static void EvaluateRule009Materials(
        RfaInspectionSnapshot snapshot,
        StarterKitSpec kitSpec,
        List<RfaLintFinding> findings)
    {
        Dictionary<string, Material3DSpec> catalog = kitSpec.Materials3D.Materials
            .ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);
        Dictionary<string, RfaMaterialSnapshot> inspectedMaterials = new(StringComparer.Ordinal);
        foreach (RfaMaterialSnapshot mat in snapshot.Materials)
        {
            if (!inspectedMaterials.ContainsKey(mat.Name))
            {
                inspectedMaterials[mat.Name] = mat;
            }
        }

        foreach (RfaSolidSnapshot solid in snapshot.Solids)
        {
            if (string.IsNullOrWhiteSpace(solid.MaterialName))
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    solid.Name,
                    $"Solid '{solid.Name}' has no assigned material (unnamed default grey material is forbidden).",
                    $"ソリッド '{solid.Name}' にマテリアルが割り当てられていません（未指定デフォルト灰色マテリアルは禁止）。");
                continue;
            }

            if (!catalog.TryGetValue(solid.MaterialName, out Material3DSpec? expectedMat) || expectedMat == null)
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    solid.Name,
                    $"Solid '{solid.Name}' uses material '{solid.MaterialName}' which is not defined in materials-3d.json (K3D_*/M3D_*).",
                    $"ソリッド '{solid.Name}' のマテリアル '{solid.MaterialName}' は materials-3d.json（K3D_*/M3D_*）に定義されていません。");
                continue;
            }

            if (!inspectedMaterials.TryGetValue(solid.MaterialName, out RfaMaterialSnapshot? actualMat) || actualMat == null)
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    solid.MaterialName,
                    $"Material '{solid.MaterialName}' referenced by solid '{solid.Name}' is missing from family material table.",
                    $"ソリッド '{solid.Name}' が参照するマテリアル '{solid.MaterialName}' がファミリのマテリアル一覧に存在しません。");
            }
        }

        foreach (RfaMaterialSnapshot mat in snapshot.Materials)
        {
            if (!catalog.TryGetValue(mat.Name, out Material3DSpec? expectedMat) || expectedMat == null)
            {
                // Ignore unrelated built-in template materials if no solid uses them, unless they pretend to be K3D_/M3D_.
                if (mat.Name.StartsWith("K3D_", StringComparison.Ordinal) ||
                    mat.Name.StartsWith("M3D_", StringComparison.Ordinal))
                {
                    AddError(
                        findings,
                        RuleApprovedMaterials,
                        mat.Name,
                        $"Material '{mat.Name}' is not registered in materials-3d.json.",
                        $"マテリアル '{mat.Name}' は materials-3d.json に登録されていません。");
                }

                continue;
            }

            if (mat.Red < 0 || mat.Red > 255 || mat.Green < 0 || mat.Green > 255 || mat.Blue < 0 || mat.Blue > 255)
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    mat.Name,
                    $"Material '{mat.Name}' has out-of-range sRGB [{mat.Red}, {mat.Green}, {mat.Blue}].",
                    $"マテリアル '{mat.Name}' の sRGB 値 [{mat.Red}, {mat.Green}, {mat.Blue}] が範囲外です。");
                continue;
            }

            if (expectedMat.Srgb.Count == 3 &&
                (mat.Red != expectedMat.Srgb[0] || mat.Green != expectedMat.Srgb[1] || mat.Blue != expectedMat.Srgb[2]))
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    mat.Name,
                    $"Material '{mat.Name}' sRGB [{mat.Red}, {mat.Green}, {mat.Blue}] does not match materials-3d.json [{expectedMat.Srgb[0]}, {expectedMat.Srgb[1]}, {expectedMat.Srgb[2]}].",
                    $"マテリアル '{mat.Name}' の sRGB 値 [{mat.Red}, {mat.Green}, {mat.Blue}] が materials-3d.json の定義値 [{expectedMat.Srgb[0]}, {expectedMat.Srgb[1]}, {expectedMat.Srgb[2]}] と一致しません。");
            }

            if (mat.Transparency != expectedMat.Transparency)
            {
                AddError(
                    findings,
                    RuleApprovedMaterials,
                    mat.Name,
                    $"Material '{mat.Name}' transparency ({mat.Transparency}) does not match materials-3d.json ({expectedMat.Transparency}).",
                    $"マテリアル '{mat.Name}' の透過度（{mat.Transparency}）が materials-3d.json の定義値（{expectedMat.Transparency}）と一致しません。");
            }
        }
    }

    private static void EvaluateRule010NoBakedIdentity(
        RfaInspectionSnapshot snapshot,
        List<RfaLintFinding> findings)
    {
        HashSet<string> forbidden = new(ForbiddenIdentityParameters, StringComparer.OrdinalIgnoreCase);

        foreach (RfaParameterSnapshot param in snapshot.Parameters)
        {
            if (forbidden.Contains(param.Name) && GisStandardDomains.HasValue(param.DefaultValue))
            {
                AddError(
                    findings,
                    RuleNoBakedIdentity,
                    param.Name,
                    $"Identity parameter '{param.Name}' must be blank in the .rfa template, found baked value '{param.DefaultValue}'.",
                    $"識別子パラメータ '{param.Name}' は .rfa 内で空である必要があります（固定値 '{param.DefaultValue}' が設定されています）。");
            }
        }

        foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> typeEntry in snapshot.TypeParameterValues)
        {
            foreach (KeyValuePair<string, string> pv in typeEntry.Value)
            {
                if (forbidden.Contains(pv.Key) && GisStandardDomains.HasValue(pv.Value))
                {
                    AddError(
                        findings,
                        RuleNoBakedIdentity,
                        $"{typeEntry.Key}.{pv.Key}",
                        $"Family type '{typeEntry.Key}' has baked identity value '{pv.Value}' in parameter '{pv.Key}'.",
                        $"ファミリタイプ '{typeEntry.Key}' のパラメータ '{pv.Key}' に固定識別子値 '{pv.Value}' が埋め込まれています。");
                }
            }
        }
    }

    private static void EvaluateRule011FileSize(
        RfaInspectionSnapshot snapshot,
        StarterKitSpec kitSpec,
        List<RfaLintFinding> findings)
    {
        if (!snapshot.FileSizeBytes.HasValue)
        {
            return;
        }

        long maxBytes = kitSpec.Header.Budgets.MaxRfaFileSizeKb * 1024L;
        long actualBytes = snapshot.FileSizeBytes.Value;

        if (actualBytes <= 0)
        {
            AddError(
                findings,
                RuleFileSizeBudget,
                "fileSizeBytes",
                $"Family .rfa file size ({actualBytes} bytes) must be positive.",
                $".rfa ファイルサイズ（{actualBytes} バイト）は正の値である必要があります。");
        }
        else if (actualBytes > maxBytes)
        {
            AddError(
                findings,
                RuleFileSizeBudget,
                "fileSizeBytes",
                $"Family .rfa file size ({actualBytes} bytes) exceeds kit.json budget ({maxBytes} bytes / {kitSpec.Header.Budgets.MaxRfaFileSizeKb} KB).",
                $".rfa ファイルサイズ（{actualBytes} バイト）が kit.json 上限（{kitSpec.Header.Budgets.MaxRfaFileSizeKb} KB / {maxBytes} バイト）を超えています。");
        }
    }

    private static void EvaluateRule012DomainDefaults(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        Dictionary<string, RfaParameterSnapshot> byName = BuildParameterMap(snapshot);

        // Validate every domain parameter value present in the snapshot
        foreach (RfaParameterSnapshot param in snapshot.Parameters)
        {
            if (GisStandardDomains.HasValue(param.DefaultValue))
            {
                ValidateDomainValue(param.Name, param.DefaultValue, param.Name, standards, findings);
            }
        }

        foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> typeEntry in snapshot.TypeParameterValues)
        {
            foreach (KeyValuePair<string, string> pv in typeEntry.Value)
            {
                if (GisStandardDomains.HasValue(pv.Value))
                {
                    ValidateDomainValue(pv.Key, pv.Value, $"{typeEntry.Key}.{pv.Key}", standards, findings);
                }
            }
        }

        // Verify non-empty spec default values match the actual snapshot default
        foreach (FamilySharedParameterSpec paramSpec in spec.SharedParameters)
        {
            if (string.IsNullOrWhiteSpace(paramSpec.DefaultValue))
            {
                continue;
            }

            if (byName.TryGetValue(paramSpec.Name, out RfaParameterSnapshot? actual))
            {
                string actualTrimmed = (actual.DefaultValue ?? string.Empty).Trim();
                if (!string.Equals(actualTrimmed, paramSpec.DefaultValue, StringComparison.Ordinal))
                {
                    AddError(
                        findings,
                        RuleValidDomainDefaults,
                        paramSpec.Name,
                        $"Parameter '{paramSpec.Name}' default value '{actualTrimmed}' does not match spec default '{paramSpec.DefaultValue}'.",
                        $"パラメータ '{paramSpec.Name}' の既定値 '{actualTrimmed}' が仕様既定値 '{paramSpec.DefaultValue}' と一致しません。");
                }
            }
        }
    }

    private static void ValidateDomainValue(
        string paramName,
        string value,
        string subject,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        if (GisStandardDomains.ContainsDisallowedCharacters(value))
        {
            AddError(
                findings,
                RuleValidDomainDefaults,
                subject,
                $"Parameter '{paramName}' contains disallowed control or line-break characters.",
                $"パラメータ '{paramName}' に不正な制御文字または改行文字が含まれています。");
            return;
        }

        string trimmed = value.Trim();

        if (string.Equals(paramName, GisSharedParameters.GisKind, StringComparison.Ordinal))
        {
            if (!standards.GisKinds.Contains(trimmed))
            {
                AddError(
                    findings,
                    RuleValidDomainDefaults,
                    subject,
                    $"Parameter '{paramName}' value '{trimmed}' is not in standards/families.json kinds.",
                    $"パラメータ '{paramName}' の値 '{trimmed}' は standards/families.json の許可リストにありません。");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisCategory, StringComparison.Ordinal) ||
            string.Equals(paramName, GisSharedParameters.Category, StringComparison.Ordinal))
        {
            if (!standards.UnitCategories.Contains(trimmed))
            {
                AddError(
                    findings,
                    RuleValidDomainDefaults,
                    subject,
                    $"Parameter '{paramName}' value '{trimmed}' is not in standards/fields.json unit.category.",
                    $"パラメータ '{paramName}' の値 '{trimmed}' は standards/fields.json の unit.category ドメイン外です。");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisRestriction, StringComparison.Ordinal))
        {
            if (!standards.Restrictions.Contains(trimmed))
            {
                AddError(
                    findings,
                    RuleValidDomainDefaults,
                    subject,
                    $"Parameter '{paramName}' value '{trimmed}' is not in standards/fields.json unit.restrict.",
                    $"パラメータ '{paramName}' の値 '{trimmed}' は standards/fields.json の unit.restrict ドメイン外です。");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisDirection, StringComparison.Ordinal))
        {
            if (!standards.Directions.Contains(trimmed))
            {
                AddError(
                    findings,
                    RuleValidDomainDefaults,
                    subject,
                    $"Parameter '{paramName}' value '{trimmed}' is not in standards/fields.json unit.direction.",
                    $"パラメータ '{paramName}' の値 '{trimmed}' は standards/fields.json の unit.direction ドメイン外です。");
            }

            return;
        }

        if (standards.OpeningDomains.TryGetValue(paramName, out IReadOnlyList<string>? allowed) && allowed != null)
        {
            if (!allowed.Contains(trimmed, StringComparer.Ordinal))
            {
                AddError(
                    findings,
                    RuleValidDomainDefaults,
                    subject,
                    $"Parameter '{paramName}' value '{trimmed}' is not in standards/opening-types.json ({string.Join(", ", allowed)}).",
                    $"パラメータ '{paramName}' の値 '{trimmed}' は standards/opening-types.json の許可ドメイン外です（許可: {string.Join(", ", allowed)}）。");
            }
        }
    }

    private static void EvaluateRule013CirculationContract(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        bool isCirculation =
            spec.Id.StartsWith("KNZ_CIRC_", StringComparison.Ordinal) ||
            string.Equals(spec.Group, "circulation", StringComparison.OrdinalIgnoreCase);
        if (!isCirculation)
        {
            return;
        }

        Dictionary<string, RfaParameterSnapshot> byName = BuildParameterMap(snapshot);

        // Required dimensional parameters on every KNZ_CIRC_* family: Width, Length/Depth, Height (+ Rise for Escalator)
        RequirePositiveDimensionParam(byName, new[] { "Width" }, "Width", findings);
        if (string.Equals(spec.Id, "KNZ_CIRC_Escalator", StringComparison.Ordinal) ||
            string.Equals(spec.Id, "KNZ_CIRC_MovingWalkway", StringComparison.Ordinal))
        {
            RequirePositiveDimensionParam(byName, new[] { "Length", "Depth" }, "Length", findings);
        }
        else
        {
            RequirePositiveDimensionParam(byName, new[] { "Depth", "Length" }, "Depth/Length", findings);
        }

        RequirePositiveDimensionParam(byName, new[] { "Height" }, "Height", findings);

        if (string.Equals(spec.Id, "KNZ_CIRC_Escalator", StringComparison.Ordinal))
        {
            RequirePositiveDimensionParam(byName, new[] { "Rise" }, "Rise", findings);
            RequireCirculationMetadataValue(byName, GisSharedParameters.GisKind, "escalator", findings);
            RequireCirculationDirection(byName, standards, findings);
        }
        else if (string.Equals(spec.Id, "KNZ_CIRC_MovingWalkway", StringComparison.Ordinal))
        {
            RequireCirculationMetadataValue(byName, GisSharedParameters.GisKind, "moving_walkway", findings);
            RequireCirculationDirection(byName, standards, findings);
        }
        else if (string.Equals(spec.Id, "KNZ_CIRC_Elevator", StringComparison.Ordinal))
        {
            RequireCirculationMetadataValue(byName, GisSharedParameters.GisKind, "elevator", findings);
            if (!byName.TryGetValue(GisSharedParameters.GisLevelsServed, out RfaParameterSnapshot? levelsServed) || !levelsServed.IsInstance)
            {
                AddError(
                    findings,
                    RuleCirculationContract,
                    GisSharedParameters.GisLevelsServed,
                    "KNZ_CIRC_Elevator must bind instance parameter 'gis_levels_served'.",
                    "KNZ_CIRC_Elevator にはインスタンスパラメータ 'gis_levels_served' のバインドが必須です。");
            }
        }
        else if (string.Equals(spec.Id, "KNZ_CIRC_ElevatorDoor", StringComparison.Ordinal))
        {
            RequireCirculationMetadataValue(byName, GisSharedParameters.GisDoor, "elevator", findings);
            if (!byName.TryGetValue(GisSharedParameters.GisAccessCon, out RfaParameterSnapshot? accessCon) || accessCon.IsInstance)
            {
                AddError(
                    findings,
                    RuleCirculationContract,
                    GisSharedParameters.GisAccessCon,
                    "KNZ_CIRC_ElevatorDoor must bind type parameter 'gis_access_con'.",
                    "KNZ_CIRC_ElevatorDoor にはタイプパラメータ 'gis_access_con' のバインドが必須です。");
            }

            if (!byName.TryGetValue(GisSharedParameters.GisOpeningCategory, out RfaParameterSnapshot? openCat) || openCat.IsInstance)
            {
                AddError(
                    findings,
                    RuleCirculationContract,
                    GisSharedParameters.GisOpeningCategory,
                    "KNZ_CIRC_ElevatorDoor must bind type parameter 'gis_opening_category'.",
                    "KNZ_CIRC_ElevatorDoor にはタイプパラメータ 'gis_opening_category' のバインドが必須です。");
            }
        }
    }

    private static void RequirePositiveDimensionParam(
        Dictionary<string, RfaParameterSnapshot> byName,
        string[] candidateNames,
        string displayName,
        List<RfaLintFinding> findings)
    {
        foreach (string name in candidateNames)
        {
            if (byName.TryGetValue(name, out RfaParameterSnapshot? param))
            {
                if (param.NumericValueMm.HasValue &&
                    !double.IsNaN(param.NumericValueMm.Value) &&
                    !double.IsInfinity(param.NumericValueMm.Value) &&
                    param.NumericValueMm.Value > 0.0)
                {
                    return;
                }

                AddError(
                    findings,
                    RuleCirculationContract,
                    name,
                    $"Circulation dimensional parameter '{name}' must have a finite positive length in mm.",
                    $"縦動線ファミリの寸法パラメータ '{name}' は正の有限値（mm）である必要があります。");
                return;
            }
        }

        AddError(
            findings,
            RuleCirculationContract,
            displayName,
            $"Circulation family is missing required dimensional parameter '{displayName}'.",
            $"縦動線ファミリに必須寸法パラメータ '{displayName}' が存在しません。");
    }

    private static void RequireCirculationMetadataValue(
        Dictionary<string, RfaParameterSnapshot> byName,
        string paramName,
        string expectedValue,
        List<RfaLintFinding> findings)
    {
        if (!byName.TryGetValue(paramName, out RfaParameterSnapshot? param) ||
            param is null ||
            !string.Equals((param.DefaultValue ?? string.Empty).Trim(), expectedValue, StringComparison.Ordinal))
        {
            string actual = param is null ? "(missing)" : (param.DefaultValue ?? string.Empty);
            AddError(
                findings,
                RuleCirculationContract,
                paramName,
                $"Circulation family requires '{paramName}' = '{expectedValue}', found '{actual}'.",
                $"縦動線ファミリには '{paramName}' = '{expectedValue}' が必要です（現在値: '{actual}'）。");
        }
    }

    private static void RequireCirculationDirection(
        Dictionary<string, RfaParameterSnapshot> byName,
        StarterKitReferenceStandards standards,
        List<RfaLintFinding> findings)
    {
        if (!byName.TryGetValue(GisSharedParameters.GisDirection, out RfaParameterSnapshot? dirParam) || !dirParam.IsInstance)
        {
            AddError(
                findings,
                RuleCirculationContract,
                GisSharedParameters.GisDirection,
                "Directional circulation family must bind instance parameter 'gis_direction'.",
                "方向付き縦動線ファミリにはインスタンスパラメータ 'gis_direction' のバインドが必須です。");
            return;
        }

        string trimmed = (dirParam.DefaultValue ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed) || !standards.Directions.Contains(trimmed))
        {
            AddError(
                findings,
                RuleCirculationContract,
                GisSharedParameters.GisDirection,
                $"Circulation 'gis_direction' default '{trimmed}' must be one of ({string.Join(", ", standards.Directions)}).",
                $"縦動線の 'gis_direction' 既定値 '{trimmed}' は有効な方向値（{string.Join(", ", standards.Directions)}）である必要があります。");
        }
    }

    private static Dictionary<string, RfaParameterSnapshot> BuildParameterMap(RfaInspectionSnapshot snapshot)
    {
        Dictionary<string, RfaParameterSnapshot> map = new(StringComparer.Ordinal);
        foreach (RfaParameterSnapshot param in snapshot.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(param.Name) && !map.ContainsKey(param.Name))
            {
                map[param.Name] = param;
            }
        }

        return map;
    }

    private static string? GetEffectiveDefaultValue(
        RfaInspectionSnapshot snapshot,
        FamilyGeometrySpec spec,
        string paramName)
    {
        RfaParameterSnapshot? snapParam = snapshot.Parameters.FirstOrDefault(
            p => string.Equals(p.Name, paramName, StringComparison.Ordinal));
        if (snapParam != null && !string.IsNullOrWhiteSpace(snapParam.DefaultValue))
        {
            return snapParam.DefaultValue.Trim();
        }

        FamilySharedParameterSpec? specParam = spec.SharedParameters.FirstOrDefault(
            p => string.Equals(p.Name, paramName, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(specParam?.DefaultValue) ? null : specParam!.DefaultValue.Trim();
    }

    private static void AddError(
        List<RfaLintFinding> findings,
        string ruleId,
        string subject,
        string messageEn,
        string messageJa)
    {
        findings.Add(new RfaLintFinding(ruleId, RuleSeverity.Error, subject, messageEn, messageJa));
    }
}
