using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Outcome for one initialized Revit <see cref="Level"/> inside <see cref="NewKanizuProjectResult"/>.</summary>
public sealed class InitializedKanizuLevelResult
{
    public InitializedKanizuLevelResult(
        long levelElementId,
        string levelName,
        string shortName,
        int ordinal,
        double elevationMeters,
        string levelId,
        bool wasCreated)
    {
        LevelElementId = levelElementId;
        LevelName = levelName ?? string.Empty;
        ShortName = shortName ?? string.Empty;
        Ordinal = ordinal;
        ElevationMeters = elevationMeters;
        LevelId = levelId ?? string.Empty;
        WasCreated = wasCreated;
    }

    public long LevelElementId { get; }

    public string LevelName { get; }

    public string ShortName { get; }

    public int Ordinal { get; }

    public double ElevationMeters { get; }

    public string LevelId { get; }

    public bool WasCreated { get; }
}

/// <summary>Structured outcome of executing <see cref="NewKanizuProjectBootstrapper"/>.</summary>
public sealed class NewKanizuProjectResult
{
    public NewKanizuProjectResult(
        bool succeeded,
        NewKanizuProjectPlan plan,
        string modelId,
        string documentTitle,
        bool createdFromTemplate,
        string? templatePath,
        bool staleIdentityCleared,
        bool worksharingEnabled,
        IReadOnlyList<InitializedKanizuLevelResult> levels,
        IReadOnlyList<string> createdWorksets,
        IReadOnlyList<string> createdViews,
        IReadOnlyList<string> appliedWorksetFilters,
        ProjectInitializationReadinessSummary postInitReadiness,
        IReadOnlyList<string> log,
        IReadOnlyList<string> warnings)
    {
        Succeeded = succeeded;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ModelId = modelId ?? string.Empty;
        DocumentTitle = documentTitle ?? string.Empty;
        CreatedFromTemplate = createdFromTemplate;
        TemplatePath = templatePath;
        StaleIdentityCleared = staleIdentityCleared;
        WorksharingEnabled = worksharingEnabled;
        Levels = Array.AsReadOnly((levels ?? Array.Empty<InitializedKanizuLevelResult>()).ToArray());
        CreatedWorksets = Array.AsReadOnly((createdWorksets ?? Array.Empty<string>()).ToArray());
        CreatedViews = Array.AsReadOnly((createdViews ?? Array.Empty<string>()).ToArray());
        AppliedWorksetFilters = Array.AsReadOnly((appliedWorksetFilters ?? Array.Empty<string>()).ToArray());
        PostInitReadiness = postInitReadiness ?? throw new ArgumentNullException(nameof(postInitReadiness));
        Log = Array.AsReadOnly((log ?? Array.Empty<string>()).ToArray());
        Warnings = Array.AsReadOnly((warnings ?? Array.Empty<string>()).ToArray());
    }

    public bool Succeeded { get; }

    public NewKanizuProjectPlan Plan { get; }

    public string ModelId { get; }

    public string DocumentTitle { get; }

    public bool CreatedFromTemplate { get; }

    public string? TemplatePath { get; }

    public bool StaleIdentityCleared { get; }

    public bool WorksharingEnabled { get; }

    public IReadOnlyList<InitializedKanizuLevelResult> Levels { get; }

    public IReadOnlyList<string> CreatedWorksets { get; }

    public IReadOnlyList<string> CreatedViews { get; }

    public IReadOnlyList<string> AppliedWorksetFilters { get; }

    public ProjectInitializationReadinessSummary PostInitReadiness { get; }

    public ProjectReadinessReport? TemplateReadinessReport => PostInitReadiness.TemplateReadinessReport;

    public IReadOnlyList<string> Log { get; }

    public IReadOnlyList<string> Warnings { get; }

    public string ToJson(bool indented = true)
    {
        JObject root = new()
        {
            ["schema"] = "kanizu.new-project-result",
            ["schemaVersion"] = 1,
            ["succeeded"] = Succeeded,
            ["modelId"] = ModelId,
            ["documentTitle"] = DocumentTitle,
            ["createdFromTemplate"] = CreatedFromTemplate,
            ["templatePath"] = TemplatePath,
            ["gisModelRole"] = Plan.ModelRole,
            ["gisKitVersion"] = Plan.KitVersion,
            ["gisStandardVersion"] = Plan.GisStandardVersion,
            ["federationMode"] = Plan.FederationMode.ToString().ToLowerInvariant(),
            ["hubModelId"] = Plan.HubModelId,
            ["staleIdentityCleared"] = StaleIdentityCleared,
            ["worksharingEnabled"] = WorksharingEnabled,
            ["postInitReady"] = PostInitReadiness.IsReady,
            ["levels"] = new JArray(Levels.Select(l => new JObject
            {
                ["elementId"] = l.LevelElementId,
                ["name"] = l.LevelName,
                ["shortName"] = l.ShortName,
                ["ordinal"] = l.Ordinal,
                ["elevationMeters"] = Math.Round(l.ElevationMeters, 4),
                ["levelId"] = l.LevelId,
                ["wasCreated"] = l.WasCreated,
            })),
            ["createdWorksets"] = new JArray(CreatedWorksets),
            ["createdViews"] = new JArray(CreatedViews),
            ["appliedWorksetFilters"] = new JArray(AppliedWorksetFilters),
            ["warnings"] = new JArray(Warnings),
            ["log"] = new JArray(Log),
        };

        return root.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }
}

/// <summary>
/// Revit API bootstrapper for T9 ("New Kanizu Project").
/// Supports BOTH creating a new project document from the Kanizu Starter Kit <c>.rte</c>
/// (<see cref="Application.NewProjectDocument(string)"/>) and initializing an already-open document,
/// minting fresh <c>ModelId</c> and unique <c>level_id</c>s, stamping <c>gis_model_role=authoring</c>,
/// <c>gis_kit_version</c>, and <c>gis_standard_version=3</c>, creating per-level GIS views, and
/// optionally enabling worksharing with Kanizu discipline worksets and non-hiding QA workset filters.
/// </summary>
public static class NewKanizuProjectBootstrapper
{
    private const string InitializeTransactionName = "Kanizu Starter Kit: New Kanizu Project";
    private const double LevelMatchToleranceFeet = 1e-3; // ~0.3 mm
    private const double LevelMoveToleranceFeet = 1e-4;  // ~0.03 mm
    private const int StaleElementSampleLimit = 16;

