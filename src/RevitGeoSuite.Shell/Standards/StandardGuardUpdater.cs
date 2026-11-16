using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.SharedUI.Localization;

namespace RevitGeoSuite.Shell.Standards;

/// <summary>
/// G4: warns at the moment of the mistake. When a floor or family is placed or re-typed on a level
/// with a <c>GIS_</c> plan and is outside the Kanizu GIS standard (<see cref="StandardGuard"/>), the
/// transaction ends with a Revit warning that names the standard. Never an error: the element stays
/// as placed. Runs only on commit and only for added or re-typed elements (not types) of the audited
/// categories, in a model set up with the standard; it reads the plan views once per such commit.
/// Doors are left out until the GIS standard page can set gis_door from its row (F6): nearly every
/// door type lacks it today, so every door placed would warn. Optional, so a model opened without
/// RevitGeoSuite never asks for it.
/// </summary>
internal sealed class StandardGuardUpdater : IUpdater
{
    private static readonly Guid UpdaterGuid = new("5b0e5f3c-7d2a-4f53-9f4e-2a8d6c1e9b40");
    private static readonly FailureDefinitionId FloorTypeFailure = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d10"));
    private static readonly FailureDefinitionId FloorFamilyFailure = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d16"));

    // One warning per kind, so each can name its own standard families; the ids never change.
    private static readonly IReadOnlyDictionary<string, FailureDefinitionId> FamilyFailures = new Dictionary<string, FailureDefinitionId>(StringComparer.Ordinal)
    {
        ["escalator"] = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d12")),
        ["moving_walkway"] = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d13")),
        ["elevator"] = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d14")),
        ["ticket_gate"] = new(new Guid("0f6c2b71-3a8e-4c55-a1d4-6e2f9b7c3d15")),
    };

    // Floors, and the family categories the audit reads.
    private static readonly BuiltInCategory[] Categories = new[] { BuiltInCategory.OST_Floors }.Concat(ModelSnapshotReader.AuditedFamilyCategories).Distinct().ToArray();

    // The export dialog's hand-mapped families, per model, read again after this long.
    private static readonly TimeSpan MappedFamiliesAge = TimeSpan.FromSeconds(30);
    // Keyed by the open document itself, so two unsaved models never share an entry and a closed one drops out.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, MappedFamiliesRead> mappedFamilies = new();

    private readonly UpdaterId id;
    private readonly GisToolStandard standard;

    private StandardGuardUpdater(AddInId addInId, GisToolStandard standard)
    {
        id = new UpdaterId(addInId, UpdaterGuid);
        this.standard = standard;
    }

