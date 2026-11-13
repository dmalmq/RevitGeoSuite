using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

public enum ParameterSetupAction
{
    /// <summary>Not bound: bind it from the standard's file.</summary>
    Bind,

    /// <summary>Bound with the standard's GUID but missing categories: add them.</summary>
    Extend,

    /// <summary>Bound under another GUID with every category: keep it as it is.</summary>
    KeepLegacy,

    /// <summary>Bound under another GUID and missing categories: keep the GUID, add the categories.</summary>
    ExtendLegacy,

    /// <summary>Bound as the wrong kind (type or instance): leave it and report it.</summary>
    BindingMismatch,

    Ok,
}

public enum SetupAction
{
    Create,
    Update,
}

public sealed class ParameterSetupStep
{
    public ParameterSetupStep(string name, ParameterSetupAction action, IEnumerable<string>? missingCategories = null, string detail = "")
    {
        Name = name;
        Action = action;
        MissingCategories = (missingCategories ?? Array.Empty<string>()).ToList();
        Detail = detail;
    }

    public string Name { get; }

    public ParameterSetupAction Action { get; }

    public IReadOnlyList<string> MissingCategories { get; }

    public string Detail { get; }
}

public sealed class MaterialSetupStep
{
    public MaterialSetupStep(string name, SetupAction action, string fill, string current = "")
    {
        Name = name;
        Action = action;
        Fill = fill;
        Current = current;
    }

    public string Name { get; }

    public SetupAction Action { get; }

    /// <summary>The standard colour, <c>#RRGGBB</c>.</summary>
    public string Fill { get; }

    /// <summary>The model's colour before an update.</summary>
    public string Current { get; }
}

/// <summary>One field of a standard floor type that differs in the model: name, old value, new value.</summary>
public sealed class FloorTypeFieldChange
{
    public FloorTypeFieldChange(string field, string oldValue, string newValue)
    {
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>gis_category, gis_restriction or material.</summary>
    public string Field { get; }

    public string OldValue { get; }

    public string NewValue { get; }
}

public sealed class FloorTypeSetupStep
{
    public FloorTypeSetupStep(string name, SetupAction action, StandardFloorType standard, long? elementId = null, IEnumerable<FloorTypeFieldChange>? changes = null)
    {
        Name = name;
        Action = action;
        Standard = standard;
        ElementId = elementId;
        Changes = (changes ?? Array.Empty<FloorTypeFieldChange>()).ToList();
    }

    public string Name { get; }

    public SetupAction Action { get; }

    public StandardFloorType Standard { get; }

    /// <summary>The existing type an update changes; null for a create.</summary>
    public long? ElementId { get; }

    public IReadOnlyList<FloorTypeFieldChange> Changes { get; }
}

/// <summary>What Set up model would change in a model, worked out before anything changes.</summary>
public sealed class SetupPlan
{
    public SetupPlan(
        IReadOnlyList<ParameterSetupStep> parameters,
        IReadOnlyList<MaterialSetupStep> materials,
        IReadOnlyList<FloorTypeSetupStep> floorTypes,
        string versionFrom,
        string versionTo,
        IReadOnlyList<long> echoFloorIds)
    {
        Parameters = parameters;
        Materials = materials;
        FloorTypes = floorTypes;
        VersionFrom = versionFrom;
        VersionTo = versionTo;
        EchoFloorIds = echoFloorIds;
    }

    public IReadOnlyList<ParameterSetupStep> Parameters { get; }

    public IReadOnlyList<MaterialSetupStep> Materials { get; }

    public IReadOnlyList<FloorTypeSetupStep> FloorTypes { get; }

    public string VersionFrom { get; }

    public string VersionTo { get; }

    /// <summary>
    /// Floors whose own category only repeats what an older export wrote back into them. They are
    /// cleared when the version is stamped, because from then on RevitGeoSuite counts every floor
    /// category as a deliberate override of the type.
    /// </summary>
    public IReadOnlyList<long> EchoFloorIds { get; }

    public bool SetsVersion => VersionFrom != VersionTo;

    public bool IsEmpty =>
        !SetsVersion &&
        Parameters.All(step => step.Action is ParameterSetupAction.Ok or ParameterSetupAction.KeepLegacy or ParameterSetupAction.BindingMismatch) &&
        Materials.Count == 0 &&
        FloorTypes.Count == 0 &&
        EchoFloorIds.Count == 0;
}

/// <summary>
/// Set up model's plan. It only adds and updates: it never deletes, never rebinds a parameter the
/// model already holds under another GUID, and never changes the type of a floor, so every element
/// keeps its UniqueId and its revit_key.
/// </summary>
public static class SetupPlanner
{
    public static SetupPlan Plan(ModelSnapshot snapshot, GisToolStandard standard)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (standard is null)
        {
            throw new ArgumentNullException(nameof(standard));
        }

