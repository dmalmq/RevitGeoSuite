using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

public enum GisAuditSeverity
{
    Error = 0,
    Warning = 1,
    Info = 2,
}

/// <summary>What the page can do about a finding in one click.</summary>
public enum GisAuditFixKind
{
    /// <summary>Write the standard type's gis_category, gis_restriction and material back onto the floor type.</summary>
    ResetFloorType,
}

/// <summary>A one-click repair of a finding: what to do and to which element.</summary>
public sealed class GisAuditFix
{
    public GisAuditFix(GisAuditFixKind kind, long targetId)
    {
        Kind = kind;
        TargetId = targetId;
    }

    public GisAuditFixKind Kind { get; }

    public long TargetId { get; }
}

/// <summary>One thing that would need fixing by hand after a GDB export.</summary>
public sealed class GisAuditFinding
{
    public GisAuditFinding(GisAuditSeverity severity, string topic, string message, IEnumerable<long>? elementIds = null, GisAuditFix? fix = null)
    {
        Severity = severity;
        Topic = topic;
        Message = message;
        ElementIds = (elementIds ?? Array.Empty<long>()).ToList();
        Fix = fix;
    }

    /// <summary>The one-click repair, or null when the finding is fixed by hand or by another tool.</summary>
    public GisAuditFix? Fix { get; }

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

