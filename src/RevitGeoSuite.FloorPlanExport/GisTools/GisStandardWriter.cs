using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>
/// Applies Set up model and Fix floor types to a model. Each runs in one transaction, so a failure
/// leaves the model as it was and one Undo in Revit takes the whole run back. Neither deletes
/// anything or changes the type of a floor, so every element keeps its UniqueId.
/// </summary>
public static class GisStandardWriter
{
    public const string SetupTransactionName = "Kanizu GIS: Set up model";
    public const string FixTransactionName = "Kanizu GIS: Fix floor types";

    /// <summary>Applies <paramref name="plan"/> and returns what it did, one line per change.</summary>
    public static IReadOnlyList<string> ApplySetup(Document document, GisToolStandard standard, SetupPlan plan)
    {
        RequireOwnable(document, plan.FloorTypes.Where(step => step.ElementId.HasValue).Select(step => step.ElementId!.Value)
            .Concat(plan.EchoFloorIds)
            .Concat(plan.Materials.Select(step => MaterialId(document, step.Name)).Where(id => id.HasValue).Select(id => id!.Value))
            .Concat(new[] { document.ProjectInformation.Id.Value }));
        List<string> log = new();
        InTransaction(document, SetupTransactionName, () =>
        {
            BindParameters(document, plan, log);
            document.Regenerate();
            Dictionary<string, ElementId> materialIds = EnsureMaterials(document, standard, plan, log);
            EnsureFloorTypes(document, plan, materialIds, log);
            if (plan.EchoFloorIds.Count > 0)
            {
                ClearFloorCategories(document, plan.EchoFloorIds);
                log.Add($"Cleared the repeated category of {plan.EchoFloorIds.Count} floors");
            }

            if (plan.SetsVersion)
            {
                Parameter parameter = document.ProjectInformation.LookupParameter(GisSharedParameters.GisStandardVersion)
                    ?? throw new InvalidOperationException("gis_standard_version is not bound to Project Information.");
                parameter.Set(plan.VersionTo);
                log.Add($"gis_standard_version = {plan.VersionTo}");
            }
        });
        return log;
    }

    /// <summary>
    /// Writes each proposal's gis_category, gis_restriction and material onto its floor type, and
    /// clears <paramref name="echoFloorIds"/>' own category, which only repeats the new one.
    /// </summary>
    public static IReadOnlyList<string> ApplyProposals(Document document, IReadOnlyList<FloorTypeProposal> proposals, IReadOnlyList<long> echoFloorIds)
    {
        RequireOwnable(document, proposals.Select(proposal => proposal.FloorType.ElementId).Concat(echoFloorIds));
        Dictionary<string, ElementId> materials = MaterialIdsByName(document);
        List<string> log = new();
        InTransaction(document, FixTransactionName, () =>
        {
            foreach (FloorTypeProposal proposal in proposals)
            {
                FloorType floorType = document.GetElement(new ElementId(proposal.FloorType.ElementId)) as FloorType
                    ?? throw new InvalidOperationException($"Floor type {proposal.FloorType.Name} is no longer in the model.");
                SetTypeValues(floorType, proposal.GisCategory, proposal.GisRestriction, materials.TryGetValue(proposal.Material, out ElementId? id) ? id : null);
                string restriction = proposal.GisRestriction.Length > 0 ? ", gis_restriction " + proposal.GisRestriction : string.Empty;
                log.Add($"{proposal.FloorType.Name}: gis_category {proposal.GisCategory}{restriction}");
            }

            if (echoFloorIds.Count > 0)
            {
                ClearFloorCategories(document, echoFloorIds);
                log.Add($"Cleared the repeated category of {echoFloorIds.Count} floors");
            }
        });
        return log;
    }

    private static void BindParameters(Document document, SetupPlan plan, List<string> log)
    {
        List<ParameterSetupStep> changing = plan.Parameters
            .Where(step => step.Action is ParameterSetupAction.Bind or ParameterSetupAction.Extend or ParameterSetupAction.ExtendLegacy)
            .ToList();
        if (changing.Count == 0)
        {
            return;
        }

        // The export binds the same way, from the shipped shared parameter file.
        List<string> warnings = new();
        new SharedParameterManager(document).EnsureParameters(warnings);
        foreach (ParameterSetupStep step in changing)
        {
            log.Add(step.Action == ParameterSetupAction.Bind ? $"Bound {step.Name}" : $"Extended {step.Name}");
        }

        log.AddRange(warnings);
    }

