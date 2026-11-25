using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Severity assigned to a <see cref="RuleCatalogEntry"/>.</summary>
public enum RuleSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One stable Kanizu readiness rule, identified by a canonical <c>KZ-*</c> ID.</summary>
public sealed class RuleCatalogEntry
{
    public RuleCatalogEntry(
        string ruleId,
        string group,
        RuleSeverity severity,
        string title,
        string description,
        IReadOnlyList<string>? legacyItemIds = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Severity = severity;
        Title = title ?? string.Empty;
        Description = description ?? string.Empty;
        LegacyItemIds = Array.AsReadOnly((legacyItemIds ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>Canonical stable id, e.g. <c>KZ-TPL-002</c>.</summary>
    public string RuleId { get; }

    /// <summary>Category/group, e.g. <c>parameters</c>, <c>template</c>, <c>view</c>, <c>family</c>.</summary>
    public string Group { get; }

    public RuleSeverity Severity { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>Legacy Phase 4B ItemIds that resolve to this canonical rule (UI and test back-compat).</summary>
    public IReadOnlyList<string> LegacyItemIds { get; }
}

/// <summary>
/// The stable Kanizu readiness rule catalog. Every <see cref="ReadinessCheckItem"/> emitted by
/// <see cref="ProjectReadinessEvaluator"/> carries a canonical <c>KZ-*</c> <see cref="RuleCatalogEntry.RuleId"/>
/// so handover receipts, dashboards and automation can match rules by a stable identifier that
/// survives UI label changes. Phase 4B legacy ItemIds remain resolvable via <see cref="TryLookupByLegacyItemId"/>.
/// </summary>
public static class RuleCatalog
{
    private static readonly RuleCatalogEntry[] EntriesArray =
    {
        // KZ-PARAM-*: shared parameter bindings
        new RuleCatalogEntry(
            "KZ-PARAM-001",
            "parameters",
            RuleSeverity.Error,
            "Kanizu GIS standard version stamped",
            "gis_standard_version must be stamped on the model at the current standard version.",
            new[] { "standard-version" }),
        new RuleCatalogEntry(
            "KZ-PARAM-002",
            "parameters",
            RuleSeverity.Error,
            "Shared parameters bound with shipped GUIDs",
            "All 15 Kanizu shared parameters must be bound to their expected categories with the shipped GUID and binding (type/instance).",
            new[] { "shared-parameters" }),

        // KZ-TPL-*: template hygiene (.rte) and QA artifacts
        new RuleCatalogEntry(
            "KZ-TPL-001",
            "template",
            RuleSeverity.Error,
            "No baked-in project identity on template",
            "In Template mode the document must not carry a baked-in GeoProjectInfo.ModelId or GisStationFolder; a template is zero-identity by construction.",
            new[] { "template-identity" }),
        new RuleCatalogEntry(
            "KZ-TPL-002",
            "template",
            RuleSeverity.Error,
            "No baked-in element or level identity",
            "In Template mode no level may carry a non-empty level_id and no placed element may carry a baked-in id or gis_key; those are per-project values.",
            new[] { "template-element-ids" }),
        new RuleCatalogEntry(
            "KZ-TPL-003",
            "template",
            RuleSeverity.Error,
            "QA schedules present",
            "The three Kanizu QA schedules (Floors, Circulation, Doors) must exist with their required fields.",
            new[] { "qa-schedule-floors", "qa-schedule-circulation", "qa-schedule-doors" }),

        // KZ-FLOOR-*: floors & materials
        new RuleCatalogEntry(
            "KZ-FLOOR-001",
            "floors",
            RuleSeverity.Error,
            "GIS materials present and on-theme",
            "GIS_* theme materials must be present at the Kanizu theme colour.",
            new[] { "gis-materials" }),
        new RuleCatalogEntry(
            "KZ-FLOOR-002",
            "floors",
            RuleSeverity.Error,
            "Standard floor types",
            "All standard Kanizu floor types must exist and match their gis_category, gis_restriction and material.",
            new[] { "standard-floor-types" }),
        new RuleCatalogEntry(
            "KZ-FLOOR-003",
            "floors",
            RuleSeverity.Warning,
            "In-use floor type categories",
            "In-use floor types must map to a known gis_category (Fix floor types can repair them).",
            new[] { "untyped-floor-types" }),

        // KZ-RFA-*: families on disk / loaded family rules
        new RuleCatalogEntry(
            "KZ-RFA-001",
            "family",
            RuleSeverity.Error,
            "Loaded circulation family bindings",
            "Loaded circulation families must bind shared parameters with the Kanizu GUIDs and bindings.",
            new[] { "circulation-families" }),
        new RuleCatalogEntry(
            "KZ-RFA-002",
            "family",
            RuleSeverity.Error,
            "Door shared parameters bound",
            "OST_Doors must have gis_door and gis_access_con bound with the shipped GUIDs.",
            new[] { "door-families" }),
        new RuleCatalogEntry(
            "KZ-RFA-003",
            "family",
            RuleSeverity.Warning,
            "External .rfa validation",
            "External .rfa families folders must pass the standards/families.json audit."),
        new RuleCatalogEntry(
            "KZ-RFA-004",
            "family",
            RuleSeverity.Warning,
            "Non-GIS .rfa folder segregated",
            "Mixed .rfa folders should segregate GIS from non-GIS families."),
        new RuleCatalogEntry(
            "KZ-RFA-005",
            "family",
            RuleSeverity.Info,
            "Family folder inspection",
            "A folder of .rfa files has been inspected for Kanizu compliance."),

        // KZ-ELEM-*: placed element data quality
        new RuleCatalogEntry(
            "KZ-ELEM-001",
            "element",
            RuleSeverity.Error,
            "Door gis_door values present",
            "In Project mode, once any placed door type has a value, all placed door types require non-empty, single-line gis_door text.",
            new[] { "door-values" }),
        new RuleCatalogEntry(
            "KZ-ELEM-002",
            "element",
            RuleSeverity.Warning,
            "Circulation gis_kind completeness",
            "Placed circulation elements should carry gis_kind matching standards/families.json."),
        new RuleCatalogEntry(
            "KZ-ELEM-003",
            "element",
            RuleSeverity.Warning,
            "Floor gis_category completeness",
            "Placed floors should map to a known gis_category by type or override."),
        new RuleCatalogEntry(
            "KZ-ELEM-004",
            "element",
            RuleSeverity.Warning,
            "Element gis_key stamped",
            "Exported elements should carry a stable gis_key written by the last export."),

        // KZ-VIEW-*: export view template & hide filters
        new RuleCatalogEntry(
            "KZ-VIEW-001",
            "view",
            RuleSeverity.Error,
            "GIS Export view template present",
            "The canonical 'GIS Export' view template must exist and be configured for GIS export.",
            new[] { "view-template-export-plan" }),
        new RuleCatalogEntry(
            "KZ-VIEW-002",
            "view",
            RuleSeverity.Warning,
            "GIS Export view template settings",
            "The 'GIS Export' view template must show the categories the export reads and hide the others."),
        new RuleCatalogEntry(
            "KZ-VIEW-003",
            "view",
            RuleSeverity.Warning,
            "GIS_* plans use canonical template",
            "Every GIS_* plan view must use the 'GIS Export' view template."),
        new RuleCatalogEntry(
            "KZ-VIEW-004",
            "view",
            RuleSeverity.Error,
            "No parameter hide-filter on export views",
            "Export plan views and their templates require visible parameter filters.",
            new[] { "view-template-hide-filter" }),
        new RuleCatalogEntry(
            "KZ-VIEW-005",
            "view",
            RuleSeverity.Warning,
            "GIS_* plan crop coverage",
            "GIS_* plan crop frames should contain the footprint of their level."),

        // KZ-FED-*: federation / project only checks (CRS, ModelIds, levels)
        new RuleCatalogEntry(
            "KZ-FED-001",
            "federation",
            RuleSeverity.Error,
            "Project CRS inside Japan Plane Rectangular range",
            "Project mode requires EPSG:6669..6687 and a verified georeference.",
            new[] { "project-crs" }),
        new RuleCatalogEntry(
            "KZ-FED-002",
            "federation",
            RuleSeverity.Error,
            "Host & linked model stable ModelId",
            "Host document and every loaded link must carry a stable GeoProjectInfo.ModelId.",
            new[] { "model-ids" }),
        new RuleCatalogEntry(
            "KZ-FED-003",
            "federation",
            RuleSeverity.Error,
            "Level ordinal & short_name completeness",
            "Every level with floors must carry a unique ordinal and short_name.",
            new[] { "level-metadata" }),
        new RuleCatalogEntry(
            "KZ-FED-004",
            "federation",
            RuleSeverity.Warning,
            "Unloaded link review",
            "Unloaded links cannot be verified for ModelId; reload before export."),
    };

    private static readonly Dictionary<string, RuleCatalogEntry> ByRuleId = BuildById();
    private static readonly Dictionary<string, RuleCatalogEntry> ByLegacy = BuildByLegacy();

    /// <summary>The whole catalog, in a stable published order.</summary>
    public static IReadOnlyList<RuleCatalogEntry> Entries { get; } = Array.AsReadOnly(EntriesArray);

    public static RuleCatalogEntry Lookup(string ruleId)
    {
        if (ruleId == null)
        {
            throw new ArgumentNullException(nameof(ruleId));
        }

        if (!ByRuleId.TryGetValue(ruleId, out RuleCatalogEntry? entry) || entry is null)
        {
            throw new KeyNotFoundException($"Unknown Kanizu rule id '{ruleId}'.");
        }

        return entry;
    }

    public static bool TryLookup(string ruleId, out RuleCatalogEntry entry)
    {
        if (ruleId != null && ByRuleId.TryGetValue(ruleId, out RuleCatalogEntry? found) && found != null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>Resolve a Phase 4B legacy ItemId (e.g. <c>view-template-export-plan</c>) to a canonical rule.</summary>
    public static bool TryLookupByLegacyItemId(string legacyItemId, out RuleCatalogEntry entry)
    {
        if (legacyItemId != null && ByLegacy.TryGetValue(legacyItemId, out RuleCatalogEntry? found) && found != null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>The canonical rule id for a Phase 4B legacy ItemId, or the input unchanged when no mapping exists.</summary>
    public static string CanonicalRuleId(string legacyItemId)
    {
        if (legacyItemId != null && ByLegacy.TryGetValue(legacyItemId, out RuleCatalogEntry? entry) && entry != null)
        {
            return entry.RuleId;
        }

        return legacyItemId ?? string.Empty;
    }

    private static Dictionary<string, RuleCatalogEntry> BuildById()
    {
        Dictionary<string, RuleCatalogEntry> map = new(StringComparer.Ordinal);
        foreach (RuleCatalogEntry entry in EntriesArray)
        {
            if (map.ContainsKey(entry.RuleId))
            {
                throw new InvalidOperationException($"Duplicate Kanizu rule id '{entry.RuleId}' in catalog.");
            }

            map[entry.RuleId] = entry;
        }

        return map;
    }

    private static Dictionary<string, RuleCatalogEntry> BuildByLegacy()
    {
        Dictionary<string, RuleCatalogEntry> map = new(StringComparer.Ordinal);
        foreach (RuleCatalogEntry entry in EntriesArray)
        {
            foreach (string legacy in entry.LegacyItemIds)
            {
                map.Add(legacy, entry);
            }
        }

        return map;
    }
}
