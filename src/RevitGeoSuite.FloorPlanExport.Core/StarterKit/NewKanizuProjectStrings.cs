using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Bilingual English / Japanese copy for the T9 "New Kanizu Project" command, modal dialog,
/// pre-flight/post-init readiness summaries, and completion report.
/// </summary>
public static class NewKanizuProjectStrings
{
    public static string Select(NewKanizuProjectLanguage language, string english, string japanese) =>
        language == NewKanizuProjectLanguage.Japanese ? japanese : english;

    public static string CommandTitle(NewKanizuProjectLanguage language) =>
        Select(language, "New Kanizu Project", "新規 Kanizu プロジェクト");

    public static string DialogSubtitle(NewKanizuProjectLanguage language) =>
        Select(
            language,
            "Initialize a clean authoring project with a fresh ModelId, unique level_ids, GIS views, and optional Kanizu worksets.",
            "固有の ModelId、各階の level_id、GIS ビュー、および任意のワークセットを設定してオーサリングプロジェクトを初期化します。");

    public static string RibbonButtonLabel => "New Kanizu\nProject";

    public static string RibbonTooltip =>
        "Create or initialize a Kanizu station authoring project with a fresh ModelId, unique level_ids, GIS views, and optional Kanizu worksets.";

    public static string ModeSectionLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Initialization Mode", "初期化モード");

    public static string ModeCreateFromTemplateLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Create new project from Starter Kit .rte template", "スターターキット .rte テンプレートから新規作成");

    public static string ModeInitializeOpenDocumentLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Initialize currently open document", "現在開いているドキュメントを初期化");

    public static string TemplatePathLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Starter Kit .rte Template Path", "スターターキット .rte テンプレートパス");

    public static string BrowseLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Browse...", "参照...");

    public static string StationNameLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Station Name (required)", "駅名（必須）");

    public static string StationCodeLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Station Code (optional, e.g. TYO)", "駅コード（任意・例: TYO）");

    public static string BuildingSlugLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Building / Zone Slug (optional, e.g. main-concourse)", "建物／ゾーン識別子（任意・例: main-concourse）");

    public static string FederationModeLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Federation Mode", "フェデレーション構成");

    public static string HubModelIdLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Hub ModelId (required for Spoke)", "ハブ ModelId（Spoke 構成では必須）");

    public static string FederationPersistenceNote(NewKanizuProjectLanguage language) =>
        Select(
            language,
            "Note: Federation mode and Hub ModelId are persisted in gis-model.json upon export (the 4 frozen Revit storage schemas remain unchanged).",
            "注記: フェデレーション構成およびハブ ModelId はエクスポート時に gis-model.json へ保存されます（固定された 4 つの Revit ストレージスキーマは変更しません）。");

    public static string LevelsSectionLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Levels (Name, Ordinal, Elevation m, Short Name — one per line)", "レベル一覧（名前, 階層番号, 高さ m, 短縮名 — 1 行 1 階）");

    public static string WorksharingCheckboxLabel(NewKanizuProjectLanguage language) =>
        Select(
            language,
            "Enable worksharing & create standard Kanizu worksets (KNZ_GIS_Areas, KNZ_Circulation, KNZ_Structure, KNZ_Fixtures, KNZ_Links)",
            "ワークシェアリングを有効化し、標準 Kanizu ワークセット（KNZ_GIS_Areas / KNZ_Circulation / KNZ_Structure / KNZ_Fixtures / KNZ_Links）を作成");

    public static string CreateExportViewsCheckboxLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Create GIS_<Level> export plan views (GIS Export template)", "各階の GIS_<階> エクスポート平面ビューを作成（GIS Export テンプレート）");

    public static string CreateWorkingViewsCheckboxLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Create WORK_<Level> working plan views (GIS Working Plan template)", "各階の WORK_<階> 作業用平面ビューを作成（GIS Working Plan テンプレート）");

    public static string CreateAreaViewsCheckboxLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Create GIS Area plan views per level", "各階の GIS Area エリア平面ビューを作成");

