using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# project bootstrap planner, validator, stale-identity detector, and post-init readiness verifier
/// for Kanizu Starter Kit T9 ("New Kanizu Project").
/// Mints guaranteed-fresh <c>ModelId</c> and per-level <c>level_id</c>s, enforces <c>gis_model_role = "authoring"</c>,
/// plans discipline worksets and non-hiding QA workset filters when worksharing is enabled, and verifies zero stale identity.
/// </summary>
public static class NewKanizuProjectPlanner
{
    public const string DefaultSharedLevelsWorksetName = "Shared Levels and Grids";
    public const string WorkingPlanViewPrefix = "WORK_";
    public const string AreaPlanViewPrefix = "GIS Area - ";
    public const string WorksetFilterPrefix = "KNZ_WS_";
    public const string WorkingPlanTemplateName = "GIS Working Plan";
    public const string QaPlanTemplateName = "GIS QA Plan";
    public const string Qa3DTemplateName = "GIS QA 3D";
    public const double MinElevationMeters = -500.0;
    public const double MaxElevationMeters = 5000.0;
    public const double ElevationEqualityToleranceMeters = 1e-4;

    /// <summary>Characters forbidden by Revit in Level and View names (<c>\ : { } [ ] | ; &lt; &gt; ? ` ~</c>).</summary>
    public static readonly char[] ForbiddenRevitNameCharacters =
    {
        '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~',
    };

    private static readonly string[] WorksetFilterTargetTemplates =
    {
        WorkingPlanTemplateName,
        QaPlanTemplateName,
        Qa3DTemplateName,
    };

    /// <summary>Net48-safe check for finite floating-point numbers (never uses <c>double.IsFinite</c>).</summary>
    public static bool IsFiniteDouble(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>Returns true if <paramref name="name"/> contains any Revit-forbidden character or control character.</summary>
    public static bool TryFindForbiddenRevitNameChar(string? name, out char offendingChar)
    {
        if (!string.IsNullOrEmpty(name))
        {
            foreach (char ch in name!)
            {
                if (char.IsControl(ch) || Array.IndexOf(ForbiddenRevitNameCharacters, ch) >= 0)
                {
                    offendingChar = ch;
                    return true;
                }
            }
        }

        offendingChar = '\0';
        return false;
    }

    /// <summary>
    /// Pure-C# category intersection for workset <c>ParameterFilterElement</c> creation:
    /// retains only requested categories that belong to Revit's filterable category set and
    /// expose <c>BuiltInParameter.ELEM_PARTITION_PARAM</c> as a filterable parameter.
    /// </summary>
    public static IReadOnlyList<long> FilterWorksetFilterableCategories(
        IEnumerable<long> requestedCategoryIds,
        IEnumerable<long> allFilterableCategoryIds,
        Func<long, bool> supportsWorksetParameter)
    {
        if (requestedCategoryIds is null)
        {
            throw new ArgumentNullException(nameof(requestedCategoryIds));
        }

        if (allFilterableCategoryIds is null)
        {
            throw new ArgumentNullException(nameof(allFilterableCategoryIds));
        }

        if (supportsWorksetParameter is null)
        {
            throw new ArgumentNullException(nameof(supportsWorksetParameter));
        }

        HashSet<long> filterable = new(allFilterableCategoryIds);
        HashSet<long> seen = new();
        List<long> valid = new();
        foreach (long categoryId in requestedCategoryIds)
        {
            if (seen.Add(categoryId) && filterable.Contains(categoryId) && supportsWorksetParameter(categoryId))
            {
                valid.Add(categoryId);
            }
        }

        return Array.AsReadOnly(valid.ToArray());
    }

    /// <summary>
    /// Pure-C# reconciliation of planned Kanizu levels against existing document levels.
    /// For fresh template projects (<paramref name="createdFromTemplate"/> = <c>true</c>), unclaimed seed levels
    /// (such as Autodesk's <c>Level 1</c>/<c>Level 2</c>) are reused in sorted elevation order before creating new
    /// levels, and any leftover unreferenced seed levels are scheduled for cleanup so post-init <c>level-metadata</c>
    /// passes cleanly. For open documents, unclaimed levels are preserved and any lacking <c>ordinal</c>/<c>short_name</c>
    /// are surfaced for warnings.
    /// </summary>
    public static LevelReconciliationPlan PlanLevelReconciliation(
        IReadOnlyList<PlannedKanizuLevel> plannedLevels,
        IReadOnlyList<ExistingLevelCandidate> existingLevels,
        bool createdFromTemplate,
        double elevationMatchToleranceFeet = 1e-3)
    {
        if (plannedLevels is null)
        {
            throw new ArgumentNullException(nameof(plannedLevels));
        }

        List<ExistingLevelCandidate> existing = (existingLevels ?? Array.Empty<ExistingLevelCandidate>())
            .Where(l => l != null)
            .OrderBy(l => l.ProjectElevationFeet)
            .ThenBy(l => l.ElementId)
            .ToList();

        HashSet<long> claimedIds = new();
        Dictionary<int, (long LevelId, bool FromUnmatchedSeed)> matchedByPlannedIndex = new();

        // Pass 1: exact name match (case-insensitive)
        for (int i = 0; i < plannedLevels.Count; i++)
        {
            PlannedKanizuLevel planned = plannedLevels[i];
            ExistingLevelCandidate? byName = existing.FirstOrDefault(e =>
                !claimedIds.Contains(e.ElementId) &&
                string.Equals(e.Name, planned.Name, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                claimedIds.Add(byName.ElementId);
                matchedByPlannedIndex[i] = (byName.ElementId, false);
            }
        }

        // Pass 2: elevation match within tolerance (using ProjectElevation)
        for (int i = 0; i < plannedLevels.Count; i++)
        {
            if (matchedByPlannedIndex.ContainsKey(i))
            {
                continue;
            }

            PlannedKanizuLevel planned = plannedLevels[i];
            ExistingLevelCandidate? byElev = existing
                .Where(e => !claimedIds.Contains(e.ElementId))
                .OrderBy(e => Math.Abs(e.ProjectElevationFeet - planned.ElevationFeet))
                .FirstOrDefault(e => Math.Abs(e.ProjectElevationFeet - planned.ElevationFeet) <= elevationMatchToleranceFeet);
            if (byElev != null)
            {
                claimedIds.Add(byElev.ElementId);
                matchedByPlannedIndex[i] = (byElev.ElementId, false);
            }
        }

        // Pass 3 (template creation only): reuse remaining unclaimed seed levels in ascending elevation order
        // before calling Level.Create so seed Level 1 / Level 2 are repurposed cleanly instead of orphaned.
        if (createdFromTemplate)
        {
            List<ExistingLevelCandidate> remainingSeeds = existing
                .Where(e => !claimedIds.Contains(e.ElementId))
                .OrderBy(e => e.ProjectElevationFeet)
                .ThenBy(e => e.ElementId)
                .ToList();

            List<int> unmatchedPlannedIndices = Enumerable.Range(0, plannedLevels.Count)
                .Where(i => !matchedByPlannedIndex.ContainsKey(i))
                .OrderBy(i => plannedLevels[i].ElevationFeet)
                .ToList();

            int reuseCount = Math.Min(remainingSeeds.Count, unmatchedPlannedIndices.Count);
            for (int k = 0; k < reuseCount; k++)
            {
                ExistingLevelCandidate seed = remainingSeeds[k];
                int plannedIdx = unmatchedPlannedIndices[k];
                claimedIds.Add(seed.ElementId);
                matchedByPlannedIndex[plannedIdx] = (seed.ElementId, true);
            }
        }

        List<LevelReconciliationAction> actions = new(plannedLevels.Count);
        for (int i = 0; i < plannedLevels.Count; i++)
        {
            if (matchedByPlannedIndex.TryGetValue(i, out (long LevelId, bool FromUnmatchedSeed) match))
            {
                actions.Add(new LevelReconciliationAction(plannedLevels[i], match.LevelId, match.FromUnmatchedSeed));
            }
            else
            {
                actions.Add(new LevelReconciliationAction(plannedLevels[i], reusedExistingLevelId: null, reusedFromUnmatchedSeed: false));
            }
        }

        List<ExistingLevelCandidate> stillUnclaimed = existing
            .Where(e => !claimedIds.Contains(e.ElementId))
            .ToList();

        List<long> toDelete = new();
        List<ExistingLevelCandidate> missingMetadata = new();
        if (createdFromTemplate)
        {
            foreach (ExistingLevelCandidate seed in stillUnclaimed)
            {
                if (!seed.IsReferencedByElements)
                {
                    toDelete.Add(seed.ElementId);
                }
                else if (!seed.HasOrdinal || !seed.HasShortName)
                {
                    missingMetadata.Add(seed);
                }
            }
        }
        else
        {
            foreach (ExistingLevelCandidate extra in stillUnclaimed)
            {
                if (!extra.HasOrdinal || !extra.HasShortName)
                {
                    missingMetadata.Add(extra);
                }
            }
        }

        return new LevelReconciliationPlan(actions, toDelete, missingMetadata);
    }

    /// <summary>
    /// Validates headless execution preconditions before opening/creating any Revit document so headless runs
    /// never leak an unsaved background document.
    /// </summary>
    public static void ValidateHeadlessOptions(NewKanizuProjectOptions options, bool hasActiveDocument)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.InitializationMode == NewKanizuProjectInitializationMode.CreateFromTemplate)
        {
            if (string.IsNullOrWhiteSpace(options.SaveAsRvtPath))
            {
                throw new ArgumentException(
                    "SaveAsRvtPath is required when running NewKanizuProjectCommand.RunHeadless in CreateFromTemplate mode so the created project is saved and closed without leaking a background document.",
                    nameof(options));
            }
        }
        else if (!hasActiveDocument)
        {
            throw new InvalidOperationException(
                "InitializationMode.InitializeOpenDocument requires an active Revit project document.");
        }
    }