        List<ParameterSetupStep> parameters = standard.Parameters.Select(parameter => PlanParameter(snapshot, parameter)).ToList();

        List<MaterialSetupStep> materials = new();
        foreach (StandardMaterial material in standard.Materials)
        {
            string fill = material.FillHex.ToUpperInvariant();
            if (!snapshot.Materials.TryGetValue(material.Name, out MaterialInfo? existing))
            {
                materials.Add(new MaterialSetupStep(material.Name, SetupAction.Create, fill));
            }
            else if (existing.Color != fill)
            {
                materials.Add(new MaterialSetupStep(material.Name, SetupAction.Update, fill, existing.Color));
            }
        }

        // An in-use old-named type is renamed to its current name by Fix floor types, so creating a
        // second type with that name now would take the name it needs.
        HashSet<string> awaitingRename = new(
            snapshot.FloorTypes
                .Where(entry => entry.FloorIds.Count > 0)
                .SelectMany(entry => FloorTypeMatcher.FreeTargets(entry, snapshot, standard)),
            StringComparer.Ordinal);
        List<FloorTypeSetupStep> floorTypes = new();
        foreach (StandardFloorType floorType in standard.FloorTypes)
        {
            FloorTypeInfo? existing = snapshot.FloorTypeByName(floorType.Name);
            if (existing is null)
            {
                if (awaitingRename.Contains(floorType.Name))
                {
                    continue;
                }

                floorTypes.Add(new FloorTypeSetupStep(floorType.Name, SetupAction.Create, floorType));
                continue;
            }

            List<FloorTypeFieldChange> changes = new();
            string restriction = floorType.GisRestriction ?? string.Empty;
            if (existing.GisCategory != floorType.GisCategory)
            {
                changes.Add(new FloorTypeFieldChange(GisSharedParameters.GisCategory, existing.GisCategory, floorType.GisCategory));
            }

            if (existing.GisRestriction != restriction)
            {
                changes.Add(new FloorTypeFieldChange(GisSharedParameters.GisRestriction, existing.GisRestriction, restriction));
            }

            if (existing.Material != floorType.Material)
            {
                changes.Add(new FloorTypeFieldChange("material", existing.Material, floorType.Material));
            }

            if (changes.Count > 0)
            {
                floorTypes.Add(new FloorTypeSetupStep(floorType.Name, SetupAction.Update, floorType, existing.ElementId, changes));
            }
        }

        string versionTo = standard.Version.ToString(CultureInfo.InvariantCulture);
        IReadOnlyList<long> echoes = snapshot.StandardVersion != versionTo ? FindCategoryEchoes(snapshot, standard) : Array.Empty<long>();
        return new SetupPlan(parameters, materials, floorTypes, snapshot.StandardVersion, versionTo, echoes);
    }

    /// <summary>
    /// Floors whose own category equals the category their type gives once Set up model has run:
    /// the standard type's gis_category, or the type's own gis_category. Older exports wrote that
    /// value back into every floor; left in place it would override the type for good.
    ///
    /// Clearing such a floor never changes what it exports, because the type gives the same value.
    /// A type that has no gis_category is left alone even when its name matches a standard type:
    /// the export then falls back to the exact type name, which can give something else (or
    /// nothing), so its floors keep their category until Fix floor types gives the type one.
    /// </summary>
    public static IReadOnlyList<long> FindCategoryEchoes(ModelSnapshot snapshot, GisToolStandard standard)
    {
        Dictionary<long, FloorTypeInfo> types = new();
        foreach (FloorTypeInfo floorType in snapshot.FloorTypes)
        {
            types[floorType.ElementId] = floorType;
        }

        List<long> echoes = new();
        foreach (FloorInfo floor in snapshot.Floors)
        {
            if (!types.TryGetValue(floor.TypeId, out FloorTypeInfo? floorType) || floor.Category.Length == 0)
            {
                continue;
            }

            string given = CategoryGivenByType(floorType, standard);
            if (given.Length > 0 && floor.Category.Trim().ToLowerInvariant() == given)
            {
                echoes.Add(floor.ElementId);
            }
        }

        return echoes;
    }

