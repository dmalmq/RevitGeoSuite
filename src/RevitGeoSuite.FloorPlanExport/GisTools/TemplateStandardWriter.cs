using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// Idempotently prepares a Revit template (.rte) or project model in one single transaction:
/// runs Set up model, creates or updates the 4 standard QA schedules, and creates or repairs
/// the <c>Kanizu GIS Export Plan</c> view template.
/// </summary>
public static class TemplateStandardWriter
{
    public const string PrepareTemplateTransactionName = "Kanizu GIS: Prepare template";

    public static IReadOnlyList<string> PrepareTemplate(
        Document document,
        GisToolStandard standard,
        SetupPlan setupPlan,
        ExportViewPlan exportViews,
        TemplatePreparationPlan templatePlan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (standard is null)
        {
            throw new ArgumentNullException(nameof(standard));
        }

        if (setupPlan is null)
        {
            throw new ArgumentNullException(nameof(setupPlan));
        }

        if (exportViews is null)
        {
            throw new ArgumentNullException(nameof(exportViews));
        }

        if (templatePlan is null)
        {
            throw new ArgumentNullException(nameof(templatePlan));
        }

        if (setupPlan.IsEmpty && exportViews.IsEmpty && templatePlan.IsEmpty)
        {
            return Array.Empty<string>();
        }

        List<long> ownableIds = GisStandardWriter.SetupTargetElementIds(document, setupPlan, exportViews).ToList();
        foreach (QaSchedulePlanStep step in templatePlan.Schedules)
        {
            if (step.ExistingScheduleId.HasValue)
            {
                ownableIds.Add(step.ExistingScheduleId.Value);
            }
        }

        if (templatePlan.RepairExportPlanTemplate
            && ExportViewSetup.FindTemplateByName(document, TemplateStandardDefinitions.ExportPlanViewTemplateName) is { } existingTemplate)
        {
            ownableIds.Add(existingTemplate.Id.Value);
        }

        GisStandardWriter.RequireOwnable(document, ownableIds);

        List<string> log = new();
        GisStandardWriter.InTransaction(document, PrepareTemplateTransactionName, log, () =>
        {
            GisStandardWriter.ApplySetupCore(document, standard, setupPlan, exportViews, log);
            document.Regenerate();

            if (templatePlan.Schedules.Count > 0)
            {
                Dictionary<string, ElementId> sharedParamIds = SharedParameterIdsByName(document);
                foreach (QaSchedulePlanStep step in templatePlan.Schedules)
                {
                    EnsureQaSchedule(document, step, sharedParamIds, log);
                }
            }

            if (templatePlan.CreateExportPlanTemplate || templatePlan.RepairExportPlanTemplate)
            {
                ExportViewSetup.EnsureNamedExportPlanTemplate(
                    document,
                    TemplateStandardDefinitions.ExportPlanViewTemplateName,
                    log,
                    BuiltInCategory.OST_MechanicalEquipment);
            }
        });

        return log;
    }

    private static void EnsureQaSchedule(
        Document document,
        QaSchedulePlanStep step,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        List<string> log)
    {
        ViewSchedule? schedule = step.ExistingScheduleId.HasValue
            ? document.GetElement(new ElementId(step.ExistingScheduleId.Value)) as ViewSchedule
            : FindScheduleByName(document, step.Definition.Name);

        bool created = false;
        if (schedule is null)
        {
            ElementId categoryId = ResolveCategoryId(step.Definition.CategoryName);
            schedule = ViewSchedule.CreateSchedule(document, categoryId);
            schedule.Name = step.Definition.Name;
            created = true;
        }

        ScheduleDefinition definition = schedule.Definition;
        IList<SchedulableField> schedulable = definition.GetSchedulableFields();
        HashSet<long> existingParamIds = new();
        HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);

        int fieldCount = definition.GetFieldCount();
        for (int i = 0; i < fieldCount; i++)
        {
            ScheduleField existingField = definition.GetField(i);
            if (existingField.ParameterId != ElementId.InvalidElementId)
            {
                existingParamIds.Add(existingField.ParameterId.Value);
            }

            string existingName = (existingField.GetName() ?? string.Empty).Trim();
            if (existingName.Length > 0)
            {
                existingNames.Add(existingName);
            }
        }