    /// <summary>Defines the warnings and registers the updater. Revit allows both only in OnStartup.</summary>
    public static void Register(UIControlledApplication application)
    {
        try
        {
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            DefineFailures(standard);
            var updater = new StandardGuardUpdater(application.ActiveAddInId, standard);
            UpdaterRegistry.RegisterUpdater(updater, true);
            var filter = new LogicalAndFilter(new ElementMulticategoryFilter(Categories), new ElementIsElementTypeFilter(true));
            UpdaterRegistry.AddTrigger(updater.id, filter, Element.GetChangeTypeElementAddition());
            UpdaterRegistry.AddTrigger(updater.id, filter, Element.GetChangeTypeParameter(new ElementId(BuiltInParameter.ELEM_TYPE_PARAM)));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"The GIS standard guard was not registered: {ex}");
        }
    }

    public static void Unregister(UIControlledApplication application)
    {
        try
        {
            UpdaterId updaterId = new(application.ActiveAddInId, UpdaterGuid);
            if (UpdaterRegistry.IsUpdaterRegistered(updaterId))
            {
                UpdaterRegistry.UnregisterUpdater(updaterId);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"The GIS standard guard was not unregistered: {ex}");
        }
    }

    private static void DefineFailures(GisToolStandard standard)
    {
        UiLocalizer text = UiLocalizer.Instance;
        FailureDefinition.CreateFailureDefinition(FloorTypeFailure, FailureSeverity.Warning, text.Get("Guard.FloorType"));
        FailureDefinition.CreateFailureDefinition(FloorFamilyFailure, FailureSeverity.Warning, text.Get("Guard.FloorFamily"));
        foreach (KeyValuePair<string, FailureDefinitionId> kind in FamilyFailures)
        {
            string families = StandardGuard.StandardFamilies(standard, kind.Key);
            string message = families.Length == 0
                ? string.Format(text.Get("Guard.Family.NoneListed"), text.Get("Guard.Kind." + kind.Key))
                : string.Format(text.Get("Guard.Family"), text.Get("Guard.Kind." + kind.Key), families);
            FailureDefinition.CreateFailureDefinition(kind.Value, FailureSeverity.Warning, message);
        }
    }

    public void Execute(UpdaterData data)
    {
        // An exception here makes Revit show a dialog and turn the updater off; a guard must never do that.
        try
        {
            Warn(data);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"The GIS standard guard skipped a change: {ex}");
        }
    }

    private void Warn(UpdaterData data)
    {
        Document document = data.GetDocument();
        if (document.IsFamilyDocument || ModelSnapshotReader.ReadText(document.ProjectInformation, GisSharedParameters.GisStandardVersion).Length == 0)
        {
            return;
        }

        List<Element> elements = data.GetAddedElementIds().Concat(data.GetModifiedElementIds())
            .Select(document.GetElement)
            .Where(element => element != null && ElementLevelResolver.Counts(element))
            .ToList();
        if (elements.Count == 0)
        {
            return;
        }

        HashSet<long> gisLevels = new(new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view => !view.IsTemplate && view.GenLevel != null && view.Name.StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(view => view.GenLevel.Id.Value));
        if (gisLevels.Count == 0)
        {
            return;
        }

        ElementLevelResolver levels = new(document);
        Dictionary<FailureDefinitionId, List<ElementId>> outside = new();
        foreach (Element element in elements)
        {
            if (levels.LevelOf(element) is long level && gisLevels.Contains(level) && Outside(document, element) is FailureDefinitionId failure)
            {
                if (!outside.TryGetValue(failure, out List<ElementId>? ids))
                {
                    outside[failure] = ids = new List<ElementId>();
                }

                ids.Add(element.Id);
            }
        }

        foreach (KeyValuePair<FailureDefinitionId, List<ElementId>> warning in outside)
        {
            document.PostFailure(new FailureMessage(warning.Key).SetFailingElements(warning.Value));
        }
    }

    private FailureDefinitionId? Outside(Document document, Element element)
    {
        ElementType? type = document.GetElement(element.GetTypeId()) as ElementType;
        if (element is Floor)
        {
            return StandardGuard.FloorTypeOutside(ModelSnapshotReader.ReadText(type, GisSharedParameters.GisCategory), standard) ? FloorTypeFailure : null;
        }

        if (element is not FamilyInstance instance || instance.SuperComponent != null || type is not FamilySymbol symbol)
        {
            return null;
        }

        if (instance.Category?.BuiltInCategory == BuiltInCategory.OST_Floors)
        {
            return FloorFamilyFailure;
        }

        string? kind = StandardGuard.FamilyOutside(
            symbol.FamilyName ?? string.Empty,
            symbol.Name ?? string.Empty,
            FirstNonEmpty(ModelSnapshotReader.ReadText(instance, GisSharedParameters.GisKind), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisKind)),
            FirstNonEmpty(ModelSnapshotReader.ReadText(instance, GisSharedParameters.Category), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisCategory)),
            standard,
            MappedFamilies(document));
        return kind != null && FamilyFailures.TryGetValue(kind, out FailureDefinitionId? failure) ? failure : null;
    }

    /// <summary>The export dialog's hand-mapped families of this model, read at most every <see cref="MappedFamiliesAge"/>.</summary>
    private IReadOnlyCollection<string> MappedFamilies(Document document)
    {
        MappedFamiliesRead cached = mappedFamilies.GetValue(document, _ => new MappedFamiliesRead());
        if (cached.Families == null || DateTime.UtcNow - cached.Read > MappedFamiliesAge)
        {
            cached.Families = ExportAuditReader.MappedFamilies(document);
            cached.Read = DateTime.UtcNow;
        }

        return cached.Families;
    }

    private sealed class MappedFamiliesRead
    {
        public DateTime Read { get; set; }

        public IReadOnlyCollection<string>? Families { get; set; }
    }

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;

    public UpdaterId GetUpdaterId() => id;

    public ChangePriority GetChangePriority() => ChangePriority.Annotations;

    public string GetUpdaterName() => "RevitGeoSuite GIS standard guard";

    public string GetAdditionalInformation() =>
        "Warns when a floor or family placed or re-typed on a level with a GIS_ plan is outside the Kanizu GIS standard. Never changes the model.";
}
