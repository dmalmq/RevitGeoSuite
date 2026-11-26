using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Canonical view-template entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestViewTemplateEntry
{
    public ManifestViewTemplateEntry(
        string name,
        string viewType,
        string detailLevel,
        bool isCanonical2DExport,
        bool hasVisibilityHidingFilters,
        IEnumerable<string> filterNames,
        bool hideNonGisLineStyles = false,
        IEnumerable<string>? hideSubcategories = null)
    {
        Name = name ?? string.Empty;
        ViewType = viewType ?? string.Empty;
        DetailLevel = detailLevel ?? string.Empty;
        IsCanonical2DExport = isCanonical2DExport;
        HasVisibilityHidingFilters = hasVisibilityHidingFilters;
        FilterNames = ExpectedManifest.SortStrings(filterNames);
        HideNonGisLineStyles = hideNonGisLineStyles;
        HideSubcategories = ExpectedManifest.SortStrings(hideSubcategories);
    }

    public string Name { get; }

    public string ViewType { get; }

    public string DetailLevel { get; }

    public bool IsCanonical2DExport { get; }

    public bool HasVisibilityHidingFilters { get; }

    public IReadOnlyList<string> FilterNames { get; }

    public bool HideNonGisLineStyles { get; }

    public IReadOnlyList<string> HideSubcategories { get; }
}

/// <summary>Canonical QA view-filter entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestQaFilterEntry
{
    public ManifestQaFilterEntry(
        string name,
        string severity,
        bool visible,
        string overrideColorHex,
        IEnumerable<string> categories)
    {
        Name = name ?? string.Empty;
        Severity = severity ?? string.Empty;
        Visible = visible;
        OverrideColorHex = (overrideColorHex ?? string.Empty).ToUpperInvariant();
        Categories = ExpectedManifest.SortStrings(categories);
    }

    public string Name { get; }

    public string Severity { get; }

    public bool Visible { get; }

    public string OverrideColorHex { get; }

    public IReadOnlyList<string> Categories { get; }
}

/// <summary>Canonical QA schedule entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestScheduleEntry
{
    public ManifestScheduleEntry(
        string id,
        string name,
        string kind,
        string category,
        bool emptyMeansPass,
        IEnumerable<string> fields,
        bool hasGisKindFilter)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Kind = kind ?? string.Empty;
        Category = category ?? string.Empty;
        EmptyMeansPass = emptyMeansPass;
        Fields = ExpectedManifest.SortStrings(fields);
        HasGisKindFilter = hasGisKindFilter;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string Category { get; }

    public bool EmptyMeansPass { get; }

    public IReadOnlyList<string> Fields { get; }

    public bool HasGisKindFilter { get; }
}

/// <summary>Canonical line-style entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestLineStyleEntry
{
    public ManifestLineStyleEntry(string name, int lineWeight, string colorHex, string linePattern)
    {
        Name = name ?? string.Empty;
        LineWeight = lineWeight;
        ColorHex = (colorHex ?? string.Empty).ToUpperInvariant();
        LinePattern = linePattern ?? string.Empty;
    }

    public string Name { get; }

    public int LineWeight { get; }

    public string ColorHex { get; }

    public string LinePattern { get; }
}

/// <summary>Canonical 3D material entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestMaterialEntry
{
    public ManifestMaterialEntry(string name, string role, string hex, int transparency, string alphaMode)
    {
        Name = name ?? string.Empty;
        Role = role ?? string.Empty;
        Hex = (hex ?? string.Empty).ToUpperInvariant();
        Transparency = transparency;
        AlphaMode = alphaMode ?? string.Empty;
    }

    public string Name { get; }

    public string Role { get; }

    public string Hex { get; }

    public int Transparency { get; }

    public string AlphaMode { get; }
}

