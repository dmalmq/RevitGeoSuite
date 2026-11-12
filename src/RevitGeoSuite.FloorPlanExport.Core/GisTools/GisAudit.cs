using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

public enum GisAuditSeverity
{
    Error = 0,
    Warning = 1,
    Info = 2,
}

/// <summary>One thing that would need fixing by hand after a GDB export.</summary>
public sealed class GisAuditFinding
{
    public GisAuditFinding(GisAuditSeverity severity, string topic, string message, IEnumerable<long>? elementIds = null)
    {
        Severity = severity;
        Topic = topic;
        Message = message;
        ElementIds = (elementIds ?? Array.Empty<long>()).ToList();
    }

    public GisAuditSeverity Severity { get; }

    /// <summary>standard, parameters, floor types, floors, levels or families.</summary>
    public string Topic { get; }

    public string Message { get; }

    /// <summary>The elements the finding is about, to select or show in Revit.</summary>
    public IReadOnlyList<long> ElementIds { get; }
}

/// <summary>
/// Audit: what would need fixing by hand after a GDB export. Reads a <see cref="ModelSnapshot"/> and
/// changes nothing. Errors make the export come out wrong; warnings make it guess.
/// </summary>
public static class GisAudit
{
    public static IReadOnlyList<string> GisKinds { get; } = new[] { "escalator", "moving_walkway", "movingwalkway", "elevator", "ticket_gate" };

    // Words in a family or type name that suggest a gis_kind, checked in order.
    private static readonly (string[] Words, string Kind)[] KindWords =
    {
        (new[] { "動く歩道", "movingwalk", "moving walk", "moving_walk", "travelator" }, "moving_walkway"),
        (new[] { "エスカレータ", "escalator" }, "escalator"),
        (new[] { "エレベータ", "elevator" }, "elevator"),
        (new[] { "自動改札", "改札機", "ticket gate", "ticket_gate" }, "ticket_gate"),
    };

    public static IReadOnlyList<GisAuditFinding> Run(ModelSnapshot snapshot, GisToolStandard standard)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (standard is null)
        {
            throw new ArgumentNullException(nameof(standard));
        }

        List<GisAuditFinding> findings = new();
        findings.AddRange(AuditVersion(snapshot, standard));
        findings.AddRange(AuditParameters(snapshot, standard));
        findings.AddRange(AuditFloorTypes(snapshot, standard));
        findings.AddRange(AuditFloors(snapshot, standard));
        findings.AddRange(AuditLevels(snapshot));
        findings.AddRange(AuditFamilies(snapshot, standard));

