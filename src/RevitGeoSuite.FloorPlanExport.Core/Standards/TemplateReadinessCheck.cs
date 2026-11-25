using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Evaluation mode for the Kanizu readiness checklist.</summary>
public enum ReadinessMode
{
    Project,
    Template,
}

/// <summary>Status of one readiness checklist item or group.</summary>
public enum ReadinessCheckStatus
{
    Pass,
    Warn,
    Fail,
    NotApplicable,
}

/// <summary>Which automated action can remediate a readiness item.</summary>
public enum ReadinessActionKind
{
    None,
    PrepareTemplate,
    SetupModel,
    FixFloorTypes,
}

/// <summary>Shared-parameter GUID and type/instance binding metadata read from an .rfa family parameter.</summary>
public sealed class FamilyParameterBindingInfo
{
    public FamilyParameterBindingInfo(string name, bool isShared, Guid? guid, bool isInstance)
    {
        Name = name ?? string.Empty;
        IsShared = isShared;
        Guid = guid;
        IsInstance = isInstance;
    }

    public string Name { get; }

    public bool IsShared { get; }

    public Guid? Guid { get; }

    public bool IsInstance { get; }
}

/// <summary>A loaded family type (in a model/template) or an external .rfa family type inspected against standards/families.json and door rules.</summary>
public sealed class LoadedFamilyCheckInfo
{
    public LoadedFamilyCheckInfo(
        long elementId,
        string familyName,
        string typeName,
        string categoryName,
        string? gisKind = null,
        string? gisCategory = null,
        string? gisDoor = null,
        string? gisAccessCon = null,
        bool hasGisKindParameter = false,
        bool hasGisDoorParameter = false,
        bool hasGisAccessConParameter = false,
        IReadOnlyDictionary<string, string>? typeParameters = null,
        IEnumerable<long>? instanceIds = null,
        string? sourcePath = null,
        IReadOnlyDictionary<string, FamilyParameterBindingInfo>? parameterBindings = null)
    {
        ElementId = elementId;
        FamilyName = familyName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        CategoryName = categoryName ?? string.Empty;
        GisKind = gisKind ?? string.Empty;
        GisCategory = gisCategory ?? string.Empty;
        GisDoor = gisDoor ?? string.Empty;
        GisAccessCon = gisAccessCon ?? string.Empty;
        HasGisKindParameter = hasGisKindParameter;
        HasGisDoorParameter = hasGisDoorParameter;
        HasGisAccessConParameter = hasGisAccessConParameter;
        TypeParameters = typeParameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        InstanceIds = (instanceIds ?? Array.Empty<long>()).ToList();
        SourcePath = sourcePath ?? string.Empty;
        ParameterBindings = parameterBindings ?? new Dictionary<string, FamilyParameterBindingInfo>(StringComparer.Ordinal);
    }

    public long ElementId { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string CategoryName { get; }

    public string GisKind { get; }

    public string GisCategory { get; }

    public string GisDoor { get; }

    public string GisAccessCon { get; }

    public bool HasGisKindParameter { get; }

    public bool HasGisDoorParameter { get; }

    public bool HasGisAccessConParameter { get; }

    public IReadOnlyDictionary<string, string> TypeParameters { get; }

    public IReadOnlyList<long> InstanceIds { get; }

    public string SourcePath { get; }

    public IReadOnlyDictionary<string, FamilyParameterBindingInfo> ParameterBindings { get; }
}

/// <summary>Validation result for a single family or .rfa file.</summary>
public sealed class FamilyValidationItem
{
    public FamilyValidationItem(
        string familyName,
        string category,
        ReadinessCheckStatus status,
        string detail,
        string filePath = "",
        IEnumerable<long>? elementIds = null)
    {
        FamilyName = familyName ?? string.Empty;
        Category = category ?? string.Empty;
        Status = status;
        Detail = detail ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        ElementIds = (elementIds ?? Array.Empty<long>()).ToList();
    }

    public string FamilyName { get; }

    public string Category { get; }

    public ReadinessCheckStatus Status { get; }

    public string Detail { get; }

    public string FilePath { get; }

    public IReadOnlyList<long> ElementIds { get; }
}

/// <summary>Result of scanning a folder of external .rfa files, including truncation metadata.</summary>
public sealed class RfaFolderValidationResult
{
    public RfaFolderValidationResult(
        IReadOnlyList<FamilyValidationItem> items,
        int totalFilesFound,
        int inspectedFileCount,
        bool truncated)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        TotalFilesFound = totalFilesFound;
        InspectedFileCount = inspectedFileCount;
        Truncated = truncated;
    }

    public IReadOnlyList<FamilyValidationItem> Items { get; }

    public int TotalFilesFound { get; }

    public int InspectedFileCount { get; }

    public bool Truncated { get; }
}

/// <summary>One linked Revit model inspected for stable ModelId readiness.</summary>
public sealed class LinkedModelReadinessInfo
{
    public LinkedModelReadinessInfo(string name, bool isLoaded, string? modelId = null)
    {
        Name = name ?? string.Empty;
        IsLoaded = isLoaded;
        ModelId = modelId ?? string.Empty;
    }

    public string Name { get; }

    public bool IsLoaded { get; }

    public string ModelId { get; }
}

/// <summary>Pure-data snapshot of a Revit project or .rte template evaluated by <see cref="ProjectReadinessEvaluator"/>.</summary>
public sealed class ReadinessCheckSnapshot
{
    public string DocumentTitle { get; set; } = string.Empty;

    public string DocumentPath { get; set; } = string.Empty;

    public bool IsEmptyModel { get; set; }

    public string StandardVersionText { get; set; } = string.Empty;

    public IReadOnlyList<BoundParameterInfo> BoundParameters { get; set; } = Array.Empty<BoundParameterInfo>();

    public IReadOnlyList<FloorTypeInfo> FloorTypes { get; set; } = Array.Empty<FloorTypeInfo>();