/// <summary>Canonical pre-loaded system family entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestSystemFamilyEntry
{
    public ManifestSystemFamilyEntry(string id, string typeName, string systemKind, string category, string materialRef)
    {
        Id = id ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        SystemKind = systemKind ?? string.Empty;
        Category = category ?? string.Empty;
        MaterialRef = materialRef ?? string.Empty;
    }

    public string Id { get; }

    public string TypeName { get; }

    public string SystemKind { get; }

    public string Category { get; }

    public string MaterialRef { get; }
}

/// <summary>Canonical shared-parameter binding entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestSharedParameterEntry
{
    public ManifestSharedParameterEntry(
        string name,
        Guid guid,
        string binding,
        string dataType,
        IEnumerable<string> categories)
    {
        Name = name ?? string.Empty;
        Guid = guid;
        Binding = binding ?? string.Empty;
        DataType = dataType ?? string.Empty;
        Categories = ExpectedManifest.SortStrings(categories);
    }

    public string Name { get; }

    public Guid Guid { get; }

    public string Binding { get; }

    public string DataType { get; }

    public IReadOnlyList<string> Categories { get; }
}

/// <summary>Canonical family entry inside an <see cref="ExpectedManifest"/>.</summary>
public sealed class ManifestFamilyEntry
{
    public ManifestFamilyEntry(
        string id,
        string familyName,
        string category,
        string seedKind,
        bool isPolygonal,
        bool hasCoarseKFootprint,
        bool hasDownwardBottomFaceAtZero,
        IEnumerable<string> subcategories,
        int coarseTriangles,
        int coarseTriangleBudget,
        int mediumFineTriangles,
        int mediumFineTriangleBudget,
        IEnumerable<Guid> sharedParameterGuids)
    {
        Id = id ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        Category = category ?? string.Empty;
        SeedKind = seedKind ?? string.Empty;
        IsPolygonal = isPolygonal;
        HasCoarseKFootprint = hasCoarseKFootprint;
        HasDownwardBottomFaceAtZero = hasDownwardBottomFaceAtZero;
        Subcategories = ExpectedManifest.SortStrings(subcategories);
        CoarseTriangles = coarseTriangles;
        CoarseTriangleBudget = coarseTriangleBudget;
        MediumFineTriangles = mediumFineTriangles;
        MediumFineTriangleBudget = mediumFineTriangleBudget;
        SharedParameterGuids = Array.AsReadOnly(
            (sharedParameterGuids ?? Array.Empty<Guid>())
                .Distinct()
                .OrderBy(g => g.ToString("D"), StringComparer.Ordinal)
                .ToArray());
    }

    public string Id { get; }

    public string FamilyName { get; }

    public string Category { get; }

    public string SeedKind { get; }

    public bool IsPolygonal { get; }

    public bool HasCoarseKFootprint { get; }

    public bool HasDownwardBottomFaceAtZero { get; }

    public IReadOnlyList<string> Subcategories { get; }

    public int CoarseTriangles { get; }

    public int CoarseTriangleBudget { get; }

    public int MediumFineTriangles { get; }

    public int MediumFineTriangleBudget { get; }

    public IReadOnlyList<Guid> SharedParameterGuids { get; }
}

/// <summary>
/// Canonical, deterministically sorted semantic manifest of a Kanizu Starter Kit build (both <c>.rte</c>
/// and <c>.rfa</c> artifacts). Used by T6/T8 <c>ManifestDumper</c> gating to verify build determinism
/// semantically rather than by brittle Revit binary byte equality.
/// </summary>
public sealed class ExpectedManifest
{
    public const string SchemaName = "kanizu.starter-kit.manifest";
    public const int SchemaVersion = 1;

