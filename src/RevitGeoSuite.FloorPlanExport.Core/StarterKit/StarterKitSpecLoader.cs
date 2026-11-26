using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Reference Kanizu GIS standards loaded either from embedded resources or from a <c>standards/</c> folder on disk,
/// used by <see cref="StarterKitSpecLoader"/> to cross-check shared parameter bindings, closed domains,
/// floor types, and standard family kinds.
/// </summary>
public sealed class StarterKitReferenceStandards
{
    public StarterKitReferenceStandards(
        IReadOnlyDictionary<string, GisParameterDefinition> parametersByName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> openingDomains,
        IReadOnlySetLike unitCategories,
        IReadOnlySetLike restrictions,
        IReadOnlySetLike directions,
        IReadOnlySetLike gisKinds,
        IReadOnlySetLike floorMaterialNames,
        IReadOnlyDictionary<string, IReadOnlySetLike>? familiesByGisKind = null)
    {
        ParametersByName = parametersByName ?? throw new ArgumentNullException(nameof(parametersByName));
        OpeningDomains = openingDomains ?? throw new ArgumentNullException(nameof(openingDomains));
        UnitCategories = unitCategories ?? throw new ArgumentNullException(nameof(unitCategories));
        Restrictions = restrictions ?? throw new ArgumentNullException(nameof(restrictions));
        Directions = directions ?? throw new ArgumentNullException(nameof(directions));
        GisKinds = gisKinds ?? throw new ArgumentNullException(nameof(gisKinds));
        FloorMaterialNames = floorMaterialNames ?? throw new ArgumentNullException(nameof(floorMaterialNames));
        FamiliesByGisKind = familiesByGisKind
            ?? new ReadOnlyDictionary<string, IReadOnlySetLike>(new Dictionary<string, IReadOnlySetLike>(StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, GisParameterDefinition> ParametersByName { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> OpeningDomains { get; }

    public IReadOnlySetLike UnitCategories { get; }

    public IReadOnlySetLike Restrictions { get; }

    public IReadOnlySetLike Directions { get; }

    public IReadOnlySetLike GisKinds { get; }

    public IReadOnlySetLike FloorMaterialNames { get; }

    /// <summary>Registered closed-list family names from <c>standards/families.json</c> keyed by <c>gis_kind</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlySetLike> FamiliesByGisKind { get; }

    /// <summary>Loads the shipped standards embedded inside <c>RevitGeoSuite.FloorPlanExport.Core</c>.</summary>
    public static StarterKitReferenceStandards LoadEmbedded()
    {
        Dictionary<string, GisParameterDefinition> paramsByName = GisSharedParameters.All
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyList<string>> openingDomains =
            GisStandardDomains.ParseOpeningTypesJson(GisStandardFiles.ReadOpeningTypes());
        GisFieldList fields = GisFieldList.Parse(GisStandardFiles.ReadFieldList(), GisFieldList.FileName);
        FloorTypeStandard floorTypes = FloorTypeStandard.Parse(GisStandardFiles.ReadFloorTypes());
        FamilyStandard families = FamilyStandard.Parse(GisStandardFiles.ReadFamilies());

        return Build(paramsByName, openingDomains, fields, floorTypes, families);
    }

    /// <summary>Loads the reference standards directly from a <c>standards/</c> folder on disk.</summary>
    public static StarterKitReferenceStandards LoadFromDirectory(string standardsDirectory)
    {
        if (string.IsNullOrWhiteSpace(standardsDirectory))
        {
            throw new ArgumentException("Standards directory path is required.", nameof(standardsDirectory));
        }

        string bindingsPath = Path.Combine(standardsDirectory, GisSharedParameters.BindingsFileName);
        string fieldsPath = Path.Combine(standardsDirectory, GisFieldList.FileName);
        string openingTypesPath = Path.Combine(standardsDirectory, GisStandardDomains.OpeningTypesFileName);
        string floorTypesPath = Path.Combine(standardsDirectory, FloorTypeStandard.FileName);
        string familiesPath = Path.Combine(standardsDirectory, FamilyStandard.FileName);

        Dictionary<string, GisParameterDefinition> paramsByName = ParseSharedParameterBindings(ReadUtf8(bindingsPath));
        IReadOnlyDictionary<string, IReadOnlyList<string>> openingDomains =
            GisStandardDomains.ParseOpeningTypesJson(ReadUtf8(openingTypesPath));
        GisFieldList fields = GisFieldList.Parse(ReadUtf8(fieldsPath), fieldsPath);
        FloorTypeStandard floorTypes = FloorTypeStandard.Parse(ReadUtf8(floorTypesPath));
        FamilyStandard families = FamilyStandard.Parse(ReadUtf8(familiesPath));

        return Build(paramsByName, openingDomains, fields, floorTypes, families);
    }

    private static StarterKitReferenceStandards Build(
        Dictionary<string, GisParameterDefinition> paramsByName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> openingDomains,
        GisFieldList fields,
        FloorTypeStandard floorTypes,
        FamilyStandard families)
    {
        HashSet<string> unitCategories = new(StringComparer.Ordinal);
        HashSet<string> restrictions = new(StringComparer.Ordinal);
        HashSet<string> directions = new(StringComparer.Ordinal);

        foreach (GisField row in fields.Fields)
        {
            if (string.Equals(row.Layer, "unit", StringComparison.Ordinal))
            {
                if (string.Equals(row.Name, "category", StringComparison.Ordinal))
                {
                    foreach (string val in row.Values)
                    {
                        unitCategories.Add(val);
                    }
                }
                else if (string.Equals(row.Name, "restrict", StringComparison.Ordinal) ||
                         string.Equals(row.Name, "gis_restriction", StringComparison.Ordinal))
                {
                    foreach (string val in row.Values)
                    {
                        restrictions.Add(val);
                    }
                }
                else if (string.Equals(row.Name, "direction", StringComparison.Ordinal))
                {
                    foreach (string val in row.Values)
                    {
                        directions.Add(val);
                    }
                }
            }
        }

        HashSet<string> gisKinds = new(families.Kinds.Select(k => k.Kind), StringComparer.Ordinal);
        HashSet<string> floorMaterials = new(floorTypes.Materials.Select(m => m.Name), StringComparer.Ordinal);
        Dictionary<string, IReadOnlySetLike> familiesByKind = new(StringComparer.Ordinal);
        foreach (StandardFamilyKind kind in families.Kinds)
        {
            familiesByKind[kind.Kind] = new OrdinalStringSet(kind.Families);
        }

        return new StarterKitReferenceStandards(
            new ReadOnlyDictionary<string, GisParameterDefinition>(paramsByName),
            openingDomains,
            new OrdinalStringSet(unitCategories),
            new OrdinalStringSet(restrictions),
            new OrdinalStringSet(directions),
            new OrdinalStringSet(gisKinds),
            new OrdinalStringSet(floorMaterials),
            new ReadOnlyDictionary<string, IReadOnlySetLike>(familiesByKind));
    }

    private static Dictionary<string, GisParameterDefinition> ParseSharedParameterBindings(string json)
    {
        JObject root = JObject.Parse(json);
        Dictionary<string, GisParameterDefinition> result = new(StringComparer.Ordinal);
        JArray parameters = root["parameters"] as JArray
            ?? throw new InvalidDataException($"{GisSharedParameters.BindingsFileName} is missing 'parameters' array.");

        foreach (JToken token in parameters)
        {
            string name = (string?)token["name"] ?? string.Empty;
            Guid guid = Guid.Parse((string?)token["guid"] ?? throw new InvalidDataException($"Missing GUID for parameter '{name}'."));
            string dataTypeText = (string?)token["data_type"] ?? "text";
            GisParameterDataType dataType = string.Equals(dataTypeText, "integer", StringComparison.OrdinalIgnoreCase)
                ? GisParameterDataType.Integer
                : GisParameterDataType.Text;
            string bindingText = (string?)token["binding"] ?? "instance";
            GisParameterBinding binding = string.Equals(bindingText, "type", StringComparison.OrdinalIgnoreCase)
                ? GisParameterBinding.Type
                : GisParameterBinding.Instance;
            List<string> categories = ((JArray?)token["categories"] ?? new JArray())
                .Select(c => (string)c!)
                .ToList();
            bool userModifiable = (bool?)token["user_modifiable"] ?? true;

            result[name] = new GisParameterDefinition(name, guid, dataType, binding, Array.AsReadOnly(categories.ToArray()), string.Empty, userModifiable);
        }

        return result;
    }

    private static string ReadUtf8(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
}

/// <summary>Minimal net48-compatible read-only string set abstraction.</summary>
public interface IReadOnlySetLike : IEnumerable<string>
{
    int Count { get; }

    bool Contains(string value);
}

internal sealed class OrdinalStringSet : IReadOnlySetLike
{
    private readonly HashSet<string> _values;

    public OrdinalStringSet(IEnumerable<string> values)
    {
        _values = new HashSet<string>(values, StringComparer.Ordinal);
    }

    public int Count => _values.Count;

    public bool Contains(string value) => value != null && _values.Contains(value);

    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

/// <summary>
/// Loads, deserializes, and strictly cross-validates the declarative Kanizu Starter Kit specifications
/// (<c>kit.json</c>, <c>rte.json</c>, <c>materials-3d.json</c>, and <c>families/*.family.json</c>).
/// </summary>
public static class StarterKitSpecLoader
{
    public const string KitFileName = "kit.json";
    public const string RteFileName = "rte.json";
    public const string Materials3DFileName = "materials-3d.json";

    private const string EmbeddedPrefix = "RevitGeoSuite.Standards.StarterKit.";
    private const string ForbiddenScheduleCategory = "OST_Levels";
    private const string CanonicalExportTemplateName = "GIS Export";
    private const string LegacyExportTemplateName = "Kanizu GIS Export Plan";

    private static readonly Regex SemVerRegex = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex HexColorRegex = new(
        @"^#[0-9A-Fa-f]{6}$",
        RegexOptions.CultureInvariant);

    private static readonly string[] RequiredMaterialRoles =
    {
        "body",
        "glass",
        "accent",
        "frame",
        "monolithic_stair",
        "glass_guard",
        "circulation",
        "fixture",
        "column",
    };

    private static readonly string[] RequiredViewTemplateNames =
    {
        "GIS Export",
        "GIS Working Plan",
        "GIS QA Plan",
        "GIS QA 3D",
        "Kanizu 3D Export",
    };

    private static readonly string[] RequiredLineStyleNames =
    {
        "GIS_detail_line",
        "GIS_platform_edge",
        "GIS_fare_boundary",
        "GIS_curb",
    };

    private static readonly string[] RequiredSystemFamilyIds =
    {
        "KNZ_Stair_Monolithic",
        "KNZ_Wall_GlassGuard_1100",
        "KNZ_Floor_OpenToBelow",
    };

    private static readonly string[] ValidFilterOperators =
    {
        "isEmpty",
        "hasValue",
        "equals",
        "notEquals",
        "contains",
    };

    private static readonly string[] ValidAlphaModes =
    {
        "opaque",
        "blend",
        "mask",
    };

    /// <summary>Supported declarative schema version for kit, rte, materials-3d, and family specs.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>Loads the embedded Starter Kit specification shipped inside this assembly and validates it.</summary>
    public static StarterKitLoadResult LoadEmbedded()
    {
        Assembly assembly = typeof(StarterKitSpecLoader).Assembly;
        StarterKitReferenceStandards standards = StarterKitReferenceStandards.LoadEmbedded();

        string kitJson = ReadEmbedded(assembly, KitFileName);
        StarterKitHeaderSpec header = ParseHeader(kitJson);
        RteTemplateSpec rte = ParseRte(ReadEmbedded(assembly, header.RteSpec));
        Materials3DSpec materials = ParseMaterials3D(ReadEmbedded(assembly, header.MaterialsSpec));

        List<FamilyGeometrySpec> families = new();
        List<StarterKitValidationFinding> loadFindings = new();
        foreach (StarterKitFamilyRegistryEntry entry in header.Families)
        {
            string? familyJson = TryReadEmbedded(assembly, entry.SpecPath);
            if (familyJson == null)
            {
                if (!entry.Optional)
                {
                    loadFindings.Add(new StarterKitValidationFinding(
                        StarterKitValidationSeverity.Error,
                        "SK-LOAD-001",
                        entry.SpecPath,
                        $"Embedded family spec '{entry.SpecPath}' for '{entry.Id}' was not found."));
                }

                continue;
            }

            families.Add(ParseFamily(familyJson));
        }

        StarterKitSpec spec = new(header, rte, materials, Array.AsReadOnly(families.ToArray()));
        StarterKitValidationResult validation = Validate(spec, standards, loadFindings);
        return new StarterKitLoadResult(spec, validation);
    }

    /// <summary>
    /// Loads a Starter Kit specification from <paramref name="starterKitDirectory"/> and validates it against
    /// <paramref name="standardsDirectory"/> (defaults to parent directory if <c>fields.json</c> exists there, else embedded standards).
    /// </summary>
    public static StarterKitLoadResult LoadFromDirectory(string starterKitDirectory, string? standardsDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(starterKitDirectory))
        {
            throw new ArgumentException("Starter kit directory path is required.", nameof(starterKitDirectory));
        }

        List<StarterKitValidationFinding> loadFindings = new();
        string kitPath = Path.Combine(starterKitDirectory, KitFileName);
        if (!File.Exists(kitPath))
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-LOAD-001",
                kitPath,
                $"Missing required starter kit manifest '{KitFileName}'."));
            return new StarterKitLoadResult(null, new StarterKitValidationResult(loadFindings));
        }

        StarterKitReferenceStandards standards = ResolveStandards(starterKitDirectory, standardsDirectory);

        StarterKitHeaderSpec header;
        try
        {
            header = ParseHeader(ReadUtf8(kitPath));
        }
        catch (Exception ex)
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-PARSE-001",
                KitFileName,
                $"Failed to parse {KitFileName}: {ex.Message}"));
            return new StarterKitLoadResult(null, new StarterKitValidationResult(loadFindings));
        }

