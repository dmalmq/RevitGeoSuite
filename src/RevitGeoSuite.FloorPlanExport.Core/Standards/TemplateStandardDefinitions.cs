using System;
using System.Collections.Generic;
using System.Linq;

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

/// <summary>Current state of one QA schedule read from a Revit document.</summary>
public sealed class QaScheduleInfo
{
    public QaScheduleInfo(long elementId, string name, IEnumerable<string>? fieldNames = null)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        FieldNames = (fieldNames ?? Array.Empty<string>()).ToList();
    }

    public long ElementId { get; }

    public string Name { get; }

    public IReadOnlyList<string> FieldNames { get; }
}

/// <summary>One QA schedule action planned by <see cref="TemplateStandardPlanner"/>.</summary>
public sealed class QaSchedulePlanStep
{
    public QaSchedulePlanStep(QaScheduleDefinition definition, bool createSchedule, IReadOnlyList<string> missingFields, long? existingScheduleId = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        CreateSchedule = createSchedule;
        MissingFields = missingFields ?? throw new ArgumentNullException(nameof(missingFields));
        ExistingScheduleId = existingScheduleId;
    }

    public QaScheduleDefinition Definition { get; }

    public bool CreateSchedule { get; }

    public IReadOnlyList<string> MissingFields { get; }

    public long? ExistingScheduleId { get; }
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

    public bool IsEmpty => Schedules.Count == 0 && !CreateExportPlanTemplate && !RepairExportPlanTemplate;
}

/// <summary>
/// Single source of truth for the standard QA schedules and export view template created by
/// Prepare Template and validated by the readiness checker.
/// </summary>
public static class TemplateStandardDefinitions
{
    public const string ExportPlanViewTemplateName = "Kanizu GIS Export Plan";

    public const string FloorsScheduleName = "Kanizu GIS - Floors";
    public const string CirculationScheduleName = "Kanizu GIS - Circulation";
    public const string DoorsScheduleName = "Kanizu GIS - Doors";
    public const string LevelsScheduleName = "Kanizu GIS - Levels";

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
        new QaScheduleDefinition(
            "qa-schedule-levels",
            LevelsScheduleName,
            "OST_Levels",
            new[]
            {
                GisSharedParameters.Ordinal,
                GisSharedParameters.ShortName,
                GisSharedParameters.LevelId,
            }),
    };
}

/// <summary>Plans the idempotent QA schedule and view template steps for Prepare Template.</summary>
public static class TemplateStandardPlanner
{
    public static TemplatePreparationPlan Plan(
        IReadOnlyList<QaScheduleInfo> existingSchedules,
        IReadOnlyList<string> existingViewTemplateNames,
        IReadOnlyList<string>? exportPlanTemplateIssues = null)
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

            HashSet<string> existingFields = new(existing.FieldNames, StringComparer.OrdinalIgnoreCase);
            List<string> missing = definition.RequiredFields
                .Where(field => !existingFields.Contains(field))
                .ToList();
            if (missing.Count > 0)
            {
                scheduleSteps.Add(new QaSchedulePlanStep(definition, createSchedule: false, missing, existing.ElementId));
            }
        }

        bool hasExportPlanTemplate = (existingViewTemplateNames ?? Array.Empty<string>())
            .Any(name => string.Equals((name ?? string.Empty).Trim(), TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase));
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
}
