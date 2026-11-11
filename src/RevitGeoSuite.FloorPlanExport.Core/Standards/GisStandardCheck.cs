using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Where a model stands against the Kanizu GIS standard.</summary>
public enum GisStandardState
{
    /// <summary>The model has no <c>gis_standard_version</c>: Set up model has never run.</summary>
    NotSetUp,

    /// <summary>On the version these tools know.</summary>
    Current,

    /// <summary>On an older version these tools still export, with a warning.</summary>
    UpdateAvailable,

    /// <summary>On a version below the oldest these tools export. The export is blocked.</summary>
    Unsupported,

    /// <summary>On a version newer than these tools: RevitGeoSuite needs updating.</summary>
    NewerThanTools,

    /// <summary><c>gis_standard_version</c> holds something that is not a version number.</summary>
    Unreadable,
}

public enum GisStandardIssueSeverity
{
    Warning,
    Error,
}

public sealed class GisStandardIssue
{
    public GisStandardIssue(GisStandardIssueSeverity severity, string message, bool blocksExport = false)
    {
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        BlocksExport = blocksExport;
    }

    public GisStandardIssueSeverity Severity { get; }

    public string Message { get; }

    public bool BlocksExport { get; }
}

/// <summary>A parameter bound in the model, as the check needs it.</summary>
public sealed class GisBoundParameterState
{
    public GisBoundParameterState(string name, Guid? guid, GisParameterBinding binding)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        Binding = binding;
    }

    public string Name { get; }

    /// <summary>The shared parameter's GUID, or null for a project (non-shared) parameter.</summary>
    public Guid? Guid { get; }

    public GisParameterBinding Binding { get; }
}

/// <summary>What the check reads from a model. Built from Revit by the add-in, by hand in tests.</summary>
public sealed class GisStandardModelState
{
    public string StandardVersionText { get; set; } = string.Empty;

    public IReadOnlyList<GisBoundParameterState> BoundParameters { get; set; } = Array.Empty<GisBoundParameterState>();

    /// <summary>The number of plan views whose name starts with <c>GIS_</c>.</summary>
    public int GisViewCount { get; set; }

    /// <summary>Names of floor types in use that are not standard types and have no known <c>gis_category</c>.</summary>
    public IReadOnlyList<string> FloorTypesWithoutCategory { get; set; } = Array.Empty<string>();
}

public sealed class GisStandardStatus
{
    internal GisStandardStatus(GisStandardState state, int? modelVersion, IReadOnlyList<GisStandardIssue> issues, bool appliesToModel)
    {
        State = state;
        ModelVersion = modelVersion;
        Issues = issues;
        AppliesToModel = appliesToModel;
    }

    public GisStandardState State { get; }

    public int? ModelVersion { get; }

    public int CurrentVersion => GisStandard.Version;

    public int OldestSupportedVersion => GisStandard.OldestSupportedVersion;

    public IReadOnlyList<GisStandardIssue> Issues { get; }

    /// <summary>
    /// True when the model holds the version stamp or any of the six parameters RevitGeoSuite has
    /// always used, so it is a GIS model and a notice about the standard is welcome.
    /// </summary>
    public bool AppliesToModel { get; }

    public bool BlocksExport => Issues.Any(issue => issue.BlocksExport);

    /// <summary>The one line the ribbon, the export and the export report show.</summary>
    public string Summary => State switch
    {
        GisStandardState.Current => $"Kanizu GIS standard {CurrentVersion}: current.",
        GisStandardState.UpdateAvailable => $"Kanizu GIS standard {ModelVersion}: version {CurrentVersion} is available. Run Set up model.",
        GisStandardState.Unsupported => $"Kanizu GIS standard {ModelVersion}: below the oldest version these tools export ({OldestSupportedVersion}). Run Set up model.",
        GisStandardState.NewerThanTools => $"Kanizu GIS standard {ModelVersion}: newer than this RevitGeoSuite ({CurrentVersion}). Update RevitGeoSuite.",
        GisStandardState.Unreadable => "Kanizu GIS standard: gis_standard_version is not a version number.",
        _ => "Kanizu GIS standard: not set up. Run Set up model.",
    };

    /// <summary>A short label for the ribbon button: current, update available, or not set up.</summary>
    public string ShortLabel => State switch
    {
        GisStandardState.Current => BlocksExport ? "Needs fixing" : "Current",
        GisStandardState.UpdateAvailable => "Update available",
        GisStandardState.Unsupported => "Update needed",
        GisStandardState.NewerThanTools => "Update add-in",
        GisStandardState.Unreadable => "Check version",
        _ => "Not set up",
    };
}

/// <summary>
/// The Kanizu GIS standards check: one set of rules for the notice when a model opens, the ribbon
/// state and the export. Reads only.
/// </summary>
/// <remarks>
/// The export is blocked only when its output would be wrong: a model below the oldest supported
/// version, or a parameter that only the standard defines (the <c>gis_</c> ones) bound under a
/// GUID other than the shipped one. The six parameters RevitGeoSuite has always used may keep an
/// older GUID; the export reads them by name and Set up model never rebinds them, so they warn.
/// </remarks>
public static class GisStandardCheck
{
    /// <summary>The parameters older RevitGeoSuite versions created, one file per PC.</summary>
    public static IReadOnlyList<string> LegacyParameterNames { get; } = new[]
    {
        GisSharedParameters.Id,
        GisSharedParameters.LevelId,
        GisSharedParameters.Name,
        GisSharedParameters.ShortName,
        GisSharedParameters.Category,
        GisSharedParameters.Ordinal,
    };

