using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A Revit stair: its base and top level, and the base levels it stands on when it is a multistorey group.</summary>
public sealed class AuditStair
{
    public AuditStair(long elementId, long? baseLevelId, long? topLevelId, IEnumerable<long>? storeyLevelIds = null)
    {
        ElementId = elementId;
        BaseLevelId = baseLevelId;
        TopLevelId = topLevelId;
        StoreyLevelIds = (storeyLevelIds ?? Array.Empty<long>()).ToList();
    }

    public long ElementId { get; }

    public long? BaseLevelId { get; }

    public long? TopLevelId { get; }

    /// <summary>The base level of each storey a multistorey stair group stands for; empty for a plain stair.</summary>
    public IReadOnlyList<long> StoreyLevelIds { get; }
}

/// <summary>A family instance that may be a stair or an escalator. The levels are filled for escalators only.</summary>
public sealed class AuditFamily
{
    public AuditFamily(
        long elementId,
        long typeId,
        string familyName,
        string typeName,
        string? gisKind = null,
        string? gisCategory = null,
        string? gisDirection = null,
        long? bottomLevelId = null,
        long? topLevelId = null)
    {
        ElementId = elementId;
        TypeId = typeId;
        FamilyName = familyName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        GisKind = GisToolStandard.Normalize(gisKind);
        GisCategory = GisToolStandard.Normalize(gisCategory);
        GisDirection = GisToolStandard.Normalize(gisDirection);
        BottomLevelId = bottomLevelId;
        TopLevelId = topLevelId;
    }

    public long ElementId { get; }

    public long TypeId { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    /// <summary>The instance's value, else the type's, as the export reads it.</summary>
    public string GisKind { get; }

    /// <summary>The instance's <c>category</c>, else the type's <c>gis_category</c>.</summary>
    public string GisCategory { get; }

    public string GisDirection { get; }

    public long? BottomLevelId { get; }

    public long? TopLevelId { get; }

    public string Label => FamilyName + " : " + TypeName;
}

/// <summary>A placed door: its type, its level and the type's <c>gis_door</c>.</summary>
public sealed class AuditDoor
{
    public AuditDoor(long elementId, long typeId, string typeLabel, long levelId, string? gisDoor)
    {
        ElementId = elementId;
        TypeId = typeId;
        TypeLabel = typeLabel ?? string.Empty;
        LevelId = levelId;
        GisDoor = GisToolStandard.Normalize(gisDoor);
    }

    public long ElementId { get; }

    public long TypeId { get; }

    /// <summary>"Family : Type".</summary>
    public string TypeLabel { get; }

    public long LevelId { get; }

    public string GisDoor { get; }
}

/// <summary>What an element the export never reads is, when it stands where a floor would.</summary>
public enum UnreadKind
{
    /// <summary>A Revit ramp: the export reads slopes from floors only.</summary>
    Ramp = 0,

    /// <summary>A family of the Floors category, in-place or loadable if Revit allows one: the export reads Revit floors only.</summary>
    FloorFamily = 1,
}

/// <summary>An element of a category the export never reads, which covers floor area on its level.</summary>
public sealed class AuditUnread
{
    public AuditUnread(long elementId, UnreadKind kind, long levelId)
    {
        ElementId = elementId;
        Kind = kind;
        LevelId = levelId;
    }

    public long ElementId { get; }

    public UnreadKind Kind { get; }

    public long LevelId { get; }
}

/// <summary>An element's <c>id</c> or a level's <c>level_id</c>.</summary>
public sealed class AuditIdValue
{
    public AuditIdValue(long elementId, string value)
    {
        ElementId = elementId;
        Value = GisToolStandard.Normalize(value);
    }

    public long ElementId { get; }

