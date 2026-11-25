using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// Idempotently prepares a Revit template (.rte) or project model in one single transaction:
/// runs Set up model, creates or updates the standard QA schedules, and creates or repairs
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

        if (setupPlan.IsEmpty && exportViews.IsEmpty && !templatePlan.HasActionableSteps)
        {
            return Array.Empty<string>();
        }

        List<long> ownableIds = GisStandardWriter.SetupTargetElementIds(document, setupPlan, exportViews).ToList();
        foreach (QaSchedulePlanStep step in templatePlan.Schedules)
        {
            if (step.IsActionable && step.ExistingScheduleId.HasValue)
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
                    if (step.CategoryMismatch)
                    {
                        string actual = TemplateStandardDefinitions.CategoryLabel(step.ActualCategoryName);
                        string expected = TemplateStandardDefinitions.CategoryLabel(step.Definition.CategoryName);
                        log.Add($"Skipped QA schedule {step.Definition.Name}: existing schedule targets {actual}, expected {expected}.");
                        continue;
                    }

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
            if (categoryId != ElementId.InvalidElementId && !ViewSchedule.IsValidCategoryForSchedule(categoryId))
            {
                throw new InvalidOperationException(
                    $"Cannot create QA schedule '{step.Definition.Name}': category '{step.Definition.CategoryName}' is not schedulable in Revit.");
            }

            schedule = ViewSchedule.CreateSchedule(document, categoryId);
            schedule.Name = step.Definition.Name;
            created = true;
        }

        ScheduleDefinition definition = schedule.Definition;
        IList<SchedulableField> schedulable = definition.GetSchedulableFields();
        HashSet<long> existingParamIds = new();
        HashSet<Guid> existingSharedGuids = new();

        int fieldCount = definition.GetFieldCount();
        for (int i = 0; i < fieldCount; i++)
        {
            ScheduleField existingField = definition.GetField(i);
            if (existingField.ParameterId != ElementId.InvalidElementId)
            {
                existingParamIds.Add(existingField.ParameterId.Value);
                if (document.GetElement(existingField.ParameterId) is SharedParameterElement shared)
                {
                    existingSharedGuids.Add(shared.GuidValue);
                }
            }
        }

        if (created)
        {
            AddDefaultIdentifierColumns(definition, schedulable, step.Definition.CategoryName, existingParamIds);
        }

        HashSet<string> requiredSet = new(step.Definition.RequiredFields, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> fieldsToAdd = created
            ? step.Definition.AllSharedParameterFields
            : step.MissingFields;

        List<string> added = new();
        List<string> skippedRequired = new();
        List<string> skippedExtra = new();
        foreach (string paramName in fieldsToAdd)
        {
            if (GisSharedParameters.TryGet(paramName, out GisParameterDefinition standardDef)
                && existingSharedGuids.Contains(standardDef.Guid))
            {
                continue;
            }

            if (TryAddSharedParameterField(definition, schedulable, sharedParamIds, paramName, existingParamIds, existingSharedGuids))
            {
                added.Add(paramName);
            }
            else if (requiredSet.Contains(paramName))
            {
                skippedRequired.Add(paramName);
            }
            else
            {
                skippedExtra.Add(paramName);
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

        if (skippedRequired.Count > 0)
        {
            log.Add($"Warning: QA schedule {step.Definition.Name} skipped unavailable required field(s): {string.Join(", ", skippedRequired)}");
        }

        if (skippedExtra.Count > 0)
        {
            log.Add($"Note: QA schedule {step.Definition.Name} skipped unavailable optional field(s): {string.Join(", ", skippedExtra)}");
        }
    }

    private static void AddDefaultIdentifierColumns(
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        string categoryName,
        HashSet<long> existingParamIds)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_CATEGORY_PARAM, existingParamIds);
            TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, existingParamIds);
            return;
        }

        TryAddBuiltInField(definition, schedulable, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, existingParamIds);
    }

    private static void TryAddBuiltInField(
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        BuiltInParameter builtInParameter,
        HashSet<long> existingParamIds)
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

        definition.AddField(match);
        existingParamIds.Add(targetId.Value);
    }

    private static bool TryAddSharedParameterField(
        ScheduleDefinition definition,
        IList<SchedulableField> schedulable,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        string paramName,
        HashSet<long> existingParamIds,
        HashSet<Guid> existingSharedGuids)
    {
        if (!sharedParamIds.TryGetValue(paramName, out ElementId? targetId))
        {
            return false;
        }

        if (existingParamIds.Contains(targetId.Value))
        {
            return false;
        }

        SchedulableField? match = schedulable.FirstOrDefault(field => field.ParameterId == targetId);
        if (match is null)
        {
            return false;
        }

        definition.AddField(match);
        existingParamIds.Add(targetId.Value);
        if (GisSharedParameters.TryGet(paramName, out GisParameterDefinition def))
        {
            existingSharedGuids.Add(def.Guid);
        }

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
        List<SharedParameterElement> allShared = new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .ToList();

        Dictionary<Guid, ElementId> byGuid = new();
        foreach (SharedParameterElement shared in allShared)
        {
            if (!byGuid.ContainsKey(shared.GuidValue))
            {
                byGuid[shared.GuidValue] = shared.Id;
            }
        }

        Dictionary<string, ElementId> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (GisParameterDefinition def in GisSharedParameters.All)
        {
            if (byGuid.TryGetValue(def.Guid, out ElementId? exactId))
            {
                resolved[def.Name] = exactId;
            }
        }

        return resolved;
    }
}