    /// <summary>
    /// The lines the export report carries when it ran with audit errors: a count, then each error.
    /// Nothing for a model without errors.
    /// </summary>
    public static IEnumerable<string> ExportRecord(IReadOnlyList<GisAuditFinding> errors)
    {
        if (errors.Count == 0)
        {
            yield break;
        }

        yield return string.Format(CultureInfo.InvariantCulture, "Exported with {0} GIS audit error(s) accepted; the output may be wrong where they apply.", errors.Count);
        foreach (GisAuditFinding error in errors)
        {
            yield return "GIS audit error: " + error.Message;
        }
    }

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
            else if (parameter.Categories.Except(bound.Categories, StringComparer.Ordinal).ToList() is { Count: > 0 } missing)
            {
                // A standard that binds a parameter to one more category (gis_kind to Mechanical
                // Equipment) reaches a model set up before it here, without raising the version.
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "parameters",
                    $"Parameter {parameter.Name} is not bound to {string.Join(", ", missing.Select(CategoryText))}, so those elements cannot hold it. Run Set up model.");
            }
        }
    }

    private static IEnumerable<GisAuditFinding> AuditFloorTypes(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (FloorTypeInfo floorType in snapshot.FloorTypes)
        {
            if (StandardTypeChanged(floorType, standard) is { } changed)
            {
                yield return changed;
                continue;
            }

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
            else if (FloorTypeMatcher.NeedsFix(floorType, snapshot, standard))
            {
                IReadOnlyList<string> renames = FloorTypeMatcher.FreeTargets(floorType, snapshot, standard);
                if (renames.Count > 0)
                {
                    yield return new GisAuditFinding(
                        GisAuditSeverity.Warning,
                        "floor types",
                        $"Floor type {floorType.Name} ({floorType.FloorIds.Count} floors) uses an old name. Run Fix floor types to rename it to {string.Join(" or ", renames)}.{OneChoiceForAllFloors(floorType, renames)}",
                        floorType.FloorIds);
                    continue;
                }

                IReadOnlyList<string> undecided = FloorTypeMatcher.UndecidedTargets(floorType, standard);
                if (undecided.Count > 0)
                {
                    yield return new GisAuditFinding(
                        GisAuditSeverity.Warning,
                        "floor types",
                        $"Floor type {floorType.Name} ({floorType.FloorIds.Count} floors) uses an old name that can be {string.Join(" or ", undecided)}. Run Fix floor types to choose one. All {floorType.FloorIds.Count} floors of this type follow the choice. If some belong to the other one, duplicate the type and move those floors to the copy first.",
                        floorType.FloorIds);
                    continue;
                }

                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "floor types",
                    $"Floor type {floorType.Name} ({floorType.FloorIds.Count} floors) has no gis_category, so its floors export by type name. Run Fix floor types.",
                    floorType.FloorIds);
            }
        }
    }

    /// <summary>
    /// A floor type with one of the 21 standard names whose gis_category or gis_restriction is not
    /// the standard's: its floors export as something the name does not say. An error when floors use
    /// it, a warning when none does yet (the next floor drawn with it would be wrong). Case and
    /// surrounding spaces do not count; a material change alone is Set up model's to repair.
    /// </summary>
    private static GisAuditFinding? StandardTypeChanged(FloorTypeInfo floorType, GisToolStandard standard)
    {
        StandardFloorType? standardType = standard.FloorType(floorType.Name);
        if (standardType is null)
        {
            return null;
        }

        List<FloorTypeFieldChange> changed = SetupPlanner.StandardTypeChanges(floorType, standardType)
            .Where(change => change.Field != "material" &&
                !string.Equals(GisToolStandard.Normalize(change.OldValue), GisToolStandard.Normalize(change.NewValue), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (changed.Count == 0)
        {
            return null;
        }

        string what = string.Join(" and ", changed.Select(change => $"{change.Field} {Quoted(change.OldValue)}"));
        string standardSays = string.Join(", ", changed.Select(change => Quoted(change.NewValue)));
        bool inUse = floorType.FloorIds.Count > 0;
        string consequence = inUse
            ? $"Its {floorType.FloorIds.Count} floors export as {Quoted(GisToolStandard.Normalize(floorType.GisCategory))}, not as the name says."
            : "No floor uses it yet; the next one drawn with it would export wrong.";
        return new GisAuditFinding(
            inUse ? GisAuditSeverity.Error : GisAuditSeverity.Warning,
            "floor types",
            $"Floor type {floorType.Name} is a standard type, but its {what} {(changed.Count == 1 ? "differs" : "differ")} from the standard ({standardSays}). {consequence} Reset it to the standard.",
            floorType.FloorIds,
            new GisAuditFix(GisAuditFixKind.ResetFloorType, floorType.ElementId));
    }

    private static string Quoted(string value) => value.Length == 0 ? "(empty)" : "'" + value + "'";

    // One floor type can cover floors on both sides of the gates; the rename moves them all.
    private static string OneChoiceForAllFloors(FloorTypeInfo floorType, IReadOnlyList<string> renames) =>
        renames.Count > 1
            ? $" All {floorType.FloorIds.Count} floors of this type get the name you choose. If some belong to the other one, duplicate the type and move those floors to the copy first."
            : string.Empty;

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
                    $"Level {level.Name} has no {string.Join(" or ", missing)}. The export counts a missing ordinal from the other levels and leaves a missing short_name empty.",
                    new[] { level.ElementId });
            }
        }

        List<LevelInfo> withFloors = snapshot.Levels.Where(level => level.HasFloors).ToList();
        if (withFloors.Count > 0 && snapshot.Levels.All(level => level.Ordinal is null))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "levels",
                "No level has an ordinal, so the export takes the level nearest elevation 0 as ground, which is wrong for a model at its real height. Setting the ground floor's ordinal to 0 is enough.",
                withFloors.Select(level => level.ElementId));
        }
    }

    private static IEnumerable<GisAuditFinding> AuditFamilies(ModelSnapshot snapshot, GisToolStandard standard)
    {
        foreach (FamilyTypeInfo familyType in snapshot.FamilyTypes)
        {
            string kind = GisToolStandard.Normalize(familyType.GisKind).ToLowerInvariant();
            string name = familyType.FamilyName + " : " + familyType.TypeName;
            if (kind.Length > 0 && !GisKinds.Contains(kind))
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Error,
                    "families",
                    $"{name} has gis_kind '{familyType.GisKind}'. Use one of {string.Join(", ", GisKinds)}.",
                    familyType.InstanceIds);
                continue;
            }

            StandardFamilyKind? listed = standard.Families.KindOf(familyType.FamilyName);
            if (listed != null)
            {
                foreach (GisAuditFinding finding in AuditStandardFamily(familyType, name, kind, listed))
                {
                    yield return finding;
                }

                continue;
            }

            if (kind.Length > 0)
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "families",
                    $"{name} has gis_kind {kind}, but {familyType.FamilyName} is not a standard {kind} family ({StandardNames(standard.Families.Kind(kind))}). Use a standard family, or add this one to standards/families.json.",
                    familyType.InstanceIds);
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
                    $"{name} looks like {("aeiou".IndexOf(suggested[0]) >= 0 ? "an" : "a")} {suggested} but has no gis_kind and is not a standard family ({StandardNames(standard.Families.Kind(suggested))}), so the export skips it unless the export dialog maps the family by hand.",
                    familyType.InstanceIds);
            }
        }
    }

    // A family on the list: its type must say the listed kind and fill the other required parameters.
    private static IEnumerable<GisAuditFinding> AuditStandardFamily(FamilyTypeInfo familyType, string name, string kind, StandardFamilyKind listed)
    {
        if (kind.Length > 0 && !SameKind(kind, listed.Kind))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Error,
                "families",
                $"{name} is a standard {listed.Kind} family, but its gis_kind says {kind}. The export follows gis_kind. Set gis_kind to {listed.Kind} on the type.",
                familyType.InstanceIds);
            yield break;
        }

        List<string> missing = listed.RequiredTypeParameters
            .Where(parameter => parameter == GisSharedParameters.GisKind
                ? kind.Length == 0
                : !familyType.TypeParameters.TryGetValue(parameter, out string? value) || value.Length == 0)
            .ToList();
        if (missing.Count > 0)
        {
            string fill = string.Join(", ", missing.Select(parameter => parameter == GisSharedParameters.GisKind ? $"gis_kind = {listed.Kind}" : parameter));
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "families",
                $"{name} is a standard {listed.Kind} family, but its type has no {string.Join(" or ", missing)}. The export finds it by its family name; fill {fill} on the type.",
                familyType.InstanceIds);
        }
    }

    private static bool SameKind(string a, string b) =>
        VerticalCirculation.TryMapGisKind(a, out string first) && VerticalCirculation.TryMapGisKind(b, out string second) && first == second;

    private static string StandardNames(StandardFamilyKind? kind) =>
        kind == null || kind.Families.Count == 0 ? "the standard lists none yet" : "standard: " + string.Join(", ", kind.Families);

    private static string CategoryText(string category) =>
        category.StartsWith("OST_", StringComparison.Ordinal) ? category.Substring(4) : category;

    internal static string GuidText(Guid? guid) => guid.HasValue ? guid.Value.ToString("D") : "(project parameter)";

    internal static string BindingText(Standards.GisParameterBinding binding) =>
        binding == Standards.GisParameterBinding.Type ? "type" : "instance";
}
