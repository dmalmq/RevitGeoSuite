using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>
/// Pure-C# RFC 4122 SHA-1 UUIDv5 generator compatible with both <c>net48</c> (Revit 2024)
/// and <c>net10.0-windows</c> (Revit 2027).
/// </summary>
public static class UuidV5
{
    /// <summary>RFC 4122 standard DNS namespace (<c>6ba7b810-9dad-11d1-80b4-00c04fd430c8</c>).</summary>
    public static readonly Guid DnsNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>RFC 4122 standard URL namespace (<c>6ba7b811-9dad-11d1-80b4-00c04fd430c8</c>).</summary>
    public static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>Computes a deterministic RFC 4122 version-5 (SHA-1) UUID for (<paramref name="namespaceId"/>, <paramref name="name"/>).</summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        byte[] namespaceBytes = ToNetworkOrderBytes(namespaceId);
        byte[] nameBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(name);
        byte[] combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

        byte[] hash;
        using (SHA1 sha1 = SHA1.Create())
        {
            hash = sha1.ComputeHash(combined);
        }

        byte[] uuidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, uuidBytes, 0, 16);

        // Set version to 5 (0101xxxx in octet 6) and RFC 4122 variant (10xxxxxx in octet 8).
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x50);
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

        return FromNetworkOrderBytes(uuidBytes);
    }

    private static byte[] ToNetworkOrderBytes(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        SwapGuidEndianness(bytes);
        return bytes;
    }

    private static Guid FromNetworkOrderBytes(byte[] networkBytes)
    {
        byte[] bytes = new byte[16];
        Buffer.BlockCopy(networkBytes, 0, bytes, 0, 16);
        SwapGuidEndianness(bytes);
        return new Guid(bytes);
    }

    private static void SwapGuidEndianness(byte[] bytes)
    {
        Swap(bytes, 0, 3);
        Swap(bytes, 1, 2);
        Swap(bytes, 4, 5);
        Swap(bytes, 6, 7);
    }

    private static void Swap(byte[] bytes, int left, int right)
    {
        byte tmp = bytes[left];
        bytes[left] = bytes[right];
        bytes[right] = tmp;
    }
}

/// <summary>Revit shared parameter data types emitted for companion <c>ks_*</c> parameters.</summary>
public enum ShowcaseParameterDataType
{
    Text,
    Integer,
    Number,
    YesNo,
}

/// <summary>One deterministic <c>ks_*</c> companion shared parameter definition.</summary>
public sealed class ShowcaseParameterDefinition
{
    public ShowcaseParameterDefinition(
        string name,
        Guid guid,
        string layerScope,
        string sourceFieldName,
        ShowcaseParameterDataType dataType,
        string description,
        bool isBuiltIn = false,
        bool isOverflowContainer = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        LayerScope = layerScope ?? throw new ArgumentNullException(nameof(layerScope));
        SourceFieldName = sourceFieldName ?? throw new ArgumentNullException(nameof(sourceFieldName));
        DataType = dataType;
        Description = description ?? string.Empty;
        IsBuiltIn = isBuiltIn;
        IsOverflowContainer = isOverflowContainer;
    }

    public string Name { get; }

    public Guid Guid { get; }

    public string LayerScope { get; }

    public string SourceFieldName { get; }

    public ShowcaseParameterDataType DataType { get; }

    public string Description { get; }

    public bool IsBuiltIn { get; }

    public bool IsOverflowContainer { get; }

    public string SeedString =>
        $"{LayerScope}/{SourceFieldName}/{ShowcaseParameterRegistry.ToSeedTypeToken(DataType)}";

    public string RevitDataTypeToken => ShowcaseParameterRegistry.ToRevitDataTypeToken(DataType);
}

/// <summary>One typed value assignment for a <see cref="ShowcaseParameterDefinition"/> on a planned element.</summary>
public sealed class ShowcaseParameterAssignment
{
    public ShowcaseParameterAssignment(
        ShowcaseParameterDefinition parameter,
        bool isNull,
        bool isEmptyString,
        string? textValue,
        int int32Value,
        double doubleValue)
    {
        Parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
        IsNull = isNull;
        IsEmptyString = isEmptyString;
        TextValue = textValue;
        Int32Value = int32Value;
        DoubleValue = doubleValue;
    }