    public string Value { get; }
}

/// <summary>What <see cref="ExportAudit"/> reads: the model's stairs, escalators, ids and export views.</summary>
public sealed class ExportAuditSnapshot
{
    public ExportAuditSnapshot(
        ExportViewSnapshot views,
        IEnumerable<AuditStair>? stairs = null,
        IEnumerable<AuditFamily>? families = null,
        IEnumerable<AuditIdValue>? elementIds = null,
        IEnumerable<AuditIdValue>? levelIds = null,
        IEnumerable<LayoutFloor>? floors = null,
        IEnumerable<LayoutOpening>? openings = null,
        IEnumerable<KeyValuePair<long, double>>? levelElevations = null,
        IEnumerable<AuditDoor>? doors = null,
        IEnumerable<AuditUnread>? unread = null,
        IEnumerable<KeyValuePair<long, long>>? elementLevels = null)
    {
        Views = views ?? throw new ArgumentNullException(nameof(views));
        Stairs = (stairs ?? Array.Empty<AuditStair>()).ToList();
        Families = (families ?? Array.Empty<AuditFamily>()).ToList();
        ElementIds = (elementIds ?? Array.Empty<AuditIdValue>()).ToList();
        LevelIds = (levelIds ?? Array.Empty<AuditIdValue>()).ToList();
        Floors = (floors ?? Array.Empty<LayoutFloor>()).ToList();
        Openings = (openings ?? Array.Empty<LayoutOpening>()).ToList();
        LevelElevations = (levelElevations ?? Array.Empty<KeyValuePair<long, double>>()).ToList();
        Doors = (doors ?? Array.Empty<AuditDoor>()).ToList();
        Unread = (unread ?? Array.Empty<AuditUnread>()).ToList();
        Dictionary<long, long> levelOf = new();
        foreach (KeyValuePair<long, long> entry in elementLevels ?? Array.Empty<KeyValuePair<long, long>>())
        {
            levelOf[entry.Key] = entry.Value;
        }

        ElementLevels = levelOf;

        GisPlanLevels = new HashSet<long>(Views.PlanViews.Where(view => view.IsGisView && view.LevelId >= 0).Select(view => view.LevelId));
    }

    public ExportViewSnapshot Views { get; }

    public IReadOnlyList<AuditStair> Stairs { get; }

    public IReadOnlyList<AuditFamily> Families { get; }

    /// <summary>Every element's non-empty <c>id</c>.</summary>
    public IReadOnlyList<AuditIdValue> ElementIds { get; }

    /// <summary>Every level's non-empty <c>level_id</c>.</summary>
    public IReadOnlyList<AuditIdValue> LevelIds { get; }

    /// <summary>Every floor, for <see cref="FloorLayoutAudit"/>.</summary>
    public IReadOnlyList<LayoutFloor> Floors { get; }

    /// <summary>Every way onto a floor, for <see cref="FloorLayoutAudit"/>.</summary>
    public IReadOnlyList<LayoutOpening> Openings { get; }

    /// <summary>Every level's Level.ProjectElevation in feet, the coordinates of the floors' heights.</summary>
    public IReadOnlyList<KeyValuePair<long, double>> LevelElevations { get; }

    /// <summary>Every placed door, when <c>gis_door</c> is bound; else none, since Set up model's warning comes first.</summary>
    public IReadOnlyList<AuditDoor> Doors { get; }

    /// <summary>Every ramp and floor family: elements the export never reads.</summary>
    public IReadOnlyList<AuditUnread> Unread { get; }

    /// <summary>The level of every floor and family instance, for the findings that hold them.</summary>
    public IReadOnlyDictionary<long, long> ElementLevels { get; }

    /// <summary>The levels a <c>GIS_</c> plan exports.</summary>
    public ISet<long> GisPlanLevels { get; }
}

/// <summary>
/// Audit of how the model is built for the export, beside <see cref="GisAudit"/>'s check of the
/// standard: stairs the export cannot link, escalators without a direction, ids two rows would
/// share, connectors ending on a level that is not exported, a changed export template, and the
/// floor layout checks of <see cref="FloorLayoutAudit"/>.
/// </summary>
public static class ExportAudit
{
    private static readonly string[] StairWords = { "階段", "stair" };