    public static string ClearStaleIdentityCheckboxLabel(NewKanizuProjectLanguage language) =>
        Select(
            language,
            "Clear stale ModelId / level_id / element IDs from this document and mint fresh identities",
            "このドキュメントに残存する旧 ModelId・level_id・要素 ID を消去し、新しい固有 ID を発行する");

    public static string InitializeButtonLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Initialize Project", "プロジェクトを初期化");

    public static string CancelButtonLabel(NewKanizuProjectLanguage language) =>
        Select(language, "Cancel", "キャンセル");

    public static string NonWorksharedFallbackEn =>
        "Worksharing is disabled: zero worksets created. Kanizu discipline worksets (KNZ_GIS_Areas, KNZ_Circulation, KNZ_Structure, KNZ_Fixtures, KNZ_Links) are created on demand when worksharing is enabled and are never baked into the .rte template.";

    public static string NonWorksharedFallbackJa =>
        "ワークシェアリング無効のためワークセットは作成されません。Kanizu 標準ワークセット（KNZ_GIS_Areas / KNZ_Circulation / KNZ_Structure / KNZ_Fixtures / KNZ_Links）はワークシェアリング有効化時にのみ作成され、.rte テンプレートには含めません。";

    public static string WorksharingEnabledNoteEn =>
        "Worksharing enabled: 5 standard Kanizu discipline worksets and non-hiding QA workset filters will be created (all GIS authoring worksets remain visible in GIS_* export views per KZ-VIEW-004).";

    public static string WorksharingEnabledNoteJa =>
        "ワークシェアリング有効: 標準 5 種類の Kanizu ワークセットと非表示にしない QA ワークセットフィルタを作成します（KZ-VIEW-004 に準拠し、GIS_* エクスポートビューでは全 GIS ワークセットを表示維持します）。";

    public static string FormatFederationMode(KanizuFederationMode mode, NewKanizuProjectLanguage language) => mode switch
    {
        KanizuFederationMode.Hub => Select(language, "Hub (host aggregating linked spokes)", "ハブ（リンクされたスポークを集約するホスト）"),
        KanizuFederationMode.Spoke => Select(language, "Spoke (linked into a hub model)", "スポーク（ハブモデルにリンクされる建物）"),
        KanizuFederationMode.Independent => Select(language, "Independent (unlinked station-folder building)", "独立モデル（駅フォルダ単位で独立出力）"),
        _ => Select(language, "Standalone (single station model)", "単独モデル（単一駅モデル）"),
    };