    private static Dictionary<string, ElementId> EnsureMaterials(Document document, GisToolStandard standard, SetupPlan plan, List<string> log)
    {
        ElementId solid = SolidFillPatternId(document);
        Dictionary<string, Material> existing = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(material => material.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> changed = new(plan.Materials.Select(step => step.Name), StringComparer.Ordinal);
        Dictionary<string, ElementId> ids = new(StringComparer.Ordinal);
        foreach (StandardMaterial entry in standard.Materials)
        {
            if (!existing.TryGetValue(entry.Name, out Material? material))
            {
                material = (Material)document.GetElement(Material.Create(document, entry.Name));
            }

            if (changed.Contains(entry.Name))
            {
                (byte red, byte green, byte blue) = GisToolStandard.HexToRgb(entry.FillHex);
                Color color = new(red, green, blue);
                material.Color = color;
                material.UseRenderAppearanceForShading = false;
                if (solid != ElementId.InvalidElementId)
                {
                    material.SurfaceForegroundPatternId = solid;
                    material.SurfaceForegroundPatternColor = color;
                    material.CutForegroundPatternId = solid;
                    material.CutForegroundPatternColor = color;
                }

                log.Add($"Material {entry.Name} {entry.FillHex.ToUpperInvariant()}");
            }

            ids[entry.Name] = material.Id;
        }

        return ids;
    }

    private static void EnsureFloorTypes(Document document, SetupPlan plan, Dictionary<string, ElementId> materialIds, List<string> log)
    {
        FloorType? baseType = null;
        foreach (FloorTypeSetupStep step in plan.FloorTypes)
        {
            StandardFloorType standardType = step.Standard;
            FloorType floorType;
            if (step.Action == SetupAction.Create)
            {
                baseType ??= BaseFloorType(document);
                floorType = (FloorType)baseType.Duplicate(standardType.Name);
                log.Add($"Created floor type {standardType.Name} from {baseType.Name}");
            }
            else
            {
                floorType = (FloorType)document.GetElement(new ElementId(step.ElementId!.Value));
                log.Add($"Updated floor type {standardType.Name}");
            }

            SetTypeValues(floorType, standardType.GisCategory, standardType.GisRestriction ?? string.Empty,
                materialIds.TryGetValue(standardType.Material, out ElementId? id) ? id : null);
        }
    }

    /// <summary>The type new standard types are copied from: the first "j " floor type, else any floor type with layers.</summary>
    private static FloorType BaseFloorType(Document document)
    {
        List<FloorType> candidates = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(type => !type.IsFoundationSlab && type.GetCompoundStructure() != null)
            .ToList();
        return candidates.FirstOrDefault(type => (type.Name ?? string.Empty).StartsWith("j ", StringComparison.Ordinal))
            ?? candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("The model has no floor type to copy the standard floor types from.");
    }

    private static void SetTypeValues(FloorType floorType, string gisCategory, string gisRestriction, ElementId? materialId)
    {
        SetText(floorType, GisSharedParameters.GisCategory, gisCategory);
        SetText(floorType, GisSharedParameters.GisRestriction, gisRestriction);
        if (materialId == null)
        {
            return;
        }

        CompoundStructure? structure = floorType.GetCompoundStructure();
        if (structure != null && structure.LayerCount > 0)
        {
            structure.SetMaterialId(0, materialId);
            floorType.SetCompoundStructure(structure);
        }
    }

    private static void SetText(Element element, string name, string value)
    {
        Parameter? parameter = element.LookupParameter(name);
        if (parameter is { IsReadOnly: false, StorageType: StorageType.String })
        {
            parameter.Set(value);
        }
    }

    private static void ClearFloorCategories(Document document, IEnumerable<long> floorIds)
    {
        foreach (long id in floorIds)
        {
            Element? floor = document.GetElement(new ElementId(id));
            if (floor != null)
            {
                SetText(floor, GisSharedParameters.Category, string.Empty);
            }
        }
    }

    private static ElementId SolidFillPatternId(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern() is { IsSolidFill: true, Target: FillPatternTarget.Drafting })
            ?.Id ?? ElementId.InvalidElementId;

    private static Dictionary<string, ElementId> MaterialIdsByName(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .GroupBy(material => material.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

    /// <summary>
    /// In a workshared model, refuses before any change when another user borrows an element the
    /// run would edit, so the user learns which ones instead of meeting a rolled-back transaction.
    /// </summary>
    private static void RequireOwnable(Document document, IEnumerable<long> elementIds)
    {
        if (!document.IsWorkshared)
        {
            return;
        }

        List<string> borrowed = new();
        foreach (long id in elementIds.Distinct())
        {
            ElementId elementId = new(id);
            if (WorksharingUtils.GetCheckoutStatus(document, elementId) == CheckoutStatus.OwnedByOtherUser)
            {
                Element? element = document.GetElement(elementId);
                string owner = WorksharingUtils.GetWorksharingTooltipInfo(document, elementId)?.Owner ?? "another user";
                borrowed.Add($"{element?.Name ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture)} ({owner})");
            }
        }

        if (borrowed.Count > 0)
        {
            throw new InvalidOperationException(
                $"Nothing was changed: {borrowed.Count} element(s) are borrowed by other users: {string.Join(", ", borrowed.Take(10))}. " +
                "Ask them to synchronize and relinquish, then reload latest and try again.");
        }
    }

    private static long? MaterialId(Document document, string name) =>
        new FilteredElementCollector(document).OfClass(typeof(Material)).FirstOrDefault(material => material.Name == name)?.Id.Value;

    private static void InTransaction(Document document, string name, Action work)
    {
        using Transaction transaction = new(document, name);
        transaction.Start();
        try
        {
            work();
            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException($"Revit did not commit '{name}'.");
            }
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }
}