        if (created)
        {
            AddDefaultIdentifierColumns(definition, schedulable, step.Definition.CategoryName, existingParamIds, existingNames);
        }

        IEnumerable<string> fieldsToAdd = created
            ? step.Definition.AllSharedParameterFields
            : step.MissingFields;

        List<string> added = new();
        foreach (string paramName in fieldsToAdd)
        {
            if (TryAddSharedParameterField(document, definition, schedulable, sharedParamIds, paramName, existingParamIds, existingNames))
            {
                added.Add(paramName);
            }
        }

        if (created)
        {
            log.Add(added.Count > 0
                ? $"Created QA schedule {step.Definition.Name} ({string.Join(", ", added)})"
                : $"Created QA schedule {step.Definition.Name}");
        }
        else if (added.Count > 0)
        {
            log.Add($"Updated QA schedule {step.Definition.Name}: added {string.Join(", ", added)}");
        }
    }

    private static void AddDefaultIdentifierColumns(
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        string categoryName,
        HashSet<long> existingParamIds,
        HashSet<string> existingNames)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_CATEGORY_PARAM, existingParamIds, existingNames);
            TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, existingParamIds, existingNames);
            return;
        }

        if (string.Equals(categoryName, "OST_Levels", StringComparison.Ordinal))
        {
            TryAddBuiltInField(definition, schedulable, BuiltInParameter.DATUM_TEXT, existingParamIds, existingNames);
            return;
        }

        TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, existingParamIds, existingNames);
    }

    private static void TryAddBuiltInField(
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        BuiltInParameter builtInParameter,
        HashSet<long> existingParamIds,
        HashSet<string> existingNames)
    {
        ElementId targetId = new(builtInParameter);
        if (existingParamIds.Contains(targetId.Value))
        {
            return;
        }

        SchedulableField? match = schedulable.FirstOrDefault(field => field.ParameterId == targetId);
        if (match is null)
        {
            return;
        }

        ScheduleField added = definition.AddField(match);
        existingParamIds.Add(targetId.Value);
        string name = (added.GetName() ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            existingNames.Add(name);
        }
    }

    private static bool TryAddSharedParameterField(
        Document document,
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        string paramName,
        HashSet<long> existingParamIds,
        HashSet<string> existingNames)
    {
        if (existingNames.Contains(paramName))
        {
            return false;
        }

        SchedulableField? match = null;
        if (sharedParamIds.TryGetValue(paramName, out ElementId? targetId))
        {
            if (existingParamIds.Contains(targetId.Value))
            {
                return false;
            }

            match = schedulable.FirstOrDefault(field => field.ParameterId == targetId);
        }

        match ??= schedulable.FirstOrDefault(field =>
            string.Equals((field.GetName(document) ?? string.Empty).Trim(), paramName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return false;
        }

        if (match.ParameterId != ElementId.InvalidElementId && existingParamIds.Contains(match.ParameterId.Value))
        {
            return false;
        }

        definition.AddField(match);
        if (match.ParameterId != ElementId.InvalidElementId)
        {
            existingParamIds.Add(match.ParameterId.Value);
        }

        existingNames.Add(paramName);
        return true;
    }

    private static ViewSchedule? FindScheduleByName(Document document, string name) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(schedule => !schedule.IsTemplate && string.Equals(schedule.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ElementId ResolveCategoryId(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return ElementId.InvalidElementId;
        }

        if (Enum.TryParse(categoryName, ignoreCase: false, out BuiltInCategory category))
        {
            return new ElementId(category);
        }

        throw new InvalidOperationException($"Unknown Revit schedule category '{categoryName}'.");
    }

    private static Dictionary<string, ElementId> SharedParameterIdsByName(Document document)
    {
        Dictionary<string, ElementId> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (SharedParameterElement shared in new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>())
        {
            string? name = shared.GetDefinition()?.Name ?? shared.Name;
            if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name!.Trim()))
            {
                map[name!.Trim()] = shared.Id;
            }
        }

        return map;
    }
}
