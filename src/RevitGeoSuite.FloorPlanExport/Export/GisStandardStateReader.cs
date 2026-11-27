using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core.Federation;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Federation;
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
        BuiltInCategory.OST_Windows,
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
            StandardVersionText = ReadParameterText(FindSharedParameter(document.ProjectInformation, GisSharedParameters.GisStandardVersion, allowNameFallback: true)),
            BoundParameters = ReadBoundParameters(document),
            GisViewCount = new FilteredElementCollector(document)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Count(view => !view.IsTemplate &&
                               (view.Name ?? string.Empty).StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase)),
            FloorTypesWithoutCategory = ReadFloorTypesWithoutCategory(document),
            ModelRole = ReadModelRoleText(document),
        };
    }

    /// <summary>Raw trimmed text of the <c>gis_model_role</c> shared parameter on Project Information (read strictly by frozen GUID).</summary>
    public static string ReadModelRoleText(Document? document)
    {
        if (document?.ProjectInformation == null)
        {
            return string.Empty;
        }

        Parameter? parameter = FindSharedParameter(
            document.ProjectInformation,
            GisSharedParameters.GisModelRole,
            allowNameFallback: false);
        return ReadRawOrDisplayText(parameter).Trim();
    }

    /// <summary>Normalized <c>gis_model_role</c> (<c>"authoring"</c>, <c>"showcase"</c>, or <c>null</c> when unset/unrecognized).</summary>
    public static string? ReadModelRole(Document? document) =>
        ShowcaseExportGuard.NormalizeModelRole(ReadModelRoleText(document));

    /// <summary>True when <paramref name="document"/> is stamped with <c>gis_model_role = "showcase"</c>.</summary>
    public static bool IsShowcaseDocument(Document? document) =>
        ShowcaseExportGuard.IsShowcaseModelRole(ReadModelRoleText(document));

    public static int? ReadStandardVersion(Document? document)
    {
        if (document?.ProjectInformation == null)
        {
            return null;
        }

        Parameter? parameter = FindSharedParameter(
            document.ProjectInformation,
            GisSharedParameters.GisStandardVersion,
            allowNameFallback: true);
        return OpeningFamilyRules.ParseStandardVersion(ReadRawOrDisplayText(parameter));
    }

    /// <summary>
    /// A standard parameter on <paramref name="element"/>: by its frozen GUID first; by name only for the legacy
    /// parameters older models may hold under another GUID, or when <paramref name="allowNameFallback"/> is set.
    /// Names outside the standard are looked up by name.
    /// </summary>
    internal static Parameter? FindSharedParameter(
        Element? element,
        string parameterName,
        bool allowNameFallback = false)
    {
        if (element == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        if (GisSharedParameters.TryGet(parameterName, out GisParameterDefinition definition))
        {
            Parameter? byGuid = element.get_Parameter(definition.Guid);
            if (byGuid != null)
            {
                return byGuid;
            }

            if (!allowNameFallback &&
                !GisStandardCheck.LegacyParameterNames.Contains(parameterName, StringComparer.Ordinal))
            {
                return null;
            }
        }

        return element.LookupParameter(parameterName);
    }

    /// <summary>The raw text of a standard type parameter (type first, then instance), read by GUID per <see cref="FindSharedParameter"/>.</summary>
    internal static string? ReadSharedTypeParameterRaw(FamilyInstance? instance, string parameterName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        Element? typeElement = (Element?)instance.Symbol ?? instance.Document?.GetElement(instance.GetTypeId());
        Parameter? parameter = FindSharedParameter(typeElement, parameterName) ??
            FindSharedParameter(instance, parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        return parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : ReadParameterText(parameter);
    }

    public static bool IsTemplateDocument(Document document) =>
        (document.PathName ?? string.Empty).EndsWith(".rte", StringComparison.OrdinalIgnoreCase)
        || (document.Title ?? string.Empty).EndsWith(".rte", StringComparison.OrdinalIgnoreCase);

    public static ReadinessMode SuggestedReadinessMode(Document document, ModelSnapshot? existingSnapshot = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string path = (document.PathName ?? string.Empty).Trim();
        string title = (document.Title ?? string.Empty).Trim();
        if (path.EndsWith(".rte", StringComparison.OrdinalIgnoreCase)
            || title.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
        {
            return ReadinessMode.Template;
        }

        if (path.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
            || title.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            return ReadinessMode.Project;
        }

        ModelSnapshot snapshot = existingSnapshot ?? ModelSnapshotReader.Read(document);
        return IsGenuinelyEmptyModel(document, snapshot) ? ReadinessMode.Template : ReadinessMode.Project;
    }

    public static ReadinessCheckSnapshot ReadReadiness(
        Document document,
        ModelSnapshot? existingSnapshot = null,
        ReadinessMode? modeOverride = null)
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

        string readinessTemplateName = viewTemplateNames.Contains(
            TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparer.OrdinalIgnoreCase)
            ? TemplateStandardDefinitions.ExportPlanViewTemplateName
            : TemplateStandardDefinitions.LegacyExportPlanViewTemplateName;

        bool isEmptyModel = IsGenuinelyEmptyModel(document, snapshot);
        ReadinessMode effectiveMode = modeOverride
            ?? ReadinessCheckSnapshot.SuggestMode(document.PathName, document.Title, isEmptyModel);

        (int levelBakedCount, List<long> levelBakedSample) = effectiveMode == ReadinessMode.Template
            ? ReadLevelsWithBakedInIds(document)
            : (0, new List<long>());
        (int elementBakedCount, List<long> elementBakedSample) = effectiveMode == ReadinessMode.Template
            ? ReadElementsWithBakedInIds(document)
            : (0, new List<long>());
        (FederationCheckSnapshot? federation, string federationError) = effectiveMode == ReadinessMode.Project
            ? TryReadFederationSnapshot(document)
            : (null, string.Empty);

        return new ReadinessCheckSnapshot
        {
            DocumentTitle = document.Title ?? string.Empty,
            DocumentPath = document.PathName ?? string.Empty,
            IsEmptyModel = isEmptyModel,
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
                readinessTemplateName),
            ExportViewHideFilterNames = ReadExportViewHideFilterNames(document),
            ProjectCrsEpsgCode = crsEpsg,
            ProjectCrsName = crsName,
            IsGeoreferenceVerified = geoInfo?.Confidence == GeoConfidenceLevel.Verified,
            HostModelId = geoInfo?.ModelId?.Trim() ?? string.Empty,
            HostStationFolder = geoInfo?.GisStationFolder?.Trim() ?? string.Empty,
            LevelsWithBakedInIdCount = levelBakedCount,
            LevelsWithBakedInIdSample = levelBakedSample,
            ElementsWithBakedInIdCount = elementBakedCount,
            ElementsWithBakedInIdSample = elementBakedSample,
            LinkedModels = ReadLinkedModels(document),
            HostModelRole = ReadModelRoleText(document),
            ShowcaseElements = ReadShowcaseElements(document),
            Federation = federation,
            FederationUnavailableReason = federationError,
        };
    }

    private static (FederationCheckSnapshot? Snapshot, string Error) TryReadFederationSnapshot(Document document)
    {
        try
        {
            return (RevitFederationSnapshotCollector.Collect(document), string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Federation snapshot failed: {ex}");
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private const int BakedInSampleLimit = 8;

    private static (int Count, List<long> Sample) ReadLevelsWithBakedInIds(Document document)
    {
        int count = 0;
        List<long> sample = new();
        foreach (Level level in new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>())
        {
            if (HasBakedInValue(level, GisSharedParameters.LevelId))
            {
                count++;
                if (sample.Count < BakedInSampleLimit)
                {
                    sample.Add(level.Id.Value);
                }
            }
        }

        return (count, sample);
    }

    private static (int Count, List<long> Sample) ReadElementsWithBakedInIds(Document document)
    {
        int count = 0;
        List<long> sample = new();
        HashSet<BuiltInCategory> categories = BakedInIdCategories(document);
        if (categories.Count == 0)
        {
            return (count, sample);
        }

        foreach (Element element in new FilteredElementCollector(document)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType())
        {
            if (!HasBakedInValue(element, GisSharedParameters.Id)
                && !HasBakedInValue(element, GisSharedParameters.GisKey))
            {
                continue;
            }

            count++;
            if (sample.Count < BakedInSampleLimit)
            {
                sample.Add(element.Id.Value);
            }
        }

        return (count, sample);
    }

    private static HashSet<BuiltInCategory> BakedInIdCategories(Document document)
    {
        HashSet<BuiltInCategory> categories = new();

        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not Definition definition
                || (!string.Equals(definition.Name, GisSharedParameters.Id, StringComparison.Ordinal)
                    && !string.Equals(definition.Name, GisSharedParameters.GisKey, StringComparison.Ordinal))
                || iterator.Current is not ElementBinding binding)
            {
                continue;
            }

            foreach (Category category in binding.Categories)
            {
                if (category.BuiltInCategory != BuiltInCategory.INVALID)
                {
                    categories.Add(category.BuiltInCategory);
                }
            }
        }

        foreach (string parameterName in new[] { GisSharedParameters.Id, GisSharedParameters.GisKey })
        {
            if (!GisSharedParameters.TryGet(parameterName, out GisParameterDefinition definition))
            {
                continue;
            }

            foreach (string categoryName in definition.Categories)
            {
                if (Enum.TryParse(categoryName, ignoreCase: false, out BuiltInCategory builtIn)
                    && builtIn != BuiltInCategory.INVALID)
                {
                    categories.Add(builtIn);
                }
            }
        }

        return categories;
    }

    private static bool HasBakedInValue(Element element, string name) =>
        element.GetParameters(name).Any(parameter => parameter.IsShared && ReadParameterText(parameter).Length > 0);

    private static List<string> ReadExportViewHideFilterNames(Document document)
    {
        HashSet<string> hidden = new(StringComparer.Ordinal);
        HashSet<ElementId> checkedViews = new();
        ElementId filtersParameterId = new(BuiltInParameter.VIS_GRAPHICS_FILTERS);
        foreach (ViewPlan view in new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>())
        {
            string name = (view.Name ?? string.Empty).Trim();
            if (view.IsTemplate)
            {
                if ((string.Equals(name, TemplateStandardDefinitions.ExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, TemplateStandardDefinitions.LegacyExportPlanViewTemplateName, StringComparison.OrdinalIgnoreCase))
                    && !view.GetNonControlledTemplateParameterIds().Contains(filtersParameterId))
                {
                    InspectFilters(view);
                }
            }
            else if (view.ViewType == ViewType.FloorPlan && name.StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId
                    && document.GetElement(view.ViewTemplateId) is ViewPlan template
                    && !template.GetNonControlledTemplateParameterIds().Contains(filtersParameterId))
                {
                    InspectFilters(template);
                }
                else
                {
                    InspectFilters(view);
                }
            }
        }

        return hidden.OrderBy(name => name, StringComparer.Ordinal).ToList();

        void InspectFilters(ViewPlan view)
        {
            if (!checkedViews.Add(view.Id))
            {
                return;
            }

            foreach (ElementId id in view.GetFilters())
            {
                if (document.GetElement(id) is ParameterFilterElement filter
                    && view.GetIsFilterEnabled(id)
                    && !view.GetFilterVisibility(id))
                {
                    hidden.Add(filter.Name);
                }
            }
        }
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
            string modelRole = ReadModelRoleText(linkedDoc);
            links.Add(new LinkedModelReadinessInfo(name, isLoaded: true, modelId: modelId, modelRole: modelRole));
        }

        return links;
    }

    private const int ShowcaseFindingSampleLimit = 64;

    private static List<ShowcaseElementFinding> ReadShowcaseElements(Document document)
    {
        List<ShowcaseElementFinding> findings = new();
        RevitShowcaseElementFilter.TypeClassificationCache typeCache = new();
        foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            if (element is RevitLinkInstance || element.ViewSpecific || element.Category == null)
            {
                continue;
            }

            if (element.Category.CategoryType != CategoryType.Model && element is not DirectShape)
            {
                continue;
            }

            ShowcaseElementCheckResult check = RevitShowcaseElementFilter.ClassifyElement(element, typeCache: typeCache);
            if (!check.IsShowcase)
            {
                continue;
            }

            findings.Add(new ShowcaseElementFinding(
                element.Id.Value,
                RevitShowcaseElementFilter.DescribeElement(element),
                check.Reason,
                check.MatchedMarker));

            if (findings.Count >= ShowcaseFindingSampleLimit)
            {
                break;
            }
        }

        return findings;
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
                // The standard-only gis_ parameters are read the same way: by their frozen GUID, never by name.
                Parameter? gisDoorParam = FindSharedParameter(symbol, GisSharedParameters.GisDoor);
                Parameter? gisAccessConParam = FindSharedParameter(symbol, GisSharedParameters.GisAccessCon);
                Parameter? gisOpeningCategoryParam = FindSharedParameter(symbol, GisSharedParameters.GisOpeningCategory);
                Parameter? gisFixtureTypeParam = FindSharedParameter(symbol, GisSharedParameters.GisFixtureType);

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
                    gisDoor: ReadRawOrDisplayText(gisDoorParam),
                    gisAccessCon: ReadRawOrDisplayText(gisAccessConParam),
                    hasGisKindParameter: gisKindParam != null,
                    hasGisDoorParameter: gisDoorParam != null,
                    hasGisAccessConParameter: gisAccessConParam != null,
                    typeParameters: requiredValues,
                    instanceIds: instancesBySymbol.TryGetValue(symbol.Id.Value, out List<long>? ids) ? ids : null,
                    gisOpeningCategory: ReadRawOrDisplayText(gisOpeningCategoryParam),
                    gisFixtureType: ReadRawOrDisplayText(gisFixtureTypeParam),
                    hasGisOpeningCategoryParameter: gisOpeningCategoryParam != null,
                    hasGisFixtureTypeParameter: gisFixtureTypeParam != null));
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

    private static string ReadRawOrDisplayText(Parameter? parameter) =>
        parameter?.StorageType == StorageType.String ? (parameter.AsString() ?? string.Empty) : ReadParameterText(parameter);

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