    public IReadOnlyList<FloorInfo> Floors { get; set; } = Array.Empty<FloorInfo>();

    public IReadOnlyList<MaterialInfo> Materials { get; set; } = Array.Empty<MaterialInfo>();

    public IReadOnlyList<LevelInfo> Levels { get; set; } = Array.Empty<LevelInfo>();

    public IReadOnlyList<LoadedFamilyCheckInfo> LoadedFamilies { get; set; } = Array.Empty<LoadedFamilyCheckInfo>();

    public IReadOnlyList<QaScheduleInfo> Schedules { get; set; } = Array.Empty<QaScheduleInfo>();

    public IReadOnlyList<string> ViewTemplateNames { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ExportPlanTemplateIssues { get; set; } = Array.Empty<string>();

    public int? ProjectCrsEpsgCode { get; set; }

    public string ProjectCrsName { get; set; } = string.Empty;

    public bool IsGeoreferenceVerified { get; set; }

    public string HostModelId { get; set; } = string.Empty;

    public IReadOnlyList<LinkedModelReadinessInfo> LinkedModels { get; set; } = Array.Empty<LinkedModelReadinessInfo>();

    public ReadinessMode SuggestedMode
    {
        get
        {
            string path = (DocumentPath ?? string.Empty).Trim();
            string title = (DocumentTitle ?? string.Empty).Trim();
            if (path.EndsWith(".rte", StringComparison.OrdinalIgnoreCase)
                || title.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
            {
                return ReadinessMode.Template;
            }

            if (path.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                || title.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                return ReadinessMode.Project;
            }

            return IsEmptyModel ? ReadinessMode.Template : ReadinessMode.Project;
        }
    }
}

public sealed class ReadinessCheckItem
{
    public ReadinessCheckItem(
        string id,
        string title,
        ReadinessCheckStatus status,
        string detail,
        ReadinessActionKind action = ReadinessActionKind.None,
        IEnumerable<long>? elementIds = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Status = status;
        Detail = detail ?? string.Empty;
        Action = action;
        ElementIds = (elementIds ?? Array.Empty<long>()).ToList();
    }

    public string Id { get; }

    public string Title { get; }

    public ReadinessCheckStatus Status { get; }

    public string Detail { get; }

    public ReadinessActionKind Action { get; }

    public IReadOnlyList<long> ElementIds { get; }
}

public sealed class ReadinessCheckGroup
{
    public ReadinessCheckGroup(string id, string title, IReadOnlyList<ReadinessCheckItem> items)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Items = items ?? throw new ArgumentNullException(nameof(items));
        PassCount = items.Count(item => item.Status == ReadinessCheckStatus.Pass);
        WarnCount = items.Count(item => item.Status == ReadinessCheckStatus.Warn);
        FailCount = items.Count(item => item.Status == ReadinessCheckStatus.Fail);
        NotApplicableCount = items.Count(item => item.Status == ReadinessCheckStatus.NotApplicable);
        Status = FailCount > 0
            ? ReadinessCheckStatus.Fail
            : WarnCount > 0
                ? ReadinessCheckStatus.Warn
                : PassCount > 0
                    ? ReadinessCheckStatus.Pass
                    : ReadinessCheckStatus.NotApplicable;
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<ReadinessCheckItem> Items { get; }

    public ReadinessCheckStatus Status { get; }

    public int PassCount { get; }

    public int WarnCount { get; }

    public int FailCount { get; }

    public int NotApplicableCount { get; }
}

public sealed class ProjectReadinessReport
{
    public ProjectReadinessReport(
        ReadinessMode mode,
        ReadinessMode suggestedMode,
        IReadOnlyList<ReadinessCheckGroup> groups,
        TemplatePreparationPlan templatePlan,
        bool canSetupModel,
        bool canFixFloorTypes)
    {
        Mode = mode;
        SuggestedMode = suggestedMode;
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        TemplatePlan = templatePlan ?? throw new ArgumentNullException(nameof(templatePlan));
        CanSetupModel = canSetupModel;
        CanFixFloorTypes = canFixFloorTypes;

        List<ReadinessCheckItem> allItems = groups.SelectMany(group => group.Items).ToList();
        PassCount = allItems.Count(item => item.Status == ReadinessCheckStatus.Pass);
        WarnCount = allItems.Count(item => item.Status == ReadinessCheckStatus.Warn);
        FailCount = allItems.Count(item => item.Status == ReadinessCheckStatus.Fail);
        NotApplicableCount = allItems.Count(item => item.Status == ReadinessCheckStatus.NotApplicable);
        ApplicableCount = PassCount + WarnCount + FailCount;
        ScorePercent = ApplicableCount == 0
            ? 100
            : (int)Math.Round((100.0 * PassCount) / ApplicableCount, MidpointRounding.AwayFromZero);
        IsReady = FailCount == 0 && WarnCount == 0;
        CanPrepareTemplate = canSetupModel || templatePlan.HasActionableSteps;
    }

    public ReadinessMode Mode { get; }

    public ReadinessMode SuggestedMode { get; }

    public IReadOnlyList<ReadinessCheckGroup> Groups { get; }

    public TemplatePreparationPlan TemplatePlan { get; }

    public int ApplicableCount { get; }

    public int PassCount { get; }

    public int WarnCount { get; }

    public int FailCount { get; }

    public int NotApplicableCount { get; }

    public int ScorePercent { get; }

    public bool IsReady { get; }

    public bool CanPrepareTemplate { get; }

    public bool CanSetupModel { get; }

    public bool CanFixFloorTypes { get; }
}

/// <summary>
/// Validates loaded family types or external .rfa family definitions against <c>standards/families.json</c>
/// (elevator, escalator, moving_walkway, ticket_gate) and door opening parameters (<c>gis_door</c>, <c>gis_access_con</c>).
/// </summary>
public static class FamilyStandardValidator
{
    public const string DoorsCategory = "OST_Doors";

    public static IReadOnlyList<string> CirculationCategories { get; } = new[]
    {
        "OST_GenericModel",
        "OST_SpecialityEquipment",
        "OST_MechanicalEquipment",
    };

    private static readonly string[] ValidatedCirculationParameters =
    {
        GisSharedParameters.GisKind,
        GisSharedParameters.GisCategory,
        GisSharedParameters.GisRestriction,
        GisSharedParameters.GisDirection,
        GisSharedParameters.GisLevelsServed,
    };

    public static IReadOnlyList<FamilyValidationItem> ValidateFamilies(
        IEnumerable<LoadedFamilyCheckInfo> familyTypes,
        GisToolStandard? standard = null)
    {
        GisToolStandard effective = standard ?? GisToolStandard.LoadEmbedded();
        List<FamilyValidationItem> results = new();
        foreach (LoadedFamilyCheckInfo familyType in familyTypes ?? Array.Empty<LoadedFamilyCheckInfo>())
        {
            if (familyType is null)
            {
                continue;
            }

            if (string.Equals(familyType.CategoryName, DoorsCategory, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(ValidateDoorFamily(familyType, effective));
                continue;
            }

            if (CirculationCategories.Contains(familyType.CategoryName, StringComparer.OrdinalIgnoreCase)
                || effective.Families.KindOf(familyType.FamilyName) != null
                || !string.IsNullOrWhiteSpace(familyType.GisKind)
                || familyType.ParameterBindings.ContainsKey(GisSharedParameters.GisKind))
            {
                results.Add(ValidateCirculationFamily(familyType, effective));
            }
        }

        return results;
    }

    private static FamilyValidationItem ValidateDoorFamily(LoadedFamilyCheckInfo familyType, GisToolStandard standard)
    {
        string displayName = DisplayName(familyType);
        List<string> bindingErrors = CollectBindingErrors(
            familyType,
            new[] { GisSharedParameters.GisDoor, GisSharedParameters.GisAccessCon },
            standard);
        if (bindingErrors.Count > 0)
        {
            return new FamilyValidationItem(
                displayName,
                DoorsCategory,
                ReadinessCheckStatus.Fail,
                string.Join(" ", bindingErrors),
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        bool isExternalRfa = !string.IsNullOrWhiteSpace(familyType.SourcePath);
        if (isExternalRfa)
        {
            bool bothEmbedded = familyType.HasGisDoorParameter && familyType.HasGisAccessConParameter;
            string detail = bothEmbedded
                ? "Door .rfa carries standard shared type parameters gis_door and gis_access_con."
                : "No conflicting embedded door parameters; project OST_Doors bindings supply gis_door and gis_access_con on load.";
            return new FamilyValidationItem(
                displayName,
                DoorsCategory,
                ReadinessCheckStatus.Pass,
                detail,
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        List<string> missingParams = new();
        if (!familyType.HasGisDoorParameter)
        {
            missingParams.Add(GisSharedParameters.GisDoor);
        }

        if (!familyType.HasGisAccessConParameter)
        {
            missingParams.Add(GisSharedParameters.GisAccessCon);
        }

        if (missingParams.Count > 0)
        {
            return new FamilyValidationItem(
                displayName,
                DoorsCategory,
                ReadinessCheckStatus.Fail,
                $"Missing door type parameter(s): {string.Join(", ", missingParams)}.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        return new FamilyValidationItem(
            displayName,
            DoorsCategory,
            ReadinessCheckStatus.Pass,
            "Door type carries gis_door and gis_access_con parameters.",
            familyType.SourcePath,
            familyType.InstanceIds);
    }

    private static FamilyValidationItem ValidateCirculationFamily(LoadedFamilyCheckInfo familyType, GisToolStandard standard)
    {
        string displayName = DisplayName(familyType);
        List<string> bindingErrors = CollectBindingErrors(familyType, ValidatedCirculationParameters, standard);
        if (bindingErrors.Count > 0)
        {
            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Fail,
                string.Join(" ", bindingErrors),
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        string kind = GisToolStandard.Normalize(familyType.GisKind).ToLowerInvariant();
        if (kind.Length > 0 && !GisAudit.GisKinds.Contains(kind))
        {
            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Fail,
                $"Invalid gis_kind '{familyType.GisKind}'. Use one of: elevator, escalator, moving_walkway, ticket_gate.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        StandardFamilyKind? listed = standard.Families.KindOf(familyType.FamilyName);
        if (listed != null)
        {
            if (kind.Length > 0 && !SameKind(kind, listed.Kind))
            {
                return new FamilyValidationItem(
                    displayName,
                    familyType.CategoryName,
                    ReadinessCheckStatus.Fail,
                    $"Standard {listed.Kind} family has mismatched gis_kind '{familyType.GisKind}'. Set gis_kind = {listed.Kind}.",
                    familyType.SourcePath,
                    familyType.InstanceIds);
            }

            List<string> missing = listed.RequiredTypeParameters
                .Where(param => param == GisSharedParameters.GisKind
                    ? kind.Length == 0
                    : !familyType.TypeParameters.TryGetValue(param, out string? value) || string.IsNullOrWhiteSpace(value))
                .ToList();
            if (missing.Count > 0)
            {
                return new FamilyValidationItem(
                    displayName,
                    familyType.CategoryName,
                    ReadinessCheckStatus.Warn,
                    $"Standard {listed.Kind} family is missing type parameter value(s): {string.Join(", ", missing)}.",
                    familyType.SourcePath,
                    familyType.InstanceIds);
            }

            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Pass,
                $"Matches standard {listed.Kind} family with gis_kind = {listed.Kind}.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        if (kind.Length > 0)
        {
            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Warn,
                $"Carries gis_kind '{kind}', but '{familyType.FamilyName}' is not listed in standards/families.json.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        if (standard.IsKnownCategory(familyType.GisCategory))
        {
            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Pass,
                $"Mapped by gis_category '{familyType.GisCategory}'.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        string? suggested = GisAudit.SuggestKind(familyType.FamilyName + " " + familyType.TypeName);
        if (suggested != null)
        {
            return new FamilyValidationItem(
                displayName,
                familyType.CategoryName,
                ReadinessCheckStatus.Warn,
                $"Looks like a {suggested} family but has no gis_kind and is not listed in standards/families.json.",
                familyType.SourcePath,
                familyType.InstanceIds);
        }

        return new FamilyValidationItem(
            displayName,
            familyType.CategoryName,
            ReadinessCheckStatus.Pass,
            "No circulation standard issues.",
            familyType.SourcePath,
            familyType.InstanceIds);
    }

    private static List<string> CollectBindingErrors(
        LoadedFamilyCheckInfo familyType,
        IEnumerable<string> parameterNames,
        GisToolStandard standard)
    {
        List<string> errors = new();
        if (familyType.ParameterBindings.Count == 0)
        {
            return errors;
        }

        foreach (string paramName in parameterNames)
        {
            if (!familyType.ParameterBindings.TryGetValue(paramName, out FamilyParameterBindingInfo? info))
            {
                continue;
            }

            GisParameterDefinition? expected = standard.Parameter(paramName);
            if (expected is null)
            {
                continue;
            }

            if (!info.IsShared)
            {
                errors.Add($"Parameter {paramName} is a non-shared family parameter; use the shipped shared parameter ({expected.Guid:D}).");
            }
            else if (info.Guid != expected.Guid)
            {
                string actualGuid = info.Guid.HasValue ? info.Guid.Value.ToString("D") : "(none)";
                errors.Add($"Parameter {paramName} has shared GUID {actualGuid}, expected {expected.Guid:D}.");
            }

            bool expectedInstance = expected.Binding == GisParameterBinding.Instance;
            if (info.IsInstance != expectedInstance)
            {
                string actualKind = info.IsInstance ? "instance" : "type";
                string expectedKind = expectedInstance ? "instance" : "type";
                errors.Add($"Parameter {paramName} is bound as an {actualKind} parameter; the standard requires a {expectedKind} parameter.");
            }
        }

        return errors;
    }

    private static bool SameKind(string a, string b) =>
        VerticalCirculation.TryMapGisKind(a, out string first)
        && VerticalCirculation.TryMapGisKind(b, out string second)
        && first == second;

    private static string DisplayName(LoadedFamilyCheckInfo familyType) =>
        string.IsNullOrWhiteSpace(familyType.TypeName) || string.Equals(familyType.FamilyName, familyType.TypeName, StringComparison.Ordinal)
            ? familyType.FamilyName
            : $"{familyType.FamilyName} : {familyType.TypeName}";
}

/// <summary>
/// Evaluates a Revit project or reusable .rte template against the Kanizu GIS standard, QA schedules,
/// export view template, family rules, and (in Project mode) CRS, ModelId, and level metadata.
/// </summary>
public static class ProjectReadinessEvaluator
{
    public const int JapanPlaneRectangularMinEpsg = 6669;
    public const int JapanPlaneRectangularMaxEpsg = 6687;

    public static ProjectReadinessReport Evaluate(
        ReadinessCheckSnapshot snapshot,
        ReadinessMode? modeOverride = null,
        GisToolStandard? standard = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        GisToolStandard effective = standard ?? GisToolStandard.LoadEmbedded();
        ReadinessMode suggestedMode = snapshot.SuggestedMode;
        ReadinessMode mode = modeOverride ?? suggestedMode;

        ModelSnapshot modelSnapshot = new(
            snapshot.BoundParameters,
            snapshot.FloorTypes,
            snapshot.Floors,
            snapshot.Materials,
            snapshot.Levels,
            Array.Empty<FamilyTypeInfo>(),
            snapshot.StandardVersionText);
        SetupPlan setupPlan = SetupPlanner.Plan(modelSnapshot, effective);
        TemplatePreparationPlan templatePlan = TemplateStandardPlanner.Plan(
            snapshot.Schedules,
            snapshot.ViewTemplateNames,
            snapshot.ExportPlanTemplateIssues);

        IReadOnlyList<FloorTypeProposal> proposals = FloorTypeMatcher.Propose(modelSnapshot, effective);
        bool canFixFloorTypes = FloorTypeMatcher.WriteBlocker(modelSnapshot).Length == 0 && proposals.Count > 0;

        List<ReadinessCheckGroup> groups = new()
        {
            EvaluateParametersGroup(modelSnapshot, effective),
            EvaluateFloorsAndMaterialsGroup(modelSnapshot, effective, proposals),
            EvaluateFamiliesGroup(snapshot, modelSnapshot, effective),
            EvaluateTemplateViewsGroup(templatePlan),
            EvaluateProjectOnlyGroup(snapshot, mode),
        };

        return new ProjectReadinessReport(
            mode,
            suggestedMode,
            groups,
            templatePlan,
            canSetupModel: !setupPlan.IsEmpty,
            canFixFloorTypes: canFixFloorTypes);
    }

    public static bool IsJapanPlaneRectangularEpsg(int? epsgCode) =>
        epsgCode.HasValue
        && epsgCode.Value >= JapanPlaneRectangularMinEpsg
        && epsgCode.Value <= JapanPlaneRectangularMaxEpsg;

    private static ReadinessCheckGroup EvaluateParametersGroup(ModelSnapshot snapshot, GisToolStandard standard)
    {
        List<ReadinessCheckItem> items = new();

        string versionText = (snapshot.StandardVersion ?? string.Empty).Trim();
        if (versionText.Length == 0)
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Fail,
                $"gis_standard_version is not stamped. Run Prepare Template or Set up model to apply v{standard.Version}.",
                ReadinessActionKind.PrepareTemplate));
        }
        else if (!int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Fail,
                $"gis_standard_version '{versionText}' is not a valid version number.",
                ReadinessActionKind.SetupModel));
        }
        else if (parsed < GisStandard.OldestSupportedVersion)
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Fail,
                $"Model is on standard v{parsed}, below oldest supported v{GisStandard.OldestSupportedVersion}. Run Set up model.",
                ReadinessActionKind.SetupModel));
        }
        else if (parsed < standard.Version)
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Warn,
                $"Model is on standard v{parsed}; v{standard.Version} is available. Run Set up model.",
                ReadinessActionKind.SetupModel));
        }
        else if (parsed > standard.Version)
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Warn,
                $"Model is on standard v{parsed}, newer than this Kanizu Revit (v{standard.Version})."));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "standard-version",
                "Kanizu GIS standard version",
                ReadinessCheckStatus.Pass,
                $"Stamped at current Kanizu GIS standard v{standard.Version}."));
        }

        List<string> missingParams = new();
        List<string> wrongGuidGisParams = new();
        List<string> wrongBindingParams = new();
        List<string> missingCategoryParams = new();
        List<string> legacyGuidParams = new();

        foreach (GisParameterDefinition def in standard.Parameters)
        {
            if (!snapshot.BoundParameters.TryGetValue(def.Name, out BoundParameterInfo? bound))
            {
                missingParams.Add(def.Name);
                continue;
            }

            if (bound.Binding != def.Binding)
            {
                wrongBindingParams.Add(def.Name);
            }

            if (bound.Guid != def.Guid)
            {
                if (GisStandardCheck.LegacyParameterNames.Contains(def.Name))
                {
                    legacyGuidParams.Add(def.Name);
                }
                else
                {
                    wrongGuidGisParams.Add(def.Name);
                }
            }

            if (def.Categories.Except(bound.Categories, StringComparer.Ordinal).Any())
            {
                missingCategoryParams.Add(def.Name);
            }
        }

        if (wrongGuidGisParams.Count > 0 || wrongBindingParams.Count > 0)
        {
            List<string> parts = new();
            if (wrongGuidGisParams.Count > 0)
            {
                parts.Add($"non-standard GUID on {string.Join(", ", wrongGuidGisParams)}");
            }

            if (wrongBindingParams.Count > 0)
            {
                parts.Add($"wrong type/instance binding on {string.Join(", ", wrongBindingParams)}");
            }

            items.Add(new ReadinessCheckItem(
                "shared-parameters",
                $"Shared parameters ({standard.Parameters.Count} frozen GUIDs)",
                ReadinessCheckStatus.Fail,
                $"Parameter conflict: {string.Join("; ", parts)}. Remove conflicting definitions and run Set up model."));
        }
        else if (missingParams.Count > 0 || missingCategoryParams.Count > 0)
        {
            List<string> parts = new();
            if (missingParams.Count > 0)
            {
                parts.Add($"{missingParams.Count} missing ({DescribeList(missingParams)})");
            }

            if (missingCategoryParams.Count > 0)
            {
                parts.Add($"{missingCategoryParams.Count} missing categories ({DescribeList(missingCategoryParams)})");
            }

            items.Add(new ReadinessCheckItem(
                "shared-parameters",
                $"Shared parameters ({standard.Parameters.Count} frozen GUIDs)",
                ReadinessCheckStatus.Fail,
                $"{string.Join("; ", parts)}. Run Prepare Template or Set up model.",
                ReadinessActionKind.PrepareTemplate));
        }
        else if (legacyGuidParams.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "shared-parameters",
                $"Shared parameters ({standard.Parameters.Count} frozen GUIDs)",
                ReadinessCheckStatus.Warn,
                $"All {standard.Parameters.Count} parameters are bound, but {string.Join(", ", legacyGuidParams)} keep legacy GUIDs from an older setup."));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "shared-parameters",
                $"Shared parameters ({standard.Parameters.Count} frozen GUIDs)",
                ReadinessCheckStatus.Pass,
                $"All {standard.Parameters.Count} shared parameters match the shipped GUIDs, bindings, and categories."));
        }

        return new ReadinessCheckGroup("parameters", "Shared parameters & standard", items);
    }

    private static ReadinessCheckGroup EvaluateFloorsAndMaterialsGroup(
        ModelSnapshot snapshot,
        GisToolStandard standard,
        IReadOnlyList<FloorTypeProposal> proposals)
    {
        List<ReadinessCheckItem> items = new();

        int missingMaterials = 0;
        int driftedMaterials = 0;
        foreach (StandardMaterial material in standard.Materials)
        {
            string expectedHex = material.FillHex.ToUpperInvariant();
            if (!snapshot.Materials.TryGetValue(material.Name, out MaterialInfo? existing))
            {
                missingMaterials++;
            }
            else if (!string.Equals(existing.Color, expectedHex, StringComparison.OrdinalIgnoreCase))
            {
                driftedMaterials++;
            }
        }

        if (missingMaterials > 0)
        {
            items.Add(new ReadinessCheckItem(
                "gis-materials",
                $"GIS materials ({standard.Materials.Count} theme colors)",
                ReadinessCheckStatus.Fail,
                $"{missingMaterials} GIS_* material(s) missing" + (driftedMaterials > 0 ? $" and {driftedMaterials} with drifted color." : "."),
                ReadinessActionKind.PrepareTemplate));
        }
        else if (driftedMaterials > 0)
        {
            items.Add(new ReadinessCheckItem(
                "gis-materials",
                $"GIS materials ({standard.Materials.Count} theme colors)",
                ReadinessCheckStatus.Warn,
                $"{driftedMaterials} GIS_* material(s) differ from the Kanizu theme color.",
                ReadinessActionKind.SetupModel));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "gis-materials",
                $"GIS materials ({standard.Materials.Count} theme colors)",
                ReadinessCheckStatus.Pass,
                $"All {standard.Materials.Count} GIS_* materials match the Kanizu theme colors."));
        }

        List<string> missingFloorTypes = new();
        List<string> driftedFloorTypes = new();
        foreach (StandardFloorType floorType in standard.FloorTypes)
        {
            FloorTypeInfo? existing = snapshot.FloorTypeByName(floorType.Name);
            if (existing is null)
            {
                missingFloorTypes.Add(floorType.Name);
            }
            else if (SetupPlanner.StandardTypeChanges(existing, floorType).Count > 0)
            {
                driftedFloorTypes.Add(floorType.Name);
            }
        }

        if (missingFloorTypes.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "standard-floor-types",
                $"Standard floor types ({standard.FloorTypes.Count})",
                ReadinessCheckStatus.Fail,
                $"{missingFloorTypes.Count} standard floor type(s) missing ({DescribeList(missingFloorTypes)})" +
                (driftedFloorTypes.Count > 0 ? $"; {driftedFloorTypes.Count} differ from standard." : "."),
                ReadinessActionKind.PrepareTemplate));
        }
        else if (driftedFloorTypes.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "standard-floor-types",
                $"Standard floor types ({standard.FloorTypes.Count})",
                ReadinessCheckStatus.Fail,
                $"{driftedFloorTypes.Count} standard floor type(s) differ from standard gis_category/gis_restriction/material ({DescribeList(driftedFloorTypes)}).",
                ReadinessActionKind.SetupModel));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "standard-floor-types",
                $"Standard floor types ({standard.FloorTypes.Count})",
                ReadinessCheckStatus.Pass,
                $"All {standard.FloorTypes.Count} standard floor types are present and match their category, restriction, and material."));
        }

        List<FloorTypeInfo> untypedInUse = snapshot.FloorTypes
            .Where(type => type.FloorIds.Count > 0 && FloorTypeMatcher.NeedsFix(type, snapshot, standard))
            .ToList();
        if (untypedInUse.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "untyped-floor-types",
                "In-use floor type categories",
                ReadinessCheckStatus.Warn,
                $"{untypedInUse.Count} in-use floor type(s) need gis_category ({DescribeList(untypedInUse.Select(t => t.Name).ToList())}).",
                proposals.Count > 0 ? ReadinessActionKind.FixFloorTypes : ReadinessActionKind.None,
                untypedInUse.SelectMany(type => type.FloorIds)));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "untyped-floor-types",
                "In-use floor type categories",
                ReadinessCheckStatus.Pass,
                "All in-use floor types have a valid gis_category (or no floors placed yet)."));
        }

        return new ReadinessCheckGroup("floors-materials", "Floor types & GIS materials", items);
    }

    private static ReadinessCheckGroup EvaluateFamiliesGroup(
        ReadinessCheckSnapshot snapshot,
        ModelSnapshot modelSnapshot,
        GisToolStandard standard)
    {
        List<ReadinessCheckItem> items = new();
        IReadOnlyList<FamilyValidationItem> validated = FamilyStandardValidator.ValidateFamilies(snapshot.LoadedFamilies, standard);

        List<FamilyValidationItem> circulation = validated
            .Where(item => !string.Equals(item.Category, FamilyStandardValidator.DoorsCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<FamilyValidationItem> circulationFails = circulation.Where(item => item.Status == ReadinessCheckStatus.Fail).ToList();
        List<FamilyValidationItem> circulationWarns = circulation.Where(item => item.Status == ReadinessCheckStatus.Warn).ToList();

        if (circulationFails.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "circulation-families",
                "Circulation families (families.json)",
                ReadinessCheckStatus.Fail,
                $"{circulationFails.Count} family type(s) have invalid or mismatched gis_kind: {DescribeList(circulationFails.Select(f => f.FamilyName).ToList())}.",
                ReadinessActionKind.None,
                circulationFails.SelectMany(f => f.ElementIds)));
        }
        else if (circulationWarns.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "circulation-families",
                "Circulation families (families.json)",
                ReadinessCheckStatus.Warn,
                $"{circulationWarns.Count} family type(s) need gis_kind or families.json review: {DescribeList(circulationWarns.Select(f => f.FamilyName).ToList())}.",
                ReadinessActionKind.None,
                circulationWarns.SelectMany(f => f.ElementIds)));
        }
        else
        {
            string detail = circulation.Count == 0
                ? "No non-conforming circulation families loaded (checked against elevator, escalator, moving_walkway, ticket_gate)."
                : $"{circulation.Count} loaded circulation family type(s) conform to standards/families.json.";
            items.Add(new ReadinessCheckItem(
                "circulation-families",
                "Circulation families (families.json)",
                ReadinessCheckStatus.Pass,
                detail));
        }

        List<FamilyValidationItem> doors = validated
            .Where(item => string.Equals(item.Category, FamilyStandardValidator.DoorsCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<FamilyValidationItem> doorFails = doors.Where(item => item.Status == ReadinessCheckStatus.Fail).ToList();
        bool doorParamsBound =
            modelSnapshot.BoundParameters.TryGetValue(GisSharedParameters.GisDoor, out BoundParameterInfo? doorParam)
            && doorParam.Categories.Contains(FamilyStandardValidator.DoorsCategory)
            && modelSnapshot.BoundParameters.TryGetValue(GisSharedParameters.GisAccessCon, out BoundParameterInfo? accessParam)
            && accessParam.Categories.Contains(FamilyStandardValidator.DoorsCategory);

        if (!doorParamsBound)
        {
            items.Add(new ReadinessCheckItem(
                "door-families",
                "Door opening parameters (gis_door / gis_access_con)",
                ReadinessCheckStatus.Fail,
                "gis_door and/or gis_access_con are not bound to Doors. Run Prepare Template or Set up model.",
                ReadinessActionKind.PrepareTemplate));
        }
        else if (doorFails.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "door-families",
                "Door opening parameters (gis_door / gis_access_con)",
                ReadinessCheckStatus.Fail,
                $"{doorFails.Count} loaded door family type(s) are missing gis_door or gis_access_con: {DescribeList(doorFails.Select(d => d.FamilyName).ToList())}.",
                ReadinessActionKind.None,
                doorFails.SelectMany(d => d.ElementIds)));
        }
        else
        {
            string detail = doors.Count == 0
                ? "gis_door and gis_access_con are bound to Doors (no door families loaded yet)."
                : $"All {doors.Count} loaded door family type(s) expose gis_door and gis_access_con.";
            items.Add(new ReadinessCheckItem(
                "door-families",
                "Door opening parameters (gis_door / gis_access_con)",
                ReadinessCheckStatus.Pass,
                detail));
        }

        return new ReadinessCheckGroup("families", "Families & .rfa standard", items);
    }

    private static ReadinessCheckGroup EvaluateTemplateViewsGroup(TemplatePreparationPlan templatePlan)
    {
        List<ReadinessCheckItem> items = new();
        Dictionary<string, QaSchedulePlanStep> pendingById = templatePlan.Schedules
            .ToDictionary(step => step.Definition.Id, StringComparer.Ordinal);

        foreach (QaScheduleDefinition definition in TemplateStandardDefinitions.QaSchedules)
        {
            if (pendingById.TryGetValue(definition.Id, out QaSchedulePlanStep? step))
            {
                if (step.CategoryMismatch)
                {
                    string actual = TemplateStandardDefinitions.CategoryLabel(step.ActualCategoryName);
                    string expected = TemplateStandardDefinitions.CategoryLabel(definition.CategoryName);
                    items.Add(new ReadinessCheckItem(
                        definition.Id,
                        definition.Name,
                        ReadinessCheckStatus.Fail,
                        $"Existing schedule '{definition.Name}' targets {actual}, expected {expected}. Rename or delete it, then run Prepare Template.",
                        ReadinessActionKind.None));
                }
                else
                {
                    string detail = step.CreateSchedule
                        ? $"QA schedule '{definition.Name}' is missing. Run Prepare Template to create it with {string.Join(", ", definition.RequiredFields)}."
                        : $"QA schedule '{definition.Name}' is missing required field(s): {string.Join(", ", step.MissingFields)}. Run Prepare Template to update it.";
                    items.Add(new ReadinessCheckItem(
                        definition.Id,
                        definition.Name,
                        ReadinessCheckStatus.Fail,
                        detail,
                        ReadinessActionKind.PrepareTemplate));
                }
            }
            else
            {
                items.Add(new ReadinessCheckItem(
                    definition.Id,
                    definition.Name,
                    ReadinessCheckStatus.Pass,
                    $"Present with required QA fields ({string.Join(", ", definition.RequiredFields)})."));
            }
        }

        if (templatePlan.CreateExportPlanTemplate)
        {
            items.Add(new ReadinessCheckItem(
                "view-template-export-plan",
                TemplateStandardDefinitions.ExportPlanViewTemplateName,
                ReadinessCheckStatus.Fail,
                $"View template '{TemplateStandardDefinitions.ExportPlanViewTemplateName}' is missing. Run Prepare Template to create it.",
                ReadinessActionKind.PrepareTemplate));
        }
        else if (templatePlan.RepairExportPlanTemplate)
        {
            items.Add(new ReadinessCheckItem(
                "view-template-export-plan",
                TemplateStandardDefinitions.ExportPlanViewTemplateName,
                ReadinessCheckStatus.Warn,
                $"View template '{TemplateStandardDefinitions.ExportPlanViewTemplateName}' needs settings repair: {string.Join("; ", templatePlan.ExportPlanTemplateIssues)}.",
                ReadinessActionKind.PrepareTemplate));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "view-template-export-plan",
                TemplateStandardDefinitions.ExportPlanViewTemplateName,
                ReadinessCheckStatus.Pass,
                $"View template '{TemplateStandardDefinitions.ExportPlanViewTemplateName}' is present and configured for GIS export."));
        }

        return new ReadinessCheckGroup("template-views", "QA schedules & export view template", items);
    }

    private static ReadinessCheckGroup EvaluateProjectOnlyGroup(ReadinessCheckSnapshot snapshot, ReadinessMode mode)
    {
        if (mode == ReadinessMode.Template)
        {
            return new ReadinessCheckGroup(
                "project-setup",
                "Project georeferencing, Model IDs & levels",
                new[]
                {
                    new ReadinessCheckItem(
                        "project-crs",
                        "Japan Plane Rectangular CRS (EPSG:6669–6687)",
                        ReadinessCheckStatus.NotApplicable,
                        "Not required in Template mode — configure per station project."),
                    new ReadinessCheckItem(
                        "model-ids",
                        "Host & linked model stable ModelId",
                        ReadinessCheckStatus.NotApplicable,
                        "Not required in Template mode — each project gets its own ModelId."),
                    new ReadinessCheckItem(
                        "level-metadata",
                        "Level ordinal & short_name completeness",
                        ReadinessCheckStatus.NotApplicable,
                        "Not required in Template mode — assign on project levels."),
                });
        }

        List<ReadinessCheckItem> items = new();

        if (!snapshot.ProjectCrsEpsgCode.HasValue || !IsJapanPlaneRectangularEpsg(snapshot.ProjectCrsEpsgCode))
        {
            string current = snapshot.ProjectCrsEpsgCode.HasValue
                ? $"Current EPSG:{snapshot.ProjectCrsEpsgCode.Value} is outside Japan Plane Rectangular CRS (EPSG:6669–6687)."
                : "No georeference CRS configured yet.";
            items.Add(new ReadinessCheckItem(
                "project-crs",
                "Japan Plane Rectangular CRS (EPSG:6669–6687)",
                ReadinessCheckStatus.Fail,
                $"{current} Run Georeference Setup and verify coordinates."));
        }
        else if (!snapshot.IsGeoreferenceVerified)
        {
            string name = string.IsNullOrWhiteSpace(snapshot.ProjectCrsName)
                ? $"EPSG:{snapshot.ProjectCrsEpsgCode.Value}"
                : $"EPSG:{snapshot.ProjectCrsEpsgCode.Value} ({snapshot.ProjectCrsName})";
            items.Add(new ReadinessCheckItem(
                "project-crs",
                "Japan Plane Rectangular CRS (EPSG:6669–6687)",
                ReadinessCheckStatus.Warn,
                $"{name} is configured, but georeference confidence is not yet Verified."));
        }
        else
        {
            string name = string.IsNullOrWhiteSpace(snapshot.ProjectCrsName)
                ? $"EPSG:{snapshot.ProjectCrsEpsgCode.Value}"
                : $"EPSG:{snapshot.ProjectCrsEpsgCode.Value} ({snapshot.ProjectCrsName})";
            items.Add(new ReadinessCheckItem(
                "project-crs",
                "Japan Plane Rectangular CRS (EPSG:6669–6687)",
                ReadinessCheckStatus.Pass,
                $"Verified on {name}."));
        }

        bool hasHostModelId = !string.IsNullOrWhiteSpace(snapshot.HostModelId);
        List<LinkedModelReadinessInfo> links = (snapshot.LinkedModels ?? Array.Empty<LinkedModelReadinessInfo>()).ToList();
        List<LinkedModelReadinessInfo> loadedMissingId = links
            .Where(link => link.IsLoaded && string.IsNullOrWhiteSpace(link.ModelId))
            .ToList();
        List<LinkedModelReadinessInfo> unloadedLinks = links
            .Where(link => !link.IsLoaded)
            .ToList();

        if (!hasHostModelId || loadedMissingId.Count > 0)
        {
            List<string> problems = new();
            if (!hasHostModelId)
            {
                problems.Add("host model has no stable ModelId");
            }

            if (loadedMissingId.Count > 0)
            {
                problems.Add($"{loadedMissingId.Count} loaded link(s) lack a ModelId ({DescribeList(loadedMissingId.Select(l => l.Name).ToList())})");
            }

            items.Add(new ReadinessCheckItem(
                "model-ids",
                "Host & linked model stable ModelId",
                ReadinessCheckStatus.Fail,
                $"{string.Join("; ", problems)}."));
        }
        else if (unloadedLinks.Count > 0)
        {
            items.Add(new ReadinessCheckItem(
                "model-ids",
                "Host & linked model stable ModelId",
                ReadinessCheckStatus.Warn,
                $"Host ModelId is set, but {unloadedLinks.Count} link(s) are unloaded and could not be checked ({DescribeList(unloadedLinks.Select(l => l.Name).ToList())})."));
        }
        else
        {
            int loadedCount = links.Count(link => link.IsLoaded);
            string detail = loadedCount == 0
                ? $"Host ModelId is present ({snapshot.HostModelId}); no linked models."
                : $"Host ModelId and all {loadedCount} loaded link(s) have stable ModelIds.";
            items.Add(new ReadinessCheckItem(
                "model-ids",
                "Host & linked model stable ModelId",
                ReadinessCheckStatus.Pass,
                detail));
        }

        IReadOnlyList<LevelInfo> levels = snapshot.Levels ?? Array.Empty<LevelInfo>();
        List<LevelInfo> checkedLevels = levels.Any(level => level.HasFloors)
            ? levels.Where(level => level.HasFloors).ToList()
            : levels.ToList();

        List<IGrouping<int, LevelInfo>> duplicateOrdinals = levels
            .Where(level => level.Ordinal.HasValue)
            .GroupBy(level => level.Ordinal!.Value)
            .Where(group => group.Count() > 1)
            .ToList();

        List<IGrouping<string, LevelInfo>> duplicateShortNames = levels
            .Where(level => !string.IsNullOrWhiteSpace(level.ShortName))
            .GroupBy(level => level.ShortName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        List<LevelInfo> missingMetadata = checkedLevels
            .Where(level => !level.Ordinal.HasValue || string.IsNullOrWhiteSpace(level.ShortName))
            .ToList();

        if (levels.Count == 0)
        {
            items.Add(new ReadinessCheckItem(
                "level-metadata",
                "Level ordinal & short_name completeness",
                ReadinessCheckStatus.Fail,
                "Model has no levels."));
        }
        else if (duplicateOrdinals.Count > 0 || duplicateShortNames.Count > 0 || missingMetadata.Count > 0)
        {
            List<string> problems = new();
            List<long> problemIds = new();
            if (duplicateOrdinals.Count > 0)
            {
                problems.Add($"duplicate ordinal(s): {string.Join(", ", duplicateOrdinals.Select(g => $"{g.Key} ({string.Join("/", g.Select(l => l.Name))})"))}");
                problemIds.AddRange(duplicateOrdinals.SelectMany(g => g.Select(l => l.ElementId)));
            }

            if (duplicateShortNames.Count > 0)
            {
                problems.Add($"duplicate short_name(s): {string.Join(", ", duplicateShortNames.Select(g => $"{g.Key} ({string.Join("/", g.Select(l => l.Name))})"))}");
                problemIds.AddRange(duplicateShortNames.SelectMany(g => g.Select(l => l.ElementId)));
            }

            if (missingMetadata.Count > 0)
            {
                problems.Add($"{missingMetadata.Count} level(s) missing ordinal or short_name ({DescribeList(missingMetadata.Select(l => l.Name).ToList())})");
                problemIds.AddRange(missingMetadata.Select(l => l.ElementId));
            }

            items.Add(new ReadinessCheckItem(
                "level-metadata",
                "Level ordinal & short_name completeness",
                ReadinessCheckStatus.Fail,
                string.Join("; ", problems) + ".",
                ReadinessActionKind.None,
                problemIds.Distinct()));
        }
        else
        {
            items.Add(new ReadinessCheckItem(
                "level-metadata",
                "Level ordinal & short_name completeness",
                ReadinessCheckStatus.Pass,
                $"All {checkedLevels.Count} level(s) have unique ordinals and short_name values."));
        }

        return new ReadinessCheckGroup("project-setup", "Project georeferencing, Model IDs & levels", items);
    }

    private static string DescribeList(IReadOnlyList<string> names)
    {
        const int maxShown = 4;
        string shown = string.Join(", ", names.Take(maxShown));
        return names.Count > maxShown ? $"{shown} and {names.Count - maxShown} more" : shown;
    }
}
