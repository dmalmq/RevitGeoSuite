using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Reads the <see cref="ExportAuditSnapshot"/> and runs both audits. Reads only.</summary>
public static class ExportAuditReader
{
    /// <summary>
    /// Every finding of <see cref="GisAudit"/> and <see cref="ExportAudit"/>, errors first.
    /// <paramref name="views"/> is read when not given.
    /// </summary>
    public static IReadOnlyList<GisAuditFinding> Audit(Document document, ModelSnapshot snapshot, GisToolStandard standard, ExportViewSnapshot? views = null) =>
        GisAudit.Run(snapshot, standard)
            .Concat(ExportAudit.Run(Read(document, views ?? ExportViewSetup.Read(document))))
            .OrderBy(finding => (int)finding.Severity)
            .ToList();

    public static ExportAuditSnapshot Read(Document document, ExportViewSnapshot views)
    {
        List<Level> levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().ToList();
        return new ExportAuditSnapshot(
            views,
            ReadStairs(document),
            ReadFamilies(document, levels),
            ReadIds(document),
            levels.Select(level => new AuditIdValue(level.Id.Value, ModelSnapshotReader.ReadText(level, GisSharedParameters.LevelId))));
    }

    private static IEnumerable<AuditStair> ReadStairs(Document document)
    {
        foreach (Stairs stairs in new FilteredElementCollector(document).OfClass(typeof(Stairs)).WhereElementIsNotElementType().Cast<Stairs>())
        {
            IEnumerable<long>? storeys = null;
            if (stairs.MultistoryStairsId != ElementId.InvalidElementId && document.GetElement(stairs.MultistoryStairsId) is MultistoryStairs group)
            {
                storeys = group.GetStairsPlacementLevels(stairs)
                    .Select(id => document.GetElement(id) as Level)
                    .Where(level => level != null)
                    .OrderBy(level => level!.ProjectElevation)
                    .Select(level => level!.Id.Value)
                    .ToList();
            }

            yield return new AuditStair(
                stairs.Id.Value,
                LevelOf(stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM)),
                LevelOf(stairs.get_Parameter(BuiltInParameter.STAIRS_TOP_LEVEL_PARAM)),
                storeys);
        }
    }

    /// <summary>Family instances named like a stair or taken for an escalator; an escalator's top level is found as the export finds it, from the top of its geometry.</summary>
    private static IEnumerable<AuditFamily> ReadFamilies(Document document, IReadOnlyList<Level> levels)
    {
        foreach (FamilyInstance instance in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            if (instance.SuperComponent != null || instance.Category?.CategoryType != CategoryType.Model || instance.Symbol is not FamilySymbol symbol)
            {
                continue;
            }

            string familyName = symbol.FamilyName ?? string.Empty;
            string typeName = symbol.Name ?? string.Empty;
            string kind = FirstNonEmpty(ModelSnapshotReader.ReadText(instance, GisSharedParameters.GisKind), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisKind));
            string category = FirstNonEmpty(ModelSnapshotReader.ReadText(instance, GisSharedParameters.Category), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisCategory));
            bool escalator = ExportAudit.IsEscalator(kind, category, familyName, typeName);
            if (!escalator && !ExportAudit.IsStairFamily(kind, familyName, typeName))
            {
                continue;
            }

            long? bottom = null;
            long? top = null;
            if (escalator && document.GetElement(instance.LevelId) is Level bottomLevel)
            {
                bottom = bottomLevel.Id.Value;
                if (instance.get_BoundingBox(null) is BoundingBoxXYZ box)
                {
                    top = VerticalCirculation.EscalatorTopLevel(bottomLevel.ProjectElevation, box.Max.Z, levels, level => level.ProjectElevation)?.Id.Value;
                }
            }

            yield return new AuditFamily(
                instance.Id.Value,
                symbol.Id.Value,
                familyName,
                typeName,
                kind,
                category,
                FirstNonEmpty(ModelSnapshotReader.ReadText(instance, GisSharedParameters.GisDirection), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisDirection)),
                bottom,
                top);
        }
    }

    /// <summary>The <c>id</c> of every element of the categories it is bound to as an instance parameter.</summary>
    private static IEnumerable<AuditIdValue> ReadIds(Document document)
    {
        List<BuiltInCategory> categories = new();
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is InternalDefinition { Name: GisSharedParameters.Id } && iterator.Current is InstanceBinding binding)
            {
                foreach (Category category in binding.Categories)
                {
                    if (category.BuiltInCategory != BuiltInCategory.INVALID)
                    {
                        categories.Add(category.BuiltInCategory);
                    }
                }
            }
        }

        if (categories.Count == 0)
        {
            yield break;
        }

        foreach (Element element in new FilteredElementCollector(document).WherePasses(new ElementMulticategoryFilter(categories)).WhereElementIsNotElementType())
        {
            string value = ModelSnapshotReader.ReadText(element, GisSharedParameters.Id);
            if (value.Length > 0)
            {
                yield return new AuditIdValue(element.Id.Value, value);
            }
        }
    }

    private static long? LevelOf(Parameter? parameter)
    {
        ElementId? id = parameter?.AsElementId();
        return id == null || id == ElementId.InvalidElementId ? null : id.Value;
    }

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;
}