    public ExpectedManifest(
        string kitVersion,
        int gisStandardVersion,
        IEnumerable<string> supportedRevitVersions,
        string canonicalExportViewTemplate,
        string startingViewName,
        string startingViewType,
        bool startingViewBilingual,
        bool zeroIdentityEnforced,
        bool zeroWorksetsEnforced,
        bool hasProjectIdentity,
        int worksetCount,
        IEnumerable<ManifestViewTemplateEntry> viewTemplates,
        IEnumerable<ManifestQaFilterEntry> qaViewFilters,
        IEnumerable<ManifestScheduleEntry> schedules,
        IEnumerable<ManifestLineStyleEntry> lineStyles,
        IEnumerable<ManifestMaterialEntry> materials,
        IEnumerable<ManifestSystemFamilyEntry> systemFamilies,
        IEnumerable<ManifestSharedParameterEntry> sharedParameters,
        IEnumerable<ManifestFamilyEntry> families)
    {
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        SupportedRevitVersions = SortStrings(supportedRevitVersions);
        CanonicalExportViewTemplate = canonicalExportViewTemplate ?? string.Empty;
        StartingViewName = startingViewName ?? string.Empty;
        StartingViewType = startingViewType ?? string.Empty;
        StartingViewBilingual = startingViewBilingual;
        ZeroIdentityEnforced = zeroIdentityEnforced;
        ZeroWorksetsEnforced = zeroWorksetsEnforced;
        HasProjectIdentity = hasProjectIdentity;
        WorksetCount = Math.Max(0, worksetCount);

        ViewTemplates = Array.AsReadOnly(
            (viewTemplates ?? Array.Empty<ManifestViewTemplateEntry>())
                .OrderBy(v => v.Name, StringComparer.Ordinal)
                .ToArray());
        QaViewFilters = Array.AsReadOnly(
            (qaViewFilters ?? Array.Empty<ManifestQaFilterEntry>())
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToArray());
        Schedules = Array.AsReadOnly(
            (schedules ?? Array.Empty<ManifestScheduleEntry>())
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ToArray());
        LineStyles = Array.AsReadOnly(
            (lineStyles ?? Array.Empty<ManifestLineStyleEntry>())
                .OrderBy(l => l.Name, StringComparer.Ordinal)
                .ToArray());
        Materials = Array.AsReadOnly(
            (materials ?? Array.Empty<ManifestMaterialEntry>())
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray());
        SystemFamilies = Array.AsReadOnly(
            (systemFamilies ?? Array.Empty<ManifestSystemFamilyEntry>())
                .OrderBy(sf => sf.Id, StringComparer.Ordinal)
                .ToArray());
        SharedParameters = Array.AsReadOnly(
            (sharedParameters ?? Array.Empty<ManifestSharedParameterEntry>())
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray());
        Families = Array.AsReadOnly(
            (families ?? Array.Empty<ManifestFamilyEntry>())
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToArray());
    }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public IReadOnlyList<string> SupportedRevitVersions { get; }

    public string CanonicalExportViewTemplate { get; }

    public string StartingViewName { get; }

    public string StartingViewType { get; }

    public bool StartingViewBilingual { get; }

    public bool ZeroIdentityEnforced { get; }

    public bool ZeroWorksetsEnforced { get; }

    public bool HasProjectIdentity { get; }

    public int WorksetCount { get; }

    public IReadOnlyList<ManifestViewTemplateEntry> ViewTemplates { get; }

    public IReadOnlyList<ManifestQaFilterEntry> QaViewFilters { get; }

    public IReadOnlyList<ManifestScheduleEntry> Schedules { get; }

    public IReadOnlyList<ManifestLineStyleEntry> LineStyles { get; }

    public IReadOnlyList<ManifestMaterialEntry> Materials { get; }

    public IReadOnlyList<ManifestSystemFamilyEntry> SystemFamilies { get; }

    public IReadOnlyList<ManifestSharedParameterEntry> SharedParameters { get; }

    public IReadOnlyList<ManifestFamilyEntry> Families { get; }

    /// <summary>Deterministically generates the expected semantic manifest from a validated <see cref="StarterKitSpec"/>.</summary>
    public static ExpectedManifest FromSpec(StarterKitSpec spec, StarterKitReferenceStandards? standards = null)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();

