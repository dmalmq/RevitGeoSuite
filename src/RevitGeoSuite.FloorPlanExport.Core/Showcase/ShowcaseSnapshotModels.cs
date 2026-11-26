using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Frozen schema constants for <c>kanizu.gis-showcase</c> v1 snapshots.</summary>
public static class ShowcaseContract
{
    public const string Schema = "kanizu.gis-showcase";
    public const int Version = 1;
    public const string DefaultDirectoryName = "gis-showcase";
    public const string FileSuffix = ".gis-showcase.json";

    /// <summary>Maximum exact integer representable in IEEE-754 double precision (<c>2^53 - 1</c>).</summary>
    public const long MaxSafeJsonInteger = (1L << 53) - 1;
}

/// <summary>Provenance of a <see cref="ShowcaseLevel"/>'s Tokyo Peil elevation.</summary>
public enum ShowcaseElevationSource
{
    Surveyed,
    Model,
    Estimated,
}

/// <summary>
/// Semantic layer classification matching Kanizu's <c>ShowcaseLayerKind</c> in exact declaration order
/// (used for canonical <c>summary.countsByKind</c> ordering).
/// </summary>
public enum ShowcaseLayerKind
{
    Unit,
    Opening,
    Fixture,
    Level,
    Detail,
    PointFacility,
    PointFacilityNetwork,
    Station,
    NetworkLink,
    NetworkConnector,
    NetworkNode,
}

/// <summary>Logical source GIS column type preserved in <c>kanizu.gis-showcase</c> v1.</summary>
public enum ShowcaseFieldType
{
    Text,
    Integer,
    Real,
    Boolean,
    Date,
    Json,
}

/// <summary>Provenance of a vertical connector's 2D/3D path geometry.</summary>
public enum ShowcaseConnectorGeometryOrigin
{
    SurveyedPath,
    Schematic,
}

/// <summary>Discriminator for <see cref="ShowcaseGeometry"/>.</summary>
public enum ShowcaseGeometryType
{
    Point,
    LineString,
    MultiLineString,
    Polygon,
    MultiPolygon,
    Connector3D,
}

/// <summary>Lossless scalar kind for a feature attribute value.</summary>
public enum ShowcaseAttributeKind
{
    Null,
    Text,
    Integer,
    Real,
    Boolean,
    Json,
}

/// <summary>Exception thrown when a <c>kanizu.gis-showcase</c> snapshot fails schema or integrity validation.</summary>
public sealed class ShowcaseSnapshotException : Exception
{
    public ShowcaseSnapshotException(string message)
        : base(message)
    {
    }

    public ShowcaseSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Lossless attribute value preserving explicit JSON <c>null</c> vs empty string <c>""</c>, exact 64-bit integers
/// (never rounded through IEEE-754 <see cref="double"/>), leading zeros, and raw JSON containers.
/// </summary>
public sealed class ShowcaseAttributeValue : IEquatable<ShowcaseAttributeValue>
{
    public static ShowcaseAttributeValue Null { get; } = new(ShowcaseAttributeKind.Null, null, null, null, null, null);

    private ShowcaseAttributeValue(
        ShowcaseAttributeKind kind,
        string? textValue,
        long? int64Value,
        double? realValue,
        bool? boolValue,
        string? rawJsonText)
    {
        Kind = kind;
        TextValue = textValue;
        Int64Value = int64Value;
        RealValue = realValue;
        BoolValue = boolValue;
        RawJsonText = rawJsonText;
    }

    public ShowcaseAttributeKind Kind { get; }

    public string? TextValue { get; }

    public long? Int64Value { get; }

    public double? RealValue { get; }

    public bool? BoolValue { get; }

    public string? RawJsonText { get; }

    public bool IsNull => Kind == ShowcaseAttributeKind.Null;

    public bool IsEmptyString => Kind == ShowcaseAttributeKind.Text && TextValue != null && TextValue.Length == 0;

    public static ShowcaseAttributeValue FromText(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new ShowcaseAttributeValue(ShowcaseAttributeKind.Text, value, null, null, null, null);
    }

    public static ShowcaseAttributeValue FromInteger(long value) =>
        new(ShowcaseAttributeKind.Integer, null, value, null, null, null);

    public static ShowcaseAttributeValue FromReal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ShowcaseSnapshotException($"Attribute real value '{value}' must be finite.");
        }