        // A stable sort: errors first, each severity in the order the checks found them.
        return findings.OrderBy(finding => (int)finding.Severity).ToList();
    }

    /// <summary>The findings that make the export come out wrong: the ones Export asks about first.</summary>
    public static IReadOnlyList<GisAuditFinding> BlockingFindings(IEnumerable<GisAuditFinding> findings) =>
        findings.Where(finding => finding.Severity == GisAuditSeverity.Error).ToList();

    /// <summary>"1 errors, 0 warnings, 2 notes": the counts by severity, for the export prompt.</summary>
    public static string Summary(IEnumerable<GisAuditFinding> findings)
    {
        List<GisAuditFinding> list = findings.ToList();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} errors, {1} warnings, {2} notes",
            list.Count(finding => finding.Severity == GisAuditSeverity.Error),
            list.Count(finding => finding.Severity == GisAuditSeverity.Warning),
            list.Count(finding => finding.Severity == GisAuditSeverity.Info));
    }

    /// <summary>The gis_kind a family or type name suggests, or null.</summary>
    public static string? SuggestKind(string? name)
    {
        string lowered = (name ?? string.Empty).ToLowerInvariant();
        foreach ((string[] words, string kind) in KindWords)
        {
            if (words.Any(word => lowered.IndexOf(word, StringComparison.Ordinal) >= 0))
            {
                return kind;
            }
        }

        return null;
    }

    private static IEnumerable<GisAuditFinding> AuditVersion(ModelSnapshot snapshot, GisToolStandard standard)
    {
        string current = standard.Version.ToString(CultureInfo.InvariantCulture);
        if (snapshot.StandardVersion.Length == 0)
        {
            yield return new GisAuditFinding(GisAuditSeverity.Warning, "standard", "The model has not been set up with the Kanizu GIS standard. Run Set up model.");
            yield break;
        }

        if (!int.TryParse(snapshot.StandardVersion.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int modelVersion))
        {
            yield return new GisAuditFinding(GisAuditSeverity.Error, "standard", $"gis_standard_version '{snapshot.StandardVersion}' is not a version number.");
            yield break;
        }

        if (modelVersion < standard.Version)
        {
            yield return new GisAuditFinding(GisAuditSeverity.Warning, "standard", $"The model is on standard version {modelVersion}; the current one is {current}. Run Set up model.");
        }
        else if (modelVersion > standard.Version)
        {
            yield return new GisAuditFinding(GisAuditSeverity.Warning, "standard", $"The model is on standard version {modelVersion}, newer than this RevitGeoSuite ({current}). Update RevitGeoSuite.");
        }
    }

    private static IEnumerable<GisAuditFinding> AuditParameters(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (var parameter in standard.Parameters)
        {
            if (!snapshot.BoundParameters.TryGetValue(parameter.Name, out BoundParameterInfo? bound))
            {
                yield return new GisAuditFinding(GisAuditSeverity.Warning, "parameters", $"Shared parameter {parameter.Name} is not bound. Run Set up model.");
            }
            else if (bound.Guid != parameter.Guid)
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Info,
                    "parameters",
                    $"Parameter {parameter.Name} uses its own GUID {GuidText(bound.Guid)}, not the standard's {parameter.Guid:D}. It exports, but other models cannot share schedules or families with it.");
            }
            else if (bound.Binding != parameter.Binding)
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "parameters",
                    $"Parameter {parameter.Name} is bound as a {BindingText(bound.Binding)} parameter; the standard binds it as {BindingText(parameter.Binding)}.");
            }
        }
    }

    private static IEnumerable<GisAuditFinding> AuditFloorTypes(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (FloorTypeInfo floorType in snapshot.FloorTypes)
        {
            if (floorType.FloorIds.Count == 0)
            {
                continue;
            }

            string category = GisToolStandard.Normalize(floorType.GisCategory);
            if (category.Length > 0 && !standard.IsKnownCategory(category))
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Error,
                    "floor types",
                    $"Floor type {floorType.Name} has gis_category '{category}', which is not a unit category. Its floors export by name instead.",
                    floorType.FloorIds);
            }
            else if (FloorTypeMatcher.NeedsFix(floorType, standard))
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "floor types",
                    $"Floor type {floorType.Name} ({floorType.FloorIds.Count} floors) has no gis_category, so its floors export by type name. Run Fix floor types.",
                    floorType.FloorIds);
            }
        }
    }

    private static IEnumerable<GisAuditFinding> AuditFloors(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (FloorInfo floor in snapshot.Floors)
        {
            if (floor.Category.Length > 0 && !standard.IsKnownCategory(floor.Category))
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Error,
                    "floors",
                    $"Floor {floor.ElementId} has category '{floor.Category}', which is not a unit category. It is ignored.",
                    new[] { floor.ElementId });
            }
        }
    }

    private static IEnumerable<GisAuditFinding> AuditLevels(ModelSnapshot snapshot)
    {
        foreach (LevelInfo level in snapshot.Levels)
        {
            if (!level.HasFloors)
            {
                continue;
            }

            List<string> missing = new();
            if (level.Ordinal is null)
            {
                missing.Add("ordinal");
            }

            if (level.ShortName.Length == 0)
            {
                missing.Add("short_name");
            }

            if (missing.Count > 0)
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "levels",
                    $"Level {level.Name} has no {string.Join(" or ", missing)}, so the export guesses it from the level name.",
                    new[] { level.ElementId });
            }
        }
    }

    private static IEnumerable<GisAuditFinding> AuditFamilies(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (FamilyTypeInfo familyType in snapshot.FamilyTypes)
        {
            string kind = GisToolStandard.Normalize(familyType.GisKind).ToLowerInvariant();
            string name = familyType.FamilyName + " : " + familyType.TypeName;
            if (kind.Length > 0)
            {
                if (!GisKinds.Contains(kind))
                {
                    yield return new GisAuditFinding(
                        GisAuditSeverity.Error,
                        "families",
                        $"{name} has gis_kind '{familyType.GisKind}'. Use one of {string.Join(", ", GisKinds)}.",
                        familyType.InstanceIds);
                }

                continue;
            }

            if (standard.IsKnownCategory(familyType.GisCategory))
            {
                // The export maps a family by its category or gis_category as well as by gis_kind.
                continue;
            }

            string? suggested = SuggestKind(familyType.FamilyName + " " + familyType.TypeName);
            if (suggested != null)
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "families",
                    $"{name} looks like a {suggested} but has no gis_kind, so it is found only by its family name, if at all.",
                    familyType.InstanceIds);
            }
        }
    }

    internal static string GuidText(Guid? guid) => guid.HasValue ? guid.Value.ToString("D") : "(project parameter)";

    internal static string BindingText(Standards.GisParameterBinding binding) =>
        binding == Standards.GisParameterBinding.Type ? "type" : "instance";
}
