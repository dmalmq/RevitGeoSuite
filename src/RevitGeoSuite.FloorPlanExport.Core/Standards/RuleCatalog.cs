using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Severity assigned to a <see cref="RuleCatalogEntry"/>.</summary>
public enum RuleSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One stable Kanizu readiness rule, identified by a canonical <c>KZ-*</c> ID.</summary>
public sealed class RuleCatalogEntry
{
    public RuleCatalogEntry(
        string ruleId,
        string group,
        RuleSeverity severity,
        string title,
        string description,
        IReadOnlyList<string>? legacyItemIds = null,
        string? titleJa = null,
        string? descriptionJa = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Severity = severity;
        Title = title ?? string.Empty;
        Description = description ?? string.Empty;
        LegacyItemIds = Array.AsReadOnly((legacyItemIds ?? Array.Empty<string>()).ToArray());
        TitleJa = titleJa ?? string.Empty;
        DescriptionJa = descriptionJa ?? string.Empty;
    }

    /// <summary>Canonical stable id, e.g. <c>KZ-TPL-002</c>.</summary>
    public string RuleId { get; }

    /// <summary>Category/group, e.g. <c>parameters</c>, <c>template</c>, <c>view</c>, <c>family</c>.</summary>
    public string Group { get; }

    public RuleSeverity Severity { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>Japanese title for bilingual readiness/receipt surfaces.</summary>
    public string TitleJa { get; }

    /// <summary>Japanese description for bilingual readiness/receipt surfaces.</summary>
    public string DescriptionJa { get; }

    /// <summary>Legacy Phase 4B ItemIds that resolve to this canonical rule (UI and test back-compat).</summary>
    public IReadOnlyList<string> LegacyItemIds { get; }
}

/// <summary>
/// The stable Kanizu readiness rule catalog. Every <see cref="ReadinessCheckItem"/> emitted by
/// <see cref="ProjectReadinessEvaluator"/> carries a canonical <c>KZ-*</c> <see cref="RuleCatalogEntry.RuleId"/>
/// so handover receipts, dashboards and automation can match rules by a stable identifier that
/// survives UI label changes. Phase 4B legacy ItemIds remain resolvable via <see cref="TryLookupByLegacyItemId"/>.
/// </summary>
public static class RuleCatalog
{
    private static readonly RuleCatalogEntry[] EntriesArray =
    {
        // KZ-PARAM-*: shared parameter bindings
        new RuleCatalogEntry(
            "KZ-PARAM-001",
            "parameters",
            RuleSeverity.Error,
            "Kanizu GIS standard version stamped",
            "gis_standard_version must be stamped on the model at the current standard version.",
            new[] { "standard-version" }),
        new RuleCatalogEntry(
            "KZ-PARAM-002",
            "parameters",
            RuleSeverity.Error,
            "Shared parameters bound with shipped GUIDs",
            "All 19 Kanizu shared parameters must be bound to their expected categories with the shipped GUID and binding (type/instance).",
            new[] { "shared-parameters" }),

        // KZ-TPL-*: template hygiene (.rte) and QA artifacts
        new RuleCatalogEntry(
            "KZ-TPL-001",
            "template",
            RuleSeverity.Error,
            "No baked-in project identity on template",
            "In Template mode the document must not carry a baked-in GeoProjectInfo.ModelId or GisStationFolder; a template is zero-identity by construction.",
            new[] { "template-identity" }),
        new RuleCatalogEntry(
            "KZ-TPL-002",
            "template",
            RuleSeverity.Error,
            "No baked-in element or level identity",
            "In Template mode no level may carry a non-empty level_id and no placed element may carry a baked-in id or gis_key; those are per-project values.",
            new[] { "template-element-ids" }),
        new RuleCatalogEntry(
            "KZ-TPL-003",
            "template",
            RuleSeverity.Error,
            "QA schedules present",
            "The three Kanizu QA schedules (Floors, Circulation, Doors) must exist with their required fields.",
            new[] { "qa-schedule-floors", "qa-schedule-circulation", "qa-schedule-doors" }),

        // KZ-FLOOR-*: floors & materials
        new RuleCatalogEntry(
            "KZ-FLOOR-001",
            "floors",
            RuleSeverity.Error,
            "GIS materials present and on-theme",
            "GIS_* theme materials must be present at the Kanizu theme colour.",
            new[] { "gis-materials" }),
        new RuleCatalogEntry(
            "KZ-FLOOR-002",
            "floors",
            RuleSeverity.Error,
            "Standard floor types",
            "All standard Kanizu floor types must exist and match their gis_category, gis_restriction and material.",
            new[] { "standard-floor-types" }),
        new RuleCatalogEntry(
            "KZ-FLOOR-003",
            "floors",
            RuleSeverity.Warning,
            "In-use floor type categories",
            "In-use floor types must map to a known gis_category (Fix floor types can repair them).",
            new[] { "untyped-floor-types" }),

        // KZ-RFA-*: deep .rfa family linting & loaded family rules (KZ-RFA-001..013)
        new RuleCatalogEntry(
            "KZ-RFA-001",
            "family",
            RuleSeverity.Error,
            "Family name, category and catalog registration",
            "Family name and BuiltInCategory (OST_*) must match the Starter Kit spec (KNZ_* naming) and standards/families.json registration where applicable; loaded circulation families must bind Kanizu shared parameters.",
            new[] { "circulation-families" },
            titleJa: "ファミリ名・カテゴリ・標準カタログ登録の一致",
            descriptionJa: "ファミリ名（KNZ_* 命名）および BuiltInCategory（OST_*）は Starter Kit 仕様と一致し、該当する場合は standards/families.json に登録されている必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-002",
            "family",
            RuleSeverity.Error,
            "Required shared parameters bound with shipped GUIDs",
            "Required Kanizu shared parameters must be bound inside the family with exact frozen GUIDs and type/instance bindings (including OST_Doors gis_door and gis_access_con).",
            new[] { "door-families" },
            titleJa: "必須共有パラメータの標準GUIDおよびタイプ/インスタンスバインド",
            descriptionJa: "必須の Kanizu 共有パラメータは、固定された標準 GUID および正しいタイプ／インスタンス設定でファミリにバインドされている必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-003",
            "family",
            RuleSeverity.Error,
            "Coarse K_Footprint vs Medium/Fine subcategory separation",
            "Coarse geometry must reside exclusively on subcategory K_Footprint (Coarse-only visibility), and Medium/Fine geometry must use only approved K_Body/K_Glass/K_Accent/K_Frame subcategories (never visible in Coarse).",
            titleJa: "詳細レベル（簡略 K_Footprint と標準・詳細サブカテゴリ）の分離",
            descriptionJa: "簡略表示ジオメトリは K_Footprint サブカテゴリ（簡略のみ表示）に限定し、標準・詳細ジオメトリは承認済みサブカテゴリ（K_Body/K_Glass/K_Accent/K_Frame、簡略では非表示）のみを使用する必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-004",
            "family",
            RuleSeverity.Error,
            "Coarse K_Footprint downward bottom PlanarFace at Z=0",
            "Polygonal Coarse K_Footprint solid must include a downward bottom PlanarFace at Z = 0 within 1 mm tolerance with FaceNormal.Z < -0.9 (matching UnitExtractor).",
            titleJa: "簡略 K_Footprint 底面 PlanarFace（Z=0・下向き法線）契約",
            descriptionJa: "ポリゴン出力対象ファミリの簡略 K_Footprint ソリッドは、Z = 0（許容誤差 1 mm 以内）かつ FaceNormal.Z < -0.9 の下向き PlanarFace を持つ必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-005",
            "family",
            RuleSeverity.Error,
            "Anti-Z-fighting clearances on Medium/Fine solids",
            "Medium/Fine solids must preserve anti-Z-fighting clearances (+4 mm slab clear, +8 mm side clear, 8 mm glass tuck).",
            titleJa: "標準・詳細ソリッドの Z ファイティング防止クリアランス",
            descriptionJa: "標準・詳細ソリッドは Z ファイティング防止クリアランス（スラブ +4 mm、側面 +8 mm、ガラス引き込み 8 mm）を満たす必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-006",
            "family",
            RuleSeverity.Error,
            "Coarse and Medium/Fine triangle budgets",
            "Coarse (when required) and Medium/Fine triangle counts must be > 0 and within both the family triangleBudget and kit.json ceilings.",
            titleJa: "簡略および標準・詳細の三角形ポリゴン数上限",
            descriptionJa: "簡略（必須時）および標準・詳細の三角形数は 0 より大きく、ファミリ仕様および kit.json の上限以内である必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-007",
            "family",
            RuleSeverity.Error,
            "Zero imported CAD/mesh or embedded raster images",
            "The .rfa family must contain zero imported CAD/DWG/mesh instances (ImportInstance) and zero embedded raster images.",
            titleJa: "インポート CAD/メッシュ・埋め込み画像の禁止",
            descriptionJa: ".rfa ファミリ内に CAD/DWG/メッシュのインポート（ImportInstance）や埋め込みラスター画像を含めることはできません。"),
        new RuleCatalogEntry(
            "KZ-RFA-008",
            "family",
            RuleSeverity.Error,
            "Zero unapproved nested families or excessive nesting",
            "The .rfa family must not embed unapproved non-starter nested families or exceed the maximum allowed nesting depth.",
            titleJa: "未承認ネストファミリおよび過度なネスト階層の禁止",
            descriptionJa: "未承認の非 Starter Kit ネストファミリや許容階層を超えるネストを含めることはできません。"),
        new RuleCatalogEntry(
            "KZ-RFA-009",
            "family",
            RuleSeverity.Error,
            "Approved 3D materials with valid sRGB values",
            "All geometry materials must be assigned from materials-3d.json (K3D_*/M3D_*) with matching sRGB values and transparency (no unnamed default grey material).",
            titleJa: "materials-3d.json 準拠マテリアルと sRGB 値",
            descriptionJa: "すべてのジオメトリには materials-3d.json に定義された K3D_*/M3D_* マテリアルを正確な sRGB 値で割り当てる必要があります（未指定デフォルト灰色マテリアル不可）。"),
        new RuleCatalogEntry(
            "KZ-RFA-010",
            "family",
            RuleSeverity.Error,
            "No baked project or element identity defaults",
            "Default family parameters must not contain baked-in project or element identity values (id, level_id, gis_key).",
            titleJa: "プロジェクト・要素 ID の固定埋め込み禁止",
            descriptionJa: "ファミリの既定パラメータ値に id、level_id、gis_key などのプロジェクト／要素識別子を固定値として埋め込んではいけません。"),
        new RuleCatalogEntry(
            "KZ-RFA-011",
            "family",
            RuleSeverity.Error,
            "Family .rfa file size within kit budget",
            "The saved .rfa file size must not exceed the kit.json maxRfaFileSizeKb budget.",
            titleJa: ".rfa ファイルサイズ上限",
            descriptionJa: "保存された .rfa ファイルのサイズは kit.json の maxRfaFileSizeKb 上限以内である必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-012",
            "family",
            RuleSeverity.Error,
            "Valid GIS default domain values",
            "Default values of GIS domain parameters (gis_kind, gis_door, gis_opening_category, gis_fixture_type, gis_category, gis_restriction, gis_direction, gis_access_con) must belong to the closed standards domains.",
            titleJa: "GIS パラメータ既定値のドメイン整合性",
            descriptionJa: "gis_kind、gis_door、gis_opening_category、gis_fixture_type 等の既定値は、fields.json / opening-types.json / families.json の定義済みドメイン値である必要があります。"),
        new RuleCatalogEntry(
            "KZ-RFA-013",
            "family",
            RuleSeverity.Error,
            "Circulation family dimensional and metadata contract",
            "KNZ_CIRC_* circulation families must expose positive Width, Length/Depth, Height (and Rise for escalators) parameters plus required directionality, levels-served, or elevator-door metadata.",
            titleJa: "縦動線ファミリ（KNZ_CIRC_*）の寸法・メタデータ契約",
            descriptionJa: "KNZ_CIRC_* ファミリは正の Width・Length/Depth・Height（エスカレーターは Rise を含む）パラメータおよび方向・停止階・エレベーター扉メタデータを持つ必要があります。"),

        // KZ-ELEM-*: placed element data quality
        new RuleCatalogEntry(
            "KZ-ELEM-001",
            "element",
            RuleSeverity.Error,
            "Door gis_door values present",
            "In Project mode, once any placed door type has a value, all placed door types require valid gis_door values from the closed domain, and any populated gis_access_con or gis_opening_category value must match its closed domain.",
            new[] { "door-values" }),
        new RuleCatalogEntry(
            "KZ-ELEM-002",
            "element",
            RuleSeverity.Warning,
            "Circulation gis_kind completeness",
            "Placed circulation elements should carry gis_kind matching standards/families.json."),
        new RuleCatalogEntry(
            "KZ-ELEM-003",
            "element",
            RuleSeverity.Warning,
            "Floor gis_category completeness",
            "Placed floors should map to a known gis_category by type or override."),
        new RuleCatalogEntry(
            "KZ-ELEM-004",
            "element",
            RuleSeverity.Warning,
            "Element gis_key stamped",
            "Exported elements should carry a stable gis_key written by the last export."),
        new RuleCatalogEntry(
            "KZ-ELEM-005",
            "element",
            RuleSeverity.Error,
            "No showcase elements in authoring model",
            "Authoring models and templates must not contain Forma Companion showcase elements (ApplicationId 'Kanizu.Showcase', KNZ_FRM_* families/types, or KS_*/ks_* companion parameters).",
            new[] { "showcase-elements" },
            titleJa: "オーサリングモデル内にショーケース要素がないこと",
            descriptionJa: "オーサリングモデルおよびテンプレートに Forma Companion ショーケース要素（ApplicationId 'Kanizu.Showcase'、KNZ_FRM_* ファミリ／タイプ、または KS_*/ks_* パラメータ）を含めることはできません。"),

        // KZ-VIEW-*: export view template & hide filters
        new RuleCatalogEntry(
            "KZ-VIEW-001",
            "view",
            RuleSeverity.Error,
            "GIS Export view template present",
            "The canonical 'GIS Export' view template must exist and be configured for GIS export.",
            new[] { "view-template-export-plan" }),
        new RuleCatalogEntry(
            "KZ-VIEW-002",
            "view",
            RuleSeverity.Warning,
            "GIS Export view template settings",
            "The 'GIS Export' view template must show the categories the export reads and hide the others."),
        new RuleCatalogEntry(
            "KZ-VIEW-003",
            "view",
            RuleSeverity.Warning,
            "GIS_* plans use canonical template",
            "Every GIS_* plan view must use the 'GIS Export' view template."),
        new RuleCatalogEntry(
            "KZ-VIEW-004",
            "view",
            RuleSeverity.Error,
            "No parameter hide-filter on export views",
            "Export plan views and their templates require visible parameter filters.",
            new[] { "view-template-hide-filter" }),
        new RuleCatalogEntry(
            "KZ-VIEW-005",
            "view",
            RuleSeverity.Warning,
            "GIS_* plan crop coverage",
            "GIS_* plan crop frames should contain the footprint of their level."),

        // KZ-FED-*: federation / project only checks (CRS, ModelIds, levels)
        new RuleCatalogEntry(
            "KZ-FED-001",
            "federation",
            RuleSeverity.Error,
            "Project CRS inside Japan Plane Rectangular range",
            "Project mode requires EPSG:6669..6687 and a verified georeference.",
            new[] { "project-crs" }),
        new RuleCatalogEntry(
            "KZ-FED-002",
            "federation",
            RuleSeverity.Error,
            "Host & linked model stable ModelId",
            "Host document and every loaded link must carry a stable GeoProjectInfo.ModelId.",
            new[] { "model-ids" }),
        new RuleCatalogEntry(
            "KZ-FED-003",
            "federation",
            RuleSeverity.Error,
            "Level ordinal & short_name completeness",
            "Every level with floors must carry a unique ordinal and short_name.",
            new[] { "level-metadata" }),
        new RuleCatalogEntry(
            "KZ-FED-004",
            "federation",
            RuleSeverity.Warning,
            "Unloaded link review",
            "Unloaded links cannot be verified for ModelId; reload before export."),
        new RuleCatalogEntry(
            "KZ-FED-009",
            "federation",
            RuleSeverity.Error,
            "No showcase companion model role conflict",
            "An authoring model must not carry gis_model_role = 'showcase' and must not link a Forma Companion showcase model (gis_model_role = 'showcase') into authoring federation.",
            new[] { "showcase-model-role" },
            titleJa: "ショーケースモデルの役割競合がないこと",
            descriptionJa: "オーサリングモデル自身を gis_model_role = 'showcase' に設定してはならず、Forma Companion ショーケースモデル（gis_model_role = 'showcase'）をオーサリング用フェデレーションにリンクしないでください。"),
    };