        Dictionary<string, QaViewFilterSpec> filtersByName = spec.Rte.QaViewFilters
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        IEnumerable<ManifestViewTemplateEntry> viewTemplates = spec.Rte.ViewTemplates.Select(vt =>
        {
            bool hasHideFilter = vt.Filters.Any(name =>
                filtersByName.TryGetValue(name, out QaViewFilterSpec? f) && f != null && !f.Visible);
            return new ManifestViewTemplateEntry(
                vt.Name,
                vt.ViewType,
                vt.DetailLevel,
                vt.IsCanonical2DExport,
                hasHideFilter,
                vt.Filters,
                vt.HideNonGisLineStyles,
                vt.HideSubcategories);
        });

        IEnumerable<ManifestQaFilterEntry> qaFilters = spec.Rte.QaViewFilters.Select(f =>
            new ManifestQaFilterEntry(f.Name, f.Severity, f.Visible, f.OverrideColorHex, f.Categories));

        IEnumerable<ManifestScheduleEntry> schedules = spec.Rte.Schedules.Select(s =>
            new ManifestScheduleEntry(
                s.Id,
                s.Name,
                s.Kind,
                s.Category,
                s.EmptyMeansPass,
                s.Fields,
                s.Filters.Any(r =>
                    string.Equals(r.Parameter, GisSharedParameters.GisKind, StringComparison.Ordinal) &&
                    string.Equals(r.Operator, "hasValue", StringComparison.Ordinal))));

        IEnumerable<ManifestLineStyleEntry> lineStyles = spec.Rte.LineStyles.Select(ls =>
            new ManifestLineStyleEntry(ls.Name, ls.LineWeight, ls.ColorHex, ls.LinePattern));

        IEnumerable<ManifestMaterialEntry> materials = spec.Materials3D.Materials.Select(m =>
            new ManifestMaterialEntry(m.Name, m.Role, m.Hex, m.Transparency, m.AlphaMode));

        IEnumerable<ManifestSystemFamilyEntry> systemFamilies = spec.Rte.SystemFamilies.Select(sf =>
            new ManifestSystemFamilyEntry(sf.Id, sf.TypeName, sf.SystemKind, sf.Category, sf.MaterialRef));

        IEnumerable<ManifestSharedParameterEntry> sharedParameters = refStandards.ParametersByName.Values.Select(p =>
            new ManifestSharedParameterEntry(
                p.Name,
                p.Guid,
                p.Binding == GisParameterBinding.Type ? "type" : "instance",
                p.DataType == GisParameterDataType.Integer ? "integer" : "text",
                p.Categories));

        IEnumerable<ManifestFamilyEntry> families = spec.Families.Select(family =>
        {
            FamilyTriangleEstimate estimate = TriangleEstimator.EstimateFamily(family);
            bool hasKFootprint = family.Coarse != null &&
                                 family.Coarse.Ops.Count > 0 &&
                                 string.Equals(family.Coarse.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal);
            bool hasBottomAtZero = hasKFootprint && family.Coarse!.RequiresDownwardPlanarBottomAtZero;

            List<Guid> paramGuids = new();
            foreach (FamilySharedParameterSpec param in family.SharedParameters)
            {
                if (refStandards.ParametersByName.TryGetValue(param.Name, out GisParameterDefinition? def) && def != null)
                {
                    paramGuids.Add(def.Guid);
                }
            }

            return new ManifestFamilyEntry(
                family.Id,
                family.FamilyName,
                family.Category,
                family.SeedKind,
                family.IsPolygonal,
                hasKFootprint,
                hasBottomAtZero,
                family.Subcategories,
                estimate.CoarseTriangles,
                estimate.CoarseBudget,
                estimate.MediumFineTriangles,
                estimate.MediumFineBudget,
                paramGuids);
        });