    /// <summary>Formats level specs into one-per-line <c>Name, Ordinal, ElevationMeters, ShortName</c> editor text.</summary>
    public static string FormatLevelsText(IReadOnlyList<NewKanizuLevelSpec> levels) =>
        string.Join(
            "\n",
            (levels ?? Array.Empty<NewKanizuLevelSpec>()).Select(l => string.Format(
                CultureInfo.InvariantCulture,
                "{0}, {1}, {2:0.##}, {3}",
                l.Name,
                l.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                l.ElevationMeters,
                l.ShortName)));

    /// <summary>Parses multi-line <c>Name, Ordinal, ElevationMeters[, ShortName]</c> level text for interactive dialogs.</summary>
    public static bool TryParseLevelsText(
        string? text,
        NewKanizuProjectLanguage language,
        out List<NewKanizuLevelSpec> levels,
        out string? error)
    {
        levels = new List<NewKanizuLevelSpec>();
        error = null;

        string[] lines = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length < 3)
            {
                error = NewKanizuProjectStrings.Select(
                    language,
                    $"Line {i + 1}: expected 'Name, Ordinal, ElevationMeters[, ShortName]'.",
                    $"{i + 1} 行目: 「レベル名, 階層番号, 標高m[, 短縮名]」の形式で入力してください。");
                return false;
            }