    public ShowcaseParameterDefinition Parameter { get; }

    public bool IsNull { get; }

    public bool IsEmptyString { get; }

    public string? TextValue { get; }

    public int Int32Value { get; }

    public double DoubleValue { get; }

    public static ShowcaseParameterAssignment ForText(ShowcaseParameterDefinition parameter, string? text)
    {
        if (text is null)
        {
            return new ShowcaseParameterAssignment(parameter, isNull: true, isEmptyString: false, null, 0, 0d);
        }

        return new ShowcaseParameterAssignment(parameter, isNull: false, isEmptyString: text.Length == 0, text, 0, 0d);
    }

    public static ShowcaseParameterAssignment ForNormalized(
        ShowcaseParameterDefinition parameter,
        NormalizedShowcaseAttribute attribute)
    {
        if (attribute.IsNull)
        {
            return new ShowcaseParameterAssignment(parameter, isNull: true, isEmptyString: false, null, 0, 0d);
        }

        switch (parameter.DataType)
        {
            case ShowcaseParameterDataType.Integer:
                if (attribute.Value.Kind == ShowcaseAttributeKind.Integer &&
                    !ShowcaseAttributeNormalizer.ExceedsRevitInt32(attribute.Value.Int64Value!.Value))
                {
                    return new ShowcaseParameterAssignment(
                        parameter,
                        isNull: false,
                        isEmptyString: false,
                        attribute.DisplayText,
                        (int)attribute.Value.Int64Value.Value,
                        attribute.Value.Int64Value.Value);
                }

                return ForText(parameter, attribute.DisplayText ?? string.Empty);

            case ShowcaseParameterDataType.Number:
                if (attribute.Value.Kind == ShowcaseAttributeKind.Real)
                {
                    return new ShowcaseParameterAssignment(
                        parameter,
                        isNull: false,
                        isEmptyString: false,
                        attribute.DisplayText,
                        0,
                        attribute.Value.RealValue!.Value);
                }

                if (attribute.Value.Kind == ShowcaseAttributeKind.Integer)
                {
                    return new ShowcaseParameterAssignment(
                        parameter,
                        isNull: false,
                        isEmptyString: false,
                        attribute.DisplayText,
                        0,
                        attribute.Value.Int64Value!.Value);
                }

                return ForText(parameter, attribute.DisplayText ?? string.Empty);

            case ShowcaseParameterDataType.YesNo:
                if (attribute.Value.Kind == ShowcaseAttributeKind.Boolean)
                {
                    int flag = attribute.Value.BoolValue!.Value ? 1 : 0;
                    return new ShowcaseParameterAssignment(
                        parameter,
                        isNull: false,
                        isEmptyString: false,
                        attribute.DisplayText,
                        flag,
                        flag);
                }

                return ForText(parameter, attribute.DisplayText ?? string.Empty);

            case ShowcaseParameterDataType.Text:
            default:
                return ForText(parameter, attribute.DisplayText ?? string.Empty);
        }
    }
}

/// <summary>
/// Deterministic UUIDv5 <c>ks_*</c> shared-parameter registry for Forma Companion <c>.rvt</c> builds.
/// Maps snapshot layer attributes to <c>ks_&lt;sanitized_name&gt;</c> definitions capped at
/// <see cref="MaxDirectParameters"/> (80) top-level parameters, with any overflow serialized losslessly
/// into <see cref="OverflowParameterName"/> (<c>ks_attributes_json</c>).
/// </summary>
public sealed class ShowcaseParameterRegistry
{
    /// <summary>Frozen Kanizu Forma Showcase UUIDv5 namespace GUID. NEVER change after release.</summary>
    public static readonly Guid NamespaceGuid = new("8b3f6c9e-4c72-4a2b-9d1e-5f8a3c6b2d40");

