using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Definition of one standard Kanizu GIS QA schedule created by Prepare Template.</summary>
public sealed class QaScheduleDefinition
{
    public QaScheduleDefinition(
        string id,
        string name,
        string categoryName,
        IReadOnlyList<string> requiredFields,
        IReadOnlyList<string>? extraFields = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        CategoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        RequiredFields = requiredFields ?? throw new ArgumentNullException(nameof(requiredFields));
        ExtraFields = extraFields ?? Array.Empty<string>();
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Revit BuiltInCategory name (such as OST_Floors), or empty for a multi-category schedule.</summary>
    public string CategoryName { get; }

    /// <summary>Shared parameter fields required for QA completeness.</summary>
    public IReadOnlyList<string> RequiredFields { get; }

    /// <summary>Additional helpful shared parameter fields added when creating the schedule.</summary>
    public IReadOnlyList<string> ExtraFields { get; }

    public IEnumerable<string> AllSharedParameterFields => RequiredFields.Concat(ExtraFields);
}

/// <summary>One shared-parameter field read from an existing Revit schedule, including its GUID.</summary>
public sealed class QaScheduleFieldInfo
{
    public QaScheduleFieldInfo(string name, Guid? sharedParameterGuid)
    {
        Name = name ?? string.Empty;
        SharedParameterGuid = sharedParameterGuid;
    }

    public string Name { get; }

    public Guid? SharedParameterGuid { get; }
}

/// <summary>Current state of one QA schedule read from a Revit document.</summary>
public sealed class QaScheduleInfo
{
    public QaScheduleInfo(
        long elementId,
        string name,
        IEnumerable<string>? fieldNames = null,
        string categoryName = "",
        IEnumerable<QaScheduleFieldInfo>? fields = null)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        FieldNames = (fieldNames ?? Array.Empty<string>()).ToList();
        CategoryName = categoryName ?? string.Empty;
        Fields = (fields ?? Array.Empty<QaScheduleFieldInfo>()).ToList();
    }

    public long ElementId { get; }

    public string Name { get; }

    /// <summary>Revit BuiltInCategory name (such as OST_Floors), or empty for a multi-category schedule.</summary>
    public string CategoryName { get; }

    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>Shared-parameter fields with their Revit SharedParameterElement GUIDs for strict validation.</summary>
    public IReadOnlyList<QaScheduleFieldInfo> Fields { get; }
}

/// <summary>One QA schedule action planned by <see cref="TemplateStandardPlanner"/>.</summary>
public sealed class QaSchedulePlanStep
{
    public QaSchedulePlanStep(
        QaScheduleDefinition definition,
        bool createSchedule,
        IReadOnlyList<string> missingFields,
        long? existingScheduleId = null,
        bool categoryMismatch = false,
        string actualCategoryName = "")
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        CreateSchedule = createSchedule;
        MissingFields = missingFields ?? throw new ArgumentNullException(nameof(missingFields));
        ExistingScheduleId = existingScheduleId;
        CategoryMismatch = categoryMismatch;
        ActualCategoryName = actualCategoryName ?? string.Empty;
    }

    public QaScheduleDefinition Definition { get; }

    public bool CreateSchedule { get; }

    public IReadOnlyList<string> MissingFields { get; }

    public long? ExistingScheduleId { get; }

    public bool CategoryMismatch { get; }

    public string ActualCategoryName { get; }

    public bool IsActionable => !CategoryMismatch;
}

/// <summary>What Prepare Template creates or repairs beyond Set up model.</summary>
public sealed class TemplatePreparationPlan
{
    public TemplatePreparationPlan(
        IReadOnlyList<QaSchedulePlanStep> schedules,
        bool createExportPlanTemplate,
        bool repairExportPlanTemplate,
        IReadOnlyList<string>? exportPlanTemplateIssues = null)
    {
        Schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        CreateExportPlanTemplate = createExportPlanTemplate;
        RepairExportPlanTemplate = repairExportPlanTemplate;
        ExportPlanTemplateIssues = exportPlanTemplateIssues ?? Array.Empty<string>();
    }

