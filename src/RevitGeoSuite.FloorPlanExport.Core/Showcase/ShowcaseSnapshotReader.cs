using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Options controlling <see cref="ShowcaseSnapshotReader"/> validation.</summary>
public sealed class ShowcaseSnapshotReadOptions
{
    public static ShowcaseSnapshotReadOptions Default { get; } = new();

    /// <summary>
    /// When <c>true</c> (default), recomputes canonical SHA-256 over the snapshot content (excluding
    /// <c>writtenUtc</c> and <c>contentHash</c>) and rejects any mismatch.
    /// </summary>
    public bool VerifyContentHash { get; set; } = true;

    /// <summary>
    /// Maximum distance (in metres) allowed between the first and last vertex of a closed polygon ring.
    /// Matches Kanizu's 1 mm coordinate precision.
    /// </summary>
    public double RingClosureToleranceMeters { get; set; } = 0.001d;
}

/// <summary>
/// Strictly parses and validates read-only <c>kanizu.gis-showcase</c> v1 JSON snapshots written by Kanizu (Rust).
/// </summary>
public static class ShowcaseSnapshotReader
{
    private const double MinRingAreaSquareMeters = 1e-8d;

    private static readonly ShowcaseLayerKind[] CanonicalLayerKindOrder =
    {
        ShowcaseLayerKind.Unit,
        ShowcaseLayerKind.Opening,
        ShowcaseLayerKind.Fixture,
        ShowcaseLayerKind.Level,
        ShowcaseLayerKind.Detail,
        ShowcaseLayerKind.PointFacility,
        ShowcaseLayerKind.PointFacilityNetwork,
        ShowcaseLayerKind.Station,
        ShowcaseLayerKind.NetworkLink,
        ShowcaseLayerKind.NetworkConnector,
        ShowcaseLayerKind.NetworkNode,
    };

    private static readonly string[] CanonicalLayerKindTokenOrder =
    {
        "unit",
        "opening",
        "fixture",
        "level",
        "detail",
        "pointFacility",
        "pointFacilityNetwork",
        "station",
        "networkLink",
        "networkConnector",
        "networkNode",
    };

    /// <summary>Reads and strictly validates a <c>kanizu.gis-showcase</c> v1 JSON snapshot from disk.</summary>
    public static ShowcaseSnapshot ReadFile(string filePath, ShowcaseSnapshotReadOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Snapshot file path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Showcase snapshot file not found: '{filePath}'.", filePath);
        }

        string json = File.ReadAllText(filePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return Parse(json, options);
    }

    /// <summary>Parses and strictly validates a <c>kanizu.gis-showcase</c> v1 JSON snapshot from text.</summary>
    public static ShowcaseSnapshot Parse(string json, ShowcaseSnapshotReadOptions? options = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ShowcaseSnapshotException("Showcase snapshot JSON is empty.");
        }

        ShowcaseSnapshotReadOptions effectiveOptions = options ?? ShowcaseSnapshotReadOptions.Default;

        JObject root = LoadRootObject(json);

        string schema = ReadRequiredString(root, "schema", "top-level");
        if (!string.Equals(schema, ShowcaseContract.Schema, StringComparison.Ordinal))
        {
            throw new ShowcaseSnapshotException(
                $"Unsupported showcase schema '{schema}'; expected '{ShowcaseContract.Schema}'.");
        }

        int version = ReadRequiredInt32(root, "version", "top-level");
        if (version != ShowcaseContract.Version)
        {
            throw new ShowcaseSnapshotException(
                $"Unsupported showcase version {version}; expected version {ShowcaseContract.Version}.");
        }

        string scope = ReadRequiredNonBlankString(root, "scope", "top-level");
        string? basedOn = ReadOptionalString(root, "basedOn", "top-level");
        string writtenUtc = ReadRequiredString(root, "writtenUtc", "top-level");
        string kanizu = ReadRequiredString(root, "kanizu", "top-level");

        int epsg = ReadRequiredInt32(root, "epsg", "top-level");
        ValidateProjectedMetricEpsg(epsg);

        string contentHash = ReadOptionalString(root, "contentHash", "top-level") ?? string.Empty;

        IReadOnlyList<ShowcaseLevel> levels = ParseLevels(root);
        IReadOnlyList<ShowcaseLayer> layers = ParseLayers(root, effectiveOptions);
        IReadOnlyList<QuarantinedShowcaseFeature> quarantined = ParseQuarantined(root);
        ShowcaseSummary summary = ParseSummary(root, layers, quarantined);

        ShowcaseSnapshot snapshot = new(
            schema,
            version,
            scope,
            basedOn,
            writtenUtc,
            kanizu,
            epsg,
            contentHash,
            levels,
            layers,
            quarantined,
            summary);