        double normalized = value == 0d ? 0d : value;
        return new ShowcaseAttributeValue(ShowcaseAttributeKind.Real, null, null, normalized, null, null);
    }

    public static ShowcaseAttributeValue FromBoolean(bool value) =>
        new(ShowcaseAttributeKind.Boolean, null, null, null, value, null);

    public static ShowcaseAttributeValue FromRawJson(string rawJson)
    {
        if (rawJson is null)
        {
            throw new ArgumentNullException(nameof(rawJson));
        }

        return new ShowcaseAttributeValue(ShowcaseAttributeKind.Json, null, null, null, null, rawJson);
    }

    /// <summary>
    /// Converts this value to exact invariant text without rounding 64-bit integers through <see cref="double"/>.
    /// Returns <c>null</c> only when <see cref="IsNull"/> is <c>true</c>; empty string returns <c>""</c>.
    /// </summary>
    public string? ToExactString()
    {
        switch (Kind)
        {
            case ShowcaseAttributeKind.Null:
                return null;
            case ShowcaseAttributeKind.Text:
                return TextValue ?? string.Empty;
            case ShowcaseAttributeKind.Integer:
                return Int64Value!.Value.ToString(CultureInfo.InvariantCulture);
            case ShowcaseAttributeKind.Real:
                return RealValue!.Value.ToString("G", CultureInfo.InvariantCulture);
            case ShowcaseAttributeKind.Boolean:
                return BoolValue!.Value ? "true" : "false";
            case ShowcaseAttributeKind.Json:
                return RawJsonText ?? string.Empty;
            default:
                return null;
        }
    }

    public bool Equals(ShowcaseAttributeValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        switch (Kind)
        {
            case ShowcaseAttributeKind.Null:
                return true;
            case ShowcaseAttributeKind.Text:
                return string.Equals(TextValue, other.TextValue, StringComparison.Ordinal);
            case ShowcaseAttributeKind.Integer:
                return Int64Value == other.Int64Value;
            case ShowcaseAttributeKind.Real:
                return Nullable.Equals(RealValue, other.RealValue);
            case ShowcaseAttributeKind.Boolean:
                return BoolValue == other.BoolValue;
            case ShowcaseAttributeKind.Json:
                return string.Equals(RawJsonText, other.RawJsonText, StringComparison.Ordinal);
            default:
                return false;
        }
    }

    public override bool Equals(object? obj) => Equals(obj as ShowcaseAttributeValue);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Kind * 397;
            switch (Kind)
            {
                case ShowcaseAttributeKind.Text:
                    return hash ^ StringComparer.Ordinal.GetHashCode(TextValue ?? string.Empty);
                case ShowcaseAttributeKind.Integer:
                    return hash ^ Int64Value.GetHashCode();
                case ShowcaseAttributeKind.Real:
                    return hash ^ RealValue.GetHashCode();
                case ShowcaseAttributeKind.Boolean:
                    return hash ^ BoolValue.GetHashCode();
                case ShowcaseAttributeKind.Json:
                    return hash ^ StringComparer.Ordinal.GetHashCode(RawJsonText ?? string.Empty);
                default:
                    return hash;
            }
        }
    }
}

/// <summary>Floor/level metadata from <c>levels[]</c>.</summary>
public sealed class ShowcaseLevel
{
    public ShowcaseLevel(
        string levelId,
        string floorKey,
        long ordinal,
        string shortName,
        double? tpM,
        ShowcaseElevationSource elevationSource)
    {
        LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
        FloorKey = floorKey ?? throw new ArgumentNullException(nameof(floorKey));
        Ordinal = ordinal;
        ShortName = shortName ?? throw new ArgumentNullException(nameof(shortName));
        TpM = tpM;
        ElevationSource = elevationSource;
    }

    public string LevelId { get; }

    public string FloorKey { get; }

    public long Ordinal { get; }

    public string ShortName { get; }

    public double? TpM { get; }

    public ShowcaseElevationSource ElevationSource { get; }
}

/// <summary>Schema definition of one attribute column on a <see cref="ShowcaseLayer"/>.</summary>
public sealed class ShowcaseField
{
    public ShowcaseField(
        string name,
        ShowcaseFieldType fieldType,
        string? aliasJa = null,
        string? domain = null,
        bool preserveAsText = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FieldType = fieldType;
        AliasJa = aliasJa;
        Domain = domain;
        PreserveAsText = preserveAsText;
    }