    /// <summary>Maximum number of top-level direct <c>ks_*</c> shared parameters bound in a companion model.</summary>
    public const int MaxDirectParameters = 80;

    public const string ParameterPrefix = "ks_";
    public const string ParameterGroupName = "Kanizu Forma Showcase";

    public const string LayerParameterName = "ks_layer";
    public const string LayerKindParameterName = "ks_layer_kind";
    public const string KeyParameterName = "ks_key";
    public const string LevelIdParameterName = "ks_level_id";
    public const string FloorKeyParameterName = "ks_floor_key";
    public const string OverflowParameterName = "ks_attributes_json";

    private const string ShowcaseBuiltInScope = "showcase";
    private const string SharedAttributeScope = "shared";

    private static readonly string[] HighPriorityFieldNames =
    {
        "id",
        "symbol_id",
        "category",
        "name",
        "short_name",
        "alt_name",
        "restriction",
        "door",
        "access_con",
        "access_control",
        "fixture_type",
        "ordinal",
        "floor",
        "level_id",
        "revit_key",
        "source",
    };

    private readonly Dictionary<(ShowcaseLayerKind Kind, string FieldName), ShowcaseParameterDefinition> _directByLayerAndField;
    private readonly HashSet<(ShowcaseLayerKind Kind, string FieldName)> _overflowLayerFields;

    private ShowcaseParameterRegistry(
        IReadOnlyList<ShowcaseParameterDefinition> directParameters,
        ShowcaseParameterDefinition overflowParameter,
        Dictionary<(ShowcaseLayerKind Kind, string FieldName), ShowcaseParameterDefinition> directByLayerAndField,
        HashSet<(ShowcaseLayerKind Kind, string FieldName)> overflowLayerFields)
    {
        DirectParameters = directParameters;
        OverflowParameter = overflowParameter;
        _directByLayerAndField = directByLayerAndField;
        _overflowLayerFields = overflowLayerFields;

        List<ShowcaseParameterDefinition> all = new(directParameters.Count + 1);
        all.AddRange(directParameters);
        all.Add(overflowParameter);
        AllParameters = all;
    }

    /// <summary>Top-level direct <c>ks_*</c> parameters (capped strictly at <see cref="MaxDirectParameters"/> = 80).</summary>
    public IReadOnlyList<ShowcaseParameterDefinition> DirectParameters { get; }

    /// <summary>Always-present <c>ks_attributes_json</c> overflow parameter carrying lossless JSON for overflow columns.</summary>
    public ShowcaseParameterDefinition OverflowParameter { get; }

    /// <summary>All <c>ks_*</c> parameters emitted into the companion shared-parameter file (<see cref="DirectParameters"/> + <see cref="OverflowParameter"/>).</summary>
    public IReadOnlyList<ShowcaseParameterDefinition> AllParameters { get; }

    public ShowcaseParameterDefinition? FindDirectParameter(ShowcaseLayerKind layerKind, string sourceFieldName)
    {
        if (string.IsNullOrEmpty(sourceFieldName))
        {
            return null;
        }

        return _directByLayerAndField.TryGetValue((layerKind, sourceFieldName), out ShowcaseParameterDefinition? def)
            ? def
            : null;
    }

    public ShowcaseParameterDefinition? FindByName(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
        {
            return null;
        }

        return AllParameters.FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
    }

    public bool IsOverflowField(ShowcaseLayerKind layerKind, string sourceFieldName) =>
        _overflowLayerFields.Contains((layerKind, sourceFieldName));

    public int OverflowFieldCount => _overflowLayerFields.Count;

    /// <summary>
    /// Computes the deterministic RFC 4122 SHA-1 UUIDv5 for <c>"{layer}/{field}/{type}"</c> under
    /// <see cref="NamespaceGuid"/>.
    /// </summary>
    public static Guid ComputeParameterGuid(string layer, string field, ShowcaseParameterDataType dataType)
    {
        if (string.IsNullOrWhiteSpace(layer))
        {
            throw new ArgumentException("Layer scope is required.", nameof(layer));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field name is required.", nameof(field));
        }

        string seed = $"{NormalizeSeedToken(layer)}/{NormalizeSeedToken(field)}/{ToSeedTypeToken(dataType)}";
        return UuidV5.Create(NamespaceGuid, seed);
    }