        if (effectiveOptions.VerifyContentHash)
        {
            // Verify against the canonical projection preserving exact validated JSON numeric lexemes
            // from the source document so Rust serde_json/ryu hashes match byte-for-byte on both net48 and net10.0.
            string expectedHash = ComputeContentHashFromValidatedJson(json);
            if (string.IsNullOrWhiteSpace(contentHash) ||
                !string.Equals(contentHash, expectedHash, StringComparison.Ordinal))
            {
                throw new ShowcaseSnapshotException(
                    $"Showcase contentHash mismatch: stored '{contentHash}', expected '{expectedHash}'.");
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Validates that <paramref name="epsg"/> names a projected CRS in metres (rejecting zero/negative,
    /// geographic/angular CRSs such as EPSG:4326 and EPSG:6668, feet-based CRSs, and unknown codes).
    /// </summary>
    public static void ValidateProjectedMetricEpsg(int epsg)
    {
        if (!IsSupportedProjectedMetricEpsg(epsg))
        {
            throw new ShowcaseSnapshotException(
                $"EPSG:{epsg} is not a supported projected CRS in metres (missing, unknown, geographic, or non-metric CRSs are rejected; use e.g. Japan Plane Rectangular EPSG:6669..6687).");
        }
    }

    /// <summary>True when <paramref name="epsg"/> is a recognized projected CRS with linear unit in metres.</summary>
    public static bool IsSupportedProjectedMetricEpsg(int epsg)
    {
        if (epsg <= 0)
        {
            return false;
        }

        // JGD2011 / JGD2024 Japan Plane Rectangular CS Zones I-XIX (6669..6687)
        if (epsg >= 6669 && epsg <= 6687)
        {
            return true;
        }

        // JGD2000 Japan Plane Rectangular CS Zones I-XIX (2443..2461)
        if (epsg >= 2443 && epsg <= 2461)
        {
            return true;
        }

        // Tokyo Datum Japan Plane Rectangular CS Zones I-XIX (30161..30179)
        if (epsg >= 30161 && epsg <= 30179)
        {
            return true;
        }

        // JGD2011 UTM zones 51N-56N (6688..6692)
        if (epsg >= 6688 && epsg <= 6692)
        {
            return true;
        }

        // WGS 84 UTM North (32601..32660) & South (32701..32760)
        if ((epsg >= 32601 && epsg <= 32660) || (epsg >= 32701 && epsg <= 32760))
        {
            return true;
        }

        // ETRS89 / UTM (25828..25838) & NAD83 / UTM (26901..26923)
        if ((epsg >= 25828 && epsg <= 25838) || (epsg >= 26901 && epsg <= 26923))
        {
            return true;
        }

        // WGS 84 / Pseudo-Mercator (3857) - projected metres
        if (epsg == 3857)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the canonical lowercase-hex SHA-256 <c>contentHash</c> matching Kanizu Rust's
    /// <c>ShowcaseFile::compute_content_hash</c> (compact UTF-8 JSON of every field except <c>writtenUtc</c> and <c>contentHash</c>).
    /// </summary>
    public static string ComputeContentHash(ShowcaseSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        string canonicalJson = SerializeCanonicalContent(snapshot);
        return Sha256HexUtf8(canonicalJson);
    }

    /// <summary>
    /// Computes the canonical lowercase-hex SHA-256 <c>contentHash</c> directly from raw snapshot JSON text,
    /// preserving original validated JSON numeric lexemes so Kanizu Rust (<c>ryu</c>) hashes match identically
    /// across <c>net48</c> and <c>net10.0</c>.
    /// </summary>
    internal static string ComputeContentHashFromValidatedJson(string json)
    {
        RawJsonValue root = RawJsonParser.Parse(json);
        if (root.Kind != RawJsonKind.Object)
        {
            throw new ShowcaseSnapshotException("Showcase snapshot root must be a JSON object.");
        }

        string canonicalJson = RawJsonCanonicalWriter.SerializeCanonicalShowcase(root);
        return Sha256HexUtf8(canonicalJson);
    }

    /// <summary>
    /// Formats a finite <see cref="double"/> using Rust <c>ryu::pretty::format64</c> rules (fixed-point down to
    /// <c>1e-5</c>, lowercase <c>e</c> with unpadded exponent outside <c>[1e-5, 1e16)</c>, <c>.0</c> on integers,
    /// and exact shortest round-trip digits on both <c>net48</c> and <c>net10.0</c>).
    /// </summary>
    public static string FormatRyuDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ShowcaseSnapshotException($"Cannot canonically format non-finite double '{value}'.");
        }

        if (value == 0d)
        {
            return "0.0";
        }

        bool negative = value < 0d;
        double abs = negative ? -value : value;

        string shortest = FormatShortestPositiveDouble(abs);
        ParseDecimalParts(shortest, out string digits, out int k);

        int length = digits.Length;
        int kk = length + k; // 10^(kk - 1) <= abs < 10^kk

        StringBuilder sb = new(24);
        if (negative)
        {
            sb.Append('-');
        }

        if (0 <= k && kk <= 16)
        {
            // e.g. 1234e7 -> 12340000000.0
            sb.Append(digits);
            sb.Append('0', kk - length);
            sb.Append(".0");
        }
        else if (0 < kk && kk <= 16)
        {
            // e.g. 1234e-2 -> 12.34
            sb.Append(digits, 0, kk);
            sb.Append('.');
            sb.Append(digits, kk, length - kk);
        }
        else if (-5 < kk && kk <= 0)
        {
            // e.g. 1e-5 (kk = -4) -> 0.00001; 1234e-6 (kk = -2) -> 0.001234
            sb.Append("0.");
            if (kk < 0)
            {
                sb.Append('0', -kk);
            }

            sb.Append(digits);
        }
        else if (length == 1)
        {
            // e.g. 1e-6, 1e30
            sb.Append(digits[0]);
            sb.Append('e');
            sb.Append((kk - 1).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            // e.g. 1.234e-6, 1.234e33
            sb.Append(digits[0]);
            sb.Append('.');
            sb.Append(digits, 1, length - 1);
            sb.Append('e');
            sb.Append((kk - 1).ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string FormatShortestPositiveDouble(double positiveValue)
    {
#if NETFRAMEWORK
        // On .NET Framework 4.8, "G" truncates to 15 digits while "R" can emit unnecessary 17th digits.
        // Probing G15 -> G16 -> G17 yields the exact shortest IEEE-754 round-trip decimal.
        string g15 = positiveValue.ToString("G15", CultureInfo.InvariantCulture);
        if (double.TryParse(g15, NumberStyles.Float, CultureInfo.InvariantCulture, out double p15) && p15 == positiveValue)
        {
            return g15;
        }

        string g16 = positiveValue.ToString("G16", CultureInfo.InvariantCulture);
        if (double.TryParse(g16, NumberStyles.Float, CultureInfo.InvariantCulture, out double p16) && p16 == positiveValue)
        {
            return g16;
        }

        return positiveValue.ToString("G17", CultureInfo.InvariantCulture);
#else
        // .NET Core / .NET 10 uses Dragonbox shortest round-trip for "G".
        return positiveValue.ToString("G", CultureInfo.InvariantCulture);
#endif
    }

    private static void ParseDecimalParts(string decimalText, out string digits, out int k)
    {
        int expIndex = decimalText.IndexOf('E');
        if (expIndex < 0)
        {
            expIndex = decimalText.IndexOf('e');
        }

        int exp = 0;
        string mantissaPart;
        if (expIndex >= 0)
        {
            mantissaPart = decimalText.Substring(0, expIndex);
            exp = int.Parse(decimalText.Substring(expIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        else
        {
            mantissaPart = decimalText;
        }

        int dotIndex = mantissaPart.IndexOf('.');
        string rawDigits;
        int fracDigits = 0;
        if (dotIndex >= 0)
        {
            rawDigits = mantissaPart.Substring(0, dotIndex) + mantissaPart.Substring(dotIndex + 1);
            fracDigits = mantissaPart.Length - dotIndex - 1;
        }
        else
        {
            rawDigits = mantissaPart;
        }

        int start = 0;
        while (start < rawDigits.Length && rawDigits[start] == '0')
        {
            start++;
        }

        int end = rawDigits.Length - 1;
        while (end >= start && rawDigits[end] == '0')
        {
            end--;
        }

        int trailingZerosTrimmed = (rawDigits.Length - 1) - end;
        digits = rawDigits.Substring(start, end - start + 1);
        k = exp - fracDigits + trailingZerosTrimmed;
    }

    private static string Sha256HexUtf8(string text)
    {
        byte[] utf8Bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        using SHA256 sha256 = SHA256.Create();
        byte[] digest = sha256.ComputeHash(utf8Bytes);
        StringBuilder hex = new(digest.Length * 2);
        for (int i = 0; i < digest.Length; i++)
        {
            hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    /// <summary>Computes the canonical <see cref="ShowcaseSummary"/> for a layer and quarantine list.</summary>
    public static ShowcaseSummary ComputeSummary(
        IReadOnlyList<ShowcaseLayer> layers,
        IReadOnlyList<QuarantinedShowcaseFeature> quarantined)
    {
        Dictionary<ShowcaseLayerKind, int> counts = new();
        int totalFeatures = 0;
        foreach (ShowcaseLayer layer in layers)
        {
            int count = layer.Features.Count;
            totalFeatures += count;
            if (counts.TryGetValue(layer.Kind, out int existing))
            {
                counts[layer.Kind] = existing + count;
            }
            else
            {
                counts[layer.Kind] = count;
            }
        }

        return new ShowcaseSummary(counts, layers.Count, totalFeatures, quarantined.Count);
    }

    /// <summary>Formats a <see cref="ShowcaseLayerKind"/> to its exact <c>kanizu.gis-showcase</c> v1 camelCase token.</summary>
    public static string ToContractString(ShowcaseLayerKind kind)
    {
        switch (kind)
        {
            case ShowcaseLayerKind.Unit: return "unit";
            case ShowcaseLayerKind.Opening: return "opening";
            case ShowcaseLayerKind.Fixture: return "fixture";
            case ShowcaseLayerKind.Level: return "level";
            case ShowcaseLayerKind.Detail: return "detail";
            case ShowcaseLayerKind.PointFacility: return "pointFacility";
            case ShowcaseLayerKind.PointFacilityNetwork: return "pointFacilityNetwork";
            case ShowcaseLayerKind.Station: return "station";
            case ShowcaseLayerKind.NetworkLink: return "networkLink";
            case ShowcaseLayerKind.NetworkConnector: return "networkConnector";
            case ShowcaseLayerKind.NetworkNode: return "networkNode";
            default: throw new ShowcaseSnapshotException($"Unknown ShowcaseLayerKind '{kind}'.");
        }
    }

    /// <summary>Formats a <see cref="ShowcaseFieldType"/> to its exact <c>kanizu.gis-showcase</c> v1 camelCase token.</summary>
    public static string ToContractString(ShowcaseFieldType fieldType)
    {
        switch (fieldType)
        {
            case ShowcaseFieldType.Text: return "text";
            case ShowcaseFieldType.Integer: return "integer";
            case ShowcaseFieldType.Real: return "real";
            case ShowcaseFieldType.Boolean: return "boolean";
            case ShowcaseFieldType.Date: return "date";
            case ShowcaseFieldType.Json: return "json";
            default: throw new ShowcaseSnapshotException($"Unknown ShowcaseFieldType '{fieldType}'.");
        }
    }

    /// <summary>Formats a <see cref="ShowcaseElevationSource"/> to its exact camelCase token.</summary>
    public static string ToContractString(ShowcaseElevationSource source)
    {
        switch (source)
        {
            case ShowcaseElevationSource.Surveyed: return "surveyed";
            case ShowcaseElevationSource.Model: return "model";
            case ShowcaseElevationSource.Estimated: return "estimated";
            default: throw new ShowcaseSnapshotException($"Unknown ShowcaseElevationSource '{source}'.");
        }
    }

    /// <summary>Formats a <see cref="ShowcaseConnectorGeometryOrigin"/> to its exact camelCase token.</summary>
    public static string ToContractString(ShowcaseConnectorGeometryOrigin origin)
    {
        switch (origin)
        {
            case ShowcaseConnectorGeometryOrigin.SurveyedPath: return "surveyedPath";
            case ShowcaseConnectorGeometryOrigin.Schematic: return "schematic";
            default: throw new ShowcaseSnapshotException($"Unknown ShowcaseConnectorGeometryOrigin '{origin}'.");
        }
    }

    private static JObject LoadRootObject(string json)
    {
        try
        {
            using StringReader stringReader = new(json);
            using JsonTextReader jsonReader = new(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            };

            JToken token = JToken.ReadFrom(jsonReader);
            if (token is not JObject obj)
            {
                throw new ShowcaseSnapshotException("Showcase snapshot root must be a JSON object.");
            }

            return obj;
        }
        catch (ShowcaseSnapshotException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ShowcaseSnapshotException($"Invalid showcase snapshot JSON: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<ShowcaseLevel> ParseLevels(JObject root)
    {
        JArray array = ReadRequiredArray(root, "levels", "top-level");
        List<ShowcaseLevel> levels = new(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"levels[{i}] must be a JSON object.");
            }

            string ctx = $"levels[{i}]";
            string levelId = ReadRequiredNonBlankString(obj, "levelId", ctx);
            string floorKey = ReadRequiredNonBlankString(obj, "floorKey", ctx);
            long ordinal = ReadRequiredInt64(obj, "ordinal", ctx);
            string shortName = ReadRequiredString(obj, "shortName", ctx);
            double? tpM = ReadOptionalFiniteDouble(obj, "tpM", ctx);
            ShowcaseElevationSource elevationSource = ParseElevationSource(ReadRequiredString(obj, "elevationSource", ctx), ctx);

            levels.Add(new ShowcaseLevel(levelId, floorKey, ordinal, shortName, tpM, elevationSource));
        }

        return levels;
    }

    private static IReadOnlyList<ShowcaseLayer> ParseLayers(JObject root, ShowcaseSnapshotReadOptions options)
    {
        JArray array = ReadRequiredArray(root, "layers", "top-level");
        List<ShowcaseLayer> layers = new(array.Count);
        HashSet<string> seenLayerNames = new(StringComparer.Ordinal);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"layers[{i}] must be a JSON object.");
            }

            string ctx = $"layers[{i}]";
            string name = ReadRequiredNonBlankString(obj, "name", ctx);
            if (!seenLayerNames.Add(name))
            {
                throw new ShowcaseSnapshotException($"Duplicate layer name '{name}' in snapshot.");
            }

            ShowcaseLayerKind kind = ParseLayerKind(ReadRequiredString(obj, "kind", ctx), ctx);
            IReadOnlyList<ShowcaseField> fields = ParseFields(obj, name);
            IReadOnlyList<ShowcaseFeature> features = ParseFeatures(obj, name, options);

            layers.Add(new ShowcaseLayer(name, kind, fields, features));
        }

        return layers;
    }

    private static IReadOnlyList<ShowcaseField> ParseFields(JObject layerObj, string layerName)
    {
        JArray array = ReadRequiredArray(layerObj, "fields", $"layer '{layerName}'");
        List<ShowcaseField> fields = new(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"layer '{layerName}' fields[{i}] must be a JSON object.");
            }

            string ctx = $"layer '{layerName}' fields[{i}]";
            string fieldName = ReadRequiredNonBlankString(obj, "name", ctx);
            ShowcaseFieldType fieldType = ParseFieldType(ReadRequiredString(obj, "fieldType", ctx), ctx);
            string? aliasJa = ReadOptionalString(obj, "aliasJa", ctx);
            string? domain = ReadOptionalString(obj, "domain", ctx);
            bool preserveAsText = ReadOptionalBoolean(obj, "preserveAsText", ctx, defaultValue: false);

            fields.Add(new ShowcaseField(fieldName, fieldType, aliasJa, domain, preserveAsText));
        }

        return fields;
    }

    private static IReadOnlyList<ShowcaseFeature> ParseFeatures(
        JObject layerObj,
        string layerName,
        ShowcaseSnapshotReadOptions options)
    {
        JArray array = ReadRequiredArray(layerObj, "features", $"layer '{layerName}'");
        List<ShowcaseFeature> features = new(array.Count);
        HashSet<string> seenKeys = new(StringComparer.Ordinal);

        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"layer '{layerName}' features[{i}] must be a JSON object.");
            }

            string ctx = $"layer '{layerName}' features[{i}]";
            string key = ReadRequiredNonBlankString(obj, "key", ctx);
            if (!seenKeys.Add(key))
            {
                throw new ShowcaseSnapshotException($"Duplicate feature key '{key}' in layer '{layerName}'.");
            }

            string? levelId = ReadOptionalString(obj, "levelId", ctx);
            string? floorKey = ReadOptionalString(obj, "floorKey", ctx);

            if (obj["geometry"] is not JObject geomObj)
            {
                throw new ShowcaseSnapshotException($"{ctx} ('{key}') is missing a geometry object.");
            }

            ShowcaseGeometry geometry = ParseGeometry(geomObj, $"{layerName}:{key}", options);

            if (obj["attributes"] is not JObject attrsObj)
            {
                throw new ShowcaseSnapshotException($"{ctx} ('{key}') is missing an attributes object.");
            }

            IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes = ParseAttributes(attrsObj, $"{layerName}:{key}");
            features.Add(new ShowcaseFeature(key, levelId, floorKey, geometry, attributes));
        }

        return features;
    }

    private static ShowcaseGeometry ParseGeometry(JObject geomObj, string featureLabel, ShowcaseSnapshotReadOptions options)
    {
        string type = ReadRequiredNonBlankString(geomObj, "type", $"geometry of {featureLabel}");
        switch (type)
        {
            case "point":
            {
                (Point2D xy, double? embeddedZ) = ParseXyWithOptionalZ(geomObj["xy"], $"point geometry of {featureLabel}");
                double? z = ReadOptionalFiniteDouble(geomObj, "z", $"point geometry of {featureLabel}") ?? embeddedZ;
                return new ShowcasePointGeometry(xy, z);
            }

            case "multiPoint":
            {
                JToken? pointsToken = geomObj["points"] ?? geomObj["coords"];
                if (pointsToken is not JArray pointsArray || pointsArray.Count == 0)
                {
                    throw new ShowcaseSnapshotException($"multiPoint geometry of {featureLabel} must have at least one point.");
                }

                List<ShowcasePointGeometry> points = new(pointsArray.Count);
                for (int i = 0; i < pointsArray.Count; i++)
                {
                    JToken item = pointsArray[i];
                    if (item is JObject ptObj)
                    {
                        (Point2D xy, double? embeddedZ) = ParseXyWithOptionalZ(ptObj["xy"], $"multiPoint[{i}] of {featureLabel}");
                        double? z = ReadOptionalFiniteDouble(ptObj, "z", $"multiPoint[{i}] of {featureLabel}") ?? embeddedZ;
                        points.Add(new ShowcasePointGeometry(xy, z));
                    }
                    else
                    {
                        (Point2D xy, double? embeddedZ) = ParseXyWithOptionalZ(item, $"multiPoint[{i}] of {featureLabel}");
                        points.Add(new ShowcasePointGeometry(xy, embeddedZ));
                    }
                }

                return new ShowcaseMultiPointGeometry(points);
            }

            case "lineString":
            {
                (IReadOnlyList<Point2D> coords, IReadOnlyList<double?>? zs) = ParseCoordinateListWithOptionalZ(
                    geomObj["coords"],
                    minCount: 2,
                    $"lineString geometry of {featureLabel}");
                return new ShowcaseLineStringGeometry(coords, zs);
            }

            case "multiLineString":
            {
                if (geomObj["lines"] is not JArray linesArray || linesArray.Count == 0)
                {
                    throw new ShowcaseSnapshotException($"multiLineString geometry of {featureLabel} must have at least one line.");
                }

                List<IReadOnlyList<Point2D>> lines = new(linesArray.Count);
                List<IReadOnlyList<double?>?> lineZs = new(linesArray.Count);
                bool anyZ = false;
                for (int i = 0; i < linesArray.Count; i++)
                {
                    (IReadOnlyList<Point2D> coords, IReadOnlyList<double?>? zs) = ParseCoordinateListWithOptionalZ(
                        linesArray[i],
                        minCount: 2,
                        $"multiLineString[{i}] of {featureLabel}");
                    lines.Add(coords);
                    lineZs.Add(zs);
                    if (zs != null)
                    {
                        anyZ = true;
                    }
                }

                return new ShowcaseMultiLineStringGeometry(lines, anyZ ? lineZs : null);
            }

            case "polygon":
            {
                return ParsePolygonRings(geomObj["rings"], $"polygon geometry of {featureLabel}", options);
            }

            case "multiPolygon":
            {
                if (geomObj["polygons"] is not JArray polysArray || polysArray.Count == 0)
                {
                    throw new ShowcaseSnapshotException($"multiPolygon geometry of {featureLabel} must have at least one polygon.");
                }

                List<ShowcasePolygonGeometry> polygons = new(polysArray.Count);
                for (int i = 0; i < polysArray.Count; i++)
                {
                    polygons.Add(ParsePolygonRings(polysArray[i], $"multiPolygon[{i}] of {featureLabel}", options));
                }

                return new ShowcaseMultiPolygonGeometry(polygons);
            }

            case "connector3D":
            {
                if (geomObj["from"] is not JObject fromObj)
                {
                    throw new ShowcaseSnapshotException($"connector3D geometry of {featureLabel} is missing 'from' endpoint.");
                }

                if (geomObj["to"] is not JObject toObj)
                {
                    throw new ShowcaseSnapshotException($"connector3D geometry of {featureLabel} is missing 'to' endpoint.");
                }

                ShowcaseEndpoint from = ParseEndpoint(fromObj, $"connector3D.from of {featureLabel}");
                ShowcaseEndpoint to = ParseEndpoint(toObj, $"connector3D.to of {featureLabel}");
                IReadOnlyList<Point2D> path = ParseCoordinateList(geomObj["path"], minCount: 2, $"connector3D.path of {featureLabel}");
                ShowcaseConnectorGeometryOrigin origin = ParseConnectorGeometryOrigin(
                    ReadRequiredString(geomObj, "geometryOrigin", $"connector3D of {featureLabel}"),
                    $"connector3D of {featureLabel}");
                return new ShowcaseConnector3DGeometry(from, to, path, origin);
            }

            default:
                throw new ShowcaseSnapshotException($"Unsupported geometry type '{type}' on {featureLabel}.");
        }
    }

    private static ShowcaseEndpoint ParseEndpoint(JObject obj, string context)
    {
        string? levelId = ReadOptionalString(obj, "levelId", context);
        string? floorKey = ReadOptionalString(obj, "floorKey", context);
        Point2D xy = ParseXyPair(obj["xy"], context);
        double? tpM = ReadOptionalFiniteDouble(obj, "tpM", context);
        return new ShowcaseEndpoint(levelId, floorKey, xy, tpM);
    }

    private static ShowcasePolygonGeometry ParsePolygonRings(
        JToken? ringsToken,
        string context,
        ShowcaseSnapshotReadOptions options)
    {
        if (ringsToken is not JArray ringsArray || ringsArray.Count == 0)
        {
            throw new ShowcaseSnapshotException($"{context} must contain at least one ring.");
        }

        List<IReadOnlyList<Point2D>> rings = new(ringsArray.Count);
        for (int ringIndex = 0; ringIndex < ringsArray.Count; ringIndex++)
        {
            IReadOnlyList<Point2D> ring = ParseCoordinateList(
                ringsArray[ringIndex],
                minCount: 4,
                $"{context} ring[{ringIndex}]");

            ValidateClosedRing(ring, $"{context} ring[{ringIndex}]", options.RingClosureToleranceMeters);
            rings.Add(ring);
        }

        return new ShowcasePolygonGeometry(rings);
    }

    private static void ValidateClosedRing(IReadOnlyList<Point2D> ring, string context, double closureToleranceMeters)
    {
        if (ring.Count < 4)
        {
            throw new ShowcaseSnapshotException($"{context} has fewer than 4 vertices.");
        }

        Point2D first = ring[0];
        Point2D last = ring[ring.Count - 1];
        double dx = first.X - last.X;
        double dy = first.Y - last.Y;
        double closureDistance = Math.Sqrt((dx * dx) + (dy * dy));
        if (closureDistance > closureToleranceMeters)
        {
            throw new ShowcaseSnapshotException(
                $"{context} is not closed (start/end gap is {closureDistance:F4} m, tolerance {closureToleranceMeters:F4} m).");
        }

        double signedArea = ComputeSignedArea(ring);
        if (Math.Abs(signedArea) <= MinRingAreaSquareMeters)
        {
            throw new ShowcaseSnapshotException($"{context} is degenerate (zero area).");
        }
    }

    /// <summary>
    /// Computes the 2D signed area of a ring shifted relative to <c>ring[0]</c> so large projected CRS
    /// coordinates (e.g. EPSG:3857 at 15,550,000 m) never suffer catastrophic floating-point cancellation.
    /// </summary>
    public static double ComputeSignedArea(IReadOnlyList<Point2D> ring)
    {
        if (ring is null || ring.Count < 3)
        {
            return 0d;
        }

        double x0 = ring[0].X;
        double y0 = ring[0].Y;
        double twiceArea = 0d;
        for (int i = 0; i < ring.Count; i++)
        {
            Point2D a = ring[i];
            Point2D b = ring[(i + 1) % ring.Count];
            twiceArea += ((a.X - x0) * (b.Y - y0)) - ((b.X - x0) * (a.Y - y0));
        }

        return twiceArea * 0.5d;
    }

    private static (IReadOnlyList<Point2D> Coords, IReadOnlyList<double?>? Zs) ParseCoordinateListWithOptionalZ(
        JToken? token,
        int minCount,
        string context)
    {
        if (token is not JArray array || array.Count < minCount)
        {
            throw new ShowcaseSnapshotException($"{context} must be a coordinate array with at least {minCount} positions.");
        }

        List<Point2D> coords = new(array.Count);
        List<double?> zs = new(array.Count);
        bool anyZ = false;
        for (int i = 0; i < array.Count; i++)
        {
            (Point2D xy, double? z) = ParseXyWithOptionalZ(array[i], $"{context}[{i}]");
            coords.Add(xy);
            zs.Add(z);
            if (z.HasValue)
            {
                anyZ = true;
            }
        }

        return (coords, anyZ ? zs : null);
    }

    private static (Point2D Xy, double? Z) ParseXyWithOptionalZ(JToken? token, string context)
    {
        if (token is not JArray pair || pair.Count < 2)
        {
            throw new ShowcaseSnapshotException($"{context} must be a 2-element [x, y] or 3-element [x, y, z] coordinate array.");
        }

        double x = ParseFiniteDouble(pair[0], $"{context}[0]");
        double y = ParseFiniteDouble(pair[1], $"{context}[1]");
        double? z = null;
        if (pair.Count >= 3 && pair[2].Type != JTokenType.Null)
        {
            z = ParseFiniteDouble(pair[2], $"{context}[2]");
        }

        return (new Point2D(x == 0d ? 0d : x, y == 0d ? 0d : y), z);
    }

    private static IReadOnlyList<Point2D> ParseCoordinateList(JToken? token, int minCount, string context)
    {
        if (token is not JArray array || array.Count < minCount)
        {
            throw new ShowcaseSnapshotException($"{context} must be a coordinate array with at least {minCount} positions.");
        }

        List<Point2D> coords = new(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            coords.Add(ParseXyPair(array[i], $"{context}[{i}]"));
        }

        return coords;
    }

    private static Point2D ParseXyPair(JToken? token, string context)
    {
        if (token is not JArray pair || pair.Count < 2)
        {
            throw new ShowcaseSnapshotException($"{context} must be a 2-element [x, y] coordinate array.");
        }

        double x = ParseFiniteDouble(pair[0], $"{context}[0]");
        double y = ParseFiniteDouble(pair[1], $"{context}[1]");
        return new Point2D(x == 0d ? 0d : x, y == 0d ? 0d : y);
    }

    private static IReadOnlyDictionary<string, ShowcaseAttributeValue> ParseAttributes(JObject attrsObj, string featureLabel)
    {
        SortedDictionary<string, ShowcaseAttributeValue> attributes = new(StringComparer.Ordinal);
        foreach (JProperty property in attrsObj.Properties())
        {
            attributes[property.Name] = ParseAttributeValue(property.Value, $"{featureLabel}.{property.Name}");
        }

        return attributes;
    }

    private static ShowcaseAttributeValue ParseAttributeValue(JToken token, string context)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return ShowcaseAttributeValue.Null;

            case JTokenType.String:
                return ShowcaseAttributeValue.FromText(token.Value<string>() ?? string.Empty);

            case JTokenType.Integer:
            {
                object? raw = ((JValue)token).Value;
                if (raw is long l)
                {
                    return ShowcaseAttributeValue.FromInteger(l);
                }

                if (raw is int i)
                {
                    return ShowcaseAttributeValue.FromInteger(i);
                }

                if (raw is BigInteger big)
                {
                    if (big >= long.MinValue && big <= long.MaxValue)
                    {
                        return ShowcaseAttributeValue.FromInteger((long)big);
                    }

                    // Preserve integers larger than Int64 losslessly as exact decimal text.
                    return ShowcaseAttributeValue.FromText(big.ToString(CultureInfo.InvariantCulture));
                }

                return ShowcaseAttributeValue.FromInteger(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
            }

            case JTokenType.Float:
            {
                double val = ParseFiniteDouble(token, context);
                return ShowcaseAttributeValue.FromReal(val);
            }

            case JTokenType.Boolean:
                return ShowcaseAttributeValue.FromBoolean(token.Value<bool>());

            case JTokenType.Object:
            case JTokenType.Array:
                return ShowcaseAttributeValue.FromRawJson(token.ToString(Formatting.None));

            default:
                throw new ShowcaseSnapshotException($"Unsupported attribute token type '{token.Type}' at {context}.");
        }
    }

    private static IReadOnlyList<QuarantinedShowcaseFeature> ParseQuarantined(JObject root)
    {
        JToken? token = root["quarantined"];
        if (token == null || token.Type == JTokenType.Null)
        {
            return Array.Empty<QuarantinedShowcaseFeature>();
        }

        if (token is not JArray array)
        {
            throw new ShowcaseSnapshotException("Field 'quarantined' must be a JSON array.");
        }

        List<QuarantinedShowcaseFeature> result = new(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"quarantined[{i}] must be a JSON object.");
            }

            string ctx = $"quarantined[{i}]";
            string layer = ReadRequiredString(obj, "layer", ctx);
            string sourceKey = ReadRequiredString(obj, "sourceKey", ctx);
            string reason = ReadRequiredString(obj, "reason", ctx);
            result.Add(new QuarantinedShowcaseFeature(layer, sourceKey, reason));
        }

        return result;
    }

    private static ShowcaseSummary ParseSummary(
        JObject root,
        IReadOnlyList<ShowcaseLayer> layers,
        IReadOnlyList<QuarantinedShowcaseFeature> quarantined)
    {
        if (root["summary"] is not JObject summaryObj)
        {
            throw new ShowcaseSnapshotException("Top-level 'summary' object is required.");
        }

        Dictionary<ShowcaseLayerKind, int> countsByKind = new();
        if (summaryObj["countsByKind"] is JObject countsObj)
        {
            foreach (JProperty prop in countsObj.Properties())
            {
                ShowcaseLayerKind kind = ParseLayerKind(prop.Name, "summary.countsByKind");
                int count = ReadRequiredInt32(countsObj, prop.Name, "summary.countsByKind");
                if (count < 0)
                {
                    throw new ShowcaseSnapshotException($"summary.countsByKind.{prop.Name} must be non-negative.");
                }

                countsByKind[kind] = count;
            }
        }
        else
        {
            throw new ShowcaseSnapshotException("summary.countsByKind object is required.");
        }

        int totalLayers = ReadRequiredInt32(summaryObj, "totalLayers", "summary");
        int totalFeatures = ReadRequiredInt32(summaryObj, "totalFeatures", "summary");
        int totalQuarantined = ReadRequiredInt32(summaryObj, "totalQuarantined", "summary");

        ShowcaseSummary expected = ComputeSummary(layers, quarantined);
        if (totalLayers != expected.TotalLayers
            || totalFeatures != expected.TotalFeatures
            || totalQuarantined != expected.TotalQuarantined)
        {
            throw new ShowcaseSnapshotException(
                $"Showcase summary counts do not match payload (summary: {totalLayers} layers / {totalFeatures} features / {totalQuarantined} quarantined; actual: {expected.TotalLayers} / {expected.TotalFeatures} / {expected.TotalQuarantined}).");
        }

        foreach (KeyValuePair<ShowcaseLayerKind, int> kvp in expected.CountsByKind)
        {
            int reported = countsByKind.TryGetValue(kvp.Key, out int value) ? value : 0;
            if (reported != kvp.Value)
            {
                throw new ShowcaseSnapshotException(
                    $"Showcase summary countsByKind.{ToContractString(kvp.Key)} ({reported}) does not match actual feature count ({kvp.Value}).");
            }
        }

        foreach (KeyValuePair<ShowcaseLayerKind, int> kvp in countsByKind)
        {
            int actual = expected.CountsByKind.TryGetValue(kvp.Key, out int value) ? value : 0;
            if (kvp.Value != actual)
            {
                throw new ShowcaseSnapshotException(
                    $"Showcase summary countsByKind.{ToContractString(kvp.Key)} ({kvp.Value}) does not match actual feature count ({actual}).");
            }
        }

        return new ShowcaseSummary(countsByKind, totalLayers, totalFeatures, totalQuarantined);
    }

    private static ShowcaseElevationSource ParseElevationSource(string raw, string context)
    {
        switch (raw)
        {
            case "surveyed": return ShowcaseElevationSource.Surveyed;
            case "model": return ShowcaseElevationSource.Model;
            case "estimated": return ShowcaseElevationSource.Estimated;
            default:
                throw new ShowcaseSnapshotException($"Invalid elevationSource '{raw}' in {context}.");
        }
    }

    private static ShowcaseLayerKind ParseLayerKind(string raw, string context)
    {
        switch (raw)
        {
            case "unit": return ShowcaseLayerKind.Unit;
            case "opening": return ShowcaseLayerKind.Opening;
            case "fixture": return ShowcaseLayerKind.Fixture;
            case "level": return ShowcaseLayerKind.Level;
            case "detail": return ShowcaseLayerKind.Detail;
            case "pointFacility": return ShowcaseLayerKind.PointFacility;
            case "pointFacilityNetwork": return ShowcaseLayerKind.PointFacilityNetwork;
            case "station": return ShowcaseLayerKind.Station;
            case "networkLink": return ShowcaseLayerKind.NetworkLink;
            case "networkConnector": return ShowcaseLayerKind.NetworkConnector;
            case "networkNode": return ShowcaseLayerKind.NetworkNode;
            default:
                throw new ShowcaseSnapshotException($"Invalid layer kind '{raw}' in {context}.");
        }
    }

    private static ShowcaseFieldType ParseFieldType(string raw, string context)
    {
        switch (raw)
        {
            case "text": return ShowcaseFieldType.Text;
            case "integer": return ShowcaseFieldType.Integer;
            case "real": return ShowcaseFieldType.Real;
            case "boolean": return ShowcaseFieldType.Boolean;
            case "date": return ShowcaseFieldType.Date;
            case "json": return ShowcaseFieldType.Json;
            default:
                throw new ShowcaseSnapshotException($"Invalid fieldType '{raw}' in {context}.");
        }
    }

    private static ShowcaseConnectorGeometryOrigin ParseConnectorGeometryOrigin(string raw, string context)
    {
        switch (raw)
        {
            case "surveyedPath": return ShowcaseConnectorGeometryOrigin.SurveyedPath;
            case "schematic": return ShowcaseConnectorGeometryOrigin.Schematic;
            default:
                throw new ShowcaseSnapshotException($"Invalid geometryOrigin '{raw}' in {context}.");
        }
    }

    private static JArray ReadRequiredArray(JObject obj, string propertyName, string context)
    {
        if (obj[propertyName] is not JArray array)
        {
            throw new ShowcaseSnapshotException($"Required array '{propertyName}' is missing or invalid in {context}.");
        }

        return array;
    }

    private static string ReadRequiredString(JObject obj, string propertyName, string context)
    {
        JToken? token = obj[propertyName];
        if (token is null || token.Type != JTokenType.String)
        {
            throw new ShowcaseSnapshotException($"Required string '{propertyName}' is missing or invalid in {context}.");
        }

        return token.Value<string>() ?? string.Empty;
    }

    private static string ReadRequiredNonBlankString(JObject obj, string propertyName, string context)
    {
        string value = ReadRequiredString(obj, propertyName, context);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ShowcaseSnapshotException($"Required string '{propertyName}' must not be blank in {context}.");
        }

        return value;
    }