    public string Name { get; }

    public ShowcaseFieldType FieldType { get; }

    public string? AliasJa { get; }

    public string? Domain { get; }

    public bool PreserveAsText { get; }
}

/// <summary>One endpoint of a <see cref="ShowcaseConnector3DGeometry"/>.</summary>
public sealed class ShowcaseEndpoint
{
    public ShowcaseEndpoint(string? levelId, string? floorKey, Point2D xy, double? tpM)
    {
        LevelId = levelId;
        FloorKey = floorKey;
        Xy = xy;
        TpM = tpM;
    }

    public string? LevelId { get; }

    public string? FloorKey { get; }

    public Point2D Xy { get; }

    public double? TpM { get; }
}

/// <summary>Base type for tagged <c>kanizu.gis-showcase</c> v1 geometries in projected CRS metres.</summary>
public abstract class ShowcaseGeometry
{
    protected ShowcaseGeometry(ShowcaseGeometryType geometryType)
    {
        GeometryType = geometryType;
    }

    public ShowcaseGeometryType GeometryType { get; }
}

public sealed class ShowcasePointGeometry : ShowcaseGeometry
{
    public ShowcasePointGeometry(Point2D xy, double? z = null)
        : base(ShowcaseGeometryType.Point)
    {
        Xy = xy;
        Z = z;
    }

    public Point2D Xy { get; }

    public double? Z { get; }
}

public sealed class ShowcaseLineStringGeometry : ShowcaseGeometry
{
    public ShowcaseLineStringGeometry(IReadOnlyList<Point2D> coords)
        : base(ShowcaseGeometryType.LineString)
    {
        Coords = coords ?? throw new ArgumentNullException(nameof(coords));
    }

    public IReadOnlyList<Point2D> Coords { get; }
}

public sealed class ShowcaseMultiLineStringGeometry : ShowcaseGeometry
{
    public ShowcaseMultiLineStringGeometry(IReadOnlyList<IReadOnlyList<Point2D>> lines)
        : base(ShowcaseGeometryType.MultiLineString)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    public IReadOnlyList<IReadOnlyList<Point2D>> Lines { get; }
}

public sealed class ShowcasePolygonGeometry : ShowcaseGeometry
{
    public ShowcasePolygonGeometry(IReadOnlyList<IReadOnlyList<Point2D>> rings)
        : base(ShowcaseGeometryType.Polygon)
    {
        if (rings is null)
        {
            throw new ArgumentNullException(nameof(rings));
        }

        if (rings.Count == 0)
        {
            throw new ArgumentException("A polygon must contain at least an exterior ring.", nameof(rings));
        }

        Rings = rings;
    }

    /// <summary>All rings in projected CRS metres: ring 0 is the exterior boundary; rings 1..N are interior holes.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> Rings { get; }

    public IReadOnlyList<Point2D> ExteriorRing => Rings[0];

    public IReadOnlyList<IReadOnlyList<Point2D>> InteriorRings =>
        Rings.Count <= 1
            ? Array.Empty<IReadOnlyList<Point2D>>()
            : Rings.Skip(1).ToArray();
}

public sealed class ShowcaseMultiPolygonGeometry : ShowcaseGeometry
{
    public ShowcaseMultiPolygonGeometry(IReadOnlyList<ShowcasePolygonGeometry> polygons)
        : base(ShowcaseGeometryType.MultiPolygon)
    {
        if (polygons is null)
        {
            throw new ArgumentNullException(nameof(polygons));
        }

        if (polygons.Count == 0)
        {
            throw new ArgumentException("A multi-polygon must contain at least one polygon.", nameof(polygons));
        }

        Polygons = polygons;
    }

    public IReadOnlyList<ShowcasePolygonGeometry> Polygons { get; }
}

public sealed class ShowcaseConnector3DGeometry : ShowcaseGeometry
{
    public ShowcaseConnector3DGeometry(
        ShowcaseEndpoint from,
        ShowcaseEndpoint to,
        IReadOnlyList<Point2D> path,
        ShowcaseConnectorGeometryOrigin geometryOrigin)
        : base(ShowcaseGeometryType.Connector3D)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        GeometryOrigin = geometryOrigin;
    }

    public ShowcaseEndpoint From { get; }

    public ShowcaseEndpoint To { get; }

    public IReadOnlyList<Point2D> Path { get; }

    public ShowcaseConnectorGeometryOrigin GeometryOrigin { get; }
}