    public IReadOnlyList<QaSchedulePlanStep> Schedules { get; }

    public bool CreateExportPlanTemplate { get; }

    public bool RepairExportPlanTemplate { get; }

    public IReadOnlyList<string> ExportPlanTemplateIssues { get; }

    public bool HasActionableSteps =>
        Schedules.Any(step => step.IsActionable) || CreateExportPlanTemplate || RepairExportPlanTemplate;

    public bool IsEmpty => Schedules.Count == 0 && !CreateExportPlanTemplate && !RepairExportPlanTemplate;
}

/// <summary>
/// Single source of truth for the standard QA schedules and export view template created by
/// Prepare Template and validated by the readiness checker.
/// </summary>
public static class TemplateStandardDefinitions
{
    /// <summary>
    /// The canonical Kanizu GIS Export view template name, matching
    /// <see cref="RevitGeoSuite.FloorPlanExport.Core.GisTools.ExportViewPlanner.TemplateName"/>.
    /// Prepare Template and Set up model create or repair exactly this one.
    /// </summary>
    public const string ExportPlanViewTemplateName = ExportViewPlanner.TemplateName;

    /// <summary>
    /// A Phase 4B name an older draft file may still carry. Readiness recognises it as an alias so an
    /// existing draft is not flagged for a double create, but Prepare Template and Set up model never
    /// write this name.
    /// </summary>
    public const string LegacyExportPlanViewTemplateName = "Kanizu GIS Export Plan";

    public const string FloorsScheduleName = "Kanizu GIS - Floors";
    public const string CirculationScheduleName = "Kanizu GIS - Circulation";
    public const string DoorsScheduleName = "Kanizu GIS - Doors";

    // Levels (OST_Levels) cannot be scheduled by Revit ViewSchedule.CreateSchedule; level
    // ordinal/short_name checks live in ProjectReadinessEvaluator's level-metadata check and the
    // Parameters CSV workflow.
    public static IReadOnlyList<QaScheduleDefinition> QaSchedules { get; } = new[]
    {
        new QaScheduleDefinition(
            "qa-schedule-floors",
            FloorsScheduleName,
            "OST_Floors",
            new[]
            {
                GisSharedParameters.GisCategory,
                GisSharedParameters.GisRestriction,
                GisSharedParameters.Category,
            },
            new[]
            {
                GisSharedParameters.Name,
                GisSharedParameters.Id,
            }),
        new QaScheduleDefinition(
            "qa-schedule-circulation",
            CirculationScheduleName,
            string.Empty,
            new[]
            {
                GisSharedParameters.GisKind,
                GisSharedParameters.GisDirection,
                GisSharedParameters.GisLevelsServed,
            },
            new[]
            {
                GisSharedParameters.Name,
                GisSharedParameters.Id,
            }),
        new QaScheduleDefinition(
            "qa-schedule-doors",
            DoorsScheduleName,
            "OST_Doors",
            new[]
            {
                GisSharedParameters.GisDoor,
                GisSharedParameters.GisAccessCon,
            },
            new[]
            {
                GisSharedParameters.Name,
                GisSharedParameters.Id,
            }),
    };

    public static string CategoryLabel(string categoryName) =>
        string.IsNullOrWhiteSpace(categoryName) ? "Multi-Category" : categoryName.Trim();
}