    private static string? ReadOptionalString(JObject obj, string propertyName, string context)
    {
        JToken? token = obj[propertyName];
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token.Type != JTokenType.String)
        {
            throw new ShowcaseSnapshotException($"Optional field '{propertyName}' must be a string or null in {context}.");
        }

        return token.Value<string>();
    }

    private static bool ReadOptionalBoolean(JObject obj, string propertyName, string context, bool defaultValue)
    {
        JToken? token = obj[propertyName];
        if (token is null || token.Type == JTokenType.Null)
        {
            return defaultValue;
        }

        if (token.Type != JTokenType.Boolean)
        {
            throw new ShowcaseSnapshotException($"Optional field '{propertyName}' must be a boolean in {context}.");
        }

        return token.Value<bool>();
    }

    private static int ReadRequiredInt32(JObject obj, string propertyName, string context)
    {
        long value = ReadRequiredInt64(obj, propertyName, context);
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new ShowcaseSnapshotException($"Integer '{propertyName}' ({value}) is out of 32-bit range in {context}.");
        }

        return (int)value;
    }

    private static long ReadRequiredInt64(JObject obj, string propertyName, string context)
    {
        JToken? token = obj[propertyName];
        if (token is null || token.Type != JTokenType.Integer)
        {
            throw new ShowcaseSnapshotException($"Required integer '{propertyName}' is missing or invalid in {context}.");
        }

        object? raw = ((JValue)token).Value;
        if (raw is long l)
        {
            return l;
        }

        if (raw is int i)
        {
            return i;
        }

        throw new ShowcaseSnapshotException($"Integer '{propertyName}' is out of 64-bit signed range in {context}.");
    }

    private static double? ReadOptionalFiniteDouble(JObject obj, string propertyName, string context)
    {
        JToken? token = obj[propertyName];
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        return ParseFiniteDouble(token, $"{context}.{propertyName}");
    }

    private static double ParseFiniteDouble(JToken? token, string context)
    {
        if (token is null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
        {
            throw new ShowcaseSnapshotException($"Expected finite numeric value at {context}.");
        }

        double value = token.Value<double>();
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ShowcaseSnapshotException($"Non-finite numeric value at {context} is not allowed.");
        }

        return value == 0d ? 0d : value;
    }

    private static string SerializeCanonicalContent(ShowcaseSnapshot snapshot)
    {
        StringBuilder sb = new();
        using (StringWriter sw = new(sb, CultureInfo.InvariantCulture))
        using (JsonTextWriter writer = new(sw))
        {
            writer.Formatting = Formatting.None;
            writer.WriteStartObject();

            writer.WritePropertyName("schema");
            writer.WriteValue(snapshot.Schema);

            writer.WritePropertyName("version");
            writer.WriteValue(snapshot.Version);

            writer.WritePropertyName("scope");
            writer.WriteValue(snapshot.Scope);

            if (snapshot.BasedOn != null)
            {
                writer.WritePropertyName("basedOn");
                writer.WriteValue(snapshot.BasedOn);
            }

            writer.WritePropertyName("kanizu");
            writer.WriteValue(snapshot.Kanizu);

            writer.WritePropertyName("epsg");
            writer.WriteValue(snapshot.Epsg);

            writer.WritePropertyName("levels");
            writer.WriteStartArray();
            foreach (ShowcaseLevel level in snapshot.Levels)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("levelId");
                writer.WriteValue(level.LevelId);
                writer.WritePropertyName("floorKey");
                writer.WriteValue(level.FloorKey);
                writer.WritePropertyName("ordinal");
                writer.WriteValue(level.Ordinal);
                writer.WritePropertyName("shortName");
                writer.WriteValue(level.ShortName);
                if (level.TpM.HasValue)
                {
                    writer.WritePropertyName("tpM");
                    WriteCanonicalDouble(writer, level.TpM.Value);
                }

                writer.WritePropertyName("elevationSource");
                writer.WriteValue(ToContractString(level.ElevationSource));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("layers");
            writer.WriteStartArray();
            foreach (ShowcaseLayer layer in snapshot.Layers)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(layer.Name);
                writer.WritePropertyName("kind");
                writer.WriteValue(ToContractString(layer.Kind));

                writer.WritePropertyName("fields");
                writer.WriteStartArray();
                foreach (ShowcaseField field in layer.Fields)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteValue(field.Name);
                    writer.WritePropertyName("fieldType");
                    writer.WriteValue(ToContractString(field.FieldType));
                    if (field.AliasJa != null)
                    {
                        writer.WritePropertyName("aliasJa");
                        writer.WriteValue(field.AliasJa);
                    }

                    if (field.Domain != null)
                    {
                        writer.WritePropertyName("domain");
                        writer.WriteValue(field.Domain);
                    }

                    writer.WritePropertyName("preserveAsText");
                    writer.WriteValue(field.PreserveAsText);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

                writer.WritePropertyName("features");
                writer.WriteStartArray();
                foreach (ShowcaseFeature feature in layer.Features)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("key");
                    writer.WriteValue(feature.Key);
                    if (feature.LevelId != null)
                    {
                        writer.WritePropertyName("levelId");
                        writer.WriteValue(feature.LevelId);
                    }

                    if (feature.FloorKey != null)
                    {
                        writer.WritePropertyName("floorKey");
                        writer.WriteValue(feature.FloorKey);
                    }

                    writer.WritePropertyName("geometry");
                    WriteCanonicalGeometry(writer, feature.Geometry);

                    writer.WritePropertyName("attributes");
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, ShowcaseAttributeValue> attr in feature.Attributes.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(attr.Key);
                        WriteCanonicalAttributeValue(writer, attr.Value);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("quarantined");
            writer.WriteStartArray();
            foreach (QuarantinedShowcaseFeature q in snapshot.Quarantined)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("layer");
                writer.WriteValue(q.Layer);
                writer.WritePropertyName("sourceKey");
                writer.WriteValue(q.SourceKey);
                writer.WritePropertyName("reason");
                writer.WriteValue(q.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("summary");
            writer.WriteStartObject();
            writer.WritePropertyName("countsByKind");
            writer.WriteStartObject();
            foreach (ShowcaseLayerKind kind in CanonicalLayerKindOrder)
            {
                if (snapshot.Summary.CountsByKind.TryGetValue(kind, out int count))
                {
                    writer.WritePropertyName(ToContractString(kind));
                    writer.WriteValue(count);
                }
            }

            writer.WriteEndObject();
            writer.WritePropertyName("totalLayers");
            writer.WriteValue(snapshot.Summary.TotalLayers);
            writer.WritePropertyName("totalFeatures");
            writer.WriteValue(snapshot.Summary.TotalFeatures);
            writer.WritePropertyName("totalQuarantined");
            writer.WriteValue(snapshot.Summary.TotalQuarantined);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return sb.ToString();
    }

    private static void WriteCanonicalGeometry(JsonTextWriter writer, ShowcaseGeometry geometry)
    {
        writer.WriteStartObject();
        switch (geometry)
        {
            case ShowcasePointGeometry point:
                writer.WritePropertyName("type");
                writer.WriteValue("point");
                writer.WritePropertyName("xy");
                WriteCanonicalXy(writer, point.Xy);
                if (point.Z.HasValue)
                {
                    writer.WritePropertyName("z");
                    WriteCanonicalDouble(writer, point.Z.Value);
                }

                break;

            case ShowcaseMultiPointGeometry multiPoint:
                writer.WritePropertyName("type");
                writer.WriteValue("multiPoint");
                writer.WritePropertyName("points");
                writer.WriteStartArray();
                foreach (ShowcasePointGeometry pt in multiPoint.Points)
                {
                    if (pt.Z.HasValue)
                    {
                        WriteCanonicalXyz(writer, pt.Xy, pt.Z.Value);
                    }
                    else
                    {
                        WriteCanonicalXy(writer, pt.Xy);
                    }
                }

                writer.WriteEndArray();
                break;

            case ShowcaseLineStringGeometry line:
                writer.WritePropertyName("type");
                writer.WriteValue("lineString");
                writer.WritePropertyName("coords");
                WriteCanonicalCoordinateListWithOptionalZ(writer, line.Coords, line.Zs);
                break;

            case ShowcaseMultiLineStringGeometry multiLine:
                writer.WritePropertyName("type");
                writer.WriteValue("multiLineString");
                writer.WritePropertyName("lines");
                writer.WriteStartArray();
                for (int i = 0; i < multiLine.Lines.Count; i++)
                {
                    IReadOnlyList<double?>? zs = multiLine.LineZs != null && i < multiLine.LineZs.Count
                        ? multiLine.LineZs[i]
                        : null;
                    WriteCanonicalCoordinateListWithOptionalZ(writer, multiLine.Lines[i], zs);
                }

                writer.WriteEndArray();
                break;

            case ShowcasePolygonGeometry polygon:
                writer.WritePropertyName("type");
                writer.WriteValue("polygon");
                writer.WritePropertyName("rings");
                WriteCanonicalRings(writer, polygon.Rings);
                break;

            case ShowcaseMultiPolygonGeometry multiPolygon:
                writer.WritePropertyName("type");
                writer.WriteValue("multiPolygon");
                writer.WritePropertyName("polygons");
                writer.WriteStartArray();
                foreach (ShowcasePolygonGeometry poly in multiPolygon.Polygons)
                {
                    WriteCanonicalRings(writer, poly.Rings);
                }

                writer.WriteEndArray();
                break;

            case ShowcaseConnector3DGeometry connector:
                writer.WritePropertyName("type");
                writer.WriteValue("connector3D");
                writer.WritePropertyName("from");
                WriteCanonicalEndpoint(writer, connector.From);
                writer.WritePropertyName("to");
                WriteCanonicalEndpoint(writer, connector.To);
                writer.WritePropertyName("path");
                WriteCanonicalCoordinateList(writer, connector.Path);
                writer.WritePropertyName("geometryOrigin");
                writer.WriteValue(ToContractString(connector.GeometryOrigin));
                break;

            default:
                throw new ShowcaseSnapshotException($"Unsupported geometry instance '{geometry.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalEndpoint(JsonTextWriter writer, ShowcaseEndpoint endpoint)
    {
        writer.WriteStartObject();
        if (endpoint.LevelId != null)
        {
            writer.WritePropertyName("levelId");
            writer.WriteValue(endpoint.LevelId);
        }

        if (endpoint.FloorKey != null)
        {
            writer.WritePropertyName("floorKey");
            writer.WriteValue(endpoint.FloorKey);
        }

        writer.WritePropertyName("xy");
        WriteCanonicalXy(writer, endpoint.Xy);
        if (endpoint.TpM.HasValue)
        {
            writer.WritePropertyName("tpM");
            WriteCanonicalDouble(writer, endpoint.TpM.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalRings(JsonTextWriter writer, IReadOnlyList<IReadOnlyList<Point2D>> rings)
    {
        writer.WriteStartArray();
        foreach (IReadOnlyList<Point2D> ring in rings)
        {
            WriteCanonicalCoordinateList(writer, ring);
        }

        writer.WriteEndArray();
    }

    private static void WriteCanonicalCoordinateListWithOptionalZ(
        JsonTextWriter writer,
        IReadOnlyList<Point2D> coords,
        IReadOnlyList<double?>? zs)
    {
        writer.WriteStartArray();
        for (int i = 0; i < coords.Count; i++)
        {
            double? z = zs != null && i < zs.Count ? zs[i] : null;
            if (z.HasValue)
            {
                WriteCanonicalXyz(writer, coords[i], z.Value);
            }
            else
            {
                WriteCanonicalXy(writer, coords[i]);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteCanonicalXyz(JsonTextWriter writer, Point2D pt, double z)
    {
        writer.WriteStartArray();
        WriteCanonicalDouble(writer, pt.X);
        WriteCanonicalDouble(writer, pt.Y);
        WriteCanonicalDouble(writer, z);
        writer.WriteEndArray();
    }

    private static void WriteCanonicalCoordinateList(JsonTextWriter writer, IReadOnlyList<Point2D> coords)
    {
        writer.WriteStartArray();
        foreach (Point2D pt in coords)
        {
            WriteCanonicalXy(writer, pt);
        }

        writer.WriteEndArray();
    }

    private static void WriteCanonicalXy(JsonTextWriter writer, Point2D pt)
    {
        writer.WriteStartArray();
        WriteCanonicalDouble(writer, pt.X);
        WriteCanonicalDouble(writer, pt.Y);
        writer.WriteEndArray();
    }

    private static void WriteCanonicalAttributeValue(JsonTextWriter writer, ShowcaseAttributeValue value)
    {
        switch (value.Kind)
        {
            case ShowcaseAttributeKind.Null:
                writer.WriteNull();
                break;
            case ShowcaseAttributeKind.Text:
                writer.WriteValue(value.TextValue ?? string.Empty);
                break;
            case ShowcaseAttributeKind.Integer:
                writer.WriteValue(value.Int64Value!.Value);
                break;
            case ShowcaseAttributeKind.Real:
                WriteCanonicalDouble(writer, value.RealValue!.Value);
                break;
            case ShowcaseAttributeKind.Boolean:
                writer.WriteValue(value.BoolValue!.Value);
                break;
            case ShowcaseAttributeKind.Json:
                writer.WriteRawValue(value.RawJsonText ?? "null");
                break;
            default:
                writer.WriteNull();
                break;
        }
    }

    private static void WriteCanonicalDouble(JsonTextWriter writer, double value)
    {
        writer.WriteRawValue(FormatRyuDouble(value));
    }

    // -----------------------------------------------------------------------
    // Raw-token JSON parser & canonical projector for byte-exact contentHash
    // verification against Kanizu Rust (serde_json / ryu) on net48 + net10.0.
    // -----------------------------------------------------------------------

    private enum RawJsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    private sealed class RawJsonValue
    {
        public RawJsonValue(
            RawJsonKind kind,
            string? text = null,
            List<RawJsonValue>? arrayItems = null,
            Dictionary<string, RawJsonValue>? objectMembers = null)
        {
            Kind = kind;
            Text = text;
            ArrayItems = arrayItems;
            ObjectMembers = objectMembers;
        }

        public RawJsonKind Kind { get; }

        public string? Text { get; }

        public List<RawJsonValue>? ArrayItems { get; }

        public Dictionary<string, RawJsonValue>? ObjectMembers { get; }

        public bool TryGetProperty(string name, out RawJsonValue value)
        {
            if (ObjectMembers != null && ObjectMembers.TryGetValue(name, out RawJsonValue? found) && found != null)
            {
                value = found;
                return true;
            }

            value = null!;
            return false;
        }
    }

    private struct RawJsonParser
    {
        private readonly string _src;
        private int _pos;

        private RawJsonParser(string src)
        {
            _src = src;
            _pos = 0;
        }

        public static RawJsonValue Parse(string json)
        {
            RawJsonParser parser = new(json);
            parser.SkipWhitespace();
            RawJsonValue value = parser.ParseValue();
            parser.SkipWhitespace();
            return value;
        }

        private RawJsonValue ParseValue()
        {
            SkipWhitespace();
            if (_pos >= _src.Length)
            {
                throw new ShowcaseSnapshotException("Unexpected end of JSON while verifying contentHash.");
            }

            char c = _src[_pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return new RawJsonValue(RawJsonKind.String, text: ParseString());
                case 't':
                    ExpectLiteral("true");
                    return new RawJsonValue(RawJsonKind.Bool, text: "true");
                case 'f':
                    ExpectLiteral("false");
                    return new RawJsonValue(RawJsonKind.Bool, text: "false");
                case 'n':
                    ExpectLiteral("null");
                    return new RawJsonValue(RawJsonKind.Null, text: "null");
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return new RawJsonValue(RawJsonKind.Number, text: ParseNumberLexeme());
                    }

                    throw new ShowcaseSnapshotException($"Unexpected JSON token '{c}' at offset {_pos}.");
            }
        }

        private RawJsonValue ParseObject()
        {
            _pos++; // '{'
            Dictionary<string, RawJsonValue> members = new(StringComparer.Ordinal);
            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == '}')
            {
                _pos++;
                return new RawJsonValue(RawJsonKind.Object, objectMembers: members);
            }

            while (true)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();
                if (_pos >= _src.Length || _src[_pos] != ':')
                {
                    throw new ShowcaseSnapshotException($"Expected ':' after object key '{key}'.");
                }

                _pos++;
                RawJsonValue val = ParseValue();
                members[key] = val;
                SkipWhitespace();
                if (_pos < _src.Length && _src[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                if (_pos < _src.Length && _src[_pos] == '}')
                {
                    _pos++;
                    return new RawJsonValue(RawJsonKind.Object, objectMembers: members);
                }

                throw new ShowcaseSnapshotException($"Expected ',' or '}}' in JSON object at offset {_pos}.");
            }
        }

        private RawJsonValue ParseArray()
        {
            _pos++; // '['
            List<RawJsonValue> items = new();
            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == ']')
            {
                _pos++;
                return new RawJsonValue(RawJsonKind.Array, arrayItems: items);
            }

            while (true)
            {
                items.Add(ParseValue());
                SkipWhitespace();
                if (_pos < _src.Length && _src[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                if (_pos < _src.Length && _src[_pos] == ']')
                {
                    _pos++;
                    return new RawJsonValue(RawJsonKind.Array, arrayItems: items);
                }

                throw new ShowcaseSnapshotException($"Expected ',' or ']' in JSON array at offset {_pos}.");
            }
        }

        private string ParseString()
        {
            if (_pos >= _src.Length || _src[_pos] != '"')
            {
                throw new ShowcaseSnapshotException($"Expected JSON string at offset {_pos}.");
            }

            _pos++; // opening '"'
            StringBuilder sb = new();
            while (_pos < _src.Length)
            {
                char c = _src[_pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    if (_pos >= _src.Length)
                    {
                        throw new ShowcaseSnapshotException("Unterminated JSON string escape.");
                    }

                    char esc = _src[_pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _src.Length)
                            {
                                throw new ShowcaseSnapshotException("Truncated \\uXXXX escape in JSON string.");
                            }

                            string hex = _src.Substring(_pos, 4);
                            sb.Append((char)Convert.ToUInt16(hex, 16));
                            _pos += 4;
                            break;
                        default:
                            throw new ShowcaseSnapshotException($"Invalid escape '\\{esc}' in JSON string.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw new ShowcaseSnapshotException("Unterminated JSON string.");
        }

        private string ParseNumberLexeme()
        {
            int start = _pos;
            if (_src[_pos] == '-')
            {
                _pos++;
            }

            while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9')
            {
                _pos++;
            }

            if (_pos < _src.Length && _src[_pos] == '.')
            {
                _pos++;
                while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9')
                {
                    _pos++;
                }
            }

            if (_pos < _src.Length && (_src[_pos] == 'e' || _src[_pos] == 'E'))
            {
                _pos++;
                if (_pos < _src.Length && (_src[_pos] == '+' || _src[_pos] == '-'))
                {
                    _pos++;
                }

                while (_pos < _src.Length && _src[_pos] >= '0' && _src[_pos] <= '9')
                {
                    _pos++;
                }
            }

            return _src.Substring(start, _pos - start);
        }

        private void ExpectLiteral(string literal)
        {
            if (_pos + literal.Length > _src.Length ||
                string.CompareOrdinal(_src, _pos, literal, 0, literal.Length) != 0)
            {
                throw new ShowcaseSnapshotException($"Expected JSON literal '{literal}' at offset {_pos}.");
            }

            _pos += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_pos < _src.Length)
            {
                char c = _src[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    _pos++;
                }
                else
                {
                    break;
                }
            }
        }
    }

    private static class RawJsonCanonicalWriter
    {
        public static string SerializeCanonicalShowcase(RawJsonValue root)
        {
            StringBuilder sb = new();
            using (StringWriter sw = new(sb, CultureInfo.InvariantCulture))
            using (JsonTextWriter writer = new(sw))
            {
                writer.Formatting = Formatting.None;
                writer.WriteStartObject();

                WriteRequiredProp(writer, root, "schema", forceFloat: false);
                WriteRequiredProp(writer, root, "version", forceFloat: false);
                WriteRequiredProp(writer, root, "scope", forceFloat: false);
                WriteOptionalNonNullProp(writer, root, "basedOn", forceFloat: false);
                WriteRequiredProp(writer, root, "kanizu", forceFloat: false);
                WriteRequiredProp(writer, root, "epsg", forceFloat: false);

                writer.WritePropertyName("levels");
                writer.WriteStartArray();
                if (root.TryGetProperty("levels", out RawJsonValue levelsVal) && levelsVal.ArrayItems != null)
                {
                    foreach (RawJsonValue level in levelsVal.ArrayItems)
                    {
                        writer.WriteStartObject();
                        WriteRequiredProp(writer, level, "levelId", forceFloat: false);
                        WriteRequiredProp(writer, level, "floorKey", forceFloat: false);
                        WriteRequiredProp(writer, level, "ordinal", forceFloat: false);
                        WriteRequiredProp(writer, level, "shortName", forceFloat: false);
                        WriteOptionalNonNullProp(writer, level, "tpM", forceFloat: true);
                        WriteRequiredProp(writer, level, "elevationSource", forceFloat: false);
                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();

                writer.WritePropertyName("layers");
                writer.WriteStartArray();
                if (root.TryGetProperty("layers", out RawJsonValue layersVal) && layersVal.ArrayItems != null)
                {
                    foreach (RawJsonValue layer in layersVal.ArrayItems)
                    {
                        writer.WriteStartObject();
                        WriteRequiredProp(writer, layer, "name", forceFloat: false);
                        WriteRequiredProp(writer, layer, "kind", forceFloat: false);

                        writer.WritePropertyName("fields");
                        writer.WriteStartArray();
                        if (layer.TryGetProperty("fields", out RawJsonValue fieldsVal) && fieldsVal.ArrayItems != null)
                        {
                            foreach (RawJsonValue field in fieldsVal.ArrayItems)
                            {
                                writer.WriteStartObject();
                                WriteRequiredProp(writer, field, "name", forceFloat: false);
                                WriteRequiredProp(writer, field, "fieldType", forceFloat: false);
                                WriteOptionalNonNullProp(writer, field, "aliasJa", forceFloat: false);
                                WriteOptionalNonNullProp(writer, field, "domain", forceFloat: false);
                                writer.WritePropertyName("preserveAsText");
                                if (field.TryGetProperty("preserveAsText", out RawJsonValue preserveVal) && preserveVal.Kind == RawJsonKind.Bool)
                                {
                                    writer.WriteRawValue(preserveVal.Text!);
                                }
                                else
                                {
                                    writer.WriteValue(false);
                                }

                                writer.WriteEndObject();
                            }
                        }

                        writer.WriteEndArray();

                        writer.WritePropertyName("features");
                        writer.WriteStartArray();
                        if (layer.TryGetProperty("features", out RawJsonValue featsVal) && featsVal.ArrayItems != null)
                        {
                            foreach (RawJsonValue feat in featsVal.ArrayItems)
                            {
                                writer.WriteStartObject();
                                WriteRequiredProp(writer, feat, "key", forceFloat: false);
                                WriteOptionalNonNullProp(writer, feat, "levelId", forceFloat: false);
                                WriteOptionalNonNullProp(writer, feat, "floorKey", forceFloat: false);

                                writer.WritePropertyName("geometry");
                                if (feat.TryGetProperty("geometry", out RawJsonValue geomVal))
                                {
                                    WriteCanonicalRawGeometry(writer, geomVal);
                                }

                                writer.WritePropertyName("attributes");
                                writer.WriteStartObject();
                                if (feat.TryGetProperty("attributes", out RawJsonValue attrsVal) && attrsVal.ObjectMembers != null)
                                {
                                    foreach (KeyValuePair<string, RawJsonValue> kvp in attrsVal.ObjectMembers.OrderBy(p => p.Key, StringComparer.Ordinal))
                                    {
                                        writer.WritePropertyName(kvp.Key);
                                        WriteRawValue(writer, kvp.Value, forceFloat: false);
                                    }
                                }

                                writer.WriteEndObject();
                                writer.WriteEndObject();
                            }
                        }

                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();

                writer.WritePropertyName("quarantined");
                writer.WriteStartArray();
                if (root.TryGetProperty("quarantined", out RawJsonValue quarantinedVal) && quarantinedVal.ArrayItems != null)
                {
                    foreach (RawJsonValue q in quarantinedVal.ArrayItems)
                    {
                        writer.WriteStartObject();
                        WriteRequiredProp(writer, q, "layer", forceFloat: false);
                        WriteRequiredProp(writer, q, "sourceKey", forceFloat: false);
                        WriteRequiredProp(writer, q, "reason", forceFloat: false);
                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();

                writer.WritePropertyName("summary");
                if (root.TryGetProperty("summary", out RawJsonValue summaryVal))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("countsByKind");
                    writer.WriteStartObject();
                    if (summaryVal.TryGetProperty("countsByKind", out RawJsonValue countsVal) && countsVal.ObjectMembers != null)
                    {
                        foreach (string kindToken in CanonicalLayerKindTokenOrder)
                        {
                            if (countsVal.ObjectMembers.TryGetValue(kindToken, out RawJsonValue? countVal) && countVal != null)
                            {
                                writer.WritePropertyName(kindToken);
                                WriteRawValue(writer, countVal, forceFloat: false);
                            }
                        }
                    }

                    writer.WriteEndObject();
                    WriteRequiredProp(writer, summaryVal, "totalLayers", forceFloat: false);
                    WriteRequiredProp(writer, summaryVal, "totalFeatures", forceFloat: false);
                    WriteRequiredProp(writer, summaryVal, "totalQuarantined", forceFloat: false);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            return sb.ToString();
        }

        private static void WriteCanonicalRawGeometry(JsonTextWriter writer, RawJsonValue geom)
        {
            writer.WriteStartObject();
            geom.TryGetProperty("type", out RawJsonValue typeVal);
            string type = typeVal?.Text ?? string.Empty;
            writer.WritePropertyName("type");
            writer.WriteValue(type);

            switch (type)
            {
                case "point":
                {
                    RawJsonValue xyVal = geom.ObjectMembers!["xy"];
                    writer.WritePropertyName("xy");
                    WriteCanonicalRawXyPair(writer, xyVal);

                    RawJsonValue? resolvedZ = null;
                    if (geom.TryGetProperty("z", out RawJsonValue explicitZ) && explicitZ.Kind != RawJsonKind.Null)
                    {
                        resolvedZ = explicitZ;
                    }
                    else if (xyVal.ArrayItems != null && xyVal.ArrayItems.Count >= 3 && xyVal.ArrayItems[2].Kind != RawJsonKind.Null)
                    {
                        resolvedZ = xyVal.ArrayItems[2];
                    }

                    if (resolvedZ != null)
                    {
                        writer.WritePropertyName("z");
                        WriteRawValue(writer, resolvedZ, forceFloat: true);
                    }

                    break;
                }

                case "multiPoint":
                    if (geom.TryGetProperty("points", out RawJsonValue pointsVal))
                    {
                        writer.WritePropertyName("points");
                        WriteCanonicalRawMultiPointItems(writer, pointsVal);
                    }
                    else if (geom.TryGetProperty("coords", out RawJsonValue coordsVal))
                    {
                        writer.WritePropertyName("points");
                        WriteCanonicalRawMultiPointItems(writer, coordsVal);
                    }

                    break;

                case "lineString":
                    writer.WritePropertyName("coords");
                    WriteCanonicalRawCoordinateList(writer, geom.ObjectMembers!["coords"]);
                    break;

                case "multiLineString":
                    writer.WritePropertyName("lines");
                    WriteCanonicalRawMultiLineStrings(writer, geom.ObjectMembers!["lines"]);
                    break;

                case "polygon":
                    writer.WritePropertyName("rings");
                    WriteFloatArrayRecursive(writer, geom.ObjectMembers!["rings"]);
                    break;

                case "multiPolygon":
                    writer.WritePropertyName("polygons");
                    WriteFloatArrayRecursive(writer, geom.ObjectMembers!["polygons"]);
                    break;

                case "connector3D":
                    writer.WritePropertyName("from");
                    WriteCanonicalRawEndpoint(writer, geom.ObjectMembers!["from"]);
                    writer.WritePropertyName("to");
                    WriteCanonicalRawEndpoint(writer, geom.ObjectMembers!["to"]);
                    writer.WritePropertyName("path");
                    WriteFloatArrayRecursive(writer, geom.ObjectMembers!["path"]);
                    WriteRequiredProp(writer, geom, "geometryOrigin", forceFloat: false);
                    break;
            }

            writer.WriteEndObject();
        }

        private static void WriteCanonicalRawXyPair(JsonTextWriter writer, RawJsonValue xyVal)
        {
            writer.WriteStartArray();
            if (xyVal.ArrayItems != null)
            {
                for (int i = 0; i < Math.Min(2, xyVal.ArrayItems.Count); i++)
                {
                    WriteRawValue(writer, xyVal.ArrayItems[i], forceFloat: true);
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteCanonicalRawCoordWithOptionalZ(JsonTextWriter writer, RawJsonValue coordVal)
        {
            if (coordVal.Kind != RawJsonKind.Array || coordVal.ArrayItems == null)
            {
                WriteFloatArrayRecursive(writer, coordVal);
                return;
            }

            writer.WriteStartArray();
            int count = coordVal.ArrayItems.Count;
            for (int i = 0; i < Math.Min(2, count); i++)
            {
                WriteRawValue(writer, coordVal.ArrayItems[i], forceFloat: true);
            }

            if (count >= 3 && coordVal.ArrayItems[2].Kind != RawJsonKind.Null)
            {
                WriteRawValue(writer, coordVal.ArrayItems[2], forceFloat: true);
            }

            writer.WriteEndArray();
        }

        private static void WriteCanonicalRawCoordinateList(JsonTextWriter writer, RawJsonValue listVal)
        {
            writer.WriteStartArray();
            if (listVal.ArrayItems != null)
            {
                foreach (RawJsonValue coord in listVal.ArrayItems)
                {
                    WriteCanonicalRawCoordWithOptionalZ(writer, coord);
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteCanonicalRawMultiLineStrings(JsonTextWriter writer, RawJsonValue linesVal)
        {
            writer.WriteStartArray();
            if (linesVal.ArrayItems != null)
            {
                foreach (RawJsonValue line in linesVal.ArrayItems)
                {
                    WriteCanonicalRawCoordinateList(writer, line);
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteCanonicalRawMultiPointItems(JsonTextWriter writer, RawJsonValue arrayVal)
        {
            writer.WriteStartArray();
            if (arrayVal.ArrayItems != null)
            {
                foreach (RawJsonValue item in arrayVal.ArrayItems)
                {
                    if (item.Kind == RawJsonKind.Object && item.ObjectMembers != null && item.TryGetProperty("xy", out RawJsonValue xyVal))
                    {
                        writer.WriteStartArray();
                        if (xyVal.ArrayItems != null)
                        {
                            for (int i = 0; i < Math.Min(2, xyVal.ArrayItems.Count); i++)
                            {
                                WriteRawValue(writer, xyVal.ArrayItems[i], forceFloat: true);
                            }
                        }

                        if (item.TryGetProperty("z", out RawJsonValue zVal) && zVal.Kind != RawJsonKind.Null)
                        {
                            WriteRawValue(writer, zVal, forceFloat: true);
                        }
                        else if (xyVal.ArrayItems != null && xyVal.ArrayItems.Count >= 3 && xyVal.ArrayItems[2].Kind != RawJsonKind.Null)
                        {
                            WriteRawValue(writer, xyVal.ArrayItems[2], forceFloat: true);
                        }

                        writer.WriteEndArray();
                    }
                    else
                    {
                        WriteCanonicalRawCoordWithOptionalZ(writer, item);
                    }
                }
            }

            writer.WriteEndArray();
        }

        private static void WriteCanonicalRawEndpoint(JsonTextWriter writer, RawJsonValue ep)
        {
            writer.WriteStartObject();
            WriteOptionalNonNullProp(writer, ep, "levelId", forceFloat: false);
            WriteOptionalNonNullProp(writer, ep, "floorKey", forceFloat: false);
            writer.WritePropertyName("xy");
            WriteFloatArrayRecursive(writer, ep.ObjectMembers!["xy"]);
            WriteOptionalNonNullProp(writer, ep, "tpM", forceFloat: true);
            writer.WriteEndObject();
        }

        private static void WriteFloatArrayRecursive(JsonTextWriter writer, RawJsonValue value)
        {
            if (value.Kind == RawJsonKind.Array && value.ArrayItems != null)
            {
                writer.WriteStartArray();
                foreach (RawJsonValue item in value.ArrayItems)
                {
                    WriteFloatArrayRecursive(writer, item);
                }

                writer.WriteEndArray();
                return;
            }

            WriteRawValue(writer, value, forceFloat: true);
        }

        private static void WriteRequiredProp(JsonTextWriter writer, RawJsonValue obj, string name, bool forceFloat)
        {
            writer.WritePropertyName(name);
            WriteRawValue(writer, obj.ObjectMembers![name], forceFloat);
        }

        private static void WriteOptionalNonNullProp(JsonTextWriter writer, RawJsonValue obj, string name, bool forceFloat)
        {
            if (obj.TryGetProperty(name, out RawJsonValue val) && val.Kind != RawJsonKind.Null)
            {
                writer.WritePropertyName(name);
                WriteRawValue(writer, val, forceFloat);
            }
        }

        private static void WriteRawValue(JsonTextWriter writer, RawJsonValue val, bool forceFloat)
        {
            switch (val.Kind)
            {
                case RawJsonKind.Null:
                    writer.WriteNull();
                    break;
                case RawJsonKind.Bool:
                    writer.WriteRawValue(val.Text!);
                    break;
                case RawJsonKind.String:
                    writer.WriteValue(val.Text ?? string.Empty);
                    break;
                case RawJsonKind.Number:
                    writer.WriteRawValue(NormalizeRawNumberToken(val.Text!, forceFloat));
                    break;
                case RawJsonKind.Array:
                    writer.WriteStartArray();
                    foreach (RawJsonValue item in val.ArrayItems!)
                    {
                        WriteRawValue(writer, item, forceFloat: false);
                    }

                    writer.WriteEndArray();
                    break;
                case RawJsonKind.Object:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, RawJsonValue> kvp in val.ObjectMembers!)
                    {
                        writer.WritePropertyName(kvp.Key);
                        WriteRawValue(writer, kvp.Value, forceFloat: false);
                    }

                    writer.WriteEndObject();
                    break;
            }
        }

        private static string NormalizeRawNumberToken(string rawLexeme, bool forceFloat)
        {
            bool hasFractionOrExp =
                rawLexeme.IndexOf('.') >= 0 ||
                rawLexeme.IndexOf('e') >= 0 ||
                rawLexeme.IndexOf('E') >= 0;

            if (rawLexeme.Length > 0 && rawLexeme[0] == '-' &&
                double.TryParse(rawLexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) &&
                d == 0d)
            {
                return (forceFloat || hasFractionOrExp) ? "0.0" : "0";
            }

            if (forceFloat && !hasFractionOrExp)
            {
                return rawLexeme + ".0";
            }

            return rawLexeme;
        }
    }
}