    /// <summary>The known category a floor type gives its floors after Set up model, lower case, or empty.</summary>
    public static string CategoryGivenByType(FloorTypeInfo floorType, GisToolStandard standard)
    {
        StandardFloorType? standardType = standard.FloorType(floorType.Name);
        if (standardType != null)
        {
            return standardType.GisCategory.Trim().ToLowerInvariant();
        }

        return standard.IsKnownCategory(floorType.GisCategory) ? floorType.GisCategory.Trim().ToLowerInvariant() : string.Empty;
    }

    /// <summary>The preview, one line per change, in the order Set up model applies them.</summary>
    public static IReadOnlyList<string> Describe(SetupPlan plan)
    {
        List<string> lines = new();
        foreach (ParameterSetupStep step in plan.Parameters)
        {
            switch (step.Action)
            {
                case ParameterSetupAction.Bind:
                    lines.Add($"Bind parameter {step.Name}");
                    break;
                case ParameterSetupAction.Extend:
                case ParameterSetupAction.ExtendLegacy:
                    lines.Add($"Add {string.Join(", ", step.MissingCategories.Select(CategoryLabel))} to parameter {step.Name}{(step.Detail.Length > 0 ? " (" + step.Detail + ")" : string.Empty)}");
                    break;
                case ParameterSetupAction.KeepLegacy:
                    lines.Add($"Keep parameter {step.Name} ({step.Detail})");
                    break;
                case ParameterSetupAction.BindingMismatch:
                    lines.Add($"Leave parameter {step.Name} as it is: {step.Detail}");
                    break;
            }
        }

        foreach (MaterialSetupStep step in plan.Materials)
        {
            lines.Add(step.Action == SetupAction.Create
                ? $"Create material {step.Name}, {step.Fill}"
                : $"Recolour material {step.Name}, {step.Current} to {step.Fill}");
        }

        foreach (FloorTypeSetupStep step in plan.FloorTypes)
        {
            if (step.Action == SetupAction.Create)
            {
                string restriction = string.IsNullOrEmpty(step.Standard.GisRestriction) ? string.Empty : ", " + step.Standard.GisRestriction;
                lines.Add($"Create floor type {step.Name} ({step.Standard.GisCategory}{restriction}, {step.Standard.Material})");
            }
            else
            {
                lines.Add($"Update floor type {step.Name}: {string.Join("; ", step.Changes.Select(change => $"{change.Field} {EmptyText(change.OldValue)} to {EmptyText(change.NewValue)}"))}");
            }
        }

        if (plan.EchoFloorIds.Count > 0)
        {
            lines.Add($"Clear the category of {plan.EchoFloorIds.Count} floors that only repeats their type (written by an older export)");
        }

        if (plan.SetsVersion)
        {
            lines.Add($"Set gis_standard_version {EmptyText(plan.VersionFrom, "(none)")} to {plan.VersionTo}");
        }

        return lines;
    }

    private static ParameterSetupStep PlanParameter(ModelSnapshot snapshot, GisParameterDefinition parameter)
    {
        if (!snapshot.BoundParameters.TryGetValue(parameter.Name, out BoundParameterInfo? bound))
        {
            return new ParameterSetupStep(parameter.Name, ParameterSetupAction.Bind, parameter.Categories);
        }

        List<string> missing = parameter.Categories.Where(category => !bound.Categories.Contains(category)).ToList();
        if (bound.Binding != parameter.Binding)
        {
            return new ParameterSetupStep(
                parameter.Name,
                ParameterSetupAction.BindingMismatch,
                detail: $"bound as {GisAudit.BindingText(bound.Binding)} parameter, the standard binds it as {GisAudit.BindingText(parameter.Binding)}");
        }

        if (bound.Guid == parameter.Guid)
        {
            return new ParameterSetupStep(parameter.Name, missing.Count > 0 ? ParameterSetupAction.Extend : ParameterSetupAction.Ok, missing);
        }

        return new ParameterSetupStep(
            parameter.Name,
            missing.Count > 0 ? ParameterSetupAction.ExtendLegacy : ParameterSetupAction.KeepLegacy,
            missing,
            $"kept under its own GUID {GisAudit.GuidText(bound.Guid)}");
    }

    private static string CategoryLabel(string category) =>
        category.StartsWith("OST_", StringComparison.Ordinal) ? category.Substring(4) : category;

    private static string EmptyText(string value, string empty = "(empty)") => value.Length > 0 ? value : empty;
}