    /// <summary>Reads a pure-C# <see cref="StaleIdentitySnapshot"/> from an open Revit <see cref="Document"/>.</summary>
    public static StaleIdentitySnapshot ReadStaleIdentitySnapshot(
        Document document,
        IReadOnlyList<string>? knownOtherProjectModelIds = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        GeoProjectInfo? geo = TryLoadGeoProjectInfo(document);
        string modelRole = GisStandardStateReader.ReadModelRoleText(document);
        string kitVersion = ReadProjectInfoSharedText(document, GisSharedParameters.GisKitVersion);

        List<ExistingLevelIdentityEntry> existingLevels = ReadPersistedLevelIdentities(document);

        (int elemCount, List<long> elemSample) = ReadElementsWithIdentity(document);

        return new StaleIdentitySnapshot
        {
            DocumentTitle = document.Title ?? string.Empty,
            DocumentPath = document.PathName ?? string.Empty,
            ExistingModelId = geo?.ModelId?.Trim() ?? string.Empty,
            ExistingStationFolder = geo?.GisStationFolder?.Trim() ?? string.Empty,
            ExistingModelRole = modelRole,
            ExistingKitVersion = kitVersion,
            IsWorkshared = document.IsWorkshared,
            ExistingLevels = existingLevels,
            ElementsWithIdentityCount = elemCount,
            ElementsWithIdentitySample = elemSample,
            KnownOtherProjectModelIds = knownOtherProjectModelIds ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Creates a brand-new Revit project document from <see cref="NewKanizuProjectOptions.TemplatePath"/>
    /// (<c>Application.NewProjectDocument</c>), applies the T9 bootstrap plan, and optionally saves it to disk.
    /// </summary>
    public static (Document? Document, NewKanizuProjectResult Result) CreateNewProject(
        Application application,
        NewKanizuProjectOptions options,
        StarterKitSpec? kitSpec = null,
        string? saveAsRvtPath = null,
        bool closeAfterSave = false)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.TemplatePath))
        {
            throw new ArgumentException("A Starter Kit .rte template path is required to create a new project.", nameof(options));
        }

        string fullTemplatePath = Path.GetFullPath(options.TemplatePath!.Trim());
        if (!File.Exists(fullTemplatePath))
        {
            throw new FileNotFoundException($"Starter Kit .rte template not found at '{fullTemplatePath}'.", fullTemplatePath);
        }

        if (!fullTemplatePath.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Template path '{fullTemplatePath}' must be a Revit .rte template file.");
        }

        // Pre-validate options before opening a new document in Revit.
        NewKanizuProjectPlan preCheck = NewKanizuProjectPlanner.Plan(options, existingDocument: null, kitSpec: kitSpec);
        if (!preCheck.IsValid)
        {
            string errorSummary = string.Join(" ", preCheck.Errors.Select(e => e.GetMessage(options.Language)));
            throw new InvalidOperationException(errorSummary);
        }

        Document? createdDoc = application.NewProjectDocument(fullTemplatePath);
        if (createdDoc is null || createdDoc.IsFamilyDocument)
        {
            throw new InvalidOperationException($"Revit failed to create a new project document from '{fullTemplatePath}'.");
        }