    public static IReadOnlyList<GisAuditFinding> Run(ExportAuditSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        Dictionary<long, string> levelNames = snapshot.Views.Levels.ToDictionary(level => level.ElementId, level => level.Name);
        string LevelName(long id) => levelNames.TryGetValue(id, out string? name) ? name : id.ToString(CultureInfo.InvariantCulture);

        List<GisAuditFinding> findings = new();
        findings.AddRange(DuplicateIds(snapshot, LevelName));
        findings.AddRange(StairFamilies(snapshot.Families));
        findings.AddRange(StairsWithoutTop(snapshot.Stairs));
        findings.AddRange(MultistoreyStairs(snapshot.Stairs, LevelName));
        findings.AddRange(EscalatorsWithoutDirection(snapshot.Families));
        findings.AddRange(EndsWithoutGisPlan(snapshot, LevelName));
        findings.AddRange(Template(snapshot.Views));
        findings.AddRange(FloorLayoutAudit.Run(snapshot.Floors, snapshot.Openings, LevelName));
        findings.AddRange(SlopesWithoutToLevel(snapshot, LevelName));
        findings.AddRange(DoorsWithoutGisDoor(snapshot));
        findings.AddRange(UnreadOnGisPlans(snapshot, LevelName));
        return findings.OrderBy(finding => (int)finding.Severity).ToList();
    }