    public static string FormatPlanSummary(
        NewKanizuProjectLanguage language,
        string modelId,
        string stationName,
        string buildingSlug,
        string kitVersion,
        int gisStandardVersion,
        KanizuFederationMode federationMode,
        IReadOnlyList<PlannedKanizuLevel> levels,
        IReadOnlyList<PlannedKanizuView> views,
        PlannedWorksetSetup worksetPlan,
        bool shouldClearStaleIdentity)
    {
        StringBuilder sb = new();
        if (language == NewKanizuProjectLanguage.Japanese)
        {
            sb.AppendLine($"新規 Kanizu プロジェクト計画: {stationName}" + (string.IsNullOrWhiteSpace(buildingSlug) ? string.Empty : $" ({buildingSlug})"));
            sb.AppendLine($"- 新規 ModelId: {modelId}");
            sb.AppendLine($"- モデル役割: gis_model_role = authoring / gis_kit_version = {kitVersion} / gis_standard_version = {gisStandardVersion}");
            sb.AppendLine($"- フェデレーション: {FormatFederationMode(federationMode, language)}（エクスポート時に gis-model.json へ保存）");
            sb.AppendLine($"- レベル数: {levels.Count} 階（固有 level_id 発行済）、作成ビュー数: {views.Count}");
            sb.AppendLine(worksetPlan.EnableWorksharing
                ? $"- ワークセット: {worksetPlan.Worksets.Count} 件 ({string.Join(", ", worksetPlan.Worksets.Select(w => w.Name))})"
                : "- ワークセット: 無効（非ワークシェアリングモード）");
            if (shouldClearStaleIdentity)
            {
                sb.AppendLine("- 旧 ID クリア: 既存の ModelId / level_id / 要素 ID を消去して再発行します。");
            }
        }
        else
        {
            sb.AppendLine($"New Kanizu Project Plan: {stationName}" + (string.IsNullOrWhiteSpace(buildingSlug) ? string.Empty : $" ({buildingSlug})"));
            sb.AppendLine($"- Fresh ModelId: {modelId}");
            sb.AppendLine($"- Project Stamps: gis_model_role = authoring | gis_kit_version = {kitVersion} | gis_standard_version = {gisStandardVersion}");
            sb.AppendLine($"- Federation: {FormatFederationMode(federationMode, language)} (persisted in gis-model.json on export)");
            sb.AppendLine($"- Levels: {levels.Count} level(s) with unique level_ids | Planned Views: {views.Count}");
            sb.AppendLine(worksetPlan.EnableWorksharing
                ? $"- Worksets: {worksetPlan.Worksets.Count} ({string.Join(", ", worksetPlan.Worksets.Select(w => w.Name))})"
                : "- Worksets: None (non-workshared mode)");
            if (shouldClearStaleIdentity)
            {
                sb.AppendLine("- Stale Identity Hygiene: Existing ModelId / level_id / element IDs will be cleared before minting fresh identities.");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatCompletionDialogBody(
        NewKanizuProjectLanguage language,
        string modelId,
        string kitVersion,
        int gisStandardVersion,
        IReadOnlyList<(string LevelName, int Ordinal, double ElevationMeters, string LevelId)> levels,
        IReadOnlyList<string> createdWorksets,
        IReadOnlyList<string> createdViews,
        bool postInitReady,
        IReadOnlyList<string> warnings)
    {
        StringBuilder sb = new();
        if (language == NewKanizuProjectLanguage.Japanese)
        {
            sb.AppendLine($"ModelId: {modelId}");
            sb.AppendLine($"gis_model_role: authoring | gis_kit_version: {kitVersion} | gis_standard_version: {gisStandardVersion}");
            sb.AppendLine($"初期化ステータス: {(postInitReady ? "合格 (READY)" : "要確認 (WARNINGS/ERRORS)")}");
            sb.AppendLine();
            sb.AppendLine($"レベル ({levels.Count}):");
            foreach ((string name, int ordinal, double elevM, string levelId) in levels)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - {0} (ordinal={1}, {2:0.00} m) -> level_id={3}", name, ordinal, elevM, levelId));
            }

            sb.AppendLine();
            sb.AppendLine($"作成ワークセット ({createdWorksets.Count}): {(createdWorksets.Count > 0 ? string.Join(", ", createdWorksets) : "なし（非ワークシェアリング）")}");
            sb.AppendLine($"作成ビュー ({createdViews.Count}): {(createdViews.Count > 0 ? string.Join(", ", createdViews.Take(12)) + (createdViews.Count > 12 ? " ..." : string.Empty) : "なし")}");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"警告 ({warnings.Count}):");
                foreach (string w in warnings.Take(8))
                {
                    sb.AppendLine($"  - {w}");
                }
            }
        }
        else
        {
            sb.AppendLine($"ModelId: {modelId}");
            sb.AppendLine($"gis_model_role: authoring | gis_kit_version: {kitVersion} | gis_standard_version: {gisStandardVersion}");
            sb.AppendLine($"Post-Init Readiness: {(postInitReady ? "READY" : "NEEDS ATTENTION")}");
            sb.AppendLine();
            sb.AppendLine($"Initialized Levels ({levels.Count}):");
            foreach ((string name, int ordinal, double elevM, string levelId) in levels)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - {0} (ordinal={1}, {2:0.00} m) -> level_id={3}", name, ordinal, elevM, levelId));
            }

            sb.AppendLine();
            sb.AppendLine($"Worksets ({createdWorksets.Count}): {(createdWorksets.Count > 0 ? string.Join(", ", createdWorksets) : "None (non-workshared)")}");
            sb.AppendLine($"Created Views ({createdViews.Count}): {(createdViews.Count > 0 ? string.Join(", ", createdViews.Take(12)) + (createdViews.Count > 12 ? " ..." : string.Empty) : "None")}");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({warnings.Count}):");
                foreach (string w in warnings.Take(8))
                {
                    sb.AppendLine($"  - {w}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}
