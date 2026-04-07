using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace RevitGeoSuite.SharedUI.Localization;

public sealed class UiLocalizer : INotifyPropertyChanged
{
    private readonly UiSettingsStore settingsStore = new UiSettingsStore();
    private readonly IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<string, string>> dictionaries;
    private UiLanguage currentLanguage;

    private UiLocalizer()
    {
        dictionaries = CreateDictionaries();
        currentLanguage = settingsStore.Load();
    }

    public static UiLocalizer Instance { get; } = new UiLocalizer();

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLanguage CurrentLanguage => currentLanguage;

    public bool IsEnglish => currentLanguage == UiLanguage.English;

    public bool IsJapanese => currentLanguage == UiLanguage.Japanese;

    public bool CanSwitchToEnglish => !IsEnglish;

    public bool CanSwitchToJapanese => !IsJapanese;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (dictionaries.TryGetValue(currentLanguage, out IReadOnlyDictionary<string, string>? currentDictionary) &&
            currentDictionary.TryGetValue(key, out string? localizedValue))
        {
            return localizedValue;
        }

        if (dictionaries.TryGetValue(UiLanguage.English, out IReadOnlyDictionary<string, string>? englishDictionary) &&
            englishDictionary.TryGetValue(key, out string? englishValue))
        {
            return englishValue;
        }

