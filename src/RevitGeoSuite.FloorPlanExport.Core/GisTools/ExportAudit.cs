using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

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
        IEnumerable<KeyValuePair<long, double>>? levelElevations = null)
    {
        Views = views ?? throw new ArgumentNullException(nameof(views));
        Stairs = (stairs ?? Array.Empty<AuditStair>()).ToList();
        Families = (families ?? Array.Empty<AuditFamily>()).ToList();
        ElementIds = (elementIds ?? Array.Empty<AuditIdValue>()).ToList();
        LevelIds = (levelIds ?? Array.Empty<AuditIdValue>()).ToList();
        Floors = (floors ?? Array.Empty<LayoutFloor>()).ToList();
        Openings = (openings ?? Array.Empty<LayoutOpening>()).ToList();
        LevelElevations = (levelElevations ?? Array.Empty<KeyValuePair<long, double>>()).ToList();
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
        return findings.OrderBy(finding => (int)finding.Severity).ToList();
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

    /// <summary>Whether the export takes the family for an escalator: by gis_kind, else by category, else by its name.</summary>
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
        foreach (IGrouping<string, AuditIdValue> shared in Shared(snapshot.ElementIds))
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Error,
                "ids",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} elements share id {1} (a copy keeps its original's id), so the GDB gets {0} rows with one id: {2}. Clear id on all but one; the export gives a cleared element a new id.",
                    shared.Count(),
                    shared.Key,
                    string.Join(", ", shared.Select(entry => entry.ElementId))),
                shared.Select(entry => entry.ElementId));
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
                shared.Select(entry => entry.ElementId));
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
                type.Select(family => family.ElementId));
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
                elements);
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
                    $"The view template {name} is missing, so the GIS_ plans may hide what the export reads. Run Set up model.");
            }

            yield break;
        }

        if (ExportViewPlanner.RepairFor(template) is TemplateRepair repair)
        {
            yield return new GisAuditFinding(
                GisAuditSeverity.Warning,
                "views",
                $"The view template {name} was changed in what the export reads. Run Set up model to {repair.Changes()}.");
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