            string name = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal))
            {
                error = NewKanizuProjectStrings.Select(
                    language,
                    $"Line {i + 1}: ordinal '{parts[1].Trim()}' must be an integer (e.g. -1, 0, 1).",
                    $"{i + 1} 行目: 階層番号 '{parts[1].Trim()}' は整数（例: -1, 0, 1）で入力してください。");
                return false;
            }

            if (!double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double elevMeters))
            {
                error = NewKanizuProjectStrings.Select(
                    language,
                    $"Line {i + 1}: elevation '{parts[2].Trim()}' must be a valid number in meters.",
                    $"{i + 1} 行目: 標高 '{parts[2].Trim()}' はメートル単位の数値で入力してください。");
                return false;
            }

            string shortName = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3])
                ? parts[3].Trim()
                : name;

            levels.Add(new NewKanizuLevelSpec(name, ordinal, elevMeters, shortName));
        }

        return true;
    }

    /// <summary>Normalizes a station/building slug to lowercase ASCII tokens separated by hyphens.</summary>
    public static string NormalizeSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        StringBuilder sb = new(raw!.Length);
        bool lastWasDash = true;
        foreach (char ch in raw.Trim())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (ch >= 'A' && ch <= 'Z')
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (ch == '-' || ch == '_' || char.IsWhiteSpace(ch))
            {
                if (!lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Inspects an open document snapshot for stale or duplicated project/level/element identity
    /// (for example when a user mistakenly copied an existing station <c>.rvt</c> via Save-As).
    /// </summary>
    public static StaleIdentityReport InspectStaleIdentity(StaleIdentitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return StaleIdentityReport.Clean;
        }

        List<StaleIdentityFinding> findings = new();

        if (ShowcaseExportGuard.IsShowcaseModelRole(snapshot.ExistingModelRole))
        {
            findings.Add(new StaleIdentityFinding(
                StaleIdentityIssueKind.ShowcaseModelRole,
                NewKanizuProjectValidationSeverity.Error,
                "T9-STALE-SHOWCASE",
                "Active document has gis_model_role = 'showcase'. Read-only Forma Companion showcase models cannot be initialized as authoring projects.",
                "開いているドキュメントは gis_model_role = 'showcase' です。閲覧専用の Forma Companion ショーケースモデルをオーサリングプロジェクトとして初期化することはできません。"));
        }

        string existingModelId = (snapshot.ExistingModelId ?? string.Empty).Trim();
        if (existingModelId.Length > 0)
        {
            HashSet<string> knownOthers = new(
                (snapshot.KnownOtherProjectModelIds ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (knownOthers.Contains(existingModelId))
            {
                findings.Add(new StaleIdentityFinding(
                    StaleIdentityIssueKind.CopiedModelIdCollision,
                    NewKanizuProjectValidationSeverity.Error,
                    "T9-STALE-MODELID-COLLISION",
                    $"Document carries ModelId '{existingModelId}', which collides with another known Kanizu project (likely copied via Save-As).",
                    $"ドキュメントの ModelId '{existingModelId}' が既存の別 Kanizu プロジェクトと重複しています（別名保存による複製の可能性があります）。"));
            }
            else
            {
                findings.Add(new StaleIdentityFinding(
                    StaleIdentityIssueKind.ExistingModelId,
                    NewKanizuProjectValidationSeverity.Warning,
                    "T9-STALE-MODELID",
                    $"Document already carries GeoProjectInfo.ModelId '{existingModelId}'. Enable ClearStaleIdentity to mint a fresh ModelId.",
                    $"ドキュメントには既に GeoProjectInfo.ModelId '{existingModelId}' が設定されています。新しい ModelId を発行するには旧 ID クリアを有効にしてください。"));
            }
        }

        string existingStationFolder = (snapshot.ExistingStationFolder ?? string.Empty).Trim();
        if (existingStationFolder.Length > 0)
        {
            findings.Add(new StaleIdentityFinding(
                StaleIdentityIssueKind.ExistingStationFolder,
                NewKanizuProjectValidationSeverity.Warning,
                "T9-STALE-STATIONFOLDER",
                "Document already carries a recorded GisStationFolder from a previous project. Clearing stale identity will unlink it.",
                "ドキュメントには別プロジェクトの GisStationFolder が記録されています。旧 ID クリアを有効にすると解除されます。"));
        }

        List<ExistingLevelIdentityEntry> levelsWithIds = (snapshot.ExistingLevels ?? Array.Empty<ExistingLevelIdentityEntry>())
            .Where(l => l != null && !string.IsNullOrWhiteSpace(l.LevelId))
            .ToList();

        if (levelsWithIds.Count > 0)
        {
            List<IGrouping<string, ExistingLevelIdentityEntry>> duplicateGroups = levelsWithIds
                .GroupBy(l => l.LevelId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (IGrouping<string, ExistingLevelIdentityEntry> group in duplicateGroups)
            {
                string levelNames = string.Join(", ", group.Select(l => l.LevelName));
                findings.Add(new StaleIdentityFinding(
                    StaleIdentityIssueKind.DuplicateLevelId,
                    NewKanizuProjectValidationSeverity.Error,
                    "T9-STALE-DUP-LEVELID",
                    $"Duplicate level_id '{group.Key}' shared across levels ({levelNames}). Every level must carry a unique level_id.",
                    $"level_id '{group.Key}' が複数のレベル（{levelNames}）で重複しています。各レベルには固有の level_id が必要です。",
                    group.Select(l => l.ElementId)));
            }

            findings.Add(new StaleIdentityFinding(
                StaleIdentityIssueKind.ExistingLevelId,
                NewKanizuProjectValidationSeverity.Warning,
                "T9-STALE-LEVELID",
                $"{levelsWithIds.Count} existing level(s) already carry level_id values. Enable ClearStaleIdentity to mint fresh unique level_ids.",
                $"既存の {levelsWithIds.Count} レベルに level_id が既に設定されています。新しい固有 level_id を発行するには旧 ID クリアを有効にしてください。",
                levelsWithIds.Select(l => l.ElementId)));
        }

        if (snapshot.ElementsWithIdentityCount > 0)
        {
            findings.Add(new StaleIdentityFinding(
                StaleIdentityIssueKind.ExistingElementIdentity,
                NewKanizuProjectValidationSeverity.Warning,
                "T9-STALE-ELEMENTID",
                $"{snapshot.ElementsWithIdentityCount} placed element(s) carry existing id or gis_key values. Enable ClearStaleIdentity to clear copied element IDs.",
                $"配置済みの {snapshot.ElementsWithIdentityCount} 要素に既存の id または gis_key が設定されています。複製された要素 ID を消去するには旧 ID クリアを有効にしてください。",
                snapshot.ElementsWithIdentitySample));
        }

        return new StaleIdentityReport(findings);
    }

    /// <summary>
    /// Validates <paramref name="options"/>, inspects <paramref name="existingDocument"/> for stale identity,
    /// mints guaranteed-unique fresh <c>ModelId</c> and per-level <c>level_id</c>s, and returns an immutable
    /// <see cref="NewKanizuProjectPlan"/> ready for <c>NewKanizuProjectBootstrapper</c>.
    /// </summary>
    public static NewKanizuProjectPlan Plan(
        NewKanizuProjectOptions options,
        StaleIdentitySnapshot? existingDocument = null,
        StarterKitSpec? kitSpec = null,
        Func<Guid>? newGuid = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        StarterKitSpec effectiveSpec = kitSpec ?? LoadDefaultSpec();
        Func<Guid> guidFactory = newGuid ?? Guid.NewGuid;

        List<NewKanizuProjectValidationFinding> findings = new();
        List<ProjectInitializationCheckItem> preFlightItems = new();

        string stationName = (options.StationName ?? string.Empty).Trim();
        string stationCode = (options.StationCode ?? string.Empty).Trim();
        string buildingSlug = NormalizeSlug(options.BuildingSlug);
        string kitVersion = string.IsNullOrWhiteSpace(options.KitVersion)
            ? effectiveSpec.Header.KitVersion
            : options.KitVersion!.Trim();
        int gisStandardVersion = options.GisStandardVersion ?? effectiveSpec.Header.GisStandardVersion;

        // 1. Station metadata validation
        if (stationName.Length == 0)
        {
            AddError(
                findings,
                "T9-STATION-001",
                nameof(options.StationName),
                "Station name is required to initialize a new Kanizu project.",
                "新規 Kanizu プロジェクトを初期化するには駅名の入力が必要です。");
        }

        // 2. Model role lock: MUST be authoring, NEVER showcase
        string rawModelRole = (options.ModelRole ?? string.Empty).Trim();
        bool roleValid = true;
        if (ShowcaseExportGuard.IsShowcaseModelRole(rawModelRole))
        {
            roleValid = false;
            AddError(
                findings,
                "T9-ROLE-001",
                nameof(options.ModelRole),
                "gis_model_role = 'showcase' is forbidden for New Kanizu Project. Authoring projects are locked to 'authoring'; use Forma Companion Builder for showcase models.",
                "新規 Kanizu プロジェクトでは gis_model_role = 'showcase' を指定できません。オーサリングプロジェクトは常に 'authoring' に固定されます。ショーケースモデルには Forma Companion ビルダーを使用してください。");
        }
        else if (rawModelRole.Length > 0 &&
                 !string.Equals(rawModelRole, ShowcaseExportGuard.AuthoringModelRole, StringComparison.OrdinalIgnoreCase))
        {
            roleValid = false;
            AddError(
                findings,
                "T9-ROLE-002",
                nameof(options.ModelRole),
                $"Invalid gis_model_role '{rawModelRole}'. New Kanizu Project only permits 'authoring'.",
                $"gis_model_role の値 '{rawModelRole}' は無効です。新規 Kanizu プロジェクトでは 'authoring' のみ許可されます。");
        }

        preFlightItems.Add(new ProjectInitializationCheckItem(
            "T9-PRE-ROLE",
            roleValid ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Authoring model role locked",
            "オーサリング役割（authoring）の固定確認",
            roleValid
                ? "Project will be stamped with gis_model_role = 'authoring'."
                : "Requested model role is rejected; only 'authoring' is allowed.",
            roleValid
                ? "プロジェクトに gis_model_role = 'authoring' を設定します。"
                : "指定されたモデル役割は許可されません（'authoring' のみ可）。"));

        // 3. Kit & Standard version validation
        if (string.IsNullOrWhiteSpace(kitVersion))
        {
            AddError(
                findings,
                "T9-KIT-001",
                nameof(options.KitVersion),
                "Starter Kit version (gis_kit_version) cannot be empty.",
                "スターターキットバージョン（gis_kit_version）は空にできません。");
        }

        if (gisStandardVersion < 3)
        {
            AddError(
                findings,
                "T9-STD-001",
                nameof(options.GisStandardVersion),
                $"Starter Kit projects require gis_standard_version >= 3 (found {gisStandardVersion}).",
                $"スターターキットプロジェクトは gis_standard_version >= 3 が必要です（指定値: {gisStandardVersion}）。");
        }

        // 4. Template path validation (when creating from .rte)
        string? templatePath = string.IsNullOrWhiteSpace(options.TemplatePath) ? null : options.TemplatePath!.Trim();
        if (options.InitializationMode == NewKanizuProjectInitializationMode.CreateFromTemplate &&
            templatePath != null &&
            !templatePath.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                findings,
                "T9-TPL-001",
                nameof(options.TemplatePath),
                $"Template path '{templatePath}' must be a Revit .rte template file.",
                $"テンプレートパス '{templatePath}' は Revit の .rte ファイルである必要があります。");
        }

        // 5. Stale identity inspection on existing document
        StaleIdentityReport staleReport = InspectStaleIdentity(existingDocument);
        bool shouldClearStaleIdentity = false;
        if (staleReport.IsShowcaseModel)
        {
            AddError(
                findings,
                "T9-STALE-SHOWCASE",
                nameof(existingDocument),
                "Cannot initialize a Forma Companion showcase document (gis_model_role = 'showcase') as a Kanizu authoring project.",
                "Forma Companion ショーケースモデル（gis_model_role = 'showcase'）をオーサリングプロジェクトとして初期化することはできません。");
            preFlightItems.Add(new ProjectInitializationCheckItem(
                "T9-PRE-STALE",
                ProjectInitializationCheckStatus.Fail,
                "Zero stale identity hygiene",
                "旧 ID 混入チェック",
                "Active document is a read-only showcase model and cannot be reinitialized.",
                "アクティブモデルは閲覧専用ショーケースモデルのため初期化できません。"));
        }
        else if (staleReport.HasStaleIdentity)
        {
            if (options.ClearStaleIdentity)
            {
                shouldClearStaleIdentity = true;
                AddWarning(
                    findings,
                    "T9-STALE-CLEARING",
                    nameof(options.ClearStaleIdentity),
                    "Stale project/level/element identities detected and will be cleared before minting fresh unique IDs.",
                    "既存のプロジェクト／レベル／要素 ID を検出しました。初期化時にこれらを消去し、新しい固有 ID を発行します。");
                preFlightItems.Add(new ProjectInitializationCheckItem(
                    "T9-PRE-STALE",
                    ProjectInitializationCheckStatus.Warn,
                    "Zero stale identity hygiene",
                    "旧 ID 混入チェック",
                    "Stale identity detected; explicit opt-in ClearStaleIdentity=true will purge old IDs and mint fresh ones.",
                    "旧 ID を検出しましたが、旧 ID クリアが有効なため消去後に新規発行します。"));
            }
            else
            {
                AddError(
                    findings,
                    "T9-STALE-001",
                    nameof(options.ClearStaleIdentity),
                    "Document already contains stale project/level/element identity (possible Save-As copy). Set ClearStaleIdentity = true to purge old IDs and mint fresh unique identities.",
                    "ドキュメントに既存のプロジェクト／レベル／要素 ID が残っています（既存 .rvt の別名保存の可能性があります）。旧 ID を消去して新規発行するには ClearStaleIdentity を有効にしてください。");
                preFlightItems.Add(new ProjectInitializationCheckItem(
                    "T9-PRE-STALE",
                    ProjectInitializationCheckStatus.Fail,
                    "Zero stale identity hygiene",
                    "旧 ID 混入チェック",
                    "Stale identity detected without ClearStaleIdentity opt-in.",
                    "旧 ID クリア未選択のまま既存 ID が検出されました。"));
            }
        }
        else
        {
            preFlightItems.Add(new ProjectInitializationCheckItem(
                "T9-PRE-STALE",
                ProjectInitializationCheckStatus.Pass,
                "Zero stale identity hygiene",
                "旧 ID 混入チェック",
                "Zero stale project, level, or element identity detected prior to initialization.",
                "初期化前のプロジェクト・レベル・要素に旧 ID の残存はありません。"));
        }

        // 6. Federation mode validation
        string? hubModelId = string.IsNullOrWhiteSpace(options.HubModelId) ? null : options.HubModelId!.Trim();
        if (options.FederationMode == KanizuFederationMode.Spoke && string.IsNullOrWhiteSpace(hubModelId))
        {
            AddError(
                findings,
                "T9-FED-001",
                nameof(options.HubModelId),
                "FederationMode 'Spoke' requires a non-empty HubModelId.",
                "フェデレーション構成 'Spoke' では HubModelId の指定が必須です。");
        }

        // 7. Forbidden ID set (guarantee minted IDs NEVER collide with stale/template/sibling/hub IDs)
        HashSet<string> usedOrForbiddenIds = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(existingDocument?.ExistingModelId))
        {
            usedOrForbiddenIds.Add(existingDocument!.ExistingModelId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(hubModelId))
        {
            usedOrForbiddenIds.Add(hubModelId!);
        }

        if (existingDocument?.KnownOtherProjectModelIds != null)
        {
            foreach (string knownId in existingDocument.KnownOtherProjectModelIds)
            {
                if (!string.IsNullOrWhiteSpace(knownId))
                {
                    usedOrForbiddenIds.Add(knownId.Trim());
                }
            }
        }

        if (existingDocument?.ExistingLevels != null)
        {
            foreach (ExistingLevelIdentityEntry entry in existingDocument.ExistingLevels)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.LevelId))
                {
                    usedOrForbiddenIds.Add(entry.LevelId.Trim());
                }
            }
        }

        // Mint fresh unique ModelId
        string freshModelId = MintUniqueId(guidFactory, usedOrForbiddenIds, findings, "ModelId");

        // 8. Validate and plan levels + mint fresh unique level_ids
        IReadOnlyList<NewKanizuLevelSpec> rawLevels = options.Levels ?? NewKanizuProjectOptions.DefaultLevels;
        List<PlannedKanizuLevel> plannedLevels = ValidateAndPlanLevels(
            rawLevels,
            guidFactory,
            usedOrForbiddenIds,
            findings);

        bool levelsValid = !findings.Any(f => f.Code.StartsWith("T9-LVL-", StringComparison.Ordinal));
        preFlightItems.Add(new ProjectInitializationCheckItem(
            "T9-PRE-LEVELS",
            levelsValid ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Level definitions & monotonic elevations",
            "レベル定義と標高の単調増加チェック",
            levelsValid
                ? $"{plannedLevels.Count} level(s) validated with unique names, ordinals, and strictly monotonic elevations."
                : "One or more level definitions failed validation (names, ordinals, or elevations).",
            levelsValid
                ? $"{plannedLevels.Count} レベルの名前・階層番号・標高順序を検証しました。"
                : "レベル定義（名前・階層番号・標高）にエラーがあります。"));

        // 9. Plan per-level views (GIS Export, GIS Working Plan, GIS Area Plan)
        List<PlannedKanizuView> plannedViews = BuildPlannedViews(
            plannedLevels,
            options.CreateExportPlanViews,
            options.CreateWorkingPlanViews,
            options.CreateAreaPlanViews);

        // 10. Plan worksets + non-hiding QA workset view filters (ONLY when worksharing is enabled)
        PlannedWorksetSetup worksetPlan = BuildWorksetPlan(options, findings);
        bool worksetValid = !findings.Any(f => f.Code.StartsWith("T9-WS-", StringComparison.Ordinal));
        preFlightItems.Add(new ProjectInitializationCheckItem(
            "T9-PRE-WORKSETS",
            worksetValid ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Worksharing & KZ-VIEW-004 workset guardrails",
            "ワークシェアリングと KZ-VIEW-004 表示保護チェック",
            worksetPlan.EnableWorksharing
                ? $"Worksharing enabled: {worksetPlan.Worksets.Count} discipline workset(s) and {worksetPlan.WorksetViewFilters.Count} non-hiding QA filter(s) planned."
                : "Non-workshared mode: zero worksets planned (standard worksets remain deferred until worksharing is enabled).",
            worksetPlan.EnableWorksharing
                ? $"ワークシェアリング有効: {worksetPlan.Worksets.Count} ワークセットと {worksetPlan.WorksetViewFilters.Count} 個の非表示なし QA フィルタを計画しました。"
                : "非ワークシェアリングモード: ワークセットは作成しません（ワークシェアリング有効化時に作成可能）。"));

        // 11. Build ProjectInformation stamps
        Dictionary<string, string> projectInfoStamps = new(StringComparer.Ordinal)
        {
            [GisSharedParameters.GisModelRole] = ShowcaseExportGuard.AuthoringModelRole,
            [GisSharedParameters.GisKitVersion] = kitVersion,
            [GisSharedParameters.GisStandardVersion] = gisStandardVersion.ToString(CultureInfo.InvariantCulture),
        };

        string projectKey = BuildProjectKey(stationCode, stationName, buildingSlug);

        ProjectInitializationReadinessSummary preFlightReadiness = new("pre-flight", preFlightItems);

        // 12. Build expected post-init readiness summary
        ProjectInitializationReadinessSummary expectedPostInitReadiness = BuildExpectedPostInitReadiness(
            freshModelId,
            ShowcaseExportGuard.AuthoringModelRole,
            kitVersion,
            gisStandardVersion,
            plannedLevels,
            worksetPlan,
            findings.All(f => f.Severity != NewKanizuProjectValidationSeverity.Error));

        string summaryEn = NewKanizuProjectStrings.FormatPlanSummary(
            NewKanizuProjectLanguage.English,
            freshModelId,
            stationName.Length > 0 ? stationName : "(unnamed station)",
            buildingSlug,
            kitVersion,
            gisStandardVersion,
            options.FederationMode,
            plannedLevels,
            plannedViews,
            worksetPlan,
            shouldClearStaleIdentity);

        string summaryJa = NewKanizuProjectStrings.FormatPlanSummary(
            NewKanizuProjectLanguage.Japanese,
            freshModelId,
            stationName.Length > 0 ? stationName : "(駅名未設定)",
            buildingSlug,
            kitVersion,
            gisStandardVersion,
            options.FederationMode,
            plannedLevels,
            plannedViews,
            worksetPlan,
            shouldClearStaleIdentity);

        return new NewKanizuProjectPlan(
            freshModelId,
            stationName,
            stationCode,
            buildingSlug,
            projectKey,
            ShowcaseExportGuard.AuthoringModelRole,
            kitVersion,
            gisStandardVersion,
            options.FederationMode,
            hubModelId,
            options.InitializationMode,
            templatePath,
            shouldClearStaleIdentity,
            projectInfoStamps,
            plannedLevels,
            plannedViews,
            worksetPlan,
            staleReport,
            preFlightReadiness,
            expectedPostInitReadiness,
            findings,
            summaryEn,
            summaryJa);
    }

    /// <summary>
    /// Evaluates post-initialization readiness against a live or simulated <see cref="ReadinessCheckSnapshot"/>
    /// (running <see cref="ProjectReadinessEvaluator.Evaluate"/> when a snapshot is supplied) and verifies every
    /// T9 identity/hygiene invariant.
    /// </summary>
    public static ProjectInitializationReadinessSummary EvaluatePostInitReadiness(
        NewKanizuProjectPlan plan,
        ReadinessCheckSnapshot? postInitSnapshot = null,
        IReadOnlyList<ExistingLevelIdentityEntry>? actualLevels = null,
        string? actualKitVersion = null,
        GisToolStandard? standard = null)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        List<ProjectInitializationCheckItem> items = new();

        string observedModelId = postInitSnapshot != null
            ? (postInitSnapshot.HostModelId ?? string.Empty).Trim()
            : plan.ModelId;
        bool modelIdOk = !string.IsNullOrWhiteSpace(observedModelId)
            && string.Equals(observedModelId, plan.ModelId, StringComparison.OrdinalIgnoreCase);

        items.Add(new ProjectInitializationCheckItem(
            "T9-POST-MODELID",
            modelIdOk ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Fresh unique ModelId stamped",
            "新規 ModelId の刻印確認",
            modelIdOk
                ? $"GeoProjectInfo.ModelId matches newly minted '{plan.ModelId}'."
                : $"Expected GeoProjectInfo.ModelId '{plan.ModelId}', found '{observedModelId}'.",
            modelIdOk
                ? $"GeoProjectInfo.ModelId に新規発行 ID '{plan.ModelId}' が設定されています。"
                : $"GeoProjectInfo.ModelId が一致しません（期待値: '{plan.ModelId}'、実際: '{observedModelId}'）。"));

        string observedRole = postInitSnapshot != null
            ? (postInitSnapshot.HostModelRole ?? string.Empty).Trim()
            : plan.ModelRole;
        bool roleOk = string.Equals(
            ShowcaseExportGuard.NormalizeModelRole(observedRole),
            ShowcaseExportGuard.AuthoringModelRole,
            StringComparison.Ordinal);

        items.Add(new ProjectInitializationCheckItem(
            "T9-POST-ROLE",
            roleOk ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "gis_model_role = authoring",
            "gis_model_role = authoring の確認",
            roleOk
                ? "Project Information carries gis_model_role = 'authoring'."
                : $"Expected gis_model_role = 'authoring', found '{observedRole}'.",
            roleOk
                ? "プロジェクト情報に gis_model_role = 'authoring' が設定されています。"
                : $"gis_model_role が 'authoring' ではありません（実際: '{observedRole}'）。"));

        string observedKitVersion = actualKitVersion != null ? actualKitVersion.Trim() : plan.KitVersion;
        bool kitOk = string.Equals(observedKitVersion, plan.KitVersion, StringComparison.Ordinal);
        items.Add(new ProjectInitializationCheckItem(
            "T9-POST-KIT",
            kitOk ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "gis_kit_version stamped",
            "gis_kit_version の刻印確認",
            kitOk
                ? $"Project Information carries gis_kit_version = '{plan.KitVersion}'."
                : $"Expected gis_kit_version = '{plan.KitVersion}', found '{observedKitVersion}'.",
            kitOk
                ? $"プロジェクト情報に gis_kit_version = '{plan.KitVersion}' が設定されています。"
                : $"gis_kit_version が一致しません（期待値: '{plan.KitVersion}'、実際: '{observedKitVersion}'）。"));

        // Verify unique non-empty level_ids across all initialized levels
        IReadOnlyList<(string Name, string LevelId)> levelPairs = actualLevels != null
            ? actualLevels.Select(l => (l.LevelName, l.LevelId)).ToList()
            : plan.Levels.Select(l => (l.Name, l.LevelId)).ToList();

        int missingLevelIds = levelPairs.Count(p => string.IsNullOrWhiteSpace(p.LevelId));
        int duplicateLevelIds = levelPairs
            .Where(p => !string.IsNullOrWhiteSpace(p.LevelId))
            .GroupBy(p => p.LevelId, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);
        bool levelsOk = levelPairs.Count > 0 && missingLevelIds == 0 && duplicateLevelIds == 0;

        items.Add(new ProjectInitializationCheckItem(
            "T9-POST-LEVELIDS",
            levelsOk ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Unique level_id on every level",
            "全レベルの固有 level_id 確認",
            levelsOk
                ? $"All {levelPairs.Count} level(s) carry distinct, non-empty level_ids."
                : $"Level identity issue: {missingLevelIds} missing level_id(s), {duplicateLevelIds} duplicate level_id group(s).",
            levelsOk
                ? $"全 {levelPairs.Count} レベルに重複のない固有 level_id が設定されています。"
                : $"レベル ID に不備があります（未設定: {missingLevelIds} 件、重複グループ: {duplicateLevelIds} 件）。"));

        // Verify zero hide filters on export views (KZ-VIEW-004)
        bool hideFiltersClear = postInitSnapshot == null
            || (postInitSnapshot.ExportViewHideFilterNames ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n) && !ShowcaseExportGuard.IsReservedShowcaseFilterName(n))
                .Count() == 0;

        items.Add(new ProjectInitializationCheckItem(
            "T9-POST-VIEWGUARD",
            hideFiltersClear ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
            "Export view visibility guardrail (KZ-VIEW-004)",
            "エクスポートビュー非表示フィルタ保護（KZ-VIEW-004）",
            hideFiltersClear
                ? "Zero visibility-hiding parameter filters attached to GIS Export or GIS_* plan views."
                : "Detected one or more visibility-hiding parameter filters on GIS Export or GIS_* plan views.",
            hideFiltersClear
                ? "GIS Export テンプレートおよび GIS_* ビューに非表示フィルタはありません。"
                : "GIS Export テンプレートまたは GIS_* ビューに非表示フィルタが検出されました。"));

        ProjectReadinessReport? readinessReport = null;
        if (postInitSnapshot != null)
        {
            readinessReport = ProjectReadinessEvaluator.Evaluate(postInitSnapshot, ReadinessMode.Project, standard);

            // In a brand-new un-georeferenced project, only project-crs is expected to be pending until Georeference Setup runs.
            List<ReadinessCheckItem> nonCrsFails = readinessReport.Groups
                .SelectMany(g => g.Items)
                .Where(i => i.Status == ReadinessCheckStatus.Fail && !string.Equals(i.Id, "project-crs", StringComparison.Ordinal))
                .ToList();
            bool crsPending = readinessReport.Groups
                .SelectMany(g => g.Items)
                .Any(i => string.Equals(i.Id, "project-crs", StringComparison.Ordinal) && i.Status == ReadinessCheckStatus.Fail);

            ProjectInitializationCheckStatus readinessStatus = nonCrsFails.Count > 0
                ? ProjectInitializationCheckStatus.Fail
                : crsPending
                    ? ProjectInitializationCheckStatus.Warn
                    : ProjectInitializationCheckStatus.Pass;

            string readinessDetailEn = nonCrsFails.Count > 0
                ? $"TemplateReadinessCheck reported {nonCrsFails.Count} non-CRS blocking item(s): {string.Join(", ", nonCrsFails.Select(i => i.RuleId))}."
                : crsPending
                    ? $"TemplateReadinessCheck passed all template/identity/view checks (score {readinessReport.ScorePercent}%); next step: run Georeference Setup to set Japan Plane Rectangular CRS (EPSG:6669-6687)."
                    : $"TemplateReadinessCheck passed (score {readinessReport.ScorePercent}%).";

            string readinessDetailJa = nonCrsFails.Count > 0
                ? $"TemplateReadinessCheck で CRS 以外の必須エラーが {nonCrsFails.Count} 件検出されました: {string.Join(", ", nonCrsFails.Select(i => i.RuleId))}。"
                : crsPending
                    ? $"テンプレート・ID・ビューの全チェックに合格しました（スコア {readinessReport.ScorePercent}%）。次のステップとして測地設定（Georeference Setup）で平面直角座標系（EPSG:6669-6687）を設定してください。"
                    : $"TemplateReadinessCheck に合格しました（スコア {readinessReport.ScorePercent}%）。";

            items.Add(new ProjectInitializationCheckItem(
                "T9-POST-READINESS",
                readinessStatus,
                "TemplateReadinessCheck verification",
                "TemplateReadinessCheck レディネス検証",
                readinessDetailEn,
                readinessDetailJa));
        }

        return new ProjectInitializationReadinessSummary("post-init", items, readinessReport);
    }

    private static List<PlannedKanizuLevel> ValidateAndPlanLevels(
        IReadOnlyList<NewKanizuLevelSpec> rawLevels,
        Func<Guid> guidFactory,
        HashSet<string> usedIds,
        List<NewKanizuProjectValidationFinding> findings)
    {
        if (rawLevels.Count == 0)
        {
            AddError(
                findings,
                "T9-LVL-001",
                nameof(NewKanizuProjectOptions.Levels),
                "At least one level is required to initialize a Kanizu project.",
                "Kanizu プロジェクトの初期化には 1 つ以上のレベルが必要です。");
            return new List<PlannedKanizuLevel>();
        }

        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenShortNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<int> seenOrdinals = new();
        List<(string Name, string ShortName, int Ordinal, double ElevationMeters)> validEntries = new();

        for (int index = 0; index < rawLevels.Count; index++)
        {
            NewKanizuLevelSpec? spec = rawLevels[index];
            string field = $"Levels[{index}]";
            if (spec is null)
            {
                AddError(
                    findings,
                    "T9-LVL-002",
                    field,
                    $"Level entry at index {index} cannot be null.",
                    $"インデックス {index} のレベル定義が null です。");
                continue;
            }

            string name = (spec.Name ?? string.Empty).Trim();
            string shortName = string.IsNullOrWhiteSpace(spec.ShortName) ? name : spec.ShortName.Trim();
            bool rowOk = true;

            if (name.Length == 0)
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-002",
                    field,
                    $"Level at index {index} must have a non-empty name.",
                    $"インデックス {index} のレベル名は空にできません。");
            }
            else if (TryFindForbiddenRevitNameChar(name, out char badNameChar))
            {
                rowOk = false;
                string charDesc = char.IsControl(badNameChar)
                    ? $"U+{((int)badNameChar):X4}"
                    : $"'{badNameChar}'";
                AddError(
                    findings,
                    "T9-LVL-010",
                    field,
                    $"Level name '{name}' contains Revit-forbidden character {charDesc} (disallowed in Level/View names: \\ : {{ }} [ ] | ; < > ? ` ~ and control characters).",
                    $"レベル名 '{name}' に Revit の禁止文字 {charDesc} が含まれています（レベル／ビュー名では \\ : {{ }} [ ] | ; < > ? ` ~ および制御文字は使用できません）。");
            }
            else if (!seenNames.Add(name))
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-003",
                    field,
                    $"Duplicate level name '{name}'. Level names must be unique.",
                    $"レベル名 '{name}' が重複しています。レベル名は一意である必要があります。");
            }

            if (shortName.Length > 0 && !seenShortNames.Add(shortName))
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-004",
                    field,
                    $"Duplicate level short_name '{shortName}'. Level short names must be unique.",
                    $"レベル短縮名（short_name）'{shortName}' が重複しています。短縮名は一意である必要があります。");
            }

            if (!IsFiniteDouble(spec.ElevationMeters))
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-005",
                    field,
                    $"Level '{name}' elevation must be a finite number (NaN/Infinity is not allowed).",
                    $"レベル '{name}' の標高は有限の数値である必要があります（NaN／無限大は不可）。");
            }
            else if (spec.ElevationMeters < MinElevationMeters || spec.ElevationMeters > MaxElevationMeters)
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-006",
                    field,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Level '{0}' elevation {1:0.###} m is outside the supported range ({2:0} m to {3:0} m).",
                        name,
                        spec.ElevationMeters,
                        MinElevationMeters,
                        MaxElevationMeters),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "レベル '{0}' の標高 {1:0.###} m は許容範囲（{2:0} m ～ {3:0} m）外です。",
                        name,
                        spec.ElevationMeters,
                        MinElevationMeters,
                        MaxElevationMeters));
            }

            if (!spec.Ordinal.HasValue)
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-007",
                    field,
                    $"Level '{name}' is missing an explicit integer ordinal (e.g. -1 for B1F, 0 for 1F, 1 for 2F).",
                    $"レベル '{name}' に階層番号（ordinal: 例 B1F=-1, 1F=0, 2F=1）が指定されていません。");
            }
            else if (!seenOrdinals.Add(spec.Ordinal.Value))
            {
                rowOk = false;
                AddError(
                    findings,
                    "T9-LVL-008",
                    field,
                    $"Duplicate level ordinal {spec.Ordinal.Value} on level '{name}'. Every level must have a unique ordinal.",
                    $"レベル '{name}' の階層番号（ordinal）{spec.Ordinal.Value} が重複しています。階層番号は一意である必要があります。");
            }

            if (rowOk)
            {
                validEntries.Add((name, shortName, spec.Ordinal!.Value, spec.ElevationMeters));
            }
        }

        // Sort ascending by ordinal (so negative basement ordinals come first, e.g. -2, -1, 0, 1, 2)
        // and verify elevations are strictly monotonically increasing with ordinal.
        List<(string Name, string ShortName, int Ordinal, double ElevationMeters)> ordered = validEntries
            .OrderBy(e => e.Ordinal)
            .ToList();

        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var curr = ordered[i];
            if (curr.ElevationMeters <= prev.ElevationMeters + ElevationEqualityToleranceMeters)
            {
                AddError(
                    findings,
                    "T9-LVL-009",
                    nameof(NewKanizuProjectOptions.Levels),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Level elevations must increase strictly with ordinal: '{0}' (ordinal {1}, {2:0.###} m) is not above '{3}' (ordinal {4}, {5:0.###} m).",
                        curr.Name,
                        curr.Ordinal,
                        curr.ElevationMeters,
                        prev.Name,
                        prev.Ordinal,
                        prev.ElevationMeters),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "レベル標高は階層番号（ordinal）の昇順に厳密増加する必要があります: '{0}' (ordinal {1}, {2:0.###} m) が '{3}' (ordinal {4}, {5:0.###} m) より高くありません。",
                        curr.Name,
                        curr.Ordinal,
                        curr.ElevationMeters,
                        prev.Name,
                        prev.Ordinal,
                        prev.ElevationMeters));
            }
        }

        List<PlannedKanizuLevel> planned = new(ordered.Count);
        foreach (var entry in ordered)
        {
            string freshLevelId = MintUniqueId(guidFactory, usedIds, findings, $"level_id({entry.Name})");
            planned.Add(new PlannedKanizuLevel(
                entry.Name,
                entry.ShortName,
                entry.Ordinal,
                entry.ElevationMeters,
                freshLevelId,
                GisModelFile.GisViewPrefix + entry.Name,
                WorkingPlanViewPrefix + entry.Name,
                AreaPlanViewPrefix + entry.Name));
        }

        return planned;
    }

    private static List<PlannedKanizuView> BuildPlannedViews(
        IReadOnlyList<PlannedKanizuLevel> levels,
        bool createExportPlanViews,
        bool createWorkingPlanViews,
        bool createAreaPlanViews)
    {
        List<PlannedKanizuView> views = new();
        foreach (PlannedKanizuLevel level in levels)
        {
            if (createExportPlanViews)
            {
                views.Add(new PlannedKanizuView(
                    level.ExportPlanViewName,
                    level.Name,
                    level.LevelId,
                    PlannedKanizuViewKind.GisExportPlan,
                    TemplateStandardDefinitions.ExportPlanViewTemplateName));
            }

            if (createWorkingPlanViews)
            {
                views.Add(new PlannedKanizuView(
                    level.WorkingPlanViewName,
                    level.Name,
                    level.LevelId,
                    PlannedKanizuViewKind.WorkingPlan,
                    WorkingPlanTemplateName));
            }

            if (createAreaPlanViews)
            {
                views.Add(new PlannedKanizuView(
                    level.AreaPlanViewName,
                    level.Name,
                    level.LevelId,
                    PlannedKanizuViewKind.GisAreaPlan,
                    viewTemplateName: null,
                    areaSchemeName: TemplateAreaColorSchemePlan.DefaultAreaSchemeName));
            }
        }

        return views;
    }

    private static PlannedWorksetSetup BuildWorksetPlan(
        NewKanizuProjectOptions options,
        List<NewKanizuProjectValidationFinding> findings)
    {
        if (!options.EnableWorksharing)
        {
            return new PlannedWorksetSetup(
                enableWorksharing: false,
                defaultSharedLevelsWorksetName: string.Empty,
                defaultActiveWorksetName: string.Empty,
                worksets: Array.Empty<KanizuWorksetSpec>(),
                worksetViewFilters: Array.Empty<PlannedWorksetViewFilter>(),
                fallbackNoteEn: NewKanizuProjectStrings.NonWorksharedFallbackEn,
                fallbackNoteJa: NewKanizuProjectStrings.NonWorksharedFallbackJa);
        }

        IReadOnlyList<KanizuWorksetSpec> requested = options.Worksets is { Count: > 0 }
            ? options.Worksets
            : KanizuWorksetSpec.StandardWorksets;

        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        List<KanizuWorksetSpec> validatedWorksets = new();
        List<PlannedWorksetViewFilter> filters = new();

        foreach (KanizuWorksetSpec? ws in requested)
        {
            if (ws is null || string.IsNullOrWhiteSpace(ws.Name))
            {
                AddError(
                    findings,
                    "T9-WS-001",
                    nameof(options.Worksets),
                    "Workset name cannot be empty.",
                    "ワークセット名は空にできません。");
                continue;
            }

            string trimmedName = ws.Name.Trim();
            if (!seenNames.Add(trimmedName))
            {
                AddError(
                    findings,
                    "T9-WS-002",
                    nameof(options.Worksets),
                    $"Duplicate workset name '{trimmedName}'.",
                    $"ワークセット名 '{trimmedName}' が重複しています。");
                continue;
            }

            if (!ws.VisibleInGisExportViews)
            {
                AddError(
                    findings,
                    "T9-WS-003",
                    nameof(options.Worksets),
                    $"Workset '{trimmedName}' must keep VisibleInGisExportViews = true to satisfy KZ-VIEW-004.",
                    $"KZ-VIEW-004 に準拠するため、ワークセット '{trimmedName}' は GIS エクスポートビューで表示（VisibleInGisExportViews = true）を維持する必要があります。");
                continue;
            }

            validatedWorksets.Add(ws);

            (byte r, byte g, byte b) = ParseHexSafe(ws.QaOverrideColorHex);
            string filterName = WorksetFilterPrefix + trimmedName;
            // Attach non-hiding workset highlight filters ONLY to Working/QA templates (never GIS Export).
            filters.Add(new PlannedWorksetViewFilter(
                filterName,
                trimmedName,
                ws.Categories.Count > 0 ? ws.Categories : new[] { "OST_Floors", "OST_GenericModel" },
                WorksetFilterTargetTemplates,
                ws.QaOverrideColorHex,
                r,
                g,
                b,
                visible: true));
        }

        string defaultActive = validatedWorksets.FirstOrDefault()?.Name ?? KanizuWorksetSpec.GisAreasName;
        return new PlannedWorksetSetup(
            enableWorksharing: true,
            defaultSharedLevelsWorksetName: DefaultSharedLevelsWorksetName,
            defaultActiveWorksetName: defaultActive,
            worksets: validatedWorksets,
            worksetViewFilters: filters,
            fallbackNoteEn: NewKanizuProjectStrings.WorksharingEnabledNoteEn,
            fallbackNoteJa: NewKanizuProjectStrings.WorksharingEnabledNoteJa);
    }

    private static ProjectInitializationReadinessSummary BuildExpectedPostInitReadiness(
        string modelId,
        string modelRole,
        string kitVersion,
        int gisStandardVersion,
        IReadOnlyList<PlannedKanizuLevel> levels,
        PlannedWorksetSetup worksetPlan,
        bool planValid)
    {
        List<ProjectInitializationCheckItem> items = new()
        {
            new ProjectInitializationCheckItem(
                "T9-EXP-MODELID",
                !string.IsNullOrWhiteSpace(modelId) ? ProjectInitializationCheckStatus.Pass : ProjectInitializationCheckStatus.Fail,
                "Unique ModelId minted",
                "固有 ModelId の発行",
                $"Will stamp fresh GeoProjectInfo.ModelId = '{modelId}'.",
                $"新規 GeoProjectInfo.ModelId = '{modelId}' を設定します。"),
            new ProjectInitializationCheckItem(
                "T9-EXP-ROLE",
                string.Equals(modelRole, ShowcaseExportGuard.AuthoringModelRole, StringComparison.Ordinal)
                    ? ProjectInitializationCheckStatus.Pass
                    : ProjectInitializationCheckStatus.Fail,
                "gis_model_role = authoring & gis_kit_version stamped",
                "gis_model_role = authoring と gis_kit_version の設定",
                $"Will stamp gis_model_role = '{modelRole}', gis_kit_version = '{kitVersion}', gis_standard_version = {gisStandardVersion}.",
                $"gis_model_role = '{modelRole}'、gis_kit_version = '{kitVersion}'、gis_standard_version = {gisStandardVersion} を設定します。"),
            new ProjectInitializationCheckItem(
                "T9-EXP-LEVELS",
                levels.Count > 0 && levels.Select(l => l.LevelId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == levels.Count
                    ? ProjectInitializationCheckStatus.Pass
                    : ProjectInitializationCheckStatus.Fail,
                "Unique level_id per level",
                "各レベルの固有 level_id",
                $"Will assign {levels.Count} distinct level_id(s) with ordinals and short names.",
                $"{levels.Count} レベルすべてに固有の level_id・ordinal・short_name を割り当てます。"),
            new ProjectInitializationCheckItem(
                "T9-EXP-WORKSETS",
                worksetPlan.WorksetViewFilters.All(f => f.Visible && !f.TargetViewTemplateNames.Contains(TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparer.OrdinalIgnoreCase))
                    ? ProjectInitializationCheckStatus.Pass
                    : ProjectInitializationCheckStatus.Fail,
                "Workset & export view guardrail (KZ-VIEW-004)",
                "ワークセットおよびエクスポートビュー保護（KZ-VIEW-004）",
                worksetPlan.EnableWorksharing
                    ? $"Will create {worksetPlan.Worksets.Count} workset(s) (all visible in GIS Export) and {worksetPlan.WorksetViewFilters.Count} non-hiding QA filter(s)."
                    : "Non-workshared fallback: zero worksets created; export views unaffected.",
                worksetPlan.EnableWorksharing
                    ? $"{worksetPlan.Worksets.Count} ワークセット（GIS Export で全表示）と {worksetPlan.WorksetViewFilters.Count} 個の非表示なし QA フィルタを作成します。"
                    : "非ワークシェアリングモードのためワークセットは作成されません。"),
        };

        if (!planValid)
        {
            items.Add(new ProjectInitializationCheckItem(
                "T9-EXP-VALID",
                ProjectInitializationCheckStatus.Fail,
                "Pre-flight validation passed",
                "事前検証の合格",
                "Resolve pre-flight validation errors before initializing the project.",
                "プロジェクト初期化を実行する前に事前検証エラーを解消してください。"));
        }

        return new ProjectInitializationReadinessSummary("expected-post-init", items);
    }

    private static string MintUniqueId(
        Func<Guid> guidFactory,
        HashSet<string> usedOrForbiddenIds,
        List<NewKanizuProjectValidationFinding> findings,
        string label)
    {
        const int maxAttempts = 16;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Guid guid = guidFactory();
            if (guid == Guid.Empty)
            {
                continue;
            }

            string candidate = guid.ToString("D").ToLowerInvariant();
            if (usedOrForbiddenIds.Add(candidate))
            {
                return candidate;
            }
        }

        // Fall back to Guid.NewGuid() if an injected deterministic factory collided with forbidden IDs,
        // while also recording a validation error so callers/tests notice a colliding generator.
        AddError(
            findings,
            "T9-ID-COLLISION",
            label,
            $"GUID generator failed to mint a unique non-colliding ID for '{label}'.",
            $"'{label}' 用の重複しない固有 ID を生成できませんでした。");
        string fallback = Guid.NewGuid().ToString("D").ToLowerInvariant();
        usedOrForbiddenIds.Add(fallback);
        return fallback;
    }

    private static string BuildProjectKey(string stationCode, string stationName, string buildingSlug)
    {
        string stationPart = !string.IsNullOrWhiteSpace(stationCode)
            ? NormalizeSlug(stationCode)
            : NormalizeSlug(stationName);
        if (stationPart.Length == 0)
        {
            stationPart = "station";
        }

        return string.IsNullOrWhiteSpace(buildingSlug)
            ? stationPart
            : $"{stationPart}-{buildingSlug}";
    }

    private static (byte R, byte G, byte B) ParseHexSafe(string hex)
    {
        try
        {
            return GisToolStandard.HexToRgb(hex);
        }
        catch (Exception)
        {
            return (0x1E, 0x88, 0xE5);
        }
    }

    private static StarterKitSpec LoadDefaultSpec()
    {
        StarterKitLoadResult loaded = StarterKitSpecLoader.LoadEmbedded();
        if (loaded.Spec is null)
        {
            throw new InvalidOperationException("Embedded Starter Kit specification could not be loaded.");
        }

        return loaded.Spec;
    }

    private static void AddError(
        List<NewKanizuProjectValidationFinding> findings,
        string code,
        string field,
        string messageEn,
        string messageJa) =>
        findings.Add(new NewKanizuProjectValidationFinding(
            NewKanizuProjectValidationSeverity.Error,
            code,
            field,
            messageEn,
            messageJa));

    private static void AddWarning(
        List<NewKanizuProjectValidationFinding> findings,
        string code,
        string field,
        string messageEn,
        string messageJa) =>
        findings.Add(new NewKanizuProjectValidationFinding(
            NewKanizuProjectValidationSeverity.Warning,
            code,
            field,
            messageEn,
            messageJa));
}