        return new ExpectedManifest(
            spec.Header.KitVersion,
            spec.Header.GisStandardVersion,
            spec.Header.SupportedRevitVersions,
            spec.Rte.CanonicalExportViewTemplate,
            spec.Rte.StartingView.Name,
            spec.Rte.StartingView.ViewType,
            spec.Rte.StartingView.Bilingual,
            spec.Rte.Hygiene.RequireZeroProjectIdentity,
            spec.Rte.Hygiene.RequireZeroWorksets,
            hasProjectIdentity: false,
            worksetCount: 0,
            viewTemplates,
            qaFilters,
            schedules,
            lineStyles,
            materials,
            systemFamilies,
            sharedParameters,
            families);
    }

    /// <summary>Serializes this manifest into canonical sorted UTF-8 JSON (LF line endings).</summary>
    public string ToCanonicalJson(bool indented = true)
    {
        JObject root = new()
        {
            ["schema"] = SchemaName,
            ["schemaVersion"] = SchemaVersion,
            ["kitVersion"] = KitVersion,
            ["gisStandardVersion"] = GisStandardVersion,
            ["supportedRevitVersions"] = new JArray(SupportedRevitVersions),
            ["canonicalExportViewTemplate"] = CanonicalExportViewTemplate,
            ["startingView"] = new JObject
            {
                ["name"] = StartingViewName,
                ["viewType"] = StartingViewType,
                ["bilingual"] = StartingViewBilingual,
            },
            ["hygiene"] = new JObject
            {
                ["zeroIdentityEnforced"] = ZeroIdentityEnforced,
                ["zeroWorksetsEnforced"] = ZeroWorksetsEnforced,
                ["hasProjectIdentity"] = HasProjectIdentity,
                ["worksetCount"] = WorksetCount,
            },
            ["viewTemplates"] = new JArray(ViewTemplates.Select(v => new JObject
            {
                ["name"] = v.Name,
                ["viewType"] = v.ViewType,
                ["detailLevel"] = v.DetailLevel,
                ["isCanonical2DExport"] = v.IsCanonical2DExport,
                ["hasVisibilityHidingFilters"] = v.HasVisibilityHidingFilters,
                ["hideNonGisLineStyles"] = v.HideNonGisLineStyles,
                ["filterNames"] = new JArray(v.FilterNames),
                ["hideSubcategories"] = new JArray(v.HideSubcategories),
            })),
            ["qaViewFilters"] = new JArray(QaViewFilters.Select(f => new JObject
            {
                ["name"] = f.Name,
                ["severity"] = f.Severity,
                ["visible"] = f.Visible,
                ["overrideColorHex"] = f.OverrideColorHex,
                ["categories"] = new JArray(f.Categories),
            })),
            ["schedules"] = new JArray(Schedules.Select(s => new JObject
            {
                ["id"] = s.Id,
                ["name"] = s.Name,
                ["kind"] = s.Kind,
                ["category"] = s.Category,
                ["emptyMeansPass"] = s.EmptyMeansPass,
                ["fields"] = new JArray(s.Fields),
                ["hasGisKindFilter"] = s.HasGisKindFilter,
            })),
            ["lineStyles"] = new JArray(LineStyles.Select(ls => new JObject
            {
                ["name"] = ls.Name,
                ["lineWeight"] = ls.LineWeight,
                ["colorHex"] = ls.ColorHex,
                ["linePattern"] = ls.LinePattern,
            })),
            ["materials"] = new JArray(Materials.Select(m => new JObject
            {
                ["name"] = m.Name,
                ["role"] = m.Role,
                ["hex"] = m.Hex,
                ["transparency"] = m.Transparency,
                ["alphaMode"] = m.AlphaMode,
            })),
            ["systemFamilies"] = new JArray(SystemFamilies.Select(sf => new JObject
            {
                ["id"] = sf.Id,
                ["typeName"] = sf.TypeName,
                ["systemKind"] = sf.SystemKind,
                ["category"] = sf.Category,
                ["materialRef"] = sf.MaterialRef,
            })),
            ["sharedParameters"] = new JArray(SharedParameters.Select(p => new JObject
            {
                ["name"] = p.Name,
                ["guid"] = p.Guid.ToString("D"),
                ["binding"] = p.Binding,
                ["dataType"] = p.DataType,
                ["categories"] = new JArray(p.Categories),
            })),
            ["families"] = new JArray(Families.Select(f => new JObject
            {
                ["id"] = f.Id,
                ["familyName"] = f.FamilyName,
                ["category"] = f.Category,
                ["seedKind"] = f.SeedKind,
                ["isPolygonal"] = f.IsPolygonal,
                ["hasCoarseKFootprint"] = f.HasCoarseKFootprint,
                ["hasDownwardBottomFaceAtZero"] = f.HasDownwardBottomFaceAtZero,
                ["subcategories"] = new JArray(f.Subcategories),
                ["coarseTriangles"] = f.CoarseTriangles,
                ["coarseTriangleBudget"] = f.CoarseTriangleBudget,
                ["mediumFineTriangles"] = f.MediumFineTriangles,
                ["mediumFineTriangleBudget"] = f.MediumFineTriangleBudget,
                ["sharedParameterGuids"] = new JArray(f.SharedParameterGuids.Select(g => g.ToString("D"))),
            })),
        };

        JToken sorted = CanonicalJson.SortKeys(root);
        return sorted.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }

    /// <summary>Computes a deterministic lowercase SHA-256 hex digest of the canonical compact manifest JSON.</summary>
    public string ComputeSha256()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalJson(indented: false));
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            StringBuilder builder = new(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    /// <summary>Parses a manifest JSON string (such as output from <c>ManifestDumper</c>) and canonicalizes ordering.</summary>
    public static ExpectedManifest FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root = JObject.Parse(json);
        string schema = (string?)root["schema"] ?? string.Empty;
        if (!string.Equals(schema, SchemaName, StringComparison.Ordinal))
        {
            throw new System.IO.InvalidDataException($"Expected manifest schema '{SchemaName}', found '{schema}'.");
        }

        JToken? schemaVersionToken = root["schemaVersion"];
        if (schemaVersionToken == null || schemaVersionToken.Type != JTokenType.Integer || (int)schemaVersionToken != SchemaVersion)
        {
            throw new System.IO.InvalidDataException($"Unsupported manifest schemaVersion '{schemaVersionToken}'. Expected {SchemaVersion}.");
        }

        JObject starting = root["startingView"] as JObject ?? new JObject();
        JObject hygiene = root["hygiene"] as JObject ?? new JObject();

        return new ExpectedManifest(
            (string?)root["kitVersion"] ?? string.Empty,
            (int?)root["gisStandardVersion"] ?? 0,
            ReadStrings(root["supportedRevitVersions"]),
            (string?)root["canonicalExportViewTemplate"] ?? string.Empty,
            (string?)starting["name"] ?? string.Empty,
            (string?)starting["viewType"] ?? string.Empty,
            (bool?)starting["bilingual"] ?? false,
            (bool?)hygiene["zeroIdentityEnforced"] ?? false,
            (bool?)hygiene["zeroWorksetsEnforced"] ?? false,
            (bool?)hygiene["hasProjectIdentity"] ?? false,
            (int?)hygiene["worksetCount"] ?? 0,
            ReadObjects(root["viewTemplates"]).Select(o => new ManifestViewTemplateEntry(
                (string?)o["name"] ?? string.Empty,
                (string?)o["viewType"] ?? string.Empty,
                (string?)o["detailLevel"] ?? string.Empty,
                (bool?)o["isCanonical2DExport"] ?? false,
                (bool?)o["hasVisibilityHidingFilters"] ?? false,
                ReadStrings(o["filterNames"]),
                (bool?)o["hideNonGisLineStyles"] ?? false,
                ReadStrings(o["hideSubcategories"]))),
            ReadObjects(root["qaViewFilters"]).Select(o => new ManifestQaFilterEntry(
                (string?)o["name"] ?? string.Empty,
                (string?)o["severity"] ?? string.Empty,
                (bool?)o["visible"] ?? true,
                (string?)o["overrideColorHex"] ?? string.Empty,
                ReadStrings(o["categories"]))),
            ReadObjects(root["schedules"]).Select(o => new ManifestScheduleEntry(
                (string?)o["id"] ?? string.Empty,
                (string?)o["name"] ?? string.Empty,
                (string?)o["kind"] ?? string.Empty,
                (string?)o["category"] ?? string.Empty,
                (bool?)o["emptyMeansPass"] ?? false,
                ReadStrings(o["fields"]),
                (bool?)o["hasGisKindFilter"] ?? false)),
            ReadObjects(root["lineStyles"]).Select(o => new ManifestLineStyleEntry(
                (string?)o["name"] ?? string.Empty,
                (int?)o["lineWeight"] ?? 0,
                (string?)o["colorHex"] ?? string.Empty,
                (string?)o["linePattern"] ?? string.Empty)),
            ReadObjects(root["materials"]).Select(o => new ManifestMaterialEntry(
                (string?)o["name"] ?? string.Empty,
                (string?)o["role"] ?? string.Empty,
                (string?)o["hex"] ?? string.Empty,
                (int?)o["transparency"] ?? 0,
                (string?)o["alphaMode"] ?? string.Empty)),
            ReadObjects(root["systemFamilies"]).Select(o => new ManifestSystemFamilyEntry(
                (string?)o["id"] ?? string.Empty,
                (string?)o["typeName"] ?? string.Empty,
                (string?)o["systemKind"] ?? string.Empty,
                (string?)o["category"] ?? string.Empty,
                (string?)o["materialRef"] ?? string.Empty)),
            ReadObjects(root["sharedParameters"]).Select(o => new ManifestSharedParameterEntry(
                (string?)o["name"] ?? string.Empty,
                Guid.Parse((string?)o["guid"] ?? Guid.Empty.ToString("D")),
                (string?)o["binding"] ?? string.Empty,
                (string?)o["dataType"] ?? string.Empty,
                ReadStrings(o["categories"]))),
            ReadObjects(root["families"]).Select(o => new ManifestFamilyEntry(
                (string?)o["id"] ?? string.Empty,
                (string?)o["familyName"] ?? string.Empty,
                (string?)o["category"] ?? string.Empty,
                (string?)o["seedKind"] ?? string.Empty,
                (bool?)o["isPolygonal"] ?? true,
                (bool?)o["hasCoarseKFootprint"] ?? false,
                (bool?)o["hasDownwardBottomFaceAtZero"] ?? false,
                ReadStrings(o["subcategories"]),
                (int?)o["coarseTriangles"] ?? 0,
                (int?)o["coarseTriangleBudget"] ?? 0,
                (int?)o["mediumFineTriangles"] ?? 0,
                (int?)o["mediumFineTriangleBudget"] ?? 0,
                ReadStrings(o["sharedParameterGuids"]).Select(Guid.Parse))));
    }

    internal static IReadOnlyList<string> SortStrings(IEnumerable<string>? values) =>
        Array.AsReadOnly(
            (values ?? Array.Empty<string>())
                .Where(v => v != null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray());

    private static IEnumerable<string> ReadStrings(JToken? token) =>
        token is JArray array ? array.Select(t => (string)t!) : Enumerable.Empty<string>();

    private static IEnumerable<JObject> ReadObjects(JToken? token) =>
        token is JArray array ? array.OfType<JObject>() : Enumerable.Empty<JObject>();
}