    public static GisStandardStatus Check(GisStandardModelState model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        List<GisStandardIssue> issues = new();
        string versionText = (model.StandardVersionText ?? string.Empty).Trim();
        int? version = null;
        GisStandardState state;
        if (versionText.Length == 0)
        {
            state = GisStandardState.NotSetUp;
            issues.Add(new GisStandardIssue(GisStandardIssueSeverity.Warning, "The model has not been set up with the Kanizu GIS standard. Run Set up model."));
        }
        else if (!int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            state = GisStandardState.Unreadable;
            issues.Add(new GisStandardIssue(GisStandardIssueSeverity.Error, $"gis_standard_version '{versionText}' is not a version number. Run Set up model."));
        }
        else
        {
            version = parsed;
            if (parsed < GisStandard.OldestSupportedVersion)
            {
                state = GisStandardState.Unsupported;
                issues.Add(new GisStandardIssue(
                    GisStandardIssueSeverity.Error,
                    $"The model is on standard version {parsed}, below the oldest version this RevitGeoSuite exports ({GisStandard.OldestSupportedVersion}). Run Set up model first.",
                    blocksExport: true));
            }
            else if (parsed < GisStandard.Version)
            {
                state = GisStandardState.UpdateAvailable;
                issues.Add(new GisStandardIssue(GisStandardIssueSeverity.Warning, $"The model is on standard version {parsed}; version {GisStandard.Version} is available. Run Set up model."));
            }
            else if (parsed > GisStandard.Version)
            {
                state = GisStandardState.NewerThanTools;
                issues.Add(new GisStandardIssue(GisStandardIssueSeverity.Warning, $"The model is on standard version {parsed}, newer than this RevitGeoSuite ({GisStandard.Version}). Update RevitGeoSuite."));
            }
            else
            {
                state = GisStandardState.Current;
            }
        }

        Dictionary<string, GisBoundParameterState> bound = new(StringComparer.Ordinal);
        foreach (GisBoundParameterState parameter in model.BoundParameters ?? Array.Empty<GisBoundParameterState>())
        {
            if (parameter != null && !bound.ContainsKey(parameter.Name))
            {
                bound[parameter.Name] = parameter;
            }
        }

        AddParameterIssues(bound, issues);

        bool setUp = state != GisStandardState.NotSetUp;
        if (setUp && model.GisViewCount == 0)
        {
            issues.Add(new GisStandardIssue(GisStandardIssueSeverity.Warning, "The model has no GIS_ plan view, so the export takes every plan view."));
        }

        IReadOnlyList<string> untyped = model.FloorTypesWithoutCategory ?? Array.Empty<string>();
        if (untyped.Count > 0)
        {
            issues.Add(new GisStandardIssue(
                GisStandardIssueSeverity.Warning,
                $"{untyped.Count} floor types in use have no gis_category, so their floors export by type name: {Describe(untyped)}. Run Fix floor types."));
        }

        bool applies = setUp || LegacyParameterNames.Any(bound.ContainsKey);
        return new GisStandardStatus(state, version, issues, applies);
    }

    private static void AddParameterIssues(IReadOnlyDictionary<string, GisBoundParameterState> bound, ICollection<GisStandardIssue> issues)
    {
        List<string> legacy = new();
        foreach (GisParameterDefinition standard in GisSharedParameters.All)
        {
            if (!bound.TryGetValue(standard.Name, out GisBoundParameterState? existing))
            {
                continue;
            }

            if (existing.Binding != standard.Binding)
            {
                issues.Add(new GisStandardIssue(
                    GisStandardIssueSeverity.Warning,
                    $"Parameter {standard.Name} is bound as {WithArticle(existing.Binding)} parameter; the standard binds it as {Label(standard.Binding)}, so the export cannot read it where the standard puts it."));
                continue;
            }

            if (existing.Guid == standard.Guid)
            {
                continue;
            }

            if (LegacyParameterNames.Contains(standard.Name))
            {
                legacy.Add(standard.Name);
                continue;
            }

            issues.Add(new GisStandardIssue(
                GisStandardIssueSeverity.Error,
                $"Parameter {standard.Name} is bound with GUID {existing.Guid?.ToString("D") ?? "(project parameter)"}, not the shipped {standard.Guid:D}. " +
                "Its values would not match other models and schedules. Remove it and run Set up model.",
                blocksExport: true));
        }

        if (legacy.Count > 0)
        {
            issues.Add(new GisStandardIssue(
                GisStandardIssueSeverity.Warning,
                $"Parameters {string.Join(", ", legacy)} keep the model's own GUIDs from an older RevitGeoSuite. They export by name, but other models cannot share schedules or families with them."));
        }
    }

    private static string WithArticle(GisParameterBinding binding) => binding == GisParameterBinding.Type ? "a type" : "an instance";

    private static string Label(GisParameterBinding binding) => binding == GisParameterBinding.Type ? "type" : "instance";

    private static string Describe(IReadOnlyList<string> names)
    {
        const int shown = 5;
        string list = string.Join(", ", names.Take(shown));
        return names.Count > shown ? list + $" and {names.Count - shown} more" : list;
    }
}