/// <summary>One exported GIS or network feature from a <see cref="ShowcaseLayer"/>.</summary>
public sealed class ShowcaseFeature
{
    public ShowcaseFeature(
        string key,
        string? levelId,
        string? floorKey,
        ShowcaseGeometry geometry,
        IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        LevelId = levelId;
        FloorKey = floorKey;
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public string Key { get; }

    public string? LevelId { get; }

    public string? FloorKey { get; }

    public ShowcaseGeometry Geometry { get; }

    public IReadOnlyDictionary<string, ShowcaseAttributeValue> Attributes { get; }
}

/// <summary>One exported layer in a <see cref="ShowcaseSnapshot"/>.</summary>
public sealed class ShowcaseLayer
{
    public ShowcaseLayer(
        string name,
        ShowcaseLayerKind kind,
        IReadOnlyList<ShowcaseField> fields,
        IReadOnlyList<ShowcaseFeature> features)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Features = features ?? throw new ArgumentNullException(nameof(features));
    }

    public string Name { get; }

    public ShowcaseLayerKind Kind { get; }

    public IReadOnlyList<ShowcaseField> Fields { get; }

    public IReadOnlyList<ShowcaseFeature> Features { get; }
}

/// <summary>Row omitted during Kanizu snapshot creation due to invalid geometry or unmapped floor.</summary>
public sealed class QuarantinedShowcaseFeature
{
    public QuarantinedShowcaseFeature(string layer, string sourceKey, string reason)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        SourceKey = sourceKey ?? throw new ArgumentNullException(nameof(sourceKey));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public string Layer { get; }

    public string SourceKey { get; }

    public string Reason { get; }
}

/// <summary>Aggregate layer/feature/quarantine counts carried by a <see cref="ShowcaseSnapshot"/>.</summary>
public sealed class ShowcaseSummary
{
    public ShowcaseSummary(
        IReadOnlyDictionary<ShowcaseLayerKind, int> countsByKind,
        int totalLayers,
        int totalFeatures,
        int totalQuarantined)
    {
        CountsByKind = countsByKind ?? throw new ArgumentNullException(nameof(countsByKind));
        TotalLayers = totalLayers;
        TotalFeatures = totalFeatures;
        TotalQuarantined = totalQuarantined;
    }

    public IReadOnlyDictionary<ShowcaseLayerKind, int> CountsByKind { get; }

    public int TotalLayers { get; }

    public int TotalFeatures { get; }

    public int TotalQuarantined { get; }
}

/// <summary>Validated in-memory representation of a <c>kanizu.gis-showcase</c> v1 JSON snapshot.</summary>
public sealed class ShowcaseSnapshot
{
    public ShowcaseSnapshot(
        string schema,
        int version,
        string scope,
        string? basedOn,
        string writtenUtc,
        string kanizu,
        int epsg,
        string contentHash,
        IReadOnlyList<ShowcaseLevel> levels,
        IReadOnlyList<ShowcaseLayer> layers,
        IReadOnlyList<QuarantinedShowcaseFeature> quarantined,
        ShowcaseSummary summary)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Version = version;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        BasedOn = basedOn;
        WrittenUtc = writtenUtc ?? throw new ArgumentNullException(nameof(writtenUtc));
        Kanizu = kanizu ?? throw new ArgumentNullException(nameof(kanizu));
        Epsg = epsg;
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        Levels = levels ?? throw new ArgumentNullException(nameof(levels));
        Layers = layers ?? throw new ArgumentNullException(nameof(layers));
        Quarantined = quarantined ?? throw new ArgumentNullException(nameof(quarantined));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public string Schema { get; }

    public int Version { get; }

    public string Scope { get; }

    public string? BasedOn { get; }

    public string WrittenUtc { get; }

    public string Kanizu { get; }

    public int Epsg { get; }

    public string ContentHash { get; }

    public IReadOnlyList<ShowcaseLevel> Levels { get; }

    public IReadOnlyList<ShowcaseLayer> Layers { get; }

    public IReadOnlyList<QuarantinedShowcaseFeature> Quarantined { get; }

    public ShowcaseSummary Summary { get; }

    public IEnumerable<ShowcaseLayer> GetLayersByKind(ShowcaseLayerKind kind) =>
        Layers.Where(layer => layer.Kind == kind);
}