        try
        {
            NewKanizuProjectResult result = InitializeDocumentCore(
                createdDoc,
                options,
                kitSpec,
                createdFromTemplate: true,
                templatePath: fullTemplatePath);

            string? targetSavePath = !string.IsNullOrWhiteSpace(saveAsRvtPath)
                ? saveAsRvtPath
                : options.SaveAsRvtPath;

            if (!string.IsNullOrWhiteSpace(targetSavePath))
            {
                string fullSavePath = Path.GetFullPath(targetSavePath!.Trim());
                string? parent = Path.GetDirectoryName(fullSavePath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                SaveAsOptions saveOptions = new()
                {
                    OverwriteExistingFile = true,
                    Compact = true,
                };
                if (createdDoc.IsWorkshared)
                {
                    saveOptions.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                }

                createdDoc.SaveAs(fullSavePath, saveOptions);
            }

            if (closeAfterSave)
            {
                createdDoc.Close(false);
                return (null, result);
            }

            return (createdDoc, result);
        }
        catch
        {
            if (createdDoc.IsValidObject)
            {
                try
                {
                    createdDoc.Close(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of failed new project document.
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Initializes an already-open Revit project document (created from the Kanizu <c>.rte</c> or copied
    /// from another <c>.rvt</c> when <see cref="NewKanizuProjectOptions.ClearStaleIdentity"/> is explicitly opted in).
    /// </summary>
    public static NewKanizuProjectResult InitializeOpenDocument(
        Document document,
        NewKanizuProjectOptions options,
        StarterKitSpec? kitSpec = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return InitializeDocumentCore(
            document,
            options,
            kitSpec,
            createdFromTemplate: false,
            templatePath: options.TemplatePath);
    }

    private static NewKanizuProjectResult InitializeDocumentCore(
        Document doc,
        NewKanizuProjectOptions options,
        StarterKitSpec? kitSpec,
        bool createdFromTemplate,
        string? templatePath)
    {
        if (doc.IsFamilyDocument)
        {
            throw new InvalidOperationException("New Kanizu Project can only initialize project documents, not family documents.");
        }

        if (doc.IsReadOnly)
        {
            throw new InvalidOperationException("Cannot initialize a read-only Revit document.");
        }

        if (doc.IsModifiable)
        {
            throw new InvalidOperationException("Close any active Revit transaction before running New Kanizu Project.");
        }

        if (GisStandardStateReader.IsShowcaseDocument(doc))
        {
            throw new InvalidOperationException(
                "Active document has gis_model_role = 'showcase'. Forma Companion showcase models cannot be initialized as authoring projects.");
        }

        StaleIdentitySnapshot staleSnapshot = ReadStaleIdentitySnapshot(doc);
        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(options, staleSnapshot, kitSpec);
        if (!plan.IsValid)
        {
            string errorSummary = string.Join(" ", plan.Errors.Select(e => e.GetMessage(options.Language)));
            throw new InvalidOperationException(errorSummary);
        }

        List<string> log = new()
        {
            $"Initializing Kanizu authoring project '{plan.StationName}' (ModelId={plan.ModelId}, kit={plan.KitVersion}, standard=v{plan.GisStandardVersion}).",
        };
        List<string> warnings = new();
        List<InitializedKanizuLevelResult> levelResults = new();
        List<string> createdWorksets = new();
        List<string> createdViews = new();
        List<string> appliedWorksetFilters = new();

        // Step 1: Enable worksharing BEFORE starting any transaction (Revit API requirement).
        bool worksharingEnabled = doc.IsWorkshared;
        bool worksharingEnabledInThisRun = false;
        if (plan.WorksetPlan.EnableWorksharing && !doc.IsWorkshared)
        {
            if (doc.CanEnableWorksharing())
            {
                try
                {
                    doc.EnableWorksharing(
                        plan.WorksetPlan.DefaultSharedLevelsWorksetName,
                        plan.WorksetPlan.DefaultActiveWorksetName);
                    worksharingEnabled = doc.IsWorkshared;
                    worksharingEnabledInThisRun = worksharingEnabled;
                    if (worksharingEnabledInThisRun && !string.IsNullOrWhiteSpace(plan.WorksetPlan.DefaultActiveWorksetName))
                    {
                        createdWorksets.Add(plan.WorksetPlan.DefaultActiveWorksetName);
                    }

                    log.Add(
                        $"Enabled worksharing (levels/grids='{plan.WorksetPlan.DefaultSharedLevelsWorksetName}', active='{plan.WorksetPlan.DefaultActiveWorksetName}').");
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not enable worksharing automatically ({ex.Message}); continuing in non-workshared mode.");
                }
            }
            else
            {
                warnings.Add("Document does not support enabling worksharing in its current state; skipping workset creation.");
            }
        }
        else if (!plan.WorksetPlan.EnableWorksharing)
        {
            log.Add(plan.WorksetPlan.FallbackNoteEn);
        }

        // Step 2: Apply shared parameters, fresh identity, levels, views, and worksets inside one clean transaction.
        string sharedParamsPath = ScopedSharedParametersFile.EnsureShippedFilePath();
        bool staleCleared = false;

        using (new ScopedSharedParametersFile(doc.Application, sharedParamsPath))
        {
            using Transaction transaction = new(doc, InitializeTransactionName);
            transaction.Start();
            try
            {
                // 2a. Ensure all 19 frozen Kanizu shared parameters are bound.
                SharedParameterManager paramManager = new(doc);
                paramManager.EnsureParameters(warnings);
                doc.Regenerate();

                // 2b. Clear stale identity if explicitly requested.
                if (plan.ShouldClearStaleIdentity)
                {
                    ClearStaleIdentityInTransaction(doc, log, warnings);
                    staleCleared = true;
                }

                // 2c. Stamp GeoProjectInfo with fresh ModelId using existing frozen ExtensibleStorage schema (zero new schemas).
                StampFreshGeoProjectInfo(doc, plan, staleCleared, log);

                // 2d. Stamp Project Information shared parameters (gis_model_role=authoring, gis_kit_version, gis_standard_version=3).
                StampProjectInformationParameters(doc, plan, log, warnings);

                // 2e. Create/update levels (matching via Level.ProjectElevation) and assign fresh unique level_ids, ordinals, and short_names.
                Dictionary<string, Level> levelsByName = EnsureLevelsAndFreshIds(
                    doc,
                    plan.Levels,
                    createdFromTemplate,
                    levelResults,
                    log,
                    warnings);
                doc.Regenerate();

                // 2f. Ensure canonical GIS Export view template and create per-level views (GIS Export, GIS Working Plan, GIS Area).
                EnsurePlannedViews(doc, plan, levelsByName, createdViews, log, warnings);

                // 2g. Create Kanizu discipline worksets + non-hiding QA workset filters when workshared.
                if (plan.WorksetPlan.EnableWorksharing && doc.IsWorkshared)
                {
                    EnsureWorksetsAndFilters(
                        doc,
                        plan.WorksetPlan,
                        createdWorksets,
                        appliedWorksetFilters,
                        log,
                        warnings);
                }

                doc.Regenerate();
                TransactionStatus commitStatus = transaction.Commit();
                if (commitStatus != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException($"Revit did not commit the '{InitializeTransactionName}' transaction (status: {commitStatus}).");
                }
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }
        }

        string federationMode = GisModelFederation.FormatMode(plan.FederationMode);
        bool recorded = GisModelSettings.RecordFederation(doc, warnings);
        log.Add(plan.FederationMode == KanizuFederationMode.Standalone
            ? "Stored federation mode 'standalone' in the model; gis-model.json carries no federation block for it."
            : recorded
                ? $"Recorded federation.mode = '{federationMode}' in gis-model.json."
                : $"Stored federation.mode = '{federationMode}' in the model; the first export into a station folder writes it to gis-model.json.");

        // Step 3: Post-initialization readiness verification (TemplateReadinessCheck + T9 identity checks).
        // Re-read persisted Level elements directly from doc after commit so T9-POST-LEVELIDS verifies real Revit state.
        ReadinessCheckSnapshot readinessSnapshot = GisStandardStateReader.ReadReadiness(
            doc,
            existingSnapshot: null,
            modeOverride: ReadinessMode.Project);
        string actualKitVersion = ReadProjectInfoSharedText(doc, GisSharedParameters.GisKitVersion);
        List<ExistingLevelIdentityEntry> persistedLevels = ReadPersistedLevelIdentities(doc);

        ProjectInitializationReadinessSummary postInitReadiness = NewKanizuProjectPlanner.EvaluatePostInitReadiness(
            plan,
            readinessSnapshot,
            persistedLevels,
            actualKitVersion);

        return new NewKanizuProjectResult(
            succeeded: postInitReadiness.IsReady,
            plan: plan,
            modelId: plan.ModelId,
            documentTitle: doc.Title ?? string.Empty,
            createdFromTemplate: createdFromTemplate,
            templatePath: templatePath,
            staleIdentityCleared: staleCleared,
            worksharingEnabled: worksharingEnabled,
            levels: levelResults,
            createdWorksets: createdWorksets,
            createdViews: createdViews,
            appliedWorksetFilters: appliedWorksetFilters,
            postInitReadiness: postInitReadiness,
            log: log,
            warnings: warnings);
    }

    private static void ClearStaleIdentityInTransaction(Document doc, List<string> log, List<string> warnings)
    {
        int clearedLevels = 0;
        foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
        {
            if (ClearSharedTextIfPresent(level, GisSharedParameters.LevelId))
            {
                clearedLevels++;
            }
        }

        int clearedElements = 0;
        HashSet<BuiltInCategory> idCategories = CollectIdCategories();
        if (idCategories.Count > 0)
        {
            foreach (Element elem in new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(idCategories))
                .WhereElementIsNotElementType())
            {
                bool clearedId = ClearSharedTextIfPresent(elem, GisSharedParameters.Id);
                bool clearedKey = ClearSharedTextIfPresent(elem, GisSharedParameters.GisKey);
                if (clearedId || clearedKey)
                {
                    clearedElements++;
                }
            }
        }

        log.Add($"Cleared stale identities on {clearedLevels} level(s) and {clearedElements} placed element(s).");
        if (clearedElements > 0)
        {
            warnings.Add($"Cleared copied id/gis_key values on {clearedElements} existing element(s); fresh IDs will be generated on next export.");
        }
    }

    private static void StampFreshGeoProjectInfo(Document doc, NewKanizuProjectPlan plan, bool staleCleared, List<string> log)
    {
        GeoProjectInfoStorage storage = new();
        RevitDocumentHandle handle = new(doc);
        GeoProjectInfo? previous = TryLoadGeoProjectInfo(doc);

        // GeoProjectInfoStorage.Save preserves previous.GisStationFolder when blank; explicitly Clear()
        // first when purging stale identity so no old station folder survives into the new project.
        if (staleCleared)
        {
            storage.Clear(handle);
        }

        GeoProjectInfo updated = previous is null
            ? new GeoProjectInfo()
            : new GeoProjectInfo
            {
                SchemaVersion = previous.SchemaVersion,
                ProjectCrs = previous.ProjectCrs,
                Origin = previous.Origin,
                TrueNorthAngle = previous.TrueNorthAngle,
                PrimaryMeshCode = previous.PrimaryMeshCode,
                Confidence = previous.Confidence,
                SetupSource = previous.SetupSource,
                GeoSetupDate = previous.GeoSetupDate,
                GisStationFolder = staleCleared ? null : previous.GisStationFolder,
            };

        updated.ModelId = plan.ModelId;
        updated.FederationMode = GisModelFederation.FormatMode(plan.FederationMode);
        updated.FederationHubModelId = plan.FederationMode == KanizuFederationMode.Spoke ? plan.HubModelId : null;
        storage.Save(handle, updated);
        log.Add($"Stamped GeoProjectInfo.ModelId = '{plan.ModelId}' (existing schema preserved).");
    }

    private static void StampProjectInformationParameters(
        Document doc,
        NewKanizuProjectPlan plan,
        List<string> log,
        List<string> warnings)
    {
        Element projectInfo = doc.ProjectInformation;
        SetSharedTextParameter(projectInfo, GisSharedParameters.GisModelRole, plan.ModelRole, warnings);
        SetSharedTextParameter(projectInfo, GisSharedParameters.GisKitVersion, plan.KitVersion, warnings);
        SetSharedTextParameter(
            projectInfo,
            GisSharedParameters.GisStandardVersion,
            plan.GisStandardVersion.ToString(CultureInfo.InvariantCulture),
            warnings);

        try
        {
            if (!string.IsNullOrWhiteSpace(plan.StationName))
            {
                string currentName = (doc.ProjectInformation.Name ?? string.Empty).Trim();
                if (currentName.Length == 0 ||
                    string.Equals(currentName, "Project Name", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(currentName, "プロジェクト名", StringComparison.Ordinal))
                {
                    doc.ProjectInformation.Name = plan.StationName;
                }
            }

            if (!string.IsNullOrWhiteSpace(plan.BuildingSlug) &&
                string.IsNullOrWhiteSpace(doc.ProjectInformation.BuildingName))
            {
                doc.ProjectInformation.BuildingName = plan.BuildingSlug;
            }
        }
        catch (Exception)
        {
            // ProjectInformation built-in metadata updates are best-effort.
        }

        log.Add($"Stamped ProjectInformation: gis_model_role={plan.ModelRole}, gis_kit_version={plan.KitVersion}, gis_standard_version={plan.GisStandardVersion}.");
    }

    private static Dictionary<string, Level> EnsureLevelsAndFreshIds(
        Document doc,
        IReadOnlyList<PlannedKanizuLevel> plannedLevels,
        bool createdFromTemplate,
        List<InitializedKanizuLevelResult> results,
        List<string> log,
        List<string> warnings)
    {
        List<Level> existingLevels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToList();

        Dictionary<long, Level> existingById = existingLevels.ToDictionary(l => l.Id.Value);
        HashSet<long> referencedLevelIds = CollectReferencedLevelIds(doc);

        List<ExistingLevelCandidate> existingCandidates = existingLevels
            .Select(l => new ExistingLevelCandidate(
                l.Id.Value,
                l.Name ?? string.Empty,
                l.ProjectElevation,
                ModelSnapshotReader.ReadInteger(l, GisSharedParameters.Ordinal).HasValue,
                ReadSharedText(l, GisSharedParameters.ShortName).Length > 0,
                referencedLevelIds.Contains(l.Id.Value)))
            .ToList();

        LevelReconciliationPlan reconciliation = NewKanizuProjectPlanner.PlanLevelReconciliation(
            plannedLevels,
            existingCandidates,
            createdFromTemplate,
            LevelMatchToleranceFeet);

        // Temporarily rename any existing levels being reused or deleted whose current name collides with a planned
        // target name held by a different level so sequential renames never fail with a duplicate-name exception.
        HashSet<string> targetNames = new(plannedLevels.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (LevelReconciliationAction action in reconciliation.Actions)
        {
            if (action.ReusedExistingLevelId is long reusedId &&
                existingById.TryGetValue(reusedId, out Level? reusedLevel) &&
                !string.Equals(reusedLevel.Name, action.Planned.Name, StringComparison.OrdinalIgnoreCase) &&
                targetNames.Contains(reusedLevel.Name ?? string.Empty))
            {
                TryRenameLevel(reusedLevel, $"__knz_tmp_{reusedId}", warnings);
            }
        }

        foreach (long deleteId in reconciliation.UnclaimedSeedLevelIdsToDelete)
        {
            if (existingById.TryGetValue(deleteId, out Level? toDelete) &&
                targetNames.Contains(toDelete.Name ?? string.Empty))
            {
                TryRenameLevel(toDelete, $"__knz_del_{deleteId}", warnings);
            }
        }

        HashSet<long> claimedIds = new();
        Dictionary<string, Level> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (LevelReconciliationAction action in reconciliation.Actions)
        {
            PlannedKanizuLevel planned = action.Planned;
            double targetFeet = UnitUtils.ConvertToInternalUnits(planned.ElevationMeters, UnitTypeId.Meters);

            bool wasCreated = false;
            Level level;
            if (action.ReusedExistingLevelId is long existingId && existingById.TryGetValue(existingId, out Level? matched))
            {
                level = matched;
                if (!string.Equals(level.Name, planned.Name, StringComparison.Ordinal))
                {
                    TryRenameLevel(level, planned.Name, warnings);
                }

                // Adjust by delta relative to Level.ProjectElevation (internal origin) rather than writing
                // targetFeet raw into LEVEL_ELEV (which is relative to the LevelType Elevation Base: PBP/Survey).
                double deltaFeet = targetFeet - level.ProjectElevation;
                if (Math.Abs(deltaFeet) > LevelMoveToleranceFeet)
                {
                    MoveLevelToProjectElevationDelta(doc, level, deltaFeet, warnings);
                }
            }
            else
            {
                level = Level.Create(doc, targetFeet);
                TryRenameLevel(level, planned.Name, warnings);
                existingLevels.Add(level);
                existingById[level.Id.Value] = level;
                wasCreated = true;
            }

            claimedIds.Add(level.Id.Value);
            byName[planned.Name] = level;

            SetSharedTextParameter(level, GisSharedParameters.LevelId, planned.LevelId, warnings);
            SetSharedTextParameter(level, GisSharedParameters.ShortName, planned.ShortName, warnings);
            SetSharedIntegerOrTextParameter(level, GisSharedParameters.Ordinal, planned.Ordinal, warnings);

            double actualMeters = UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, UnitTypeId.Meters);
            results.Add(new InitializedKanizuLevelResult(
                level.Id.Value,
                level.Name ?? planned.Name,
                planned.ShortName,
                planned.Ordinal,
                actualMeters,
                planned.LevelId,
                wasCreated));

            log.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} level '{1}' (ordinal={2}, elev={3:0.00} m, level_id={4}).",
                wasCreated ? "Created" : (action.ReusedFromUnmatchedSeed ? "Reused seed" : "Initialized"),
                level.Name ?? planned.Name,
                planned.Ordinal,
                actualMeters,
                planned.LevelId));
        }

        // Delete leftover unreferenced seed levels on brand-new template projects after planned levels exist
        // (always retaining at least 1 level) so fresh projects contain exactly the requested stack and pass level-metadata.
        HashSet<long> deletedIds = new();
        int remainingLevelCount = existingLevels.Count;
        foreach (long seedId in reconciliation.UnclaimedSeedLevelIdsToDelete)
        {
            if (remainingLevelCount <= 1 || !existingById.TryGetValue(seedId, out Level? seedLevel))
            {
                continue;
            }

            try
            {
                string seedName = seedLevel.Name ?? seedId.ToString(CultureInfo.InvariantCulture);
                doc.Delete(seedLevel.Id);
                deletedIds.Add(seedId);
                remainingLevelCount--;
                log.Add($"Removed unused template seed level '{seedName}' so the project contains only the planned level stack.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not remove unused template seed level '{seedLevel.Name}': {ex.Message}");
            }
        }

        // Ensure any remaining unclaimed levels (e.g. InitializeOpenDocument) never keep duplicate or empty level_ids,
        // and warn clearly if any lack ordinal/short_name metadata.
        HashSet<string> assignedLevelIds = new(
            results.Select(r => r.LevelId),
            StringComparer.OrdinalIgnoreCase);
        foreach (Level extra in existingLevels.Where(l => !claimedIds.Contains(l.Id.Value) && !deletedIds.Contains(l.Id.Value)))
        {
            string current = ReadSharedText(extra, GisSharedParameters.LevelId);
            if (current.Length == 0 || !assignedLevelIds.Add(current))
            {
                string minted = Guid.NewGuid().ToString("D").ToLowerInvariant();
                assignedLevelIds.Add(minted);
                SetSharedTextParameter(extra, GisSharedParameters.LevelId, minted, warnings);
                log.Add($"Assigned fresh unique level_id '{minted}' to pre-existing extra level '{extra.Name}'.");
            }
        }

        foreach (ExistingLevelCandidate missingMeta in reconciliation.UnclaimedLevelsMissingMetadata)
        {
            if (!deletedIds.Contains(missingMeta.ElementId))
            {
                warnings.Add(
                    $"Unclaimed existing level '{missingMeta.Name}' is missing ordinal and/or short_name; set them in Revit or Kanizu GIS Readiness before export.");
            }
        }

        return byName;
    }

    private static void MoveLevelToProjectElevationDelta(Document doc, Level level, double deltaFeet, List<string> warnings)
    {
        try
        {
            Parameter? elevParam = level.get_Parameter(BuiltInParameter.LEVEL_ELEV);
            if (elevParam != null && !elevParam.IsReadOnly)
            {
                elevParam.Set(elevParam.AsDouble() + deltaFeet);
                return;
            }

            ElementTransformUtils.MoveElement(doc, level.Id, new XYZ(0, 0, deltaFeet));
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not adjust elevation of level '{level.Name}': {ex.Message}");
        }
    }

    private static void TryRenameLevel(Level level, string newName, List<string> warnings)
    {
        if (string.Equals(level.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            level.Name = newName;
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not rename level '{level.Name}' to '{newName}': {ex.Message}");
        }
    }

    private static HashSet<long> CollectReferencedLevelIds(Document doc)
    {
        HashSet<long> ids = new();
        foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            if (element is Level || element is View || element.ViewSpecific || element.Category == null)
            {
                continue;
            }

            if (element.Category.CategoryType != CategoryType.Model)
            {
                continue;
            }

            ElementId levelId = element.LevelId;
            if (levelId != ElementId.InvalidElementId)
            {
                ids.Add(levelId.Value);
            }
        }

        return ids;
    }

    private static List<ExistingLevelIdentityEntry> ReadPersistedLevelIdentities(Document document)
    {
        List<ExistingLevelIdentityEntry> persisted = new();
        foreach (Level level in new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.ProjectElevation))
        {
            string levelId = ReadSharedText(level, GisSharedParameters.LevelId);
            string shortName = ReadSharedText(level, GisSharedParameters.ShortName);
            int? ordinal = ModelSnapshotReader.ReadInteger(level, GisSharedParameters.Ordinal);
            double elevMeters = UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, UnitTypeId.Meters);
            persisted.Add(new ExistingLevelIdentityEntry(
                level.Id.Value,
                level.Name ?? string.Empty,
                levelId,
                ordinal,
                shortName,
                elevMeters));
        }

        return persisted;
    }

    private static void EnsurePlannedViews(
        Document doc,
        NewKanizuProjectPlan plan,
        IReadOnlyDictionary<string, Level> levelsByName,
        List<string> createdViews,
        List<string> log,
        List<string> warnings)
    {
        if (plan.PlannedViews.Count == 0)
        {
            return;
        }

        // Ensure canonical "GIS Export" template exists before assigning it to GIS_* views.
        if (plan.PlannedViews.Any(v => v.ViewKind == PlannedKanizuViewKind.GisExportPlan))
        {
            ExportViewSetup.EnsureNamedExportPlanTemplate(
                doc,
                TemplateStandardDefinitions.ExportPlanViewTemplateName,
                log);
        }

        Dictionary<string, View> templatesByTypeAndName = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => ViewTypeAndNameKey(v.ViewType, v.Name!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Dictionary<string, View> existingViewsByTypeAndName = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => ViewTypeAndNameKey(v.ViewType, v.Name!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        ElementId floorPlanTypeId = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan)
            ?.Id ?? ElementId.InvalidElementId;

        AreaScheme? gisAreaScheme = new FilteredElementCollector(doc)
            .OfClass(typeof(AreaScheme))
            .Cast<AreaScheme>()
            .FirstOrDefault(a => string.Equals(a.Name, TemplateAreaColorSchemePlan.DefaultAreaSchemeName, StringComparison.OrdinalIgnoreCase));

        foreach (PlannedKanizuView viewPlan in plan.PlannedViews)
        {
            if (!levelsByName.TryGetValue(viewPlan.LevelName, out Level? level) || level is null)
            {
                warnings.Add($"Skipped view '{viewPlan.ViewName}': level '{viewPlan.LevelName}' was not found.");
                continue;
            }

            ViewType expectedViewType = viewPlan.ViewKind == PlannedKanizuViewKind.GisAreaPlan
                ? ViewType.AreaPlan
                : ViewType.FloorPlan;
            string viewKey = ViewTypeAndNameKey(expectedViewType, viewPlan.ViewName);

            try
            {
                if (existingViewsByTypeAndName.TryGetValue(viewKey, out View? existing))
                {
                    ApplyViewTemplateIfPresent(existing, viewPlan.ViewTemplateName, templatesByTypeAndName, warnings);
                    continue;
                }

                ViewPlan? created = null;
                if (viewPlan.ViewKind == PlannedKanizuViewKind.GisAreaPlan)
                {
                    if (gisAreaScheme is null)
                    {
                        warnings.Add($"AreaScheme '{TemplateAreaColorSchemePlan.DefaultAreaSchemeName}' not found; skipped Area Plan '{viewPlan.ViewName}'.");
                        continue;
                    }

                    created = ViewPlan.CreateAreaPlan(doc, gisAreaScheme.Id, level.Id);
                }
                else
                {
                    if (floorPlanTypeId == ElementId.InvalidElementId)
                    {
                        warnings.Add($"FloorPlan ViewFamilyType not found; skipped view '{viewPlan.ViewName}'.");
                        continue;
                    }

                    created = ViewPlan.Create(doc, floorPlanTypeId, level.Id);
                }

                if (created != null)
                {
                    created.Name = viewPlan.ViewName;
                    ApplyViewTemplateIfPresent(created, viewPlan.ViewTemplateName, templatesByTypeAndName, warnings);
                    existingViewsByTypeAndName[viewKey] = created;
                    createdViews.Add(viewPlan.ViewName);
                    log.Add($"Created view '{viewPlan.ViewName}' for level '{level.Name}'.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not configure view '{viewPlan.ViewName}': {ex.Message}");
            }
        }
    }

    private static string ViewTypeAndNameKey(ViewType viewType, string viewName) =>
        $"{(int)viewType}\0{(viewName ?? string.Empty).Trim().ToUpperInvariant()}";

    private static void ApplyViewTemplateIfPresent(
        View view,
        string? templateName,
        IReadOnlyDictionary<string, View> templatesByTypeAndName,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return;
        }

        string templateKey = ViewTypeAndNameKey(view.ViewType, templateName!);
        if (templatesByTypeAndName.TryGetValue(templateKey, out View? template) && template != null)
        {
            if (view.ViewTemplateId != template.Id)
            {
                view.ViewTemplateId = template.Id;
            }
        }
        else
        {
            warnings.Add($"Compatible {view.ViewType} view template '{templateName}' not found for view '{view.Name}'.");
        }
    }

    private static void EnsureWorksetsAndFilters(
        Document doc,
        PlannedWorksetSetup worksetPlan,
        List<string> createdWorksets,
        List<string> appliedWorksetFilters,
        List<string> log,
        List<string> warnings)
    {
        Dictionary<string, Workset> existingWorksets = new FilteredWorksetCollector(doc)
            .OfKind(WorksetKind.UserWorkset)
            .GroupBy(w => w.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        WorksetDefaultVisibilitySettings? defaultVis = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);

        // 1. Create standard Kanizu discipline worksets.
        foreach (KanizuWorksetSpec wsSpec in worksetPlan.Worksets)
        {
            Workset? ws;
            if (!existingWorksets.TryGetValue(wsSpec.Name, out ws))
            {
                if (!WorksetTable.IsWorksetNameUnique(doc, wsSpec.Name))
                {
                    warnings.Add($"Workset name '{wsSpec.Name}' already exists in document table; reusing existing workset.");
                    continue;
                }

                ws = Workset.Create(doc, wsSpec.Name);
                existingWorksets[wsSpec.Name] = ws;
                createdWorksets.Add(wsSpec.Name);
                log.Add($"Created workset '{wsSpec.Name}'.");
            }

            if (ws != null && defaultVis != null && wsSpec.VisibleInGisExportViews)
            {
                defaultVis.SetWorksetVisibility(ws.Id, true);
            }
        }

        // 2. Guarantee all GIS authoring worksets remain visible on GIS Export template and GIS_* views (KZ-VIEW-004).
        List<View> gisExportViewsAndTemplates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v =>
                (v.IsTemplate && string.Equals(v.Name, TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase))
                || (!v.IsTemplate && (v.Name ?? string.Empty).StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (KanizuWorksetSpec wsSpec in worksetPlan.Worksets)
        {
            if (!wsSpec.VisibleInGisExportViews || !existingWorksets.TryGetValue(wsSpec.Name, out Workset? ws) || ws is null)
            {
                continue;
            }

            foreach (View exportView in gisExportViewsAndTemplates)
            {
                try
                {
                    exportView.SetWorksetVisibility(ws.Id, WorksetVisibility.Visible);
                }
                catch (Exception)
                {
                    // Ignore views whose template controls workset visibility.
                }
            }
        }

        // 3. Create non-hiding workset QA filters and attach to Working / QA templates (NEVER GIS Export).
        Dictionary<string, ParameterFilterElement> existingFilters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .GroupBy(f => f.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Dictionary<string, View> templatesByName = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => v.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        ElementId worksetParamId = new(BuiltInParameter.ELEM_PARTITION_PARAM);
        HashSet<long> allFilterableCategoryIds = new(
            ParameterFilterUtilities.GetAllFilterableCategories().Select(id => id.Value));

        foreach (PlannedWorksetViewFilter filterPlan in worksetPlan.WorksetViewFilters)
        {
            if (!existingWorksets.TryGetValue(filterPlan.WorksetName, out Workset? ws) || ws is null)
            {
                continue;
            }

            if (!int.TryParse(ws.Id.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericWorksetId))
            {
                warnings.Add($"Could not resolve numeric WorksetId for workset '{ws.Name}'; skipping filter '{filterPlan.FilterName}'.");
                continue;
            }

            List<ElementId> resolvedCategoryIds = ResolveBuiltInCategoryIds(filterPlan.Categories, warnings);
            if (resolvedCategoryIds.Count == 0)
            {
                continue;
            }

            // Intersect with Revit's filterable categories AND retain only categories that support ELEM_PARTITION_PARAM
            // (skips unfilterable categories like OST_RvtLinks cleanly instead of throwing ArgumentException).
            IReadOnlyList<long> validCategoryValues = NewKanizuProjectPlanner.FilterWorksetFilterableCategories(
                resolvedCategoryIds.Select(id => id.Value),
                allFilterableCategoryIds,
                catVal =>
                {
                    ICollection<ElementId> commonParams = ParameterFilterUtilities.GetFilterableParametersInCommon(
                        doc,
                        new[] { new ElementId(catVal) });
                    return commonParams != null && commonParams.Contains(worksetParamId);
                });

            if (validCategoryValues.Count == 0)
            {
                log.Add(
                    $"Skipped workset parameter filter '{filterPlan.FilterName}' for workset '{filterPlan.WorksetName}': " +
                    $"none of its categories ({string.Join(", ", filterPlan.Categories)}) support workset parameter filtering in Revit.");
                continue;
            }

            HashSet<ElementId> validCategorySet = new(validCategoryValues.Select(v => new ElementId(v)));
            List<ElementId> validCategoryIds = validCategorySet.ToList();

            FilterRule rule = ParameterFilterRuleFactory.CreateEqualsRule(worksetParamId, numericWorksetId);
            ElementFilter elementFilter = new ElementParameterFilter(rule);

            if (!ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(doc, validCategorySet, elementFilter))
            {
                log.Add(
                    $"Skipped workset parameter filter '{filterPlan.FilterName}' for workset '{filterPlan.WorksetName}': " +
                    "Revit rejected the workset filter predicate for the selected categories.");
                continue;
            }

            ParameterFilterElement? filterElement;
            try
            {
                if (!existingFilters.TryGetValue(filterPlan.FilterName, out filterElement))
                {
                    filterElement = ParameterFilterElement.Create(doc, filterPlan.FilterName, validCategoryIds, elementFilter);
                    existingFilters[filterPlan.FilterName] = filterElement;
                }
                else
                {
                    filterElement.SetCategories(validCategoryIds);
                    filterElement.SetElementFilter(elementFilter);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not create or update workset filter '{filterPlan.FilterName}': {ex.Message}");
                continue;
            }

            Color color = new(filterPlan.Red, filterPlan.Green, filterPlan.Blue);
            OverrideGraphicSettings ogs = new();
            if (color.IsValid)
            {
                ogs.SetProjectionLineColor(color);
            }

            bool attachedAny = false;
            foreach (string targetTemplateName in filterPlan.TargetViewTemplateNames)
            {
                // Hard guardrail: NEVER attach workset filters onto the canonical GIS Export template.
                if (string.Equals(targetTemplateName, TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!templatesByName.TryGetValue(targetTemplateName, out View? targetTemplate) || targetTemplate is null)
                {
                    continue;
                }

                try
                {
                    if (!targetTemplate.IsFilterApplied(filterElement.Id))
                    {
                        targetTemplate.AddFilter(filterElement.Id);
                    }

                    // Always visible=true to satisfy KZ-VIEW-004.
                    targetTemplate.SetFilterVisibility(filterElement.Id, filterPlan.Visible);
                    targetTemplate.SetIsFilterEnabled(filterElement.Id, true);
                    targetTemplate.SetFilterOverrides(filterElement.Id, ogs);
                    attachedAny = true;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not attach workset filter '{filterPlan.FilterName}' to template '{targetTemplateName}': {ex.Message}");
                }
            }

            if (attachedAny)
            {
                appliedWorksetFilters.Add(filterPlan.FilterName);
            }
        }

        if (appliedWorksetFilters.Count > 0)
        {
            log.Add($"Attached {appliedWorksetFilters.Count} non-hiding workset filter(s) to Working/QA view templates.");
        }
    }

    private static List<ElementId> ResolveBuiltInCategoryIds(IReadOnlyList<string> categoryNames, List<string> warnings)
    {
        List<ElementId> ids = new();
        foreach (string catName in categoryNames)
        {
            if (Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
            {
                ids.Add(new ElementId(bic));
            }
            else
            {
                warnings.Add($"Unknown BuiltInCategory '{catName}' in workset filter specification.");
            }
        }

        return ids;
    }

    private static (int Count, List<long> Sample) ReadElementsWithIdentity(Document doc)
    {
        int count = 0;
        List<long> sample = new();
        HashSet<BuiltInCategory> categories = CollectIdCategories();
        if (categories.Count == 0)
        {
            return (0, sample);
        }

        foreach (Element elem in new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType())
        {
            if (ReadSharedText(elem, GisSharedParameters.Id).Length == 0 &&
                ReadSharedText(elem, GisSharedParameters.GisKey).Length == 0)
            {
                continue;
            }

            count++;
            if (sample.Count < StaleElementSampleLimit)
            {
                sample.Add(elem.Id.Value);
            }
        }

        return (count, sample);
    }

    private static HashSet<BuiltInCategory> CollectIdCategories()
    {
        HashSet<BuiltInCategory> categories = new();
        foreach (string name in new[] { GisSharedParameters.Id, GisSharedParameters.GisKey })
        {
            if (GisSharedParameters.TryGet(name, out GisParameterDefinition def))
            {
                foreach (string catName in def.Categories)
                {
                    if (Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
                    {
                        categories.Add(bic);
                    }
                }
            }
        }

        return categories;
    }

    private static GeoProjectInfo? TryLoadGeoProjectInfo(Document doc)
    {
        try
        {
            return new GeoProjectInfoStorage().Load(new RevitDocumentHandle(doc));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ReadProjectInfoSharedText(Document doc, string paramName) =>
        doc.ProjectInformation is null ? string.Empty : ReadSharedText(doc.ProjectInformation, paramName);

    private static string ReadSharedText(Element element, string paramName)
    {
        Parameter? param = GisStandardStateReader.FindSharedParameter(element, paramName, allowNameFallback: true);
        if (param is null || !param.HasValue)
        {
            return string.Empty;
        }

        return (param.StorageType == StorageType.String ? param.AsString() : param.AsValueString())?.Trim() ?? string.Empty;
    }

    private static bool ClearSharedTextIfPresent(Element element, string paramName)
    {
        Parameter? param = GisStandardStateReader.FindSharedParameter(element, paramName, allowNameFallback: true);
        if (param is null || param.IsReadOnly || param.StorageType != StorageType.String)
        {
            return false;
        }

        string? value = param.AsString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return param.Set(string.Empty);
    }

    private static void SetSharedTextParameter(Element element, string paramName, string value, List<string> warnings)
    {
        Parameter? param = GisStandardStateReader.FindSharedParameter(element, paramName, allowNameFallback: true);
        if (param is null || param.StorageType != StorageType.String)
        {
            warnings.Add($"Shared text parameter '{paramName}' was not found on element {element.Id.Value}.");
            return;
        }

        if (param.IsReadOnly)
        {
            warnings.Add($"Shared text parameter '{paramName}' on element {element.Id.Value} is read-only.");
            return;
        }

        param.Set(value ?? string.Empty);
    }

    private static void SetSharedIntegerOrTextParameter(Element element, string paramName, int value, List<string> warnings)
    {
        Parameter? param = GisStandardStateReader.FindSharedParameter(element, paramName, allowNameFallback: true);
        if (param is null)
        {
            warnings.Add($"Shared parameter '{paramName}' was not found on element {element.Id.Value}.");
            return;
        }

        if (param.IsReadOnly)
        {
            warnings.Add($"Shared parameter '{paramName}' on element {element.Id.Value} is read-only.");
            return;
        }

        if (param.StorageType == StorageType.Integer)
        {
            param.Set(value);
        }
        else if (param.StorageType == StorageType.String)
        {
            param.Set(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
