using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.Extractors;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Reads the <see cref="ExportAuditSnapshot"/> and runs both audits. Reads only.</summary>
public static class ExportAuditReader
{
    /// <summary>
    /// Every finding of <see cref="GisAudit"/> and <see cref="ExportAudit"/>, errors first, with the
    /// guesses on <c>GIS_</c> plans raised to errors. <paramref name="views"/> is read when not given.
    /// Doors are checked only once <c>gis_door</c> is bound as the standard binds it (a type parameter
    /// of Doors); until then the parameter audit says what is wrong with the binding. A family the
    /// export dialog maps by hand is not a guess.
    /// </summary>
    public static IReadOnlyList<GisAuditFinding> Audit(Document document, ModelSnapshot snapshot, GisToolStandard standard, ExportViewSnapshot? views = null)
    {
        ExportAuditSnapshot audit = Read(document, views ?? ExportViewSetup.Read(document), DoorsCanHoldGisDoor(snapshot));
        IReadOnlyCollection<string> mapped = MappedFamilies(document);
        return ExportAudit.RaiseGuessesOnGisPlans(GisAudit.Run(snapshot, standard, mapped).Concat(ExportAudit.Run(audit)), audit);
    }

    /// <summary>
    /// The families the export dialog maps by hand, as its rules store them. The audit is the single
    /// entry for the page, the export gate and the report, so a rules file it cannot read only means
    /// no family counts as mapped.
    /// </summary>
    public static IReadOnlyCollection<string> MappedFamilies(Document document)
    {
        try
        {
            return GisRulesSourceFactory.For(document).Load().Value.FamilyCategoryOverrides.Keys.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"The audit read no hand-mapped families: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static bool DoorsCanHoldGisDoor(ModelSnapshot snapshot) =>
        snapshot.BoundParameters.TryGetValue(GisSharedParameters.GisDoor, out BoundParameterInfo? bound) &&
        bound.Binding == GisParameterBinding.Type &&
        bound.Categories.Contains("OST_Doors", StringComparer.Ordinal);

    public static ExportAuditSnapshot Read(Document document, ExportViewSnapshot views, bool readDoors)
    {
        List<Level> levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().ToList();
        List<AuditStair> stairs = ReadStairs(document).ToList();
        List<AuditFamily> families = ReadFamilies(document, levels).ToList();
        ElementLevelResolver resolver = new(document);
        return new ExportAuditSnapshot(
            views,
            stairs,
            families,
            ReadIds(document),
            levels.Select(level => new AuditIdValue(level.Id.Value, ModelSnapshotReader.ReadText(level, GisSharedParameters.LevelId))),
            ReadFloors(document),
            ReadWaysIn(document, stairs, families),
            levels.Select(level => new KeyValuePair<long, double>(level.Id.Value, level.ProjectElevation)),
            readDoors ? ReadDoors(document, resolver).ToList() : null,
            ReadUnread(document, resolver).ToList(),
            ReadElementLevels(document, resolver, families).ToList());
    }

    /// <summary>Every door the export's views show, with its type's gis_door, read once per type.</summary>
    private static IEnumerable<AuditDoor> ReadDoors(Document document, ElementLevelResolver resolver)
    {
        Dictionary<long, (string Label, string GisDoor)> types = new();
        foreach (FamilyInstance door in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Doors).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            if (!ElementLevelResolver.Counts(door) || door.Symbol is not FamilySymbol symbol || resolver.LevelOf(door) is not long level)
            {
                continue;
            }

            if (!types.TryGetValue(symbol.Id.Value, out (string Label, string GisDoor) type))
            {
                type = ((symbol.FamilyName ?? string.Empty) + " : " + (symbol.Name ?? string.Empty), ModelSnapshotReader.ReadText(symbol, GisSharedParameters.GisDoor));
                types[symbol.Id.Value] = type;
            }

            yield return new AuditDoor(door.Id.Value, symbol.Id.Value, type.Label, level, type.GisDoor);
        }
    }

    /// <summary>Revit ramps and families of the Floors category the export's views show: the export reads neither.</summary>
    private static IEnumerable<AuditUnread> ReadUnread(Document document, ElementLevelResolver resolver)
    {
        foreach (Element ramp in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Ramps).WhereElementIsNotElementType())
        {
            if (ElementLevelResolver.Counts(ramp) && resolver.LevelOf(ramp) is long level)
            {
                yield return new AuditUnread(ramp.Id.Value, UnreadKind.Ramp, level);
            }
        }

        foreach (FamilyInstance floor in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Floors).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            if (floor.SuperComponent == null && ElementLevelResolver.Counts(floor) && resolver.LevelOf(floor) is long level)
            {
                yield return new AuditUnread(floor.Id.Value, UnreadKind.FloorFamily, level);
            }
        }
    }

    /// <summary>
    /// The level of every element a guess can name: floors, family instances of the audited family
    /// categories, and the stairs made as families. Demolished elements and secondary design options
    /// are left out, so a guess about them is never raised.
    /// </summary>
    private static IEnumerable<KeyValuePair<long, long>> ReadElementLevels(Document document, ElementLevelResolver resolver, IEnumerable<AuditFamily> families)
    {
        IEnumerable<Element> floors = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType();
        IEnumerable<Element> instances = new FilteredElementCollector(document)
            .WherePasses(new ElementMulticategoryFilter(ModelSnapshotReader.AuditedFamilyCategories))
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType();
        IEnumerable<Element> stairFamilies = families.Select(family => document.GetElement(new ElementId(family.ElementId))).Where(element => element != null);
        HashSet<long> seen = new();
        foreach (Element element in floors.Concat(instances).Concat(stairFamilies))
        {
            if (seen.Add(element.Id.Value) && ElementLevelResolver.Counts(element) && resolver.LevelOf(element) is long level)
            {
                yield return new KeyValuePair<long, long>(element.Id.Value, level);
            }
        }
    }

    /// <summary>Every floor with the unit category the export resolves for it, its top outline and the height of its top's lowest and highest point.</summary>
    private static IEnumerable<LayoutFloor> ReadFloors(Document document)
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault(), GisRulesSourceFactory.For(document).Load().Value.FloorCategoryOverrides);
        bool setUp = ModelSnapshotReader.ReadText(document.ProjectInformation, GisSharedParameters.GisStandardVersion).Length > 0;
        foreach (Floor floor in new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>())
        {
            Element? type = document.GetElement(floor.GetTypeId());
            string typeName = (type?.Name ?? string.Empty).Trim();
            string rawName = typeName.Length == 0 ? $"<floor-{floor.Id.Value}>" : typeName;
            string zoneName = UnitExtractor.TryResolveFloorZoneName(typeName, out string parsed, out _) ? parsed : rawName;
            ResolvedFloorCategory category = resolver.Resolve(
                rawName,
                zoneName,
                new FloorStandardParameters(
                    NullIfEmpty(ModelSnapshotReader.ReadText(floor, GisSharedParameters.Category)),
                    NullIfEmpty(ModelSnapshotReader.ReadText(type, GisSharedParameters.GisCategory)),
                    NullIfEmpty(ModelSnapshotReader.ReadText(type, GisSharedParameters.GisRestriction)),
                    setUp));
            (List<Polygon2D> outline, double low, double high) = FloorTopFaces.Read(floor);
            if (outline.Count > 0)
            {
                yield return new LayoutFloor(floor.Id.Value, floor.LevelId.Value, category.ZoneInfo.Category, outline, low, high);
            }
        }
    }

    /// <summary>Doors, windows and accepted openings on their level, and each stair's and escalator's footprint on both of its levels.</summary>
    private static IEnumerable<LayoutOpening> ReadWaysIn(Document document, IReadOnlyList<AuditStair> stairs, IReadOnlyList<AuditFamily> families)
    {
        IReadOnlyList<string> accepted = GisRulesSourceFactory.For(document).Load().Value.AcceptedOpeningFamilies;
        foreach (FamilyInstance instance in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            if (instance.LevelId != ElementId.InvalidElementId && OpeningFamilyClassifier.IsAcceptedOpening(instance, accepted) && Corners(instance) is List<Point2D> corners)
            {
                yield return new LayoutOpening(instance.Id.Value, instance.LevelId.Value, corners);
            }
        }

        IEnumerable<(long Id, long? Bottom, long? Top)> connectors = stairs.Select(stair => (stair.ElementId, stair.BaseLevelId, stair.TopLevelId))
            .Concat(families.Where(family => ExportAudit.IsEscalator(family.GisKind, family.GisCategory, family.FamilyName, family.TypeName))
                .Select(family => (family.ElementId, family.BottomLevelId, family.TopLevelId)));
        foreach ((long id, long? bottom, long? top) in connectors)
        {
            if (Corners(document.GetElement(new ElementId(id))) is not List<Point2D> corners)
            {
                continue;
            }

            foreach (long level in new[] { bottom, top }.Where(level => level.HasValue).Select(level => level!.Value).Distinct())
            {
                yield return new LayoutOpening(id, level, corners);
            }
        }
    }

    private static List<Point2D>? Corners(Element? element)
    {
        if (element?.get_BoundingBox(null) is not BoundingBoxXYZ box)
        {
            return null;
        }

        return new List<Point2D>
        {
            new(box.Min.X, box.Min.Y),
            new(box.Max.X, box.Min.Y),
            new(box.Max.X, box.Max.Y),
            new(box.Min.X, box.Max.Y),
        };
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

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