/// <summary>Plans the idempotent QA schedule and view template steps for Prepare Template.</summary>
public static class TemplateStandardPlanner
{
    public static TemplatePreparationPlan Plan(
        IReadOnlyList<QaScheduleInfo> existingSchedules,
        IReadOnlyList<string> existingViewTemplateNames,
        IReadOnlyList<string>? exportPlanTemplateIssues = null,
        bool recognizeLegacyAlias = true)
    {
        Dictionary<string, QaScheduleInfo> schedulesByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (QaScheduleInfo schedule in existingSchedules ?? Array.Empty<QaScheduleInfo>())
        {
            if (schedule != null && !string.IsNullOrWhiteSpace(schedule.Name) && !schedulesByName.ContainsKey(schedule.Name.Trim()))
            {
                schedulesByName[schedule.Name.Trim()] = schedule;
            }
        }

        List<QaSchedulePlanStep> scheduleSteps = new();
        foreach (QaScheduleDefinition definition in TemplateStandardDefinitions.QaSchedules)
        {
            if (!schedulesByName.TryGetValue(definition.Name, out QaScheduleInfo? existing))
            {
                scheduleSteps.Add(new QaSchedulePlanStep(definition, createSchedule: true, definition.RequiredFields));
                continue;
            }

            if (!string.Equals(existing.CategoryName.Trim(), definition.CategoryName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                scheduleSteps.Add(new QaSchedulePlanStep(
                    definition,
                    createSchedule: false,
                    definition.RequiredFields,
                    existing.ElementId,
                    categoryMismatch: true,
                    actualCategoryName: existing.CategoryName));
                continue;
            }

            List<string> missing = definition.RequiredFields
                .Where(field => !HasStandardScheduleField(existing, field))
                .ToList();
            if (missing.Count > 0)
            {
                scheduleSteps.Add(new QaSchedulePlanStep(definition, createSchedule: false, missing, existing.ElementId));
            }
        }

        bool hasExportPlanTemplate = (existingViewTemplateNames ?? Array.Empty<string>())
            .Any(name =>
            {
                string trimmed = (name ?? string.Empty).Trim();
                return string.Equals(trimmed, TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase)
                    || (recognizeLegacyAlias && string.Equals(trimmed, TemplateStandardDefinitions.LegacyExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase));
            });
        List<string> templateIssues = (exportPlanTemplateIssues ?? Array.Empty<string>())
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .ToList();

        return new TemplatePreparationPlan(
            scheduleSteps,
            createExportPlanTemplate: !hasExportPlanTemplate,
            repairExportPlanTemplate: hasExportPlanTemplate && templateIssues.Count > 0,
            exportPlanTemplateIssues: templateIssues);
    }

    public static IReadOnlyList<string> Describe(TemplatePreparationPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        List<string> lines = new();
        foreach (QaSchedulePlanStep step in plan.Schedules)
        {
            if (step.CategoryMismatch)
            {
                continue;
            }

            if (step.CreateSchedule)
            {
                lines.Add($"Create QA schedule {step.Definition.Name} ({string.Join(", ", step.Definition.RequiredFields)})");
            }
            else
            {
                lines.Add($"Update QA schedule {step.Definition.Name}: add {string.Join(", ", step.MissingFields)}");
            }
        }

        if (plan.CreateExportPlanTemplate)
        {
            lines.Add($"Create view template {TemplateStandardDefinitions.ExportPlanViewTemplateName}");
        }
        else if (plan.RepairExportPlanTemplate)
        {
            lines.Add($"Update view template {TemplateStandardDefinitions.ExportPlanViewTemplateName}: {string.Join("; ", plan.ExportPlanTemplateIssues)}");
        }

        return lines;
    }

    private static bool HasStandardScheduleField(QaScheduleInfo schedule, string requiredFieldName)
    {
        if (schedule.Fields.Count > 0)
        {
            if (GisSharedParameters.TryGet(requiredFieldName, out GisParameterDefinition expected))
            {
                return schedule.Fields.Any(field => field.SharedParameterGuid == expected.Guid);
            }

            return schedule.Fields.Any(field => string.Equals(field.Name, requiredFieldName, StringComparison.OrdinalIgnoreCase));
        }

        return schedule.FieldNames.Any(name => string.Equals(name, requiredFieldName, StringComparison.OrdinalIgnoreCase));
    }
}