        return key;
    }

    public void SetLanguage(UiLanguage language)
    {
        if (currentLanguage == language)
        {
            return;
        }

        currentLanguage = language;
        settingsStore.Save(language);
        RaiseChanged(nameof(CurrentLanguage));
        RaiseChanged(nameof(IsEnglish));
        RaiseChanged(nameof(IsJapanese));
        RaiseChanged(nameof(CanSwitchToEnglish));
        RaiseChanged(nameof(CanSwitchToJapanese));
        RaiseChanged("Item[]");
    }

    private void RaiseChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<string, string>> CreateDictionaries()
    {
        return new Dictionary<UiLanguage, IReadOnlyDictionary<string, string>>
        {
            [UiLanguage.English] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["App.ProductTitle"] = "Revit Geo Suite",
                ["App.ShortTitle"] = "Geo Suite",
                ["Common.Back"] = "Back",
                ["Common.Next"] = "Next",
                ["Common.Close"] = "Close",
                ["Common.Apply"] = "Apply",
                ["Common.Browse"] = "Browse...",
                ["Common.Search"] = "Search",
                ["Common.Clear"] = "Clear",
                ["Common.LoadPreview"] = "Load Preview",
                ["Common.ImportContext"] = "Import Context",
                ["Common.PrepareExport"] = "Prepare Export",
                ["Common.Export3DTiles"] = "Export 3D Tiles",
                ["Common.ExportCityGml"] = "Export CityGML",
                ["Common.CopyMesh"] = "Copy Mesh",
                ["Common.SaveCanonicalMesh"] = "Save Canonical Mesh",
                ["Common.ScanFolder"] = "Scan Folder",
                ["Language.English"] = "EN",
                ["Language.Japanese"] = "日本語",
                ["Language.Label"] = "Language",
                ["Nav.Georeference"] = "GEO",
                ["Nav.PlateauImport"] = "PLT",
                ["Nav.MeshInspector"] = "MSH",
                ["Nav.Validation"] = "CHK",
                ["Nav.Tiles3DExport"] = "3DT",
                ["Nav.CityGmlExport"] = "GML",
                ["Module.Georeference"] = "Georeference",
                ["Module.PlateauImport"] = "PLATEAU Import",
                ["Module.MeshInspector"] = "Mesh",
                ["Module.Validation"] = "Validation",
                ["Module.Tiles3DExport"] = "3D Tiles",
                ["Module.CityGmlExport"] = "CityGML",
                ["Georef.Step.CurrentState"] = "Current State",
                ["Georef.Step.Crs"] = "CRS",
                ["Georef.Step.SiteReference"] = "Site Reference",
                ["Georef.Step.Review"] = "Review",
                ["Georef.Step.Intent"] = "Intent",
                ["Georef.Step.Preview"] = "Preview",
                ["Georef.Sidebar.ProjectContext"] = "Project Context",
                ["Georef.Sidebar.PrimaryAnchor"] = "Primary Apply Anchor",
                ["Georef.Sidebar.WorkingPoint"] = "Working Project Base Point",
                ["Georef.Sidebar.Alerts"] = "Alerts",
                ["Georef.Section.StepperHint"] = "Follow the guided setup. Only the active step stays in focus; supporting detail moves into the right panel.",
                ["Georef.Section.MapContext"] = "Map search, zoom, and markers stay available for context while you define the main anchor and any optional working point.",
                ["Georef.Section.TechnicalDetails"] = "Technical Details",
                ["Georef.Action.ZoomSite"] = "Zoom To Site",
                ["Georef.Action.ZoomProjectPoint"] = "Zoom To Project Base Point",
                ["Georef.Action.UseCoordinates"] = "Use Coordinates",
                ["Georef.Action.RefreshCurrent"] = "Refresh Current Survey Point",
                ["Georef.Action.ApplyManual"] = "Apply is always manual. Review the preview carefully before committing changes.",
                ["Plateau.Header"] = "Map-first import. Scan a package, click the exact grids you want, then preview and import only that filtered subset.",
                ["Plateau.Center.Package"] = "Package Scan",
                ["Plateau.Center.TileMap"] = "Detected Tile Map",
                ["Plateau.Center.Filters"] = "Import Filters",
                ["Plateau.Center.Preview"] = "Preview",
                ["Plateau.Sidebar.Reference"] = "Reference Context",
                ["Plateau.Sidebar.LastImport"] = "Last Import",
                ["Plateau.Sidebar.Warnings"] = "Warnings",
                ["Plateau.Sidebar.SourceFiles"] = "Detected Source Files",
                ["Mesh.Header"] = "Inspect the mesh around the active reference without changing canonical CRS or orientation facts.",
                ["Mesh.Center.Map"] = "Mesh Overlay",
                ["Mesh.Center.Neighbors"] = "Neighbor Meshes",
                ["Mesh.Sidebar.Reference"] = "Reference",
                ["Mesh.Sidebar.Details"] = "Mesh Details",
                ["Validation.Header"] = "Check readiness, blocking issues, and export health from one compact project dashboard.",
                ["Validation.Center.Health"] = "Project Health",
                ["Validation.Center.Findings"] = "Findings",
                ["Validation.Center.Readiness"] = "Export Readiness",
                ["Validation.Sidebar.Snapshot"] = "Shared Metadata Snapshot",
                ["Tiles.Header"] = "Prepare a lightweight viewer package, review the extracted scope, then export only when the summary looks correct.",
                ["Tiles.Center.Scope"] = "Export Scope",
                ["Tiles.Center.Prepared"] = "Prepared Export",
                ["Tiles.Center.Elements"] = "Prepared Elements",
                ["Tiles.Sidebar.Reference"] = "Reference Context",
                ["Tiles.Sidebar.LastExport"] = "Last Export",
                ["Tiles.Sidebar.Advanced"] = "Advanced Settings",
                ["City.Header"] = "Prepare a lightweight CityGML export from the selected 3D view, review validation, then write the package when the scope looks right.",
                ["City.Center.Scope"] = "Export Scope",
                ["City.Center.Prepared"] = "Prepared Export",
                ["City.Center.Validation"] = "Validation Messages",
                ["City.Center.Features"] = "Prepared Features",
                ["City.Sidebar.Reference"] = "Reference Context",
                ["City.Sidebar.LastExport"] = "Last Export",
                ["City.Sidebar.Advanced"] = "Advanced Options",
                ["Shell.RightPanel"] = "Context Panel"
            },
            [UiLanguage.Japanese] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["App.ProductTitle"] = "Revit Geo Suite",
                ["App.ShortTitle"] = "Geo Suite",
                ["Common.Back"] = "戻る",
                ["Common.Next"] = "次へ",
                ["Common.Close"] = "閉じる",
                ["Common.Apply"] = "適用",
                ["Common.Browse"] = "参照...",
                ["Common.Search"] = "検索",
                ["Common.Clear"] = "クリア",
                ["Common.LoadPreview"] = "プレビュー生成",
                ["Common.ImportContext"] = "コンテキスト取込",
                ["Common.PrepareExport"] = "書き出し準備",
                ["Common.Export3DTiles"] = "3D Tiles 書き出し",
                ["Common.ExportCityGml"] = "CityGML 書き出し",
                ["Common.CopyMesh"] = "メッシュをコピー",
                ["Common.SaveCanonicalMesh"] = "正規メッシュを保存",
                ["Common.ScanFolder"] = "フォルダをスキャン",
                ["Language.English"] = "EN",
                ["Language.Japanese"] = "日本語",
                ["Language.Label"] = "言語",
                ["Nav.Georeference"] = "GEO",
                ["Nav.PlateauImport"] = "PLT",
                ["Nav.MeshInspector"] = "MSH",
                ["Nav.Validation"] = "CHK",
                ["Nav.Tiles3DExport"] = "3DT",
                ["Nav.CityGmlExport"] = "GML",
                ["Module.Georeference"] = "ジオリファレンス",
                ["Module.PlateauImport"] = "PLATEAU取込",
                ["Module.MeshInspector"] = "メッシュ",
                ["Module.Validation"] = "検証",
                ["Module.Tiles3DExport"] = "3D Tiles",
                ["Module.CityGmlExport"] = "CityGML",
                ["Georef.Step.CurrentState"] = "現在状態",
                ["Georef.Step.Crs"] = "CRS",
                ["Georef.Step.SiteReference"] = "基準位置",
                ["Georef.Step.Review"] = "確認",
                ["Georef.Step.Intent"] = "設定内容",
                ["Georef.Step.Preview"] = "プレビュー",
                ["Georef.Sidebar.ProjectContext"] = "プロジェクト状況",
                ["Georef.Sidebar.PrimaryAnchor"] = "主アンカー",
                ["Georef.Sidebar.WorkingPoint"] = "作業用プロジェクト基点",
                ["Georef.Sidebar.Alerts"] = "注意事項",
                ["Georef.Section.StepperHint"] = "ガイドに沿って進めます。主作業だけを中央に表示し、補足情報は右パネルに集約します。",
                ["Georef.Section.MapContext"] = "検索・ズーム・マーカーで位置関係を確認しながら主アンカーと任意の作業点を設定できます。",
                ["Georef.Section.TechnicalDetails"] = "技術詳細",
                ["Georef.Action.ZoomSite"] = "サイト位置へズーム",
                ["Georef.Action.ZoomProjectPoint"] = "プロジェクト基点へズーム",
                ["Georef.Action.UseCoordinates"] = "座標を使用",
                ["Georef.Action.RefreshCurrent"] = "現在の測量点を更新",
                ["Georef.Action.ApplyManual"] = "適用は常に手動です。変更を確定する前にプレビューを確認してください。",
                ["Plateau.Header"] = "地図中心の取込です。パッケージを読み込み、必要なグリッドだけをクリックして選択し、絞り込んだ内容だけを取り込みます。",
                ["Plateau.Center.Package"] = "パッケージ読込",
                ["Plateau.Center.TileMap"] = "検出グリッド地図",
                ["Plateau.Center.Filters"] = "取込フィルタ",
                ["Plateau.Center.Preview"] = "プレビュー",
                ["Plateau.Sidebar.Reference"] = "参照コンテキスト",
                ["Plateau.Sidebar.LastImport"] = "前回の取込",
                ["Plateau.Sidebar.Warnings"] = "警告",
                ["Plateau.Sidebar.SourceFiles"] = "検出ソースファイル",
                ["Mesh.Header"] = "基準点まわりのメッシュを確認します。正規CRSや方位の事実は変更しません。",
                ["Mesh.Center.Map"] = "メッシュ重ね表示",
                ["Mesh.Center.Neighbors"] = "周辺メッシュ",
                ["Mesh.Sidebar.Reference"] = "参照元",
                ["Mesh.Sidebar.Details"] = "メッシュ詳細",
                ["Validation.Header"] = "準備状況、要対応事項、書き出し readiness を1つのダッシュボードで確認します。",
                ["Validation.Center.Health"] = "プロジェクト健全性",
                ["Validation.Center.Findings"] = "検出結果",
                ["Validation.Center.Readiness"] = "書き出し準備状況",
                ["Validation.Sidebar.Snapshot"] = "共有メタデータのスナップショット",
                ["Tiles.Header"] = "軽量ビューア用パッケージを準備し、内容を確認してから3D Tilesを書き出します。",
                ["Tiles.Center.Scope"] = "書き出し範囲",
                ["Tiles.Center.Prepared"] = "準備済み書き出し",
                ["Tiles.Center.Elements"] = "準備済み要素",
                ["Tiles.Sidebar.Reference"] = "参照コンテキスト",
                ["Tiles.Sidebar.LastExport"] = "前回の書き出し",
                ["Tiles.Sidebar.Advanced"] = "詳細設定",
                ["City.Header"] = "選択した3Dビューから軽量CityGMLを書き出します。検証内容を確認してから保存します。",
                ["City.Center.Scope"] = "書き出し範囲",
                ["City.Center.Prepared"] = "準備済み書き出し",
                ["City.Center.Validation"] = "検証メッセージ",
                ["City.Center.Features"] = "準備済みフィーチャ",
                ["City.Sidebar.Reference"] = "参照コンテキスト",
                ["City.Sidebar.LastExport"] = "前回の書き出し",
                ["City.Sidebar.Advanced"] = "詳細オプション",
                ["Shell.RightPanel"] = "コンテキストパネル"
            }
        };
    }
}