    /// <summary>
    /// A warning that the export decides by guess becomes an error once one of its elements is on a
    /// level a <c>GIS_</c> plan exports: the export then asks to fix or accept it first. Off those
    /// levels it stays a warning, since nothing of it is exported. Errors first, as <see cref="Run"/>.
    /// </summary>
    public static IReadOnlyList<GisAuditFinding> RaiseGuessesOnGisPlans(IEnumerable<GisAuditFinding> findings, ExportAuditSnapshot snapshot)
    {
        if (findings is null)
        {
            throw new ArgumentNullException(nameof(findings));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return findings.Select(finding => Raise(finding, snapshot)).OrderBy(finding => (int)finding.Severity).ToList();
    }

    private static GisAuditFinding Raise(GisAuditFinding finding, ExportAuditSnapshot snapshot)
    {
        if (!finding.ExportsByGuess || finding.Severity != GisAuditSeverity.Warning)
        {
            return finding;
        }

        int onPlans = finding.ElementIds.Count(id => snapshot.ElementLevels.TryGetValue(id, out long level) && snapshot.GisPlanLevels.Contains(level));
        if (onPlans == 0)
        {
            return finding;
        }

        int count = finding.ElementIds.Count;
        string which = count == 1
            ? "Its element is"
            : onPlans == count
                ? string.Format(CultureInfo.InvariantCulture, "All {0} of its elements are", count)
                : string.Format(CultureInfo.InvariantCulture, "{0} of its {1} elements are", onPlans, count);
        string message = finding.Message + " " + which + " on levels with a GIS_ plan, so the export asks to fix or accept this first.";
        return new GisAuditFinding(GisAuditSeverity.Error, finding.Topic, message, finding.ElementIds, exportsByGuess: true, fix: finding.Fix);
    }

    /// <summary>
    /// A door type without <c>gis_door</c> exports its openings with no door type; only doors on
    /// <c>GIS_</c> plans count. A warning, not an error: a blank <c>door</c> keeps the GDB's value in
    /// Kanizu where the opening already has one (kept when blank, A3), and an error on nearly every
    /// door type with no way to fix it from the row would teach people to export anyway. Revisit when
    /// the GIS standard page can set gis_door from the row (F6).
    /// </summary>
    private static IEnumerable<GisAuditFinding> DoorsWithoutGisDoor(ExportAuditSnapshot snapshot)
    {
        foreach (IGrouping<long, AuditDoor> type in snapshot.Doors
            .Where(door => door.GisDoor.Length == 0 && snapshot.GisPlanLevels.Contains(door.LevelId))
            .GroupBy(door => door.TypeId))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "doors",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Door type {0} ({1} on levels with a GIS_ plan) has no gis_door, so its openings export with no door type; Kanizu keeps the GDB's door type only where the opening already has one. Set gis_door on the type.",
                    type.First().TypeLabel,
                    type.Count()),
                type.Select(door => door.ElementId));
        }
    }

    /// <summary>Ramps and floor families on a level a <c>GIS_</c> plan exports: the area they cover is missing from the export.</summary>
    private static IEnumerable<GisAuditFinding> UnreadOnGisPlans(ExportAuditSnapshot snapshot, Func<long, string> levelName)
    {
        foreach (IGrouping<(long Level, UnreadKind Kind), AuditUnread> group in snapshot.Unread
            .Where(element => snapshot.GisPlanLevels.Contains(element.LevelId))
            .GroupBy(element => (element.LevelId, element.Kind)))
        {
            string what = group.Key.Kind == UnreadKind.Ramp
                ? "Revit ramp(s), which the export never reads: their area is missing from the export. Model each slope as a floor of a ramp floor type"
                : "floor(s) made as a family, which the export never reads: their area is missing from the export. Model them as Revit floors";
            yield return new GisAuditFinding(
                GisAuditSeverity.Error,
                "floors",
                string.Format(CultureInfo.InvariantCulture, "Level {0} has a GIS_ plan and {1} {2}.", levelName(group.Key.Level), group.Count(), what),
                group.Select(element => element.ElementId));
        }
    }

    /// <summary>
    /// Slope floors that rise like a level change but get no to_level: neither end sits on their own
    /// level, or the far end sits between levels. The export leaves to_level empty rather than guess.
    /// </summary>
    private static IEnumerable<GisAuditFinding> SlopesWithoutToLevel(ExportAuditSnapshot snapshot, Func<long, string> levelName)
    {
        const double MetresPerFoot = 0.3048;
        foreach (LayoutFloor floor in snapshot.Floors.Where(floor => floor.Category == VerticalCirculation.SlopeCategory))
        {
            SlopeReach<long> reach = VerticalCirculation.SlopeToLevel(floor.LevelId, floor.LowFeet, floor.HighFeet, snapshot.LevelElevations);
            string near = Metres(reach.NearEndFeet * MetresPerFoot);
            string far = Metres(reach.FarEndFeet * MetresPerFoot);
            string nearest = reach.NearestLevel is long other
                ? string.Format(CultureInfo.InvariantCulture, "the nearest level is {0}, {1:0.00} m from it", levelName(other), reach.NearestDistanceFeet * MetresPerFoot)
                : "there is no other level";
            string? message = reach.Outcome switch
            {
                SlopeOutcome.OwnLevelAtNeitherEnd => string.Format(
                    CultureInfo.InvariantCulture,
                    "Floor {0} (ramp) on level {1} runs from {2} to {3} against its level, so neither end is on it and the export does not guess where it leads. Its unit gets no to_level. Set its level and offset so its lower or upper end sits on its level.",
                    floor.ElementId,
                    levelName(floor.LevelId),
                    near,
                    far),
                SlopeOutcome.FarEndBetweenLevels => string.Format(
                    CultureInfo.InvariantCulture,
                    "Floor {0} (ramp) on level {1} leads to {2} against its level, which is on no level: {3}. Its unit gets no to_level, so the network does not link it to another level.",
                    floor.ElementId,
                    levelName(floor.LevelId),
                    far,
                    nearest),
                _ => null,
            };
            if (message != null)
            {
                yield return new GisAuditFinding(GisAuditSeverity.Info, "floors", message, new[] { floor.ElementId });
            }
        }
    }

    private static string Metres(double value) => value.ToString("+0.00 m;-0.00 m;0.00 m", CultureInfo.InvariantCulture);

    /// <summary>Whether the export takes the family for an escalator: by gis_kind, else by category, else by the standard family list, else by its name.</summary>
    public static bool IsEscalator(string? gisKind, string? gisCategory, string familyName, string typeName)
    {
        string kind = GisToolStandard.Normalize(gisKind);
        if (kind.Length > 0)
        {
            return VerticalCirculation.TryMapGisKind(kind, out string mapped) && mapped == VerticalCirculation.EscalatorCategory;
        }

        string category = GisToolStandard.Normalize(gisCategory);
        if (category.Length > 0)
        {
            return string.Equals(category, VerticalCirculation.EscalatorCategory, StringComparison.OrdinalIgnoreCase);
        }

        // The shipped list, as the export catalog reads it. A station-level family list would have to
        // reach this check, the audit (GisToolStandard.Families) and ZoneCatalog together.
        StandardFamilyKind? listed = FamilyStandard.Embedded.KindOf(familyName);
        if (listed != null)
        {
            return VerticalCirculation.TryMapGisKind(listed.Kind, out string mapped) && mapped == VerticalCirculation.EscalatorCategory;
        }

        return GisAudit.SuggestKind(familyName + " " + typeName) == "escalator";
    }

    /// <summary>Whether a family is named like a stair (階段, stair) and has no gis_kind that says it is something else.</summary>
    public static bool IsStairFamily(string? gisKind, string familyName, string typeName)
    {
        string name = (familyName + " " + typeName).ToLowerInvariant();
        return GisToolStandard.Normalize(gisKind).Length == 0 &&
            GisAudit.SuggestKind(name) == null &&
            StairWords.Any(word => name.IndexOf(word, StringComparison.Ordinal) >= 0);
    }

    private static IEnumerable<GisAuditFinding> DuplicateIds(ExportAuditSnapshot snapshot, Func<long, string> levelName)
    {
        foreach (CopiedId copied in CopiedIds.Find(snapshot.ElementIds))
        {
            int count = copied.Copies.Count + 1;
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "ids",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} elements share id {1} (a copy keeps its original's id): {2}. The next export keeps it on {3}, the oldest, and gives each copy it exports a new id.",
                    count,
                    copied.Id,
                    string.Join(", ", new[] { copied.Original }.Concat(copied.Copies)),
                    copied.Original),
                new[] { copied.Original }.Concat(copied.Copies));
        }

        foreach (IGrouping<string, AuditIdValue> shared in Shared(snapshot.LevelIds))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Error,
                "ids",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Levels {0} share level_id {1}, so the GDB gets one id for different levels. Clear level_id on all but one; Set up model gives a cleared level a new one.",
                    string.Join(", ", shared.Select(entry => levelName(entry.ElementId))),
                    shared.Key),
                shared.Select(entry => entry.ElementId),
                fix: GisFix.Write(
                    "levelid:" + shared.Key,
                    shared.Skip(1).Select(entry => new GisFixWrite(entry.ElementId, levelName(entry.ElementId), GisSharedParameters.LevelId, entry.Value, string.Empty)),
                    new[] { $"{levelName(shared.First().ElementId)} keeps level_id {shared.Key}." }));
        }
    }

    private static IEnumerable<IGrouping<string, AuditIdValue>> Shared(IEnumerable<AuditIdValue> values) =>
        values
            .Where(entry => entry.Value.Length > 0)
            .GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.ElementId).Distinct().Count() > 1);

    private static IEnumerable<GisAuditFinding> StairFamilies(IEnumerable<AuditFamily> families)
    {
        foreach (IGrouping<long, AuditFamily> type in families.Where(family => IsStairFamily(family.GisKind, family.FamilyName, family.TypeName)).GroupBy(family => family.TypeId))
        {
            AuditFamily first = type.First();
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "stairs",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1} placed) looks like a stair but is a family, not a Revit stair, so the export gives it no to_level and the network no stair link. Model it as a Revit stair.",
                    first.Label,
                    type.Count()),
                type.Select(family => family.ElementId),
                exportsByGuess: true);
        }
    }

    private static IEnumerable<GisAuditFinding> StairsWithoutTop(IEnumerable<AuditStair> stairs)
    {
        List<AuditStair> missing = stairs.Where(stair => stair.TopLevelId is null || stair.TopLevelId == stair.BaseLevelId).ToList();
        if (missing.Count > 0)
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "stairs",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} stair(s) have no top level other than their base, so their units get no to_level: {1}. Set each one's top level.",
                    missing.Count,
                    string.Join(", ", missing.Select(stair => stair.ElementId))),
                missing.Select(stair => stair.ElementId));
        }
    }

    private static IEnumerable<GisAuditFinding> MultistoreyStairs(IEnumerable<AuditStair> stairs, Func<long, string> levelName)
    {
        foreach (AuditStair stair in stairs.Where(stair => stair.StoreyLevelIds.Count > 1))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "stairs",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Stair {0} is a multistorey group on levels {1}. The export reads only its own base and top level, so the other storeys get a wrong or no to_level. Unpin each storey so it is a stair of its own.",
                    stair.ElementId,
                    string.Join(", ", stair.StoreyLevelIds.Select(levelName))),
                new[] { stair.ElementId });
        }
    }

    private static IEnumerable<GisAuditFinding> EscalatorsWithoutDirection(IEnumerable<AuditFamily> families)
    {
        foreach (IGrouping<long, AuditFamily> type in families
            .Where(family => IsEscalator(family.GisKind, family.GisCategory, family.FamilyName, family.TypeName) && VerticalCirculation.NormalizeDirection(family.GisDirection) is null)
            .GroupBy(family => family.TypeId))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "escalators",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} escalator(s) have no gis_direction of up, down or both, so the network cannot tell which way they run.",
                    type.First().Label,
                    type.Count()),
                type.Select(family => family.ElementId));
        }
    }

    private static IEnumerable<GisAuditFinding> EndsWithoutGisPlan(ExportAuditSnapshot snapshot, Func<long, string> levelName)
    {
        HashSet<long> exported = new(snapshot.Views.PlanViews.Where(view => view.IsGisView).Select(view => view.LevelId));
        HashSet<long> withFloors = new(snapshot.Views.Levels.Where(level => level.HasFloors).Select(level => level.ElementId));
        List<(long Level, long Element, bool Stair)> ends = new();
        foreach (AuditStair stair in snapshot.Stairs)
        {
            ends.AddRange(new[] { stair.BaseLevelId, stair.TopLevelId }.Where(level => level.HasValue).Select(level => (level!.Value, stair.ElementId, true)));
        }

        foreach (AuditFamily escalator in snapshot.Families.Where(family => IsEscalator(family.GisKind, family.GisCategory, family.FamilyName, family.TypeName)))
        {
            ends.AddRange(new[] { escalator.BottomLevelId, escalator.TopLevelId }.Where(level => level.HasValue).Select(level => (level!.Value, escalator.ElementId, false)));
        }

        foreach (IGrouping<long, (long Level, long Element, bool Stair)> level in ends.Where(end => !exported.Contains(end.Level)).GroupBy(end => end.Level))
        {
            List<long> elements = level.Select(end => end.Element).Distinct().ToList();
            int stairs = level.Where(end => end.Stair).Select(end => end.Element).Distinct().Count();
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "levels",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Level {0} has no GIS_ plan, so the export does not write it, but {1} stair(s) and {2} escalator(s) end on it: their links have nowhere to land. {3}",
                    levelName(level.Key),
                    stairs,
                    elements.Count - stairs,
                    withFloors.Contains(level.Key)
                        ? "Run Set up model to add its plan."
                        : "Add a GIS_ plan for it, or end them on a level that is exported."),
                elements,
                fix: withFloors.Contains(level.Key) ? GisFix.SetUp : GisFix.AddPlan(level.Key, ExportViewPlanner.Describe(ExportViewPlanner.PlanFor(snapshot.Views, level.Key))));
        }
    }

    private static IEnumerable<GisAuditFinding> Template(ExportViewSnapshot views)
    {
        string name = ExportViewPlanner.TemplateName;
        if (views.Template is not ExportTemplate template)
        {
            if (views.Levels.Any(level => level.HasFloors))
            {
                yield return new GisAuditFinding(
                    GisAuditSeverity.Warning,
                    "views",
                    $"The view template {name} is missing, so the GIS_ plans may hide what the export reads. Run Set up model.",
                    fix: GisFix.SetUp);
            }

            yield break;
        }

        if (ExportViewPlanner.RepairFor(template) is TemplateRepair repair)
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "views",
                $"The view template {name} was changed in what the export reads. Run Set up model to {repair.Changes()}.",
                fix: GisFix.SetUp);
        }

        List<ExportPlanView> others = views.PlanViews.Where(view => view.IsGisView && view.TemplateId != template.ElementId).ToList();
        if (others.Count > 0)
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "views",
                $"GIS_ plans {string.Join(", ", others.Select(view => view.Name))} do not use the view template {name}, so they may hide what the export reads or show lines it should not. Give them the template if they should export like the others.");
        }
    }
}