        if (!TryResolveSafeChildPath(starterKitDirectory, header.RteSpec, out string rtePath, out string rtePathError))
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-LOAD-005",
                header.RteSpec,
                $"Invalid rteSpec path '{header.RteSpec}': {rtePathError}"));
        }
        else if (!File.Exists(rtePath))
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-LOAD-002",
                header.RteSpec,
                $"Referenced RTE spec '{header.RteSpec}' does not exist."));
        }

        if (!TryResolveSafeChildPath(starterKitDirectory, header.MaterialsSpec, out string materialsPath, out string materialsPathError))
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-LOAD-005",
                header.MaterialsSpec,
                $"Invalid materialsSpec path '{header.MaterialsSpec}': {materialsPathError}"));
        }
        else if (!File.Exists(materialsPath))
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-LOAD-003",
                header.MaterialsSpec,
                $"Referenced materials-3d spec '{header.MaterialsSpec}' does not exist."));
        }

        if (loadFindings.Count > 0)
        {
            return new StarterKitLoadResult(null, new StarterKitValidationResult(loadFindings));
        }

        RteTemplateSpec rte;
        Materials3DSpec materials;
        try
        {
            rte = ParseRte(ReadUtf8(rtePath));
            materials = ParseMaterials3D(ReadUtf8(materialsPath));
        }
        catch (Exception ex)
        {
            loadFindings.Add(new StarterKitValidationFinding(
                StarterKitValidationSeverity.Error,
                "SK-PARSE-002",
                starterKitDirectory,
                $"Failed to parse starter kit sub-spec: {ex.Message}"));
            return new StarterKitLoadResult(null, new StarterKitValidationResult(loadFindings));
        }

        List<FamilyGeometrySpec> families = new();
        foreach (StarterKitFamilyRegistryEntry entry in header.Families)
        {
            if (!TryResolveSafeChildPath(starterKitDirectory, entry.SpecPath, out string familyPath, out string familyPathError))
            {
                loadFindings.Add(new StarterKitValidationFinding(
                    StarterKitValidationSeverity.Error,
                    "SK-LOAD-005",
                    entry.SpecPath,
                    $"Invalid family specPath '{entry.SpecPath}' for '{entry.Id}': {familyPathError}"));
                continue;
            }

            if (!File.Exists(familyPath))
            {
                if (!entry.Optional)
                {
                    loadFindings.Add(new StarterKitValidationFinding(
                        StarterKitValidationSeverity.Error,
                        "SK-LOAD-004",
                        entry.SpecPath,
                        $"Family spec '{entry.SpecPath}' referenced by '{entry.Id}' does not exist."));
                }

                continue;
            }

            try
            {
                families.Add(ParseFamily(ReadUtf8(familyPath)));
            }
            catch (Exception ex)
            {
                loadFindings.Add(new StarterKitValidationFinding(
                    StarterKitValidationSeverity.Error,
                    "SK-PARSE-003",
                    entry.SpecPath,
                    $"Failed to parse family spec '{entry.SpecPath}': {ex.Message}"));
            }
        }

        StarterKitSpec spec = new(header, rte, materials, Array.AsReadOnly(families.ToArray()));
        StarterKitValidationResult validation = Validate(spec, standards, loadFindings);
        return new StarterKitLoadResult(spec, validation);
    }

    /// <summary>Loads and validates a Starter Kit directory, throwing <see cref="InvalidDataException"/> on any error.</summary>
    public static StarterKitSpec LoadValidFromDirectory(string starterKitDirectory, string? standardsDirectory = null)
    {
        StarterKitLoadResult result = LoadFromDirectory(starterKitDirectory, standardsDirectory);
        if (!result.IsValid || result.Spec == null)
        {
            string details = string.Join("; ", result.Validation.Errors.Select(e => e.ToString()));
            throw new InvalidDataException($"Invalid Starter Kit specification: {details}");
        }

        return result.Spec;
    }

    /// <summary>Validates an in-memory <see cref="StarterKitSpec"/> against Kanizu GIS standards.</summary>
    public static StarterKitValidationResult Validate(
        StarterKitSpec spec,
        StarterKitReferenceStandards? standards = null,
        IEnumerable<StarterKitValidationFinding>? initialFindings = null)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        List<StarterKitValidationFinding> findings = new(initialFindings ?? Array.Empty<StarterKitValidationFinding>());

        ValidateHeader(spec.Header, findings);
        HashSet<string> valid3DMaterials = ValidateMaterials(spec.Materials3D, findings);
        ValidateRte(spec.Rte, valid3DMaterials, refStandards, findings);
        ValidateFamilies(spec, valid3DMaterials, refStandards, findings);

        return new StarterKitValidationResult(findings);
    }

    public static StarterKitHeaderSpec ParseHeader(string json)
    {
        JObject root = JObject.Parse(json);
        JObject budgetsObj = RequireObject(root, "budgets", KitFileName);
        JObject clearancesObj = RequireObject(budgetsObj, "antiZFightingClearancesMm", KitFileName);

        AntiZFightingClearances clearances = new(
            RequireDouble(clearancesObj, "slabClearMm"),
            RequireDouble(clearancesObj, "sideClearMm"),
            RequireDouble(clearancesObj, "glassTuckMm"));

        StarterKitBudgets budgets = new(
            RequireInt(budgetsObj, "maxRfaFileSizeKb"),
            RequireInt(budgetsObj, "maxRteFileSizeMb"),
            RequireInt(budgetsObj, "maxCoarseTrianglesPerFamily"),
            RequireInt(budgetsObj, "maxMediumTrianglesPerFamily"),
            RequireInt(budgetsObj, "maxStairFlightTriangles"),
            clearances);

        List<StarterKitFamilyRegistryEntry> families = ReadArray(root, "families")
            .Select(token => new StarterKitFamilyRegistryEntry(
                ReadString(token, "id"),
                ReadString(token, "specPath"),
                ReadString(token, "group"),
                ReadString(token, "phase"),
                ReadBool(token, "optional", defaultValue: false)))
            .ToList();

        return new StarterKitHeaderSpec(
            ReadString(root, "schema"),
            ReadInt(root, "schemaVersion"),
            ReadString(root, "kitVersion"),
            ReadInt(root, "gisStandardVersion"),
            ReadStringList(root, "supportedRevitVersions"),
            ReadString(root, "rteSpec"),
            ReadString(root, "materialsSpec"),
            budgets,
            Array.AsReadOnly(families.ToArray()));
    }

    public static Materials3DSpec ParseMaterials3D(string json)
    {
        JObject root = JObject.Parse(json);
        Dictionary<string, string> notes = new(StringComparer.Ordinal);
        if (root["alignmentNotes"] is JObject notesObj)
        {
            foreach (JProperty prop in notesObj.Properties())
            {
                notes[prop.Name] = prop.Value.Type == JTokenType.String ? (string)prop.Value! : prop.Value.ToString();
            }
        }

        List<Material3DSpec> materials = ReadArray(root, "materials")
            .Select(token => new Material3DSpec(
                ReadString(token, "name"),
                ReadString(token, "role"),
                ReadIntList(token, "srgb"),
                ReadString(token, "hex"),
                ReadInt(token, "transparency"),
                ReadString(token, "alphaMode"),
                ReadDouble(token, "metallic"),
                ReadDouble(token, "roughness"),
                ReadString(token, "description")))
            .ToList();

        return new Materials3DSpec(
            ReadString(root, "schema"),
            ReadInt(root, "schemaVersion"),
            new ReadOnlyDictionary<string, string>(notes),
            Array.AsReadOnly(materials.ToArray()));
    }

    public static RteTemplateSpec ParseRte(string json)
    {
        JObject root = JObject.Parse(json);
        JObject startingObj = RequireObject(root, "startingView", RteFileName);
        StartingViewSpec startingView = new(
            ReadString(startingObj, "name"),
            ReadString(startingObj, "viewType"),
            ReadBool(startingObj, "bilingual"),
            ReadStringList(startingObj, "languages"),
            ReadString(startingObj, "titleEn"),
            ReadString(startingObj, "titleJa"));

        JObject hygieneObj = RequireObject(root, "hygiene", RteFileName);
        RteHygieneSpec hygiene = new(
            ReadBool(hygieneObj, "requireZeroProjectIdentity"),
            ReadBool(hygieneObj, "requireZeroWorksets"),
            ReadStringList(hygieneObj, "forbiddenIdentityFields"),
            ReadStringList(hygieneObj, "forbiddenScheduleCategories"));

        List<ViewTemplateSpec> viewTemplates = ReadArray(root, "viewTemplates")
            .Select(token => new ViewTemplateSpec(
                ReadString(token, "name"),
                ReadString(token, "viewType"),
                ReadString(token, "detailLevel"),
                ReadBool(token, "isCanonical2DExport"),
                ReadBool(token, "allowVisibilityHidingFilters"),
                ReadStringList(token, "filters"),
                ReadStringList(token, "visibleCategories"),
                ReadStringList(token, "visibleLineStylePrefixes"),
                ReadBool(token, "hideNonGisLineStyles"),
                ReadStringList(token, "hideSubcategories")))
            .ToList();

        List<QaViewFilterSpec> filters = ReadArray(root, "qaViewFilters")
            .Select(token =>
            {
                Dictionary<string, string> valueColors = new(StringComparer.Ordinal);
                if (token["valueColors"] is JObject vcObj)
                {
                    foreach (JProperty prop in vcObj.Properties())
                    {
                        valueColors[prop.Name] = (string?)prop.Value ?? string.Empty;
                    }
                }

                return new QaViewFilterSpec(
                    ReadString(token, "name"),
                    ReadString(token, "severity"),
                    ReadBool(token, "visible", defaultValue: true),
                    ReadString(token, "overrideColorHex"),
                    ReadOptionalString(token, "secondaryColorHex"),
                    ReadStringList(token, "categories"),
                    ParseFilterRules(token["rules"] as JArray),
                    new ReadOnlyDictionary<string, string>(valueColors),
                    ReadString(token, "description"));
            })
            .ToList();

        List<ScheduleSpec> schedules = ReadArray(root, "schedules")
            .Select(token => new ScheduleSpec(
                ReadString(token, "id"),
                ReadString(token, "name"),
                ReadString(token, "kind"),
                ReadString(token, "category"),
                ReadBool(token, "emptyMeansPass"),
                ReadStringList(token, "fields"),
                ParseFilterRules(token["filters"] as JArray)))
            .ToList();

        List<LineStyleSpec> lineStyles = ReadArray(root, "lineStyles")
            .Select(token => new LineStyleSpec(
                ReadString(token, "name"),
                ReadInt(token, "lineWeight"),
                ReadString(token, "colorHex"),
                ReadString(token, "linePattern"),
                ReadString(token, "purpose")))
            .ToList();

        List<SystemFamilySpec> systemFamilies = ReadArray(root, "systemFamilies")
            .Select(token => new SystemFamilySpec(
                ReadString(token, "id"),
                ReadString(token, "typeName"),
                ReadString(token, "systemKind"),
                ReadString(token, "category"),
                ReadString(token, "materialRef"),
                ReadOptionalString(token, "gisCategory"),
                ReadOptionalInt(token, "triangleBudgetPerFlight"),
                ReadOptionalDouble(token, "thicknessMm"),
                ReadOptionalDouble(token, "defaultHeightMm"),
                ReadString(token, "description")))
            .ToList();

        return new RteTemplateSpec(
            ReadString(root, "schema"),
            ReadInt(root, "schemaVersion"),
            ReadString(root, "templateName"),
            ReadInt(root, "gisStandardVersion"),
            ReadString(root, "canonicalExportViewTemplate"),
            startingView,
            hygiene,
            Array.AsReadOnly(viewTemplates.ToArray()),
            Array.AsReadOnly(filters.ToArray()),
            Array.AsReadOnly(schedules.ToArray()),
            Array.AsReadOnly(lineStyles.ToArray()),
            Array.AsReadOnly(systemFamilies.ToArray()),
            ReadBool(root, "includeAllStandardFloorTypes"),
            ReadBool(root, "includeAllStandardSharedParameters"));
    }

    public static FamilyGeometrySpec ParseFamily(string json)
    {
        JObject root = JObject.Parse(json);
        JObject clearancesObj = RequireObject(root, "clearancesMm", "family.json");
        AntiZFightingClearances clearances = new(
            RequireDouble(clearancesObj, "slabClearMm"),
            RequireDouble(clearancesObj, "sideClearMm"),
            RequireDouble(clearancesObj, "glassTuckMm"));

        JObject dimsObj = RequireObject(root, "defaultDimensionsMm", "family.json");
        FamilyDimensionsSpec dimensions = new(
            RequireDouble(dimsObj, "widthMm"),
            RequireDouble(dimsObj, "depthMm"),
            RequireDouble(dimsObj, "heightMm"));

        List<FamilySharedParameterSpec> sharedParams = ReadArray(root, "sharedParameters")
            .Select(token => new FamilySharedParameterSpec(
                ReadString(token, "name"),
                ReadString(token, "binding"),
                ReadBool(token, "required"),
                ReadString(token, "defaultValue")))
            .ToList();

        JObject budgetObj = RequireObject(root, "triangleBudget", "family.json");
        FamilyTriangleBudgetSpec triangleBudget = new(
            RequireInt(budgetObj, "coarseMax"),
            RequireInt(budgetObj, "mediumFineMax"));

        CoarseGeometrySpec? coarse = null;
        if (root["coarse"] is JObject coarseObj)
        {
            coarse = new CoarseGeometrySpec(
                ReadString(coarseObj, "subcategory"),
                ReadString(coarseObj, "materialRef"),
                ReadBool(coarseObj, "requiresDownwardPlanarBottomAtZero"),
                ParseGeometryOps(coarseObj["ops"] as JArray));
        }

        JObject mediumFineObj = RequireObject(root, "mediumFine", "family.json");
        MediumFineGeometrySpec mediumFine = new(ParseGeometryOps(mediumFineObj["ops"] as JArray));

        return new FamilyGeometrySpec(
            ReadString(root, "schema"),
            ReadInt(root, "schemaVersion"),
            ReadString(root, "id"),
            ReadString(root, "familyName"),
            ReadString(root, "displayNameEn"),
            ReadString(root, "displayNameJa"),
            ReadString(root, "group"),
            ReadString(root, "category"),
            ReadString(root, "seedKind"),
            ReadBool(root, "isPolygonal", defaultValue: true),
            ReadStringList(root, "subcategories"),
            clearances,
            dimensions,
            Array.AsReadOnly(sharedParams.ToArray()),
            triangleBudget,
            coarse,
            mediumFine);
    }

    private static void ValidateHeader(StarterKitHeaderSpec header, List<StarterKitValidationFinding> findings)
    {
        if (!string.Equals(header.Schema, StarterKitHeaderSpec.ExpectedSchema, StringComparison.Ordinal))
        {
            AddError(findings, "SK-KIT-001", KitFileName, $"Expected schema '{StarterKitHeaderSpec.ExpectedSchema}', found '{header.Schema}'.");
        }

        if (header.SchemaVersion != SupportedSchemaVersion)
        {
            AddError(findings, "SK-KIT-002", KitFileName, $"Unsupported kit schemaVersion '{header.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        if (!SemVerRegex.IsMatch(header.KitVersion))
        {
            AddError(findings, "SK-KIT-003", KitFileName, $"kitVersion '{header.KitVersion}' must be valid semver (X.Y.Z).");
        }

        if (header.GisStandardVersion != GisStandard.Version)
        {
            AddError(findings, "SK-KIT-004", KitFileName, $"gisStandardVersion must be {GisStandard.Version}, found {header.GisStandardVersion}.");
        }

        if (!header.SupportedRevitVersions.Contains("2024", StringComparer.Ordinal) ||
            !header.SupportedRevitVersions.Contains("2027", StringComparer.Ordinal))
        {
            AddError(findings, "SK-KIT-005", KitFileName, "supportedRevitVersions must include both '2024' and '2027'.");
        }

        if (header.Budgets.MaxRfaFileSizeKb <= 0 ||
            header.Budgets.MaxRteFileSizeMb <= 0 ||
            header.Budgets.MaxCoarseTrianglesPerFamily <= 0 ||
            header.Budgets.MaxMediumTrianglesPerFamily <= 0 ||
            header.Budgets.MaxStairFlightTriangles <= 0)
        {
            AddError(findings, "SK-KIT-006", KitFileName, "All file size and triangle budgets in kit.json must be positive.");
        }

        ValidateClearances(header.Budgets.Clearances, KitFileName, findings);

        if (header.Families.Count == 0)
        {
            AddError(findings, "SK-KIT-007", KitFileName, "kit.json must register at least one family.");
        }

        HashSet<string> seenIds = new(StringComparer.Ordinal);
        foreach (StarterKitFamilyRegistryEntry entry in header.Families)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !seenIds.Add(entry.Id))
            {
                AddError(findings, "SK-KIT-008", KitFileName, $"Duplicate or blank family id '{entry.Id}' in kit.json.");
            }
        }
    }

    private static HashSet<string> ValidateMaterials(Materials3DSpec materialsSpec, List<StarterKitValidationFinding> findings)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        if (!string.Equals(materialsSpec.Schema, Materials3DSpec.ExpectedSchema, StringComparison.Ordinal))
        {
            AddError(findings, "SK-MAT-001", Materials3DFileName, $"Expected schema '{Materials3DSpec.ExpectedSchema}', found '{materialsSpec.Schema}'.");
        }

        if (materialsSpec.SchemaVersion != SupportedSchemaVersion)
        {
            AddError(findings, "SK-MAT-013", Materials3DFileName, $"Unsupported materials-3d schemaVersion '{materialsSpec.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        if (materialsSpec.Materials.Count == 0)
        {
            AddError(findings, "SK-MAT-002", Materials3DFileName, "materials-3d.json must define at least one material.");
            return names;
        }

        HashSet<string> roles = new(StringComparer.OrdinalIgnoreCase);
        foreach (Material3DSpec mat in materialsSpec.Materials)
        {
            string path = $"{Materials3DFileName}#{mat.Name}";
            if (string.IsNullOrWhiteSpace(mat.Name))
            {
                AddError(findings, "SK-MAT-003", path, "Material name cannot be empty.");
                continue;
            }

            if (!names.Add(mat.Name))
            {
                AddError(findings, "SK-MAT-004", path, $"Duplicate material name '{mat.Name}'.");
            }

            if (!mat.Name.StartsWith("K3D_", StringComparison.Ordinal) && !mat.Name.StartsWith("M3D_", StringComparison.Ordinal))
            {
                AddError(findings, "SK-MAT-005", path, $"3D material '{mat.Name}' must start with 'K3D_' or 'M3D_' so GIS_* 2D materials remain frozen.");
            }

            if (mat.Srgb.Count != 3 || mat.Srgb.Any(c => c < 0 || c > 255))
            {
                AddError(findings, "SK-MAT-006", path, $"Material '{mat.Name}' must specify 3 sRGB bytes in [0..255].");
            }
            else
            {
                string expectedHex = string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:X2}{1:X2}{2:X2}",
                    mat.Srgb[0],
                    mat.Srgb[1],
                    mat.Srgb[2]);
                if (!string.Equals(mat.Hex, expectedHex, StringComparison.OrdinalIgnoreCase))
                {
                    AddError(findings, "SK-MAT-007", path, $"Material '{mat.Name}' hex '{mat.Hex}' does not match sRGB '{expectedHex}'.");
                }
            }

            if (mat.Transparency < 0 || mat.Transparency > 100)
            {
                AddError(findings, "SK-MAT-008", path, $"Material '{mat.Name}' transparency {mat.Transparency} must be in [0..100].");
            }

            if (!IsFinite(mat.Metallic) || mat.Metallic < 0.0 || mat.Metallic > 1.0 ||
                !IsFinite(mat.Roughness) || mat.Roughness < 0.0 || mat.Roughness > 1.0)
            {
                AddError(findings, "SK-MAT-009", path, $"Material '{mat.Name}' metallic and roughness must be finite and in [0..1].");
            }

            if (!ValidAlphaModes.Contains(mat.AlphaMode, StringComparer.Ordinal))
            {
                AddError(findings, "SK-MAT-011", path, $"Material '{mat.Name}' alphaMode '{mat.AlphaMode}' must be one of: {string.Join(", ", ValidAlphaModes)}.");
            }
            else if (string.Equals(mat.AlphaMode, "opaque", StringComparison.Ordinal) && mat.Transparency != 0)
            {
                AddError(findings, "SK-MAT-012", path, $"Material '{mat.Name}' has alphaMode 'opaque' but non-zero transparency {mat.Transparency}.");
            }
            else if (string.Equals(mat.AlphaMode, "blend", StringComparison.Ordinal) && mat.Transparency <= 0)
            {
                AddError(findings, "SK-MAT-012", path, $"Material '{mat.Name}' has alphaMode 'blend' but transparency {mat.Transparency} <= 0.");
            }

            if (!string.IsNullOrWhiteSpace(mat.Role))
            {
                roles.Add(mat.Role);
            }
        }

        foreach (string requiredRole in RequiredMaterialRoles)
        {
            if (!roles.Contains(requiredRole))
            {
                AddError(findings, "SK-MAT-010", Materials3DFileName, $"Missing required 3D material role '{requiredRole}'.");
            }
        }

        return names;
    }

    private static void ValidateRte(
        RteTemplateSpec rte,
        HashSet<string> valid3DMaterials,
        StarterKitReferenceStandards standards,
        List<StarterKitValidationFinding> findings)
    {
        if (!string.Equals(rte.Schema, RteTemplateSpec.ExpectedSchema, StringComparison.Ordinal))
        {
            AddError(findings, "SK-RTE-001", RteFileName, $"Expected schema '{RteTemplateSpec.ExpectedSchema}', found '{rte.Schema}'.");
        }

        if (rte.SchemaVersion != SupportedSchemaVersion)
        {
            AddError(findings, "SK-RTE-008", RteFileName, $"Unsupported rte schemaVersion '{rte.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        if (rte.GisStandardVersion != GisStandard.Version)
        {
            AddError(findings, "SK-RTE-002", RteFileName, $"rte.json gisStandardVersion must be {GisStandard.Version}, found {rte.GisStandardVersion}.");
        }

        // Canonical 2D export view template contract
        if (!string.Equals(rte.CanonicalExportViewTemplate, CanonicalExportTemplateName, StringComparison.Ordinal))
        {
            AddError(findings, "SK-RTE-003", RteFileName, $"canonicalExportViewTemplate must be '{CanonicalExportTemplateName}', found '{rte.CanonicalExportViewTemplate}'.");
        }

        // Zero-identity & zero-worksets assertions
        if (!rte.Hygiene.RequireZeroProjectIdentity)
        {
            AddError(findings, "SK-RTE-004", RteFileName, "rte.json hygiene.requireZeroProjectIdentity must be true.");
        }

        if (!rte.Hygiene.RequireZeroWorksets)
        {
            AddError(findings, "SK-RTE-005", RteFileName, "rte.json hygiene.requireZeroWorksets must be true.");
        }

        if (!rte.Hygiene.ForbiddenScheduleCategories.Contains(ForbiddenScheduleCategory, StringComparer.Ordinal))
        {
            AddError(findings, "SK-RTE-006", RteFileName, $"rte.json hygiene.forbiddenScheduleCategories must list '{ForbiddenScheduleCategory}'.");
        }

        // Starting view
        if (!string.Equals(rte.StartingView.Name, "00_Start_Here", StringComparison.Ordinal) ||
            !string.Equals(rte.StartingView.ViewType, "DraftingView", StringComparison.Ordinal) ||
            !rte.StartingView.Bilingual ||
            !rte.StartingView.Languages.Contains("en", StringComparer.OrdinalIgnoreCase) ||
            !rte.StartingView.Languages.Contains("ja", StringComparer.OrdinalIgnoreCase))
        {
            AddError(findings, "SK-RTE-007", RteFileName, "startingView must be the bilingual EN/JA DraftingView '00_Start_Here'.");
        }

        // QA view filters (8 traffic-light filters, all visible: true, includes GateSide check)
        Dictionary<string, QaViewFilterSpec> filterByName = new(StringComparer.Ordinal);
        foreach (QaViewFilterSpec filter in rte.QaViewFilters)
        {
            string path = $"{RteFileName}#qaViewFilters/{filter.Name}";
            if (string.IsNullOrWhiteSpace(filter.Name) || filterByName.ContainsKey(filter.Name))
            {
                AddError(findings, "SK-RTE-010", path, $"Duplicate or empty QA view filter name '{filter.Name}'.");
                continue;
            }

            filterByName[filter.Name] = filter;
            if (!filter.Visible)
            {
                AddError(findings, "SK-RTE-011", path, $"QA view filter '{filter.Name}' must have visible=true (never hide geometry).");
            }

            if (!HexColorRegex.IsMatch(filter.OverrideColorHex))
            {
                AddError(findings, "SK-RTE-012", path, $"QA view filter '{filter.Name}' has invalid hex color '{filter.OverrideColorHex}'.");
            }

            if (filter.Categories.Count == 0)
            {
                AddError(findings, "SK-RTE-013", path, $"QA view filter '{filter.Name}' must target at least one Revit category.");
            }

            foreach (FilterRuleSpec rule in filter.Rules)
            {
                ValidateFilterRule(rule, filter.Categories, standards, path, findings);
            }
        }

        if (rte.QaViewFilters.Count < 8)
        {
            AddError(findings, "SK-RTE-014", RteFileName, $"rte.json must define at least 8 traffic-light QA view filters, found {rte.QaViewFilters.Count}.");
        }

        if (!filterByName.TryGetValue("KNZ_QA_INFO_GateSide_Check", out QaViewFilterSpec? gateSideFilter) ||
            gateSideFilter == null ||
            !gateSideFilter.ValueColors.ContainsKey("rachi_nai") ||
            !gateSideFilter.ValueColors.ContainsKey("rachi_gai"))
        {
            AddError(findings, "SK-RTE-015", RteFileName, "rte.json must include 'KNZ_QA_INFO_GateSide_Check' contrasting rachi_nai and rachi_gai.");
        }

        // View templates
        Dictionary<string, ViewTemplateSpec> templatesByName = new(StringComparer.Ordinal);
        foreach (ViewTemplateSpec vt in rte.ViewTemplates)
        {
            string path = $"{RteFileName}#viewTemplates/{vt.Name}";
            if (string.Equals(vt.Name, LegacyExportTemplateName, StringComparison.OrdinalIgnoreCase))
            {
                AddError(findings, "SK-RTE-020", path, $"Forbidden legacy 2D export view template name '{LegacyExportTemplateName}'. Use '{CanonicalExportTemplateName}'.");
            }

            if (string.IsNullOrWhiteSpace(vt.Name) || templatesByName.ContainsKey(vt.Name))
            {
                AddError(findings, "SK-RTE-021", path, $"Duplicate or empty view template name '{vt.Name}'.");
                continue;
            }

            templatesByName[vt.Name] = vt;
            foreach (string filterName in vt.Filters)
            {
                if (!filterByName.ContainsKey(filterName))
                {
                    AddError(findings, "SK-RTE-022", path, $"View template '{vt.Name}' references unknown QA filter '{filterName}'.");
                }
            }
        }

        foreach (string requiredTemplate in RequiredViewTemplateNames)
        {
            if (!templatesByName.ContainsKey(requiredTemplate))
            {
                AddError(findings, "SK-RTE-023", RteFileName, $"Missing required view template '{requiredTemplate}'.");
            }
        }

        if (templatesByName.TryGetValue(CanonicalExportTemplateName, out ViewTemplateSpec? gisExport) && gisExport != null)
        {
            if (!gisExport.IsCanonical2DExport || !string.Equals(gisExport.DetailLevel, "Coarse", StringComparison.Ordinal))
            {
                AddError(findings, "SK-RTE-024", RteFileName, "'GIS Export' view template must be canonical 2D export at Coarse detail level.");
            }

            if (gisExport.AllowVisibilityHidingFilters || gisExport.Filters.Count > 0)
            {
                AddError(findings, "SK-RTE-025", RteFileName, "'GIS Export' view template must allow zero visibility-hiding filters and attach zero filters (KZ-VIEW-004).");
            }
        }

        if (templatesByName.TryGetValue("Kanizu 3D Export", out ViewTemplateSpec? export3d) && export3d != null)
        {
            if (!string.Equals(export3d.DetailLevel, "Medium", StringComparison.Ordinal))
            {
                AddError(findings, "SK-RTE-026", RteFileName, "'Kanizu 3D Export' view template must use Medium detail level.");
            }
        }

        // Schedules: strictly ZERO OST_Levels schedules!
        HashSet<string> scheduleNames = new(StringComparer.Ordinal);
        bool hasFilteredCirculation = false;
        int exceptionScheduleCount = 0;
        foreach (ScheduleSpec schedule in rte.Schedules)
        {
            string path = $"{RteFileName}#schedules/{schedule.Name}";
            if (string.Equals(schedule.Category, ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase))
            {
                AddError(findings, "SK-RTE-030", path, $"Schedule '{schedule.Name}' uses forbidden category '{ForbiddenScheduleCategory}' (unschedulable in Revit API).");
            }

            if (string.IsNullOrWhiteSpace(schedule.Name) || !scheduleNames.Add(schedule.Name))
            {
                AddError(findings, "SK-RTE-031", path, $"Duplicate or empty schedule name '{schedule.Name}'.");
            }

            if (schedule.Fields.Count == 0)
            {
                AddError(findings, "SK-RTE-032", path, $"Schedule '{schedule.Name}' must define at least one field.");
            }

            foreach (string fieldName in schedule.Fields)
            {
                if (!standards.ParametersByName.TryGetValue(fieldName, out GisParameterDefinition? paramDef) || paramDef == null)
                {
                    AddError(findings, "SK-RTE-033", path, $"Schedule '{schedule.Name}' references unknown shared parameter '{fieldName}'.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(schedule.Category) &&
                    !paramDef.Categories.Contains(schedule.Category, StringComparer.Ordinal))
                {
                    AddError(findings, "SK-RTE-034", path, $"Schedule '{schedule.Name}' ({schedule.Category}) references parameter '{fieldName}' which is not bound to {schedule.Category}.");
                }
            }

            IReadOnlyList<string> ruleCategories = string.IsNullOrWhiteSpace(schedule.Category)
                ? Array.Empty<string>()
                : new[] { schedule.Category };
            foreach (FilterRuleSpec rule in schedule.Filters)
            {
                ValidateFilterRule(rule, ruleCategories, standards, path, findings);
            }

            if (string.Equals(schedule.Id, "qa-schedule-circulation", StringComparison.Ordinal) &&
                schedule.Filters.Any(f => string.Equals(f.Parameter, GisSharedParameters.GisKind, StringComparison.Ordinal) &&
                                          string.Equals(f.Operator, "hasValue", StringComparison.Ordinal)))
            {
                hasFilteredCirculation = true;
            }

            if (schedule.EmptyMeansPass && string.Equals(schedule.Kind, "exception", StringComparison.Ordinal))
            {
                exceptionScheduleCount++;
            }
        }

        if (!hasFilteredCirculation)
        {
            AddError(findings, "SK-RTE-035", RteFileName, "Circulation schedule ('qa-schedule-circulation') must filter on gis_kind hasValue.");
        }

        if (exceptionScheduleCount < 3)
        {
            AddError(findings, "SK-RTE-036", RteFileName, $"rte.json must define at least 3 empty-means-pass exception schedules, found {exceptionScheduleCount}.");
        }

        // Line styles
        HashSet<string> lineStyleNames = new(StringComparer.Ordinal);
        foreach (LineStyleSpec ls in rte.LineStyles)
        {
            string path = $"{RteFileName}#lineStyles/{ls.Name}";
            if (!ls.Name.StartsWith("GIS_", StringComparison.Ordinal))
            {
                AddError(findings, "SK-RTE-040", path, $"Line style '{ls.Name}' must start with 'GIS_'.");
            }

            if (!lineStyleNames.Add(ls.Name))
            {
                AddError(findings, "SK-RTE-041", path, $"Duplicate line style '{ls.Name}'.");
            }

            if (ls.LineWeight < 1 || ls.LineWeight > 16)
            {
                AddError(findings, "SK-RTE-042", path, $"Line style '{ls.Name}' weight {ls.LineWeight} must be in [1..16].");
            }

            if (!HexColorRegex.IsMatch(ls.ColorHex))
            {
                AddError(findings, "SK-RTE-043", path, $"Line style '{ls.Name}' has invalid hex color '{ls.ColorHex}'.");
            }
        }

        foreach (string requiredLineStyle in RequiredLineStyleNames)
        {
            if (!lineStyleNames.Contains(requiredLineStyle))
            {
                AddError(findings, "SK-RTE-044", RteFileName, $"Missing required line style '{requiredLineStyle}'.");
            }
        }

        // System families
        HashSet<string> systemFamilyIds = new(StringComparer.Ordinal);
        foreach (SystemFamilySpec sf in rte.SystemFamilies)
        {
            string path = $"{RteFileName}#systemFamilies/{sf.Id}";
            if (!systemFamilyIds.Add(sf.Id))
            {
                AddError(findings, "SK-RTE-050", path, $"Duplicate system family id '{sf.Id}'.");
            }

            bool materialExists = valid3DMaterials.Contains(sf.MaterialRef) || standards.FloorMaterialNames.Contains(sf.MaterialRef);
            if (!materialExists)
            {
                AddError(findings, "SK-RTE-051", path, $"System family '{sf.Id}' references unknown material '{sf.MaterialRef}'.");
            }

            if (!string.IsNullOrWhiteSpace(sf.GisCategory) && !standards.UnitCategories.Contains(sf.GisCategory!))
            {
                AddError(findings, "SK-RTE-052", path, $"System family '{sf.Id}' has invalid gisCategory '{sf.GisCategory}'.");
            }

            if (sf.ThicknessMm.HasValue && (!IsFinite(sf.ThicknessMm.Value) || sf.ThicknessMm.Value <= 0.0))
            {
                AddError(findings, "SK-RTE-054", path, $"System family '{sf.Id}' thicknessMm must be finite and positive.");
            }

            if (sf.DefaultHeightMm.HasValue && (!IsFinite(sf.DefaultHeightMm.Value) || sf.DefaultHeightMm.Value <= 0.0))
            {
                AddError(findings, "SK-RTE-054", path, $"System family '{sf.Id}' defaultHeightMm must be finite and positive.");
            }

            if (sf.TriangleBudgetPerFlight.HasValue && sf.TriangleBudgetPerFlight.Value <= 0)
            {
                AddError(findings, "SK-RTE-054", path, $"System family '{sf.Id}' triangleBudgetPerFlight must be positive.");
            }
        }

        foreach (string requiredSf in RequiredSystemFamilyIds)
        {
            if (!systemFamilyIds.Contains(requiredSf))
            {
                AddError(findings, "SK-RTE-053", RteFileName, $"Missing required pre-loaded system family '{requiredSf}'.");
            }
        }
    }

    private static void ValidateFamilies(
        StarterKitSpec spec,
        HashSet<string> valid3DMaterials,
        StarterKitReferenceStandards standards,
        List<StarterKitValidationFinding> findings)
    {
        Dictionary<string, StarterKitFamilyRegistryEntry> registryById = new(StringComparer.Ordinal);
        foreach (StarterKitFamilyRegistryEntry entry in spec.Header.Families)
        {
            if (!registryById.ContainsKey(entry.Id))
            {
                registryById[entry.Id] = entry;
            }
        }

        HashSet<string> loadedIds = new(StringComparer.Ordinal);
        foreach (FamilyGeometrySpec family in spec.Families)
        {
            string path = $"families/{family.Id}.family.json";
            if (!loadedIds.Add(family.Id))
            {
                AddError(findings, "SK-FAM-001", path, $"Duplicate loaded family spec id '{family.Id}'.");
            }

            if (!string.Equals(family.Schema, FamilyGeometrySpec.ExpectedSchema, StringComparison.Ordinal))
            {
                AddError(findings, "SK-FAM-002", path, $"Expected schema '{FamilyGeometrySpec.ExpectedSchema}', found '{family.Schema}'.");
            }

            if (family.SchemaVersion != SupportedSchemaVersion)
            {
                AddError(findings, "SK-FAM-014", path, $"Unsupported family schemaVersion '{family.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
            }

            if (registryById.TryGetValue(family.Id, out StarterKitFamilyRegistryEntry? regEntry) && regEntry != null)
            {
                if (!string.Equals(family.Group, regEntry.Group, StringComparison.Ordinal))
                {
                    AddError(findings, "SK-FAM-003", path, $"Family '{family.Id}' group '{family.Group}' does not match kit.json group '{regEntry.Group}'.");
                }
            }
            else
            {
                AddError(findings, "SK-FAM-004", path, $"Family '{family.Id}' is not registered in kit.json.");
            }

            if (!family.Category.StartsWith("OST_", StringComparison.Ordinal))
            {
                AddError(findings, "SK-FAM-005", path, $"Family '{family.Id}' category '{family.Category}' must be a Revit BuiltInCategory (OST_*).");
            }

            if (!string.Equals(family.SeedKind, FamilyGeometrySpec.SeedProcedural, StringComparison.Ordinal) &&
                !string.Equals(family.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal))
            {
                AddError(findings, "SK-FAM-006", path, $"Family '{family.Id}' has invalid seedKind '{family.SeedKind}'.");
            }

            if (string.Equals(family.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal) &&
                !string.Equals(family.Category, "OST_Doors", StringComparison.Ordinal))
            {
                AddError(findings, "SK-FAM-007", path, $"Family '{family.Id}' uses seedKind 'wallHostedDoor' but category is '{family.Category}' (expected 'OST_Doors').");
            }

            if (!IsFinite(family.DefaultDimensionsMm.WidthMm) || family.DefaultDimensionsMm.WidthMm <= 0 ||
                !IsFinite(family.DefaultDimensionsMm.DepthMm) || family.DefaultDimensionsMm.DepthMm <= 0 ||
                !IsFinite(family.DefaultDimensionsMm.HeightMm) || family.DefaultDimensionsMm.HeightMm <= 0)
            {
                AddError(findings, "SK-FAM-008", path, $"Family '{family.Id}' defaultDimensionsMm must all be finite and positive.");
            }

            ValidateClearances(family.ClearancesMm, path, findings);
            ValidateFamilySharedParameters(family, standards, path, findings);
            ValidateFamilyGeometry(family, spec.Header.Budgets, valid3DMaterials, path, findings);
        }

        foreach (StarterKitFamilyRegistryEntry entry in spec.Header.Families)
        {
            if (!entry.Optional && !loadedIds.Contains(entry.Id))
            {
                AddError(findings, "SK-FAM-009", entry.SpecPath, $"Required registered family '{entry.Id}' was not loaded.");
            }
        }
    }

    private static void ValidateFamilySharedParameters(
        FamilyGeometrySpec family,
        StarterKitReferenceStandards standards,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (FamilySharedParameterSpec param in family.SharedParameters)
        {
            if (!seen.Add(param.Name))
            {
                AddError(findings, "SK-FAM-010", path, $"Family '{family.Id}' lists shared parameter '{param.Name}' twice.");
                continue;
            }

            if (!standards.ParametersByName.TryGetValue(param.Name, out GisParameterDefinition? def) || def == null)
            {
                AddError(findings, "SK-FAM-011", path, $"Family '{family.Id}' references unknown shared parameter '{param.Name}'.");
                continue;
            }

            string expectedBinding = def.Binding == GisParameterBinding.Type ? "type" : "instance";
            if (!string.Equals(param.Binding, expectedBinding, StringComparison.OrdinalIgnoreCase))
            {
                AddError(findings, "SK-FAM-012", path, $"Family '{family.Id}' parameter '{param.Name}' binding '{param.Binding}' does not match standard binding '{expectedBinding}'.");
            }

            if (!def.Categories.Contains(family.Category, StringComparer.Ordinal))
            {
                AddError(findings, "SK-FAM-013", path, $"Family '{family.Id}' ({family.Category}) declares parameter '{param.Name}' which is not bound to {family.Category} in shared-parameter-bindings.json.");
            }

            if (!string.IsNullOrWhiteSpace(param.DefaultValue))
            {
                ValidateDomainValue(param.Name, param.DefaultValue, standards, path, findings);

                if (string.Equals(param.Name, GisSharedParameters.GisKind, StringComparison.Ordinal) &&
                    standards.FamiliesByGisKind.TryGetValue(param.DefaultValue, out IReadOnlySetLike? registeredFamilies) &&
                    registeredFamilies != null &&
                    !registeredFamilies.Contains(family.FamilyName))
                {
                    AddError(
                        findings,
                        "SK-FAM-015",
                        path,
                        $"Family '{family.FamilyName}' declares gis_kind '{param.DefaultValue}' but is not registered under '{param.DefaultValue}' in standards/families.json.");
                }
            }
        }
    }

    private static void ValidateDomainValue(
        string paramName,
        string value,
        StarterKitReferenceStandards standards,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        if (string.Equals(paramName, GisSharedParameters.GisCategory, StringComparison.Ordinal) ||
            string.Equals(paramName, GisSharedParameters.Category, StringComparison.Ordinal))
        {
            if (!standards.UnitCategories.Contains(value))
            {
                AddError(findings, "SK-DOM-001", path, $"Parameter '{paramName}' defaultValue '{value}' is not in standards/fields.json unit.category.");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisRestriction, StringComparison.Ordinal))
        {
            if (!standards.Restrictions.Contains(value))
            {
                AddError(findings, "SK-DOM-002", path, $"Parameter '{paramName}' defaultValue '{value}' is not in standards/fields.json unit.restrict.");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisKind, StringComparison.Ordinal))
        {
            if (!standards.GisKinds.Contains(value))
            {
                AddError(findings, "SK-DOM-003", path, $"Parameter '{paramName}' defaultValue '{value}' is not in standards/families.json kinds.");
            }

            return;
        }

        if (string.Equals(paramName, GisSharedParameters.GisDirection, StringComparison.Ordinal))
        {
            if (!standards.Directions.Contains(value))
            {
                AddError(findings, "SK-DOM-004", path, $"Parameter '{paramName}' defaultValue '{value}' is not in standards/fields.json unit.direction.");
            }

            return;
        }

        if (standards.OpeningDomains.TryGetValue(paramName, out IReadOnlyList<string>? allowed) && allowed != null)
        {
            if (!allowed.Contains(value, StringComparer.Ordinal))
            {
                AddError(findings, "SK-DOM-005", path, $"Parameter '{paramName}' defaultValue '{value}' is not in standards/opening-types.json {paramName}.");
            }
        }
    }

    private static void ValidateFamilyGeometry(
        FamilyGeometrySpec family,
        StarterKitBudgets kitBudgets,
        HashSet<string> valid3DMaterials,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        HashSet<string> declaredSubcategories = new(family.Subcategories, StringComparer.Ordinal);
        if (family.IsPolygonal)
        {
            if (family.Coarse == null || family.Coarse.Ops.Count == 0)
            {
                AddError(findings, "SK-GEO-001", path, $"Polygonal family '{family.Id}' must define Coarse K_Footprint geometry.");
            }
            else
            {
                if (!string.Equals(family.Coarse.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
                {
                    AddError(findings, "SK-GEO-002", path, $"Polygonal family '{family.Id}' coarse subcategory must be '{CoarseGeometrySpec.FootprintSubcategory}'.");
                }

                if (!family.Coarse.RequiresDownwardPlanarBottomAtZero)
                {
                    AddError(findings, "SK-GEO-003", path, $"Polygonal family '{family.Id}' must require a downward bottom PlanarFace at Z=0 (normal.Z < -0.9).");
                }

                if (!declaredSubcategories.Contains(CoarseGeometrySpec.FootprintSubcategory))
                {
                    AddError(findings, "SK-GEO-004", path, $"Polygonal family '{family.Id}' subcategories list must include '{CoarseGeometrySpec.FootprintSubcategory}'.");
                }
            }
        }

        if (family.Coarse != null)
        {
            if (!valid3DMaterials.Contains(family.Coarse.MaterialRef))
            {
                AddError(findings, "SK-GEO-005", path, $"Family '{family.Id}' coarse materialRef '{family.Coarse.MaterialRef}' is not in materials-3d.json.");
            }

            ValidateOpTree(
                family.Coarse.Ops,
                declaredSubcategories,
                valid3DMaterials,
                isCoarse: true,
                slabClearMm: 0.0,
                requireBottomAtZero: family.Coarse.RequiresDownwardPlanarBottomAtZero,
                path,
                findings);
        }

        if (family.MediumFine.Ops.Count == 0)
        {
            AddError(findings, "SK-GEO-006", path, $"Family '{family.Id}' must define at least one Medium/Fine geometry op.");
        }
        else
        {
            ValidateOpTree(
                family.MediumFine.Ops,
                declaredSubcategories,
                valid3DMaterials,
                isCoarse: false,
                slabClearMm: family.ClearancesMm.SlabClearMm,
                requireBottomAtZero: false,
                path,
                findings);
        }

        // Triangle estimation & budget enforcement
        try
        {
            FamilyTriangleEstimate estimate = TriangleEstimator.EstimateFamily(family);
            if (!estimate.CoarseWithinBudget)
            {
                AddError(findings, "SK-TRI-001", path, $"Family '{family.Id}' coarse triangles ({estimate.CoarseTriangles}) exceed family budget ({estimate.CoarseBudget}).");
            }

            if (!estimate.MediumFineWithinBudget)
            {
                AddError(findings, "SK-TRI-002", path, $"Family '{family.Id}' mediumFine triangles ({estimate.MediumFineTriangles}) exceed family budget ({estimate.MediumFineBudget}).");
            }

            if (estimate.CoarseTriangles > kitBudgets.MaxCoarseTrianglesPerFamily)
            {
                AddError(findings, "SK-TRI-003", path, $"Family '{family.Id}' coarse triangles ({estimate.CoarseTriangles}) exceed kit budget ({kitBudgets.MaxCoarseTrianglesPerFamily}).");
            }

            if (estimate.MediumFineTriangles > kitBudgets.MaxMediumTrianglesPerFamily)
            {
                AddError(findings, "SK-TRI-004", path, $"Family '{family.Id}' mediumFine triangles ({estimate.MediumFineTriangles}) exceed kit budget ({kitBudgets.MaxMediumTrianglesPerFamily}).");
            }
        }
        catch (Exception ex)
        {
            AddError(findings, "SK-TRI-005", path, $"Family '{family.Id}' failed triangle estimation: {ex.Message}");
        }
    }

    private static void ValidateOpTree(
        IReadOnlyList<GeometryOpSpec> ops,
        HashSet<string> declaredSubcategories,
        HashSet<string> valid3DMaterials,
        bool isCoarse,
        double slabClearMm,
        bool requireBottomAtZero,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        HashSet<string> solidNames = new(StringComparer.Ordinal);
        CollectSolidOpNames(ops, solidNames, depth: 0);

        bool foundBottomAtZero = false;
        foreach (GeometryOpSpec op in ops)
        {
            ValidateSingleOp(
                op,
                declaredSubcategories,
                valid3DMaterials,
                solidNames,
                isCoarse,
                slabClearMm,
                ref foundBottomAtZero,
                path,
                findings,
                depth: 0);
        }

        if (requireBottomAtZero && !foundBottomAtZero)
        {
            AddError(findings, "SK-GEO-010", path, "Coarse K_Footprint geometry must place at least one planar solid bottom at Z = 0.");
        }
    }

    private static void CollectSolidOpNames(IReadOnlyList<GeometryOpSpec> ops, HashSet<string> names, int depth)
    {
        if (depth > TriangleEstimator.MaxNestingDepth)
        {
            return;
        }

        foreach (GeometryOpSpec op in ops)
        {
            if (!string.Equals(op.Op, GeometryOpSpec.OpVoid, StringComparison.Ordinal) &&
                !string.Equals(op.Op, GeometryOpSpec.OpArray, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(op.Name))
            {
                names.Add(op.Name);
            }

            if (string.Equals(op.Op, GeometryOpSpec.OpArray, StringComparison.Ordinal))
            {
                CollectSolidOpNames(TriangleEstimator.CollectArrayChildren(op), names, depth + 1);
            }
        }
    }

    private static void ValidateSingleOp(
        GeometryOpSpec op,
        HashSet<string> declaredSubcategories,
        HashSet<string> valid3DMaterials,
        HashSet<string> solidNames,
        bool isCoarse,
        double slabClearMm,
        ref bool foundBottomAtZero,
        string path,
        List<StarterKitValidationFinding> findings,
        int depth)
    {
        if (depth > TriangleEstimator.MaxNestingDepth)
        {
            AddError(findings, "SK-GEO-011", path, $"Op '{op.Name}' exceeds maximum nesting depth.");
            return;
        }

        if (string.Equals(op.Op, GeometryOpSpec.OpArray, StringComparison.Ordinal))
        {
            if (!op.Count.HasValue || op.Count.Value < 1)
            {
                AddError(findings, "SK-GEO-012", path, $"Array op '{op.Name}' requires count >= 1.");
            }

            if (!IsFiniteVector3(op.StepMm))
            {
                AddError(findings, "SK-GEO-023", path, $"Array op '{op.Name}' requires a finite 3-element stepMm vector.");
            }

            List<GeometryOpSpec> children = TriangleEstimator.CollectArrayChildren(op);
            if (children.Count == 0)
            {
                AddError(findings, "SK-GEO-013", path, $"Array op '{op.Name}' must define child or children.");
                return;
            }

            foreach (GeometryOpSpec child in children)
            {
                ValidateSingleOp(child, declaredSubcategories, valid3DMaterials, solidNames, isCoarse, slabClearMm, ref foundBottomAtZero, path, findings, depth + 1);
            }

            return;
        }

        if (string.Equals(op.Op, GeometryOpSpec.OpVoid, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(op.TargetOp) && !solidNames.Contains(op.TargetOp!))
            {
                AddError(findings, "SK-GEO-014", path, $"Void op '{op.Name}' references unknown targetOp '{op.TargetOp}'.");
            }

            string voidShape = string.IsNullOrWhiteSpace(op.Shape) ? GeometryOpSpec.OpBox : op.Shape!.Trim();
            if (string.Equals(voidShape, GeometryOpSpec.OpBox, StringComparison.Ordinal))
            {
                if (!IsFiniteVector3(op.OriginMm))
                {
                    AddError(findings, "SK-GEO-021", path, $"Box void op '{op.Name}' requires a finite 3-element originMm vector.");
                }

                if (!IsPositiveFiniteVector3(op.SizeMm))
                {
                    AddError(findings, "SK-GEO-022", path, $"Box void op '{op.Name}' requires a positive finite 3-element sizeMm vector.");
                }
            }
            else if (string.Equals(voidShape, GeometryOpSpec.OpNPrism, StringComparison.Ordinal))
            {
                if (!IsFiniteVector3(op.CenterMm))
                {
                    AddError(findings, "SK-GEO-021", path, $"nprism void op '{op.Name}' requires a finite 3-element centerMm vector.");
                }
            }

            return;
        }

        if (!string.Equals(op.Op, GeometryOpSpec.OpBox, StringComparison.Ordinal) &&
            !string.Equals(op.Op, GeometryOpSpec.OpNPrism, StringComparison.Ordinal) &&
            !string.Equals(op.Op, GeometryOpSpec.OpSlopedPrism, StringComparison.Ordinal))
        {
            AddError(findings, "SK-GEO-015", path, $"Unknown geometry op kind '{op.Op}' on op '{op.Name}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(op.Subcategory) || !declaredSubcategories.Contains(op.Subcategory!))
        {
            AddError(findings, "SK-GEO-016", path, $"Solid op '{op.Name}' subcategory '{op.Subcategory}' must be declared in family subcategories.");
        }
        else if (isCoarse && !string.Equals(op.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
        {
            AddError(findings, "SK-GEO-017", path, $"Coarse solid op '{op.Name}' must use subcategory '{CoarseGeometrySpec.FootprintSubcategory}'.");
        }
        else if (!isCoarse && string.Equals(op.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
        {
            AddError(findings, "SK-GEO-018", path, $"Medium/Fine solid op '{op.Name}' must not use Coarse-only subcategory '{CoarseGeometrySpec.FootprintSubcategory}'.");
        }

        if (string.IsNullOrWhiteSpace(op.MaterialRef) || !valid3DMaterials.Contains(op.MaterialRef!))
        {
            AddError(findings, "SK-GEO-019", path, $"Solid op '{op.Name}' references unknown materialRef '{op.MaterialRef}'.");
        }

        double? zBottom = ValidateSolidPlacementAndGetBottomZ(op, path, findings);
        if (zBottom.HasValue)
        {
            if (isCoarse && Math.Abs(zBottom.Value) <= 1e-6)
            {
                foundBottomAtZero = true;
            }
            else if (!isCoarse && (!IsFinite(slabClearMm) || zBottom.Value + 1e-6 < slabClearMm))
            {
                AddError(findings, "SK-GEO-020", path, $"Medium/Fine solid op '{op.Name}' bottom Z ({zBottom.Value} mm) violates slab anti-Z-fighting clearance ({slabClearMm} mm).");
            }
        }
    }

    private static double? ValidateSolidPlacementAndGetBottomZ(
        GeometryOpSpec op,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        if (string.Equals(op.Op, GeometryOpSpec.OpBox, StringComparison.Ordinal))
        {
            if (!IsFiniteVector3(op.OriginMm))
            {
                AddError(findings, "SK-GEO-021", path, $"Box op '{op.Name}' requires a finite 3-element originMm vector.");
                return null;
            }

            if (!IsPositiveFiniteVector3(op.SizeMm))
            {
                AddError(findings, "SK-GEO-022", path, $"Box op '{op.Name}' requires a positive finite 3-element sizeMm vector.");
            }

            return op.OriginMm![2];
        }

        if (string.Equals(op.Op, GeometryOpSpec.OpSlopedPrism, StringComparison.Ordinal))
        {
            if (!IsFiniteVector3(op.OriginMm))
            {
                AddError(findings, "SK-GEO-021", path, $"slopedPrism op '{op.Name}' requires a finite 3-element originMm vector.");
                return null;
            }

            return op.OriginMm![2];
        }

        if (string.Equals(op.Op, GeometryOpSpec.OpNPrism, StringComparison.Ordinal))
        {
            if (!IsFiniteVector3(op.CenterMm))
            {
                AddError(findings, "SK-GEO-021", path, $"nprism op '{op.Name}' requires a finite 3-element centerMm vector.");
                return null;
            }

            return op.CenterMm![2];
        }

        return null;
    }

    private static void ValidateClearances(AntiZFightingClearances clearances, string path, List<StarterKitValidationFinding> findings)
    {
        if (!IsFinite(clearances.SlabClearMm) || clearances.SlabClearMm < AntiZFightingClearances.MinimumSlabClearMm)
        {
            AddError(findings, "SK-CLR-001", path, $"slabClearMm ({clearances.SlabClearMm}) must be finite and >= {AntiZFightingClearances.MinimumSlabClearMm} mm.");
        }

        if (!IsFinite(clearances.SideClearMm) || clearances.SideClearMm < AntiZFightingClearances.MinimumSideClearMm)
        {
            AddError(findings, "SK-CLR-002", path, $"sideClearMm ({clearances.SideClearMm}) must be finite and >= {AntiZFightingClearances.MinimumSideClearMm} mm.");
        }

        if (!IsFinite(clearances.GlassTuckMm) || clearances.GlassTuckMm < AntiZFightingClearances.MinimumGlassTuckMm)
        {
            AddError(findings, "SK-CLR-003", path, $"glassTuckMm ({clearances.GlassTuckMm}) must be finite and >= {AntiZFightingClearances.MinimumGlassTuckMm} mm.");
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFiniteVector3(IReadOnlyList<double>? vector) =>
        vector != null &&
        vector.Count == 3 &&
        IsFinite(vector[0]) &&
        IsFinite(vector[1]) &&
        IsFinite(vector[2]);

    private static bool IsPositiveFiniteVector3(IReadOnlyList<double>? vector) =>
        IsFiniteVector3(vector) &&
        vector![0] > 0.0 &&
        vector[1] > 0.0 &&
        vector[2] > 0.0;

    private static void ValidateFilterRule(
        FilterRuleSpec rule,
        IReadOnlyList<string> categories,
        StarterKitReferenceStandards standards,
        string path,
        List<StarterKitValidationFinding> findings)
    {
        if (!ValidFilterOperators.Contains(rule.Operator, StringComparer.Ordinal))
        {
            AddError(findings, "SK-FLT-001", path, $"Unsupported filter operator '{rule.Operator}' for parameter '{rule.Parameter}'.");
        }

        if (!standards.ParametersByName.TryGetValue(rule.Parameter, out GisParameterDefinition? paramDef) || paramDef == null)
        {
            AddError(findings, "SK-FLT-002", path, $"Filter rule references unknown shared parameter '{rule.Parameter}'.");
            return;
        }

        foreach (string category in categories)
        {
            if (!paramDef.Categories.Contains(category, StringComparer.Ordinal))
            {
                AddError(findings, "SK-FLT-003", path, $"Filter rule parameter '{rule.Parameter}' is not bound to category '{category}'.");
            }
        }
    }

    private static IReadOnlyList<FilterRuleSpec> ParseFilterRules(JArray? array)
    {
        if (array == null)
        {
            return Array.Empty<FilterRuleSpec>();
        }

        return Array.AsReadOnly(array.Select(token => new FilterRuleSpec(
            ReadString(token, "parameter"),
            ReadString(token, "operator"),
            ReadString(token, "value"))).ToArray());
    }

    private static IReadOnlyList<GeometryOpSpec> ParseGeometryOps(JArray? array)
    {
        if (array == null)
        {
            return Array.Empty<GeometryOpSpec>();
        }

        return Array.AsReadOnly(array.Select(ParseGeometryOp).ToArray());
    }

    private static GeometryOpSpec ParseGeometryOp(JToken token)
    {
        GeometryOpSpec? child = token["child"] is JObject childObj ? ParseGeometryOp(childObj) : null;
        IReadOnlyList<GeometryOpSpec>? children = token["children"] is JArray childrenArr
            ? ParseGeometryOps(childrenArr)
            : null;

        return new GeometryOpSpec(
            op: ReadString(token, "op"),
            name: ReadString(token, "name"),
            subcategory: ReadOptionalString(token, "subcategory"),
            materialRef: ReadOptionalString(token, "materialRef"),
            originMm: ReadOptionalDoubleList(token, "originMm"),
            sizeMm: ReadOptionalDoubleList(token, "sizeMm"),
            centerMm: ReadOptionalDoubleList(token, "centerMm"),
            radiusMm: ReadOptionalDouble(token, "radiusMm"),
            heightMm: ReadOptionalDouble(token, "heightMm"),
            sides: ReadOptionalInt(token, "sides"),
            widthMm: ReadOptionalDouble(token, "widthMm"),
            runMm: ReadOptionalDouble(token, "runMm"),
            riseMm: ReadOptionalDouble(token, "riseMm"),
            thicknessMm: ReadOptionalDouble(token, "thicknessMm"),
            count: ReadOptionalInt(token, "count"),
            stepMm: ReadOptionalDoubleList(token, "stepMm"),
            child: child,
            children: children,
            shape: ReadOptionalString(token, "shape"),
            throughCut: ReadOptionalBool(token, "throughCut"),
            targetOp: ReadOptionalString(token, "targetOp"));
    }

    private static StarterKitReferenceStandards ResolveStandards(string starterKitDirectory, string? standardsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(standardsDirectory))
        {
            return StarterKitReferenceStandards.LoadFromDirectory(standardsDirectory!);
        }

        DirectoryInfo? parent = Directory.GetParent(starterKitDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent != null && File.Exists(Path.Combine(parent.FullName, GisSharedParameters.BindingsFileName)))
        {
            return StarterKitReferenceStandards.LoadFromDirectory(parent.FullName);
        }

        return StarterKitReferenceStandards.LoadEmbedded();
    }

    internal static bool TryResolveSafeChildPath(
        string rootDir,
        string? relativePath,
        out string resolvedPath,
        out string errorReason)
    {
        resolvedPath = string.Empty;
        errorReason = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            errorReason = "Path cannot be empty.";
            return false;
        }

        string trimmed = relativePath!.Trim();
        if (trimmed.IndexOf('\0') >= 0)
        {
            errorReason = "Path contains NUL character.";
            return false;
        }

        // Reject rooted paths on both Windows and POSIX (including drive-letter paths evaluated on Linux).
        bool hasWindowsDrivePrefix = trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("\\", StringComparison.Ordinal) || hasWindowsDrivePrefix)
        {
            errorReason = "Rooted or absolute paths are not allowed.";
            return false;
        }

        string[] segments = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => string.Equals(s, "..", StringComparison.Ordinal)))
        {
            errorReason = "Parent-directory traversal ('..') is not allowed.";
            return false;
        }

        string normalized = string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        string fullRoot = Path.GetFullPath(rootDir);
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized));

        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            errorReason = "Resolved path escapes starter-kit root directory.";
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    private static string ReadEmbedded(Assembly assembly, string relativePath)
    {
        string? content = TryReadEmbedded(assembly, relativePath);
        if (content == null)
        {
            throw new InvalidOperationException($"Embedded starter-kit resource '{relativePath}' was not found in {assembly.GetName().Name}.");
        }

        return content;
    }

    private static string? TryReadEmbedded(Assembly assembly, string relativePath)
    {
        string resourceName = EmbeddedPrefix + relativePath.Replace('\\', '.').Replace('/', '.');
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using StreamReader reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    private static string ReadUtf8(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");

    private static JObject RequireObject(JToken parent, string property, string context) =>
        parent[property] as JObject
        ?? throw new InvalidDataException($"{context} is missing required object '{property}'.");

    private static JArray ReadArray(JToken parent, string property) =>
        parent[property] as JArray ?? new JArray();

    private static string ReadString(JToken parent, string property) =>
        (string?)parent[property] ?? string.Empty;

    private static string? ReadOptionalString(JToken parent, string property) =>
        parent[property]?.Type == JTokenType.String ? (string?)parent[property] : null;

    private static int ReadInt(JToken parent, string property) =>
        (int?)parent[property] ?? 0;

    private static int RequireInt(JToken parent, string property) =>
        parent[property] != null && parent[property]!.Type == JTokenType.Integer
            ? (int)parent[property]!
            : throw new InvalidDataException($"Missing required integer property '{property}'.");

    private static int? ReadOptionalInt(JToken parent, string property) =>
        parent[property] != null && parent[property]!.Type == JTokenType.Integer ? (int?)parent[property] : null;

    private static double ReadDouble(JToken parent, string property) =>
        (double?)parent[property] ?? 0.0;

    private static double RequireDouble(JToken parent, string property) =>
        parent[property] != null && (parent[property]!.Type == JTokenType.Float || parent[property]!.Type == JTokenType.Integer)
            ? (double)parent[property]!
            : throw new InvalidDataException($"Missing required numeric property '{property}'.");

    private static double? ReadOptionalDouble(JToken parent, string property) =>
        parent[property] != null && (parent[property]!.Type == JTokenType.Float || parent[property]!.Type == JTokenType.Integer)
            ? (double?)parent[property]
            : null;

    private static bool ReadBool(JToken parent, string property, bool defaultValue = false) =>
        parent[property] != null && parent[property]!.Type == JTokenType.Boolean ? (bool)parent[property]! : defaultValue;

    private static bool? ReadOptionalBool(JToken parent, string property) =>
        parent[property] != null && parent[property]!.Type == JTokenType.Boolean ? (bool?)parent[property] : null;

    private static IReadOnlyList<string> ReadStringList(JToken parent, string property) =>
        Array.AsReadOnly(ReadArray(parent, property).Select(t => (string)t!).ToArray());

    private static IReadOnlyList<int> ReadIntList(JToken parent, string property) =>
        Array.AsReadOnly(ReadArray(parent, property).Select(t => (int)t!).ToArray());

    private static IReadOnlyList<double>? ReadOptionalDoubleList(JToken parent, string property) =>
        parent[property] is JArray arr
            ? Array.AsReadOnly(arr.Select(t => (double)t!).ToArray())
            : null;

    private static void AddError(List<StarterKitValidationFinding> findings, string code, string path, string message) =>
        findings.Add(new StarterKitValidationFinding(StarterKitValidationSeverity.Error, code, path, message));
}
