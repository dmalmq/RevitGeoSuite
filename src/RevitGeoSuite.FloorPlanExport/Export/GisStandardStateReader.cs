using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>Reads what <see cref="GisStandardCheck"/> and <see cref="ProjectReadinessEvaluator"/> need from a Revit model. Reads only.</summary>
public static class GisStandardStateReader
{
    private static readonly BuiltInCategory[] ReadinessFamilyCategories =
    {
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_Doors,
    };

    public static GisStandardStatus Check(Document document)
    {
        return GisStandardCheck.Check(Read(document));
    }

    public static GisStandardModelState Read(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new GisStandardModelState
        {
            StandardVersionText = ReadText(document.ProjectInformation, GisSharedParameters.GisStandardVersion),
            BoundParameters = ReadBoundParameters(document),
            GisViewCount = new FilteredElementCollector(document)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Count(view => !view.IsTemplate &&
                               (view.Name ?? string.Empty).StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase)),
            FloorTypesWithoutCategory = ReadFloorTypesWithoutCategory(document),
        };
    }

    public static ReadinessCheckSnapshot ReadReadiness(Document document, ModelSnapshot? existingSnapshot = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        ModelSnapshot snapshot = existingSnapshot ?? ModelSnapshotReader.Read(document);
        GeoProjectInfo? geoInfo = TryLoadGeoProjectInfo(document);
        int? crsEpsg = null;
        string crsName = string.Empty;
        if (geoInfo?.ProjectCrs is { EpsgCode: > 0 } rawCrs)
        {
            CrsReference migrated = JapanCrsPresets.MigrateLegacyReference(rawCrs);
            crsEpsg = migrated.EpsgCode;
            crsName = migrated.NameSnapshot ?? string.Empty;
        }

        List<string> viewTemplateNames = new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view => view.IsTemplate)
            .Select(view => view.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();

        return new ReadinessCheckSnapshot
        {
            DocumentTitle = document.Title ?? string.Empty,
            DocumentPath = document.PathName ?? string.Empty,
            IsEmptyModel = IsGenuinelyEmptyModel(document, snapshot),
            StandardVersionText = snapshot.StandardVersion,
            BoundParameters = snapshot.BoundParameters.Values.ToList(),
            FloorTypes = snapshot.FloorTypes,
            Floors = snapshot.Floors,
            Materials = snapshot.Materials.Values.ToList(),
            Levels = snapshot.Levels,
            LoadedFamilies = ReadLoadedFamilies(document),
            Schedules = ReadQaSchedules(document),
            ViewTemplateNames = viewTemplateNames,
            ExportPlanTemplateIssues = ExportViewSetup.InspectTemplateIssues(
                document,
                TemplateStandardDefinitions.ExportPlanViewTemplateName,
                BuiltInCategory.OST_MechanicalEquipment),
            ProjectCrsEpsgCode = crsEpsg,
            ProjectCrsName = crsName,
            IsGeoreferenceVerified = geoInfo?.Confidence == GeoConfidenceLevel.Verified,
            HostModelId = geoInfo?.ModelId?.Trim() ?? string.Empty,
            LinkedModels = ReadLinkedModels(document),
        };
    }

    private static bool IsGenuinelyEmptyModel(Document document, ModelSnapshot snapshot)
    {
        if (snapshot.FloorTypes.Any(type => type.FloorIds.Count > 0))
        {
            return false;
        }

        if (new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).WhereElementIsNotElementType().Any())
        {
            return false;
        }

        if (new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Any())
        {
            return false;
        }

        if (new FilteredElementCollector(document).OfClass(typeof(Wall)).WhereElementIsNotElementType().Any())
        {
            return false;
        }

        if (new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Any())
        {
            return false;
        }

        if (new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().Any())
        {
            return false;
        }

        return true;
    }

    private static GeoProjectInfo? TryLoadGeoProjectInfo(Document document)
    {
        try
        {
            return new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<LinkedModelReadinessInfo> ReadLinkedModels(Document document)
    {
        List<LinkedModelReadinessInfo> links = new();
        HashSet<string> seenKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (RevitLinkInstance link in new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .WhereElementIsNotElementType()
            .Cast<RevitLinkInstance>())
        {
            Document? linkedDoc = link.GetLinkDocument();
            string dedupeKey = LinkDeduplicationKey(link, linkedDoc);
            if (!seenKeys.Add(dedupeKey))
            {
                continue;
            }

            string name = linkedDoc?.Title ?? link.Name ?? $"Link {link.Id.Value}";
            if (linkedDoc is null)
            {
                links.Add(new LinkedModelReadinessInfo(name, isLoaded: false));
                continue;
            }

            string modelId = TryLoadGeoProjectInfo(linkedDoc)?.ModelId?.Trim() ?? string.Empty;
            links.Add(new LinkedModelReadinessInfo(name, isLoaded: true, modelId: modelId));
        }

        return links;
    }

    private static string LinkDeduplicationKey(RevitLinkInstance link, Document? linkedDoc)
    {
        if (linkedDoc != null && !string.IsNullOrWhiteSpace(linkedDoc.PathName))
        {
            try
            {
                return "path:" + Path.GetFullPath(linkedDoc.PathName);
            }
            catch (Exception)
            {
                return "path:" + linkedDoc.PathName.Trim();
            }
        }

        ElementId typeId = link.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            return "type:" + typeId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return "id:" + link.Id.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static List<LoadedFamilyCheckInfo> ReadLoadedFamilies(Document document)
    {
        Dictionary<long, List<long>> instancesBySymbol = new();
        foreach (BuiltInCategory category in ReadinessFamilyCategories)
        {
            foreach (FamilyInstance instance in new FilteredElementCollector(document)
                .OfCategory(category)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                long symbolId = instance.GetTypeId().Value;
                if (!instancesBySymbol.TryGetValue(symbolId, out List<long>? ids))
                {
                    instancesBySymbol[symbolId] = ids = new List<long>();
                }

                ids.Add(instance.Id.Value);
            }
        }

        List<string> requiredParamNames = FamilyStandard.Embedded.Kinds
            .SelectMany(kind => kind.RequiredTypeParameters)
            .Where(name => !string.Equals(name, GisSharedParameters.GisKind, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<LoadedFamilyCheckInfo> loaded = new();
        foreach (BuiltInCategory category in ReadinessFamilyCategories)
        {
            string categoryName = category.ToString();
            foreach (FamilySymbol symbol in new FilteredElementCollector(document)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>())
            {
                Parameter? gisKindParam = symbol.LookupParameter(GisSharedParameters.GisKind);
                Parameter? gisDoorParam = symbol.LookupParameter(GisSharedParameters.GisDoor);
                Parameter? gisAccessConParam = symbol.LookupParameter(GisSharedParameters.GisAccessCon);

                Dictionary<string, string> requiredValues = new(StringComparer.Ordinal);
                foreach (string paramName in requiredParamNames)
                {
                    if (symbol.LookupParameter(paramName) is { } param)
                    {
                        requiredValues[paramName] = ReadParameterText(param);
                    }
                }

                loaded.Add(new LoadedFamilyCheckInfo(
                    symbol.Id.Value,
                    symbol.FamilyName ?? string.Empty,
                    symbol.Name ?? string.Empty,
                    categoryName,
                    gisKind: ReadParameterText(gisKindParam),
                    gisCategory: ReadText(symbol, GisSharedParameters.GisCategory),
                    gisDoor: ReadParameterText(gisDoorParam),
                    gisAccessCon: ReadParameterText(gisAccessConParam),
                    hasGisKindParameter: gisKindParam != null,
                    hasGisDoorParameter: gisDoorParam != null,
                    hasGisAccessConParameter: gisAccessConParam != null,
                    typeParameters: requiredValues,
                    instanceIds: instancesBySymbol.TryGetValue(symbol.Id.Value, out List<long>? ids) ? ids : null));
            }
        }

        return loaded;
    }

    private static List<QaScheduleInfo> ReadQaSchedules(Document document)
    {
        List<QaScheduleInfo> schedules = new();
        foreach (ViewSchedule schedule in new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>())
        {
            if (schedule.IsTemplate)
            {
                continue;
            }

            ScheduleDefinition definition = schedule.Definition;
            string categoryName = ResolveScheduleCategoryName(definition.CategoryId);
            List<string> fieldNames = new();
            List<QaScheduleFieldInfo> fields = new();
            int count = definition.GetFieldCount();
            for (int index = 0; index < count; index++)
            {
                ScheduleField field = definition.GetField(index);
                if (ResolveSharedScheduleField(document, field) is { } resolved)
                {
                    fieldNames.Add(resolved.Name);
                    fields.Add(resolved);
                }
            }

            schedules.Add(new QaScheduleInfo(schedule.Id.Value, schedule.Name ?? string.Empty, fieldNames, categoryName, fields));
        }

        return schedules;
    }

    private static string ResolveScheduleCategoryName(ElementId categoryId)
    {
        if (categoryId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        long raw = categoryId.Value;
        if (raw >= int.MinValue && raw <= int.MaxValue)
        {
            BuiltInCategory builtIn = (BuiltInCategory)(int)raw;
            if (Enum.IsDefined(typeof(BuiltInCategory), builtIn) && builtIn != BuiltInCategory.INVALID)
            {
                return builtIn.ToString();
            }
        }

        return raw.ToString(CultureInfo.InvariantCulture);
    }

    private static QaScheduleFieldInfo? ResolveSharedScheduleField(Document document, ScheduleField field)
    {
        if (field.ParameterId != ElementId.InvalidElementId
            && document.GetElement(field.ParameterId) is SharedParameterElement shared)
        {
            string name = (shared.GetDefinition()?.Name ?? shared.Name ?? string.Empty).Trim();
            if (name.Length > 0)
            {
                return new QaScheduleFieldInfo(name, shared.GuidValue);
            }
        }

        return null;
    }

    private static List<GisBoundParameterState> ReadBoundParameters(Document document)
    {
        List<GisBoundParameterState> bound = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || iterator.Current is not ElementBinding binding)
            {
                continue;
            }

            if (!seen.Add(definition.Name))
            {
                continue;
            }

            Guid? guid = document.GetElement(definition.Id) is SharedParameterElement shared ? shared.GuidValue : null;
            GisParameterBinding kind = binding is TypeBinding ? GisParameterBinding.Type : GisParameterBinding.Instance;
            bound.Add(new GisBoundParameterState(definition.Name, guid, kind));
        }

        return bound;
    }

    private static List<string> ReadFloorTypesWithoutCategory(Document document)
    {
        HashSet<long> typesInUse = new(new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Select(floor => floor.GetTypeId().Value));
        FloorTypeStandard standard = FloorTypeStandard.LoadEmbedded();
        HashSet<string> known = new(FloorTypeStandard.KnownCategories, StringComparer.OrdinalIgnoreCase);
        return new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(type => typesInUse.Contains(type.Id.Value))
            .Where(type => !standard.TryGetFloorType(type.Name, out _))
            .Where(type => !known.Contains(ReadText(type, GisSharedParameters.GisCategory)))
            .Select(type => type.Name ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string ReadText(Element? element, string parameterName)
    {
        return ReadParameterText(element?.LookupParameter(parameterName));
    }

    private static string ReadParameterText(Parameter? parameter)
    {
        if (parameter == null || !parameter.HasValue)
        {
            return string.Empty;
        }

        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value?.Trim() ?? string.Empty;
    }
}
