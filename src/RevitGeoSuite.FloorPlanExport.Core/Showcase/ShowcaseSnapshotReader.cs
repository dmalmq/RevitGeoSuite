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
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
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
            string expectedHash = ComputeContentHash(snapshot);
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
        byte[] utf8Bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(canonicalJson);
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
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JObject obj)
            {
                throw new ShowcaseSnapshotException($"layers[{i}] must be a JSON object.");
            }

            string ctx = $"layers[{i}]";
            string name = ReadRequiredNonBlankString(obj, "name", ctx);
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
                Point2D xy = ParseXyPair(geomObj["xy"], $"point geometry of {featureLabel}");
                double? z = ReadOptionalFiniteDouble(geomObj, "z", $"point geometry of {featureLabel}");
                return new ShowcasePointGeometry(xy, z);
            }

            case "lineString":
            {
                IReadOnlyList<Point2D> coords = ParseCoordinateList(
                    geomObj["coords"],
                    minCount: 2,
                    $"lineString geometry of {featureLabel}");
                return new ShowcaseLineStringGeometry(coords);
            }

            case "multiLineString":
            {
                if (geomObj["lines"] is not JArray linesArray || linesArray.Count == 0)
                {
                    throw new ShowcaseSnapshotException($"multiLineString geometry of {featureLabel} must have at least one line.");
                }

                List<IReadOnlyList<Point2D>> lines = new(linesArray.Count);
                for (int i = 0; i < linesArray.Count; i++)
                {
                    lines.Add(ParseCoordinateList(linesArray[i], minCount: 2, $"multiLineString[{i}] of {featureLabel}"));
                }

                return new ShowcaseMultiLineStringGeometry(lines);
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

    internal static double ComputeSignedArea(IReadOnlyList<Point2D> ring)
    {
        if (ring.Count < 3)
        {
            return 0d;
        }

        double twiceArea = 0d;
        for (int i = 0; i < ring.Count; i++)
        {
            Point2D a = ring[i];
            Point2D b = ring[(i + 1) % ring.Count];
            twiceArea += (a.X * b.Y) - (b.X * a.Y);
        }

        return twiceArea * 0.5d;
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

        int actualFeatures = layers.Sum(layer => layer.Features.Count);
        if (totalLayers != layers.Count || totalFeatures != actualFeatures || totalQuarantined != quarantined.Count)
        {
            throw new ShowcaseSnapshotException(
                $"Showcase summary counts do not match payload (summary: {totalLayers} layers / {totalFeatures} features / {totalQuarantined} quarantined; actual: {layers.Count} / {actualFeatures} / {quarantined.Count}).");
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

            case ShowcaseLineStringGeometry line:
                writer.WritePropertyName("type");
                writer.WriteValue("lineString");
                writer.WritePropertyName("coords");
                WriteCanonicalCoordinateList(writer, line.Coords);
                break;

            case ShowcaseMultiLineStringGeometry multiLine:
                writer.WritePropertyName("type");
                writer.WriteValue("multiLineString");
                writer.WritePropertyName("lines");
                writer.WriteStartArray();
                foreach (IReadOnlyList<Point2D> l in multiLine.Lines)
                {
                    WriteCanonicalCoordinateList(writer, l);
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
        double normalized = value == 0d ? 0d : value;
        string text = normalized.ToString("G", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0)
        {
            text += ".0";
        }

        writer.WriteRawValue(text);
    }
}