    /// <summary>
    /// Builds a deterministic <see cref="ShowcaseParameterRegistry"/> from a validated <see cref="ShowcaseSnapshot"/>.
    /// </summary>
    public static ShowcaseParameterRegistry Build(
        ShowcaseSnapshot snapshot,
        bool includeBuiltInMetadata = true,
        int maxDirectParameters = MaxDirectParameters)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (maxDirectParameters < 0 || maxDirectParameters > MaxDirectParameters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDirectParameters),
                $"maxDirectParameters must be between 0 and {MaxDirectParameters}.");
        }

        List<ShowcaseParameterDefinition> direct = new();
        HashSet<string> usedParameterNames = new(StringComparer.OrdinalIgnoreCase);

        if (includeBuiltInMetadata)
        {
            ShowcaseParameterDefinition[] builtIns =
            {
                CreateBuiltInDefinition(LayerParameterName, "layer", "Source GIS layer name in the Kanizu showcase snapshot."),
                CreateBuiltInDefinition(LayerKindParameterName, "layer_kind", "Semantic layer classification (unit, opening, fixture, level, etc.)."),
                CreateBuiltInDefinition(KeyParameterName, "key", "Deterministic stable feature key within the layer."),
                CreateBuiltInDefinition(LevelIdParameterName, "level_id", "Resolved showcase level identifier."),
                CreateBuiltInDefinition(FloorKeyParameterName, "floor_key", "Resolved showcase floor key."),
            };

            foreach (ShowcaseParameterDefinition builtIn in builtIns)
            {
                if (direct.Count < maxDirectParameters)
                {
                    direct.Add(builtIn);
                    usedParameterNames.Add(builtIn.Name);
                }
            }
        }

        // Always reserve ks_attributes_json so no source column named "attributes_json" can collide with it.
        usedParameterNames.Add(OverflowParameterName);
        ShowcaseParameterDefinition overflowParameter = new(
            OverflowParameterName,
            ComputeParameterGuid(ShowcaseBuiltInScope, "attributes_json", ShowcaseParameterDataType.Text),
            ShowcaseBuiltInScope,
            "attributes_json",
            ShowcaseParameterDataType.Text,
            "Lossless JSON overflow bag for showcase attributes beyond the 80-parameter direct cap.",
            isBuiltIn: true,
            isOverflowContainer: true);

        // Collect all (layerKind, fieldName) candidates deterministically across layers.
        Dictionary<(ShowcaseLayerKind Kind, string FieldName), CandidateField> candidatesByKey = new();
        foreach (ShowcaseLayer layer in snapshot.Layers.OrderBy(l => l.Kind).ThenBy(l => l.Name, StringComparer.Ordinal))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);

            foreach (ShowcaseField field in layer.Fields)
            {
                bool preserveAsText = preserveMap.TryGetValue(field.Name, out bool flag) && flag;
                ShowcaseParameterDataType dataType = ResolveParameterDataType(field.FieldType, preserveAsText);
                RegisterCandidate(candidatesByKey, layer.Kind, field.Name, dataType, field.AliasJa, field.Domain);
            }

            foreach (ShowcaseFeature feature in layer.Features)
            {
                foreach (KeyValuePair<string, ShowcaseAttributeValue> attr in feature.Attributes)
                {
                    if (candidatesByKey.ContainsKey((layer.Kind, attr.Key)))
                    {
                        continue;
                    }

                    bool preserveAsText = preserveMap.TryGetValue(attr.Key, out bool flag) && flag;
                    ShowcaseFieldType inferredType = InferFieldType(attr.Value);
                    ShowcaseParameterDataType dataType = ResolveParameterDataType(inferredType, preserveAsText);
                    RegisterCandidate(candidatesByKey, layer.Kind, attr.Key, dataType, aliasJa: null, domain: null);
                }
            }
        }

        // Deterministic priority ordering: high-priority GIS fields first, then layer kind order, then ordinal field name.
        List<CandidateField> orderedCandidates = candidatesByKey.Values
            .OrderBy(c => GetFieldPriority(c.SourceFieldName))
            .ThenBy(c => c.LayerKind)
            .ThenBy(c => c.SourceFieldName, StringComparer.Ordinal)
            .ToList();

        Dictionary<(ShowcaseLayerKind Kind, string FieldName), ShowcaseParameterDefinition> directByLayerAndField = new();
        HashSet<(ShowcaseLayerKind Kind, string FieldName)> overflowLayerFields = new();

        // Map sanitized parameter name -> existing definition so compatible cross-layer attributes share one ks_* parameter.
        Dictionary<string, ShowcaseParameterDefinition> sharedByParamName = new(StringComparer.OrdinalIgnoreCase);

        foreach (CandidateField candidate in orderedCandidates)
        {
            string sanitizedField = SanitizeFieldToken(candidate.SourceFieldName);
            string baseParamName = ParameterPrefix + sanitizedField;

            // If another layer already registered the exact same ks_<field> with the same Revit data type, reuse it!
            if (sharedByParamName.TryGetValue(baseParamName, out ShowcaseParameterDefinition? existingShared) &&
                existingShared.DataType == candidate.DataType)
            {
                directByLayerAndField[(candidate.LayerKind, candidate.SourceFieldName)] = existingShared;
                continue;
            }

            if (direct.Count >= maxDirectParameters)
            {
                overflowLayerFields.Add((candidate.LayerKind, candidate.SourceFieldName));
                continue;
            }

            string layerScope;
            string paramName;
            string seedField;

            if (!usedParameterNames.Contains(baseParamName))
            {
                layerScope = SharedAttributeScope;
                paramName = baseParamName;
                seedField = sanitizedField;
            }
            else
            {
                // Collision with a built-in parameter (e.g. source field "key" or "layer" or "level_id") or a
                // type-conflicting parameter in another layer: scope deterministically by layer kind.
                string kindToken = ToLayerKindScopeToken(candidate.LayerKind);
                string scopedName = $"{ParameterPrefix}{kindToken}_{sanitizedField}";
                if (usedParameterNames.Contains(scopedName))
                {
                    scopedName = $"{scopedName}_{ToSeedTypeToken(candidate.DataType)}";
                }

                if (usedParameterNames.Contains(scopedName))
                {
                    overflowLayerFields.Add((candidate.LayerKind, candidate.SourceFieldName));
                    continue;
                }

                layerScope = kindToken;
                paramName = scopedName;
                seedField = sanitizedField;
            }

            Guid guid = ComputeParameterGuid(layerScope, seedField, candidate.DataType);
            string description = BuildParameterDescription(candidate);
            ShowcaseParameterDefinition definition = new(
                paramName,
                guid,
                layerScope,
                seedField,
                candidate.DataType,
                description);

            direct.Add(definition);
            usedParameterNames.Add(paramName);
            if (string.Equals(layerScope, SharedAttributeScope, StringComparison.Ordinal))
            {
                sharedByParamName[paramName] = definition;
            }

            directByLayerAndField[(candidate.LayerKind, candidate.SourceFieldName)] = definition;
        }

        return new ShowcaseParameterRegistry(direct, overflowParameter, directByLayerAndField, overflowLayerFields);
    }

    /// <summary>
    /// Splits a feature's normalized attributes into direct <c>ks_*</c> parameter assignments plus an optional
    /// lossless <c>ks_attributes_json</c> overflow string when any attribute overflows the 80-parameter cap.
    /// </summary>
    public (IReadOnlyList<ShowcaseParameterAssignment> DirectAssignments, string? OverflowJson) BindFeatureAttributes(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        IReadOnlyDictionary<string, bool>? columnPreserveMap = null)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        IReadOnlyList<NormalizedShowcaseAttribute> normalized =
            ShowcaseAttributeNormalizer.NormalizeFeatureAttributes(layer, feature, columnPreserveMap);

        List<ShowcaseParameterAssignment> assignments = new();
        List<NormalizedShowcaseAttribute> overflowAttributes = new();

        AddBuiltInAssignment(assignments, LayerParameterName, layer.Name);
        AddBuiltInAssignment(assignments, LayerKindParameterName, ShowcaseSnapshotReader.ToContractString(layer.Kind));
        AddBuiltInAssignment(assignments, KeyParameterName, feature.Key);
        AddBuiltInAssignment(assignments, LevelIdParameterName, feature.LevelId);
        AddBuiltInAssignment(assignments, FloorKeyParameterName, feature.FloorKey);

        foreach (NormalizedShowcaseAttribute attr in normalized)
        {
            ShowcaseParameterDefinition? paramDef = FindDirectParameter(layer.Kind, attr.FieldName);
            if (paramDef != null)
            {
                assignments.Add(ShowcaseParameterAssignment.ForNormalized(paramDef, attr));
            }
            else
            {
                overflowAttributes.Add(attr);
            }
        }

        string? overflowJson = overflowAttributes.Count > 0
            ? ShowcaseAttributeNormalizer.SerializeLosslessAttributesJson(overflowAttributes)
            : null;

        return (assignments, overflowJson);
    }

    /// <summary>
    /// Generates standard Revit shared-parameter file text (<c>*META</c>, <c>*GROUP</c>, <c>*PARAM</c>)
    /// with deterministic UUIDv5 GUIDs, LF line endings, and optional inclusion of the frozen
    /// <c>gis_model_role</c> parameter so a companion builder can bind everything in a single pass.
    /// </summary>
    public string ToSharedParameterFileText(bool includeGisModelRoleParameter = false)
    {
        StringBuilder builder = new();
        builder.Append("# This is a Revit shared parameter file.\n");
        builder.Append("# Generated deterministically by RevitGeoSuite Forma Showcase Companion Builder (UUIDv5).\n");
        builder.Append("*META\tVERSION\tMINVERSION\n");
        builder.Append("META\t2\t1\n");
        builder.Append("*GROUP\tID\tNAME\n");
        builder.Append("GROUP\t1\t").Append(ParameterGroupName).Append('\n');
        if (includeGisModelRoleParameter)
        {
            builder.Append("GROUP\t2\t").Append(GisSharedParameters.GroupName).Append('\n');
        }

        builder.Append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOEXISTENT\n");

        foreach (ShowcaseParameterDefinition def in AllParameters)
        {
            int hideWhenNoValue = def.IsBuiltIn ? 0 : 1;
            builder
                .Append("PARAM\t")
                .Append(def.Guid.ToString("D"))
                .Append('\t').Append(def.Name)
                .Append('\t').Append(def.RevitDataTypeToken)
                .Append("\t\t1\t1\t")
                .Append(SanitizeTsvDescription(def.Description))
                .Append("\t0\t")
                .Append(hideWhenNoValue)
                .Append('\n');
        }

        if (includeGisModelRoleParameter &&
            GisSharedParameters.TryGet(GisSharedParameters.GisModelRole, out GisParameterDefinition roleDef))
        {
            builder
                .Append("PARAM\t")
                .Append(roleDef.Guid.ToString("D"))
                .Append('\t').Append(roleDef.Name)
                .Append('\t').Append(roleDef.DataType == GisParameterDataType.Integer ? "INTEGER" : "TEXT")
                .Append("\t\t2\t1\t")
                .Append(SanitizeTsvDescription(roleDef.Description))
                .Append('\t').Append(roleDef.UserModifiable ? '1' : '0')
                .Append("\t0\n");
        }

        return builder.ToString();
    }

    /// <summary>Sanitizes a source column name into a deterministic ASCII lowercase token suitable for <c>ks_&lt;token&gt;</c>.</summary>
    public static string SanitizeFieldToken(string rawFieldName)
    {
        if (string.IsNullOrWhiteSpace(rawFieldName))
        {
            return "field";
        }

        string trimmed = rawFieldName.Trim();
        StringBuilder sb = new(trimmed.Length);
        bool previousWasUnderscore = false;

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                previousWasUnderscore = false;
            }
            else if (c >= 'A' && c <= 'Z')
            {
                sb.Append((char)(c - 'A' + 'a'));
                previousWasUnderscore = false;
            }
            else if (!previousWasUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                previousWasUnderscore = true;
            }
        }

        string token = sb.ToString().Trim('_');
        if (token.Length == 0)
        {
            // Non-ASCII field name (e.g. Japanese column name): deterministic 8-hex suffix so distinct names never collide.
            Guid hashGuid = UuidV5.Create(NamespaceGuid, "nonascii/" + trimmed);
            token = "f_" + hashGuid.ToString("N").Substring(0, 8);
        }

        // Strip redundant leading "ks_" if the source field already starts with it.
        if (token.StartsWith(ParameterPrefix, StringComparison.Ordinal) && token.Length > ParameterPrefix.Length)
        {
            token = token.Substring(ParameterPrefix.Length);
        }

        if (token.Length > 48)
        {
            Guid hashGuid = UuidV5.Create(NamespaceGuid, "truncate/" + trimmed);
            token = token.Substring(0, 39).TrimEnd('_') + "_" + hashGuid.ToString("N").Substring(0, 8);
        }

        return token;
    }

    internal static string ToSeedTypeToken(ShowcaseParameterDataType dataType)
    {
        switch (dataType)
        {
            case ShowcaseParameterDataType.Text: return "text";
            case ShowcaseParameterDataType.Integer: return "integer";
            case ShowcaseParameterDataType.Number: return "number";
            case ShowcaseParameterDataType.YesNo: return "yesno";
            default: return "text";
        }
    }

    internal static string ToRevitDataTypeToken(ShowcaseParameterDataType dataType)
    {
        switch (dataType)
        {
            case ShowcaseParameterDataType.Text: return "TEXT";
            case ShowcaseParameterDataType.Integer: return "INTEGER";
            case ShowcaseParameterDataType.Number: return "NUMBER";
            case ShowcaseParameterDataType.YesNo: return "YESNO";
            default: return "TEXT";
        }
    }

    internal static string ToLayerKindScopeToken(ShowcaseLayerKind kind)
    {
        switch (kind)
        {
            case ShowcaseLayerKind.Unit: return "unit";
            case ShowcaseLayerKind.Opening: return "opening";
            case ShowcaseLayerKind.Fixture: return "fixture";
            case ShowcaseLayerKind.Level: return "level";
            case ShowcaseLayerKind.Detail: return "detail";
            case ShowcaseLayerKind.PointFacility: return "poi";
            case ShowcaseLayerKind.PointFacilityNetwork: return "poinet";
            case ShowcaseLayerKind.Station: return "station";
            case ShowcaseLayerKind.NetworkLink: return "netlink";
            case ShowcaseLayerKind.NetworkConnector: return "netconn";
            case ShowcaseLayerKind.NetworkNode: return "netnode";
            default: return "layer";
        }
    }

    private void AddBuiltInAssignment(List<ShowcaseParameterAssignment> assignments, string paramName, string? value)
    {
        ShowcaseParameterDefinition? def = FindByName(paramName);
        if (def != null)
        {
            assignments.Add(ShowcaseParameterAssignment.ForText(def, value));
        }
    }

    private static ShowcaseParameterDefinition CreateBuiltInDefinition(string name, string seedField, string description) =>
        new(
            name,
            ComputeParameterGuid(ShowcaseBuiltInScope, seedField, ShowcaseParameterDataType.Text),
            ShowcaseBuiltInScope,
            seedField,
            ShowcaseParameterDataType.Text,
            description,
            isBuiltIn: true);

    private static ShowcaseParameterDataType ResolveParameterDataType(ShowcaseFieldType fieldType, bool preserveAsText)
    {
        if (preserveAsText)
        {
            return ShowcaseParameterDataType.Text;
        }

        switch (fieldType)
        {
            case ShowcaseFieldType.Integer: return ShowcaseParameterDataType.Integer;
            case ShowcaseFieldType.Real: return ShowcaseParameterDataType.Number;
            case ShowcaseFieldType.Boolean: return ShowcaseParameterDataType.YesNo;
            case ShowcaseFieldType.Text:
            case ShowcaseFieldType.Date:
            case ShowcaseFieldType.Json:
            default:
                return ShowcaseParameterDataType.Text;
        }
    }

    private static ShowcaseFieldType InferFieldType(ShowcaseAttributeValue value)
    {
        switch (value.Kind)
        {
            case ShowcaseAttributeKind.Integer: return ShowcaseFieldType.Integer;
            case ShowcaseAttributeKind.Real: return ShowcaseFieldType.Real;
            case ShowcaseAttributeKind.Boolean: return ShowcaseFieldType.Boolean;
            case ShowcaseAttributeKind.Json: return ShowcaseFieldType.Json;
            default: return ShowcaseFieldType.Text;
        }
    }

    private static void RegisterCandidate(
        Dictionary<(ShowcaseLayerKind Kind, string FieldName), CandidateField> map,
        ShowcaseLayerKind kind,
        string fieldName,
        ShowcaseParameterDataType dataType,
        string? aliasJa,
        string? domain)
    {
        (ShowcaseLayerKind, string) key = (kind, fieldName);
        if (map.TryGetValue(key, out CandidateField? existing))
        {
            // If two layers of the same kind (e.g. 1F_unit and 2F_unit) disagree on type, widen losslessly to Text.
            if (existing.DataType != dataType)
            {
                existing.DataType = ShowcaseParameterDataType.Text;
            }

            if (string.IsNullOrWhiteSpace(existing.AliasJa) && !string.IsNullOrWhiteSpace(aliasJa))
            {
                existing.AliasJa = aliasJa;
            }

            if (string.IsNullOrWhiteSpace(existing.Domain) && !string.IsNullOrWhiteSpace(domain))
            {
                existing.Domain = domain;
            }

            return;
        }

        map[key] = new CandidateField(kind, fieldName, dataType, aliasJa, domain);
    }

    private static int GetFieldPriority(string fieldName)
    {
        string lower = fieldName.Trim().ToLowerInvariant();
        for (int i = 0; i < HighPriorityFieldNames.Length; i++)
        {
            if (string.Equals(lower, HighPriorityFieldNames[i], StringComparison.Ordinal))
            {
                return i;
            }
        }

        return HighPriorityFieldNames.Length;
    }

    private static string BuildParameterDescription(CandidateField candidate)
    {
        StringBuilder sb = new();
        sb.Append("Showcase attribute '").Append(candidate.SourceFieldName).Append('\'');
        if (!string.IsNullOrWhiteSpace(candidate.AliasJa))
        {
            sb.Append(" (").Append(candidate.AliasJa!.Trim()).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(candidate.Domain))
        {
            sb.Append(" [domain: ").Append(candidate.Domain!.Trim()).Append(']');
        }

        sb.Append('.');
        return sb.ToString();
    }

    private static string NormalizeSeedToken(string raw) =>
        raw.Trim().ToLowerInvariant();

    private static string SanitizeTsvDescription(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        return description
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private sealed class CandidateField
    {
        public CandidateField(
            ShowcaseLayerKind layerKind,
            string sourceFieldName,
            ShowcaseParameterDataType dataType,
            string? aliasJa,
            string? domain)
        {
            LayerKind = layerKind;
            SourceFieldName = sourceFieldName;
            DataType = dataType;
            AliasJa = aliasJa;
            Domain = domain;
        }

        public ShowcaseLayerKind LayerKind { get; }

        public string SourceFieldName { get; }

        public ShowcaseParameterDataType DataType { get; set; }

        public string? AliasJa { get; set; }

        public string? Domain { get; set; }
    }
}