    private static readonly Dictionary<string, RuleCatalogEntry> ByRuleId = BuildById();
    private static readonly Dictionary<string, RuleCatalogEntry> ByLegacy = BuildByLegacy();

    /// <summary>The whole catalog, in a stable published order.</summary>
    public static IReadOnlyList<RuleCatalogEntry> Entries { get; } = Array.AsReadOnly(EntriesArray);

    public static RuleCatalogEntry Lookup(string ruleId)
    {
        if (ruleId == null)
        {
            throw new ArgumentNullException(nameof(ruleId));
        }

        if (!ByRuleId.TryGetValue(ruleId, out RuleCatalogEntry? entry) || entry is null)
        {
            throw new KeyNotFoundException($"Unknown Kanizu rule id '{ruleId}'.");
        }

        return entry;
    }

    public static bool TryLookup(string ruleId, out RuleCatalogEntry entry)
    {
        if (ruleId != null && ByRuleId.TryGetValue(ruleId, out RuleCatalogEntry? found) && found != null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>Resolve a Phase 4B legacy ItemId (e.g. <c>view-template-export-plan</c>) to a canonical rule.</summary>
    public static bool TryLookupByLegacyItemId(string legacyItemId, out RuleCatalogEntry entry)
    {
        if (legacyItemId != null && ByLegacy.TryGetValue(legacyItemId, out RuleCatalogEntry? found) && found != null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>The canonical rule id for a Phase 4B legacy ItemId, or the input unchanged when no mapping exists.</summary>
    public static string CanonicalRuleId(string legacyItemId)
    {
        if (legacyItemId != null && ByLegacy.TryGetValue(legacyItemId, out RuleCatalogEntry? entry) && entry != null)
        {
            return entry.RuleId;
        }

        return legacyItemId ?? string.Empty;
    }

    private static Dictionary<string, RuleCatalogEntry> BuildById()
    {
        Dictionary<string, RuleCatalogEntry> map = new(StringComparer.Ordinal);
        foreach (RuleCatalogEntry entry in EntriesArray)
        {
            if (map.ContainsKey(entry.RuleId))
            {
                throw new InvalidOperationException($"Duplicate Kanizu rule id '{entry.RuleId}' in catalog.");
            }

            map[entry.RuleId] = entry;
        }

        return map;
    }

    private static Dictionary<string, RuleCatalogEntry> BuildByLegacy()
    {
        Dictionary<string, RuleCatalogEntry> map = new(StringComparer.Ordinal);
        foreach (RuleCatalogEntry entry in EntriesArray)
        {
            foreach (string legacy in entry.LegacyItemIds)
            {
                map.Add(legacy, entry);
            }
        }

        return map;
    }
}
