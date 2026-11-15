using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

public sealed class GisNetworkLevel
{
    public GisNetworkLevel(string levelId, string floorKey, int ordinal, string shortName)
    {
        LevelId = levelId;
        FloorKey = floorKey;
        Ordinal = ordinal;
        ShortName = shortName;
    }

    public string LevelId { get; }

    public string FloorKey { get; }

    public int Ordinal { get; }

    public string ShortName { get; }
}

/// <summary>A walking link on one floor: two or more positions in the file's CRS, in metres.</summary>
public sealed class GisNetworkLink
{
    public GisNetworkLink(long id, string levelId, IReadOnlyList<Point2D> line, bool oneWay, bool stepFree, int passageType)
    {
        Id = id;
        LevelId = levelId;
        Line = line;
        OneWay = oneWay;
        StepFree = stepFree;
        PassageType = passageType;
    }

    public long Id { get; }

    public string LevelId { get; }

    public IReadOnlyList<Point2D> Line { get; }

    /// <summary>Walked only from the first position to the last.</summary>
    public bool OneWay { get; }

    public bool StepFree { get; }

    public int PassageType { get; }
}

public sealed class GisNetworkEnd
{
    public GisNetworkEnd(string levelId, Point2D at)
    {
        LevelId = levelId;
        At = at;
    }

    public string LevelId { get; }

    public Point2D At { get; }
}

/// <summary>A level connector: elevator, escalator, stairs, ramp, movingwalkway, or a kind a later Kanizu adds.</summary>
public sealed class GisNetworkConnector
{
    public GisNetworkConnector(long id, string kind, string uniqueId, GisNetworkEnd from, GisNetworkEnd to, bool oneWay, bool stepFree, int passageType)
    {
        Id = id;
        Kind = kind;
        UniqueId = uniqueId;
        From = from;
        To = to;
        OneWay = oneWay;
        StepFree = stepFree;
        PassageType = passageType;
    }

    public long Id { get; }

    public string Kind { get; }

    /// <summary>The element the connector belongs to; empty for one drawn by hand or left untied.</summary>
    public string UniqueId { get; }

    public GisNetworkEnd From { get; }

    public GisNetworkEnd To { get; }

    /// <summary>Walked only from <see cref="From"/> to <see cref="To"/>.</summary>
    public bool OneWay { get; }

    public bool StepFree { get; }

    public int PassageType { get; }
}

/// <summary>Something Kanizu found in the network that needs a model change.</summary>
public sealed class GisNetworkFinding
{
    public GisNetworkFinding(string kind, string levelId, Point2D? at, string uniqueId, string detail)
    {
        Kind = kind;
        LevelId = levelId;
        At = at;
        UniqueId = uniqueId;
        Detail = detail;
    }

    public string Kind { get; }

    /// <summary>Empty when the finding has no floor; such a finding goes to the audit only.</summary>
    public string LevelId { get; }

    public Point2D? At { get; }

    public string UniqueId { get; }

    /// <summary>Kanizu's English sentence, for a tooltip and for kinds this reader does not know.</summary>
    public string Detail { get; }
}

public sealed class GisNetworkFile
{
    public GisNetworkFile(
        string model,
        string basedOn,
        string writtenUtc,
        string kanizu,
        int epsg,
        IReadOnlyList<GisNetworkLevel> levels,
        IReadOnlyList<GisNetworkLink> links,
        IReadOnlyList<GisNetworkConnector> connectors,
        IReadOnlyList<GisNetworkFinding> findings,
        int notShown,
        IReadOnlyList<string>? notChecked = null,
        int unreadable = 0)
    {
        Model = model;
        BasedOn = basedOn;
        WrittenUtc = writtenUtc;
        Kanizu = kanizu;
        Epsg = epsg;
        Levels = levels;
        Links = links;
        Connectors = connectors;
        Findings = findings;
        NotShown = notShown;
        NotChecked = notChecked ?? Array.Empty<string>();
        Unreadable = unreadable;
    }

    public string Model { get; }

    /// <summary>The export version whose CRS and level ids the file uses, relative to the station folder.</summary>
    public string BasedOn { get; }

    public string WrittenUtc { get; }

    public string Kanizu { get; }

    public int Epsg { get; }

    public IReadOnlyList<GisNetworkLevel> Levels { get; }

    public IReadOnlyList<GisNetworkLink> Links { get; }

    public IReadOnlyList<GisNetworkConnector> Connectors { get; }

    public IReadOnlyList<GisNetworkFinding> Findings { get; }

    /// <summary>Links, connectors and findings Kanizu left out because their floor has no level of this model.</summary>
    public int NotShown { get; }

    /// <summary>Finding kinds Kanizu did not look for when it wrote the file; empty when it checked every kind.</summary>
    public IReadOnlyList<string> NotChecked { get; }

    /// <summary>Links and connectors left out because a position is missing or not a finite number, or a link has fewer than 2 positions.</summary>
    public int Unreadable { get; }
}

/// <summary>What each stroke of the network picture shows; the Revit side picks the colour.</summary>
public enum NetworkInk
{
    Walk,
    WalkNotStepFree,
    Elevator,
    Escalator,
    Stairs,
    Ramp,
    MovingWalkway,
    OtherConnector,
    Warning,
    Info,
}

/// <summary>A Revit level the network can be drawn on: its element id and its height in model feet.</summary>
public readonly struct NetworkLevel
{
    public NetworkLevel(long elementId, double elevationFeet)
    {
        ElementId = elementId;
        ElevationFeet = elevationFeet;
    }

    public long ElementId { get; }

    public double ElevationFeet { get; }
}

/// <summary>One straight stroke in model feet.</summary>
public readonly struct NetworkStroke
{
    public NetworkStroke(double x1, double y1, double z1, double x2, double y2, double z2, NetworkInk ink, long planLevel)
    {
        X1 = x1;
        Y1 = y1;
        Z1 = z1;
        X2 = x2;
        Y2 = y2;
        Z2 = z2;
        Ink = ink;
        PlanLevel = planLevel;
    }

    public double X1 { get; }

    public double Y1 { get; }

    public double Z1 { get; }

    public double X2 { get; }

    public double Y2 { get; }

    public double Z2 { get; }

    public NetworkInk Ink { get; }

    /// <summary>The level whose plan shows the stroke; 0 for a stroke between floors, which only 3D views show. 3D views show every stroke.</summary>
    public long PlanLevel { get; }
}

public sealed class NetworkPicture
{
    public NetworkPicture(string refused, IReadOnlyList<NetworkStroke> strokes, int links, int connectors, int findings, int unknownLevel)
    {
        Refused = refused;
        Strokes = strokes;
        Links = links;
        Connectors = connectors;
        Findings = findings;
        UnknownLevel = unknownLevel;
    }

    /// <summary>Why nothing is drawn; empty when the picture is drawn.</summary>
    public string Refused { get; }

    public IReadOnlyList<NetworkStroke> Strokes { get; }

    public int Links { get; }

    public int Connectors { get; }

    /// <summary>Findings drawn on a floor; findings without one go to the audit only.</summary>
    public int Findings { get; }

    /// <summary>Links, connectors and findings whose level_id no level of this model has.</summary>
    public int UnknownLevel { get; }
}

/// <summary>
/// The walking network Kanizu writes back for this model (<c>gis-network/&lt;model&gt;.gis-network.json</c>
/// in the station folder), drawn read-only over the review views. Kanizu is its only writer; nothing
/// in it is a value to accept into the model, and nothing of the picture is exported. The format is
/// described in docs/kanizu-link-contract.md.
/// </summary>
public static class GisNetwork
{
    public const string Schema = "kanizu.gis-network";
    public const int Version = 1;
    public const string Folder = "gis-network";
    public const string Topic = "network";

    /// <summary>Points are drawn this far above their level, so the floor's top face does not hide them and plans with the usual view range show them.</summary>
    public const double LiftFeet = 0.1 / 0.3048;

    private const double MarkerFeet = 0.4 / 0.3048;
    private const double ArrowFeet = 0.8 / 0.3048;

    public static string FileName(string model) => GisDrift.SafeName(model) + ".gis-network.json";

    /// <summary>The network file from its JSON text. Throws <see cref="FormatException"/> for another schema or a newer version.</summary>
    public static GisNetworkFile Parse(string text, string name = "")
    {
        string file = name.Length > 0 ? " " + name : string.Empty;
        JToken data;
        try
        {
            using JsonTextReader reader = new(new StringReader(text ?? string.Empty)) { DateParseHandling = DateParseHandling.None };
            data = JToken.ReadFrom(reader);
        }
        catch (JsonException error)
        {
            throw new FormatException($"The network file{file} is not JSON: {error.Message}", error);
        }

        if (data is not JObject root || (string?)(root["schema"] as JValue) != Schema)
        {
            throw new FormatException($"The file{file} is not a Kanizu network file (schema {Schema}).");
        }

        // A file without a version is version 1.
        JToken? version = root["version"];
        if (version != null && version.Type != JTokenType.Null)
        {
            if (Finite(version) is not double value)
            {
                throw new FormatException($"The network file{file} has version {version}, which is not a number.");
            }

            if (value > Version)
            {
                throw new FormatException($"The network file{file} is version {value.ToString(CultureInfo.InvariantCulture)}; this tool reads version {Version}. Update RevitGeoSuite.");
            }
        }

        List<GisNetworkLevel> levels = Entries(root["levels"])
            .Select(entry => new GisNetworkLevel(Text(entry["levelId"]), Text(entry["floorKey"]), Small(entry["ordinal"]), Text(entry["shortName"])))
            .ToList();

        // A link with one bad position is left out whole: dropping the position would draw a shortcut.
        int unreadable = 0;
        List<GisNetworkLink> links = new();
        foreach (JObject entry in Entries(root["links"]))
        {
            List<Point2D?> line = (entry["line"] as JArray)?.Select(Position).ToList() ?? new List<Point2D?>();
            if (line.Count < 2 || line.Any(point => point == null))
            {
                unreadable++;
                continue;
            }

            links.Add(new GisNetworkLink(Whole(entry["id"]), Text(entry["levelId"]), line.Select(point => point!.Value).ToList(), Truthy(entry["oneWay"]), Truthy(entry["stepFree"]), Small(entry["passageType"])));
        }

        List<GisNetworkConnector> connectors = new();
        foreach (JObject entry in Entries(root["connectors"]))
        {
            GisNetworkEnd? from = End(entry["from"]);
            GisNetworkEnd? to = End(entry["to"]);
            if (from == null || to == null)
            {
                unreadable++;
            }
            else
            {
                connectors.Add(new GisNetworkConnector(
                    Whole(entry["id"]),
                    Text(entry["kind"]),
                    UniqueIdOf(entry),
                    from,
                    to,
                    Truthy(entry["oneWay"]),
                    Truthy(entry["stepFree"]),
                    Small(entry["passageType"])));
            }
        }

        List<GisNetworkFinding> findings = Entries(root["findings"])
            .Select(entry => new GisNetworkFinding(Text(entry["kind"]), Text(entry["levelId"]), Position(entry["at"]), UniqueIdOf(entry), Text(entry["detail"])))
            .ToList();

        int notShown = root["notShown"] is JObject counts ? (int)(Whole(counts["links"]) + Whole(counts["connectors"]) + Whole(counts["findings"])) : 0;
        return new GisNetworkFile(
            Text(root["model"]),
            Text(root["basedOn"]),
            Text(root["writtenUtc"]),
            Text(root["kanizu"]),
            Small(root["epsg"]),
            levels,
            links,
            connectors,
            findings,
            notShown,
            (root["notChecked"] as JArray)?.Select(code => Text(code)).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
            unreadable);
    }

    /// <summary>Reads a network file, with or without a byte order mark.</summary>
    public static GisNetworkFile Read(string path)
    {
        using StreamReader reader = new(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), Path.GetFileName(path));
    }

    /// <summary>
    /// The strokes that draw the file in this model. Every position goes back to model feet through
    /// the export's shared-coordinate affine, inverted, and sits on its level's height; a connector
    /// runs from one end's level to the other's. Nothing is drawn when the file's CRS is not the
    /// model's project CRS, since the affine only holds for that CRS.
    /// </summary>
    /// <param name="modelToProject">The export's transform from model feet to the project CRS in metres.</param>
    /// <param name="levels">This model's levels by their level_id value.</param>
    public static NetworkPicture Draw(GisNetworkFile file, AffineTransform2D modelToProject, int modelEpsg, IReadOnlyDictionary<string, NetworkLevel> levels)
    {
        if (modelEpsg == 0)
        {
            return Refuse($"This model has no project CRS, so the network file (EPSG:{file.Epsg}) cannot be placed. Set the project CRS with Georeference.");
        }

        if (file.Epsg != modelEpsg)
        {
            return Refuse($"The network file is in EPSG:{file.Epsg}, but this model's project CRS is EPSG:{modelEpsg}. Nothing is drawn; export the model again so Kanizu writes it in the project CRS.");
        }

        AffineTransform2D projectToModel = modelToProject.Inverse();
        List<NetworkStroke> strokes = new();
        int links = 0;
        int connectors = 0;
        int findings = 0;
        int unknown = 0;

        foreach (GisNetworkLink link in file.Links)
        {
            if (!levels.TryGetValue(link.LevelId, out NetworkLevel level))
            {
                unknown++;
                continue;
            }

            links++;
            NetworkInk ink = link.StepFree ? NetworkInk.Walk : NetworkInk.WalkNotStepFree;
            double z = level.ElevationFeet + LiftFeet;
            List<Point2D> points = link.Line.Select(point => projectToModel.Transform(point)).ToList();
            for (int index = 1; index < points.Count; index++)
            {
                strokes.Add(Stroke(points[index - 1], z, points[index], z, ink, level.ElementId));
            }

            if (link.OneWay)
            {
                Arrow(strokes, points[points.Count - 2], points[points.Count - 1], z, ink, level.ElementId);
            }
        }

        foreach (GisNetworkConnector connector in file.Connectors)
        {
            if (!levels.TryGetValue(connector.From.LevelId, out NetworkLevel from) || !levels.TryGetValue(connector.To.LevelId, out NetworkLevel to))
            {
                unknown++;
                continue;
            }

            connectors++;
            NetworkInk ink = InkOf(connector.Kind);
            Point2D start = projectToModel.Transform(connector.From.At);
            Point2D end = projectToModel.Transform(connector.To.At);
            double startZ = from.ElevationFeet + LiftFeet;
            double endZ = to.ElevationFeet + LiftFeet;
            // A connector within one floor (a moving walkway, a ramp on one level) is drawn in that floor's plan too.
            strokes.Add(Stroke(start, startZ, end, endZ, ink, from.ElementId == to.ElementId ? from.ElementId : 0));
            Square(strokes, start, startZ, ink, from.ElementId);
            Square(strokes, end, endZ, ink, to.ElementId);
            if (connector.OneWay)
            {
                // In plan the arrow points from the lower end's place toward the upper end's, on the "from" floor.
                Arrow(strokes, start, end, startZ, ink, from.ElementId);
            }
        }

        foreach (GisNetworkFinding finding in file.Findings)
        {
            if (finding.LevelId.Length == 0 || finding.At == null)
            {
                continue;
            }

            if (!levels.TryGetValue(finding.LevelId, out NetworkLevel level))
            {
                unknown++;
                continue;
            }

            findings++;
            Cross(strokes, projectToModel.Transform(finding.At.Value), level.ElevationFeet + LiftFeet, SeverityOf(finding.Kind) == GisAuditSeverity.Warning ? NetworkInk.Warning : NetworkInk.Info, level.ElementId);
        }

        return new NetworkPicture(string.Empty, strokes, links, connectors, findings, unknown);
    }

    /// <summary>doorWithoutLink is information, every other known kind a warning, and an unknown kind information.</summary>
    public static GisAuditSeverity SeverityOf(string kind) =>
        kind != "doorWithoutLink" && Wording.ContainsKey(kind) ? GisAuditSeverity.Warning : GisAuditSeverity.Info;

    /// <summary>
    /// The file's findings as audit findings, in the audit's own words; an unknown kind shows Kanizu's
    /// detail. <paramref name="elementOf"/> turns a UniqueId into this model's element id, or null.
    /// </summary>
    public static IReadOnlyList<GisAuditFinding> Audit(GisNetworkFile file, Func<string, long?> elementOf)
    {
        Dictionary<string, string> names = file.Levels
            .GroupBy(level => level.LevelId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ShortName, StringComparer.Ordinal);
        List<GisAuditFinding> audit = new();
        foreach (GisNetworkFinding finding in file.Findings)
        {
            string level = finding.LevelId.Length == 0
                ? string.Empty
                : names.TryGetValue(finding.LevelId, out string? shortName) && shortName.Length > 0 ? shortName : finding.LevelId;
            string message = Wording.TryGetValue(finding.Kind, out string? words)
                ? string.Format(CultureInfo.InvariantCulture, words, level.Length > 0 ? " on " + level : string.Empty)
                : $"Kanizu reports {(finding.Kind.Length > 0 ? finding.Kind : "a network finding")}{(level.Length > 0 ? " on " + level : string.Empty)}.";
            if (finding.Detail.Length > 0)
            {
                message += $" (Kanizu: {finding.Detail})";
            }

            long? element = finding.UniqueId.Length > 0 ? elementOf(finding.UniqueId) : null;
            audit.Add(new GisAuditFinding(SeverityOf(finding.Kind), Topic, message, element.HasValue ? new[] { element.Value } : null));
        }

        if (file.Unreadable > 0)
        {
            audit.Add(new GisAuditFinding(GisAuditSeverity.Warning, Topic, $"{file.Unreadable} link(s) or connector(s) in the walking network file have a position that is missing or not a number, so they are not drawn. Generate the network in Kanizu again."));
        }

        // A kind Kanizu did not look for is not "none found": say so once, so a clean audit does not mislead.
        if (file.NotChecked.Count > 0)
        {
            string kinds = string.Join(", ", file.NotChecked.Select(kind => Names.TryGetValue(kind, out string? name) ? name : kind));
            audit.Add(new GisAuditFinding(GisAuditSeverity.Info, Topic, $"Kanizu did not check the walking network for: {kinds}. Generate the network in Kanizu and save it to check them."));
        }

        return audit;
    }

    // {0} is " on <level>" or empty.
    private static readonly Dictionary<string, string> Wording = new(StringComparer.Ordinal)
    {
        ["transitWithoutLink"] = "A stair, escalator or elevator{0} reaches no unit on the next floor, so the walking network has no link through it. Check its run and levels, or its unit on the other floor.",
        ["connectorWithoutElement"] = "A level connector{0} has no stair, escalator, elevator or ramp at both ends. Add the missing element or move it to the connector.",
        ["connectorOutsideUnit"] = "A level connector end{0} stands in no unit. Check the unit around the element.",
        ["floorWithoutConnector"] = "A floor{0} has walking links, but no stair, escalator, elevator or ramp reaches it.",
        ["rampUnresolved"] = "A ramp{0} has a to_level that is paired with no floor. Check the ramp's levels or the level pairing.",
        ["rampUnjoined"] = "A ramp{0} reaches a floor with no walkway at its end. Check the floor at its top or bottom.",
        ["linkThroughWall"] = "A walking link{0} crosses a wall with no door. Add the door or opening, or move the wall.",
        ["doorWithoutLink"] = "A door{0} leads nowhere in the walking network.",
    };

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["transitWithoutLink"] = "stairs, escalators and elevators with no link",
        ["connectorWithoutElement"] = "level connectors with no element",
        ["connectorOutsideUnit"] = "connector ends outside a unit",
        ["floorWithoutConnector"] = "floors no connector reaches",
        ["rampUnresolved"] = "ramps with an unpaired to_level",
        ["rampUnjoined"] = "ramps with no walkway at an end",
        ["linkThroughWall"] = "links through walls",
        ["doorWithoutLink"] = "doors that lead nowhere",
    };

    private static NetworkInk InkOf(string kind) => kind switch
    {
        "elevator" => NetworkInk.Elevator,
        "escalator" => NetworkInk.Escalator,
        "stairs" => NetworkInk.Stairs,
        "ramp" => NetworkInk.Ramp,
        "movingwalkway" => NetworkInk.MovingWalkway,
        _ => NetworkInk.OtherConnector,
    };

    private static NetworkPicture Refuse(string why) => new(why, Array.Empty<NetworkStroke>(), 0, 0, 0, 0);

    private static NetworkStroke Stroke(Point2D a, double za, Point2D b, double zb, NetworkInk ink, long planLevel) =>
        new(a.X, a.Y, za, b.X, b.Y, zb, ink, planLevel);

    // Two barbs at the end of the segment from a to b.
    private static void Arrow(List<NetworkStroke> strokes, Point2D a, Point2D b, double z, NetworkInk ink, long planLevel)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
        {
            return;
        }

        double size = Math.Min(ArrowFeet, length / 2);
        double ux = dx / length;
        double uy = dy / length;
        foreach (double side in new[] { 1.0, -1.0 })
        {
            Point2D barb = new(b.X - (size * ux) - (side * size * 0.5 * uy), b.Y - (size * uy) + (side * size * 0.5 * ux));
            strokes.Add(Stroke(barb, z, b, z, ink, planLevel));
        }
    }

    private static void Square(List<NetworkStroke> strokes, Point2D at, double z, NetworkInk ink, long planLevel)
    {
        Point2D[] corners =
        {
            new(at.X - MarkerFeet, at.Y - MarkerFeet),
            new(at.X + MarkerFeet, at.Y - MarkerFeet),
            new(at.X + MarkerFeet, at.Y + MarkerFeet),
            new(at.X - MarkerFeet, at.Y + MarkerFeet),
        };
        for (int index = 0; index < 4; index++)
        {
            strokes.Add(Stroke(corners[index], z, corners[(index + 1) % 4], z, ink, planLevel));
        }
    }

    private static void Cross(List<NetworkStroke> strokes, Point2D at, double z, NetworkInk ink, long planLevel)
    {
        double size = MarkerFeet * 2;
        strokes.Add(Stroke(new Point2D(at.X - size, at.Y - size), z, new Point2D(at.X + size, at.Y + size), z, ink, planLevel));
        strokes.Add(Stroke(new Point2D(at.X - size, at.Y + size), z, new Point2D(at.X + size, at.Y - size), z, ink, planLevel));
    }

    private static IEnumerable<JObject> Entries(JToken? token) => (token as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

    private static GisNetworkEnd? End(JToken? token)
    {
        if (token is not JObject end)
        {
            return null;
        }

        Point2D? at = Position(end["at"]);
        return at == null ? null : new GisNetworkEnd(Text(end["levelId"]), at.Value);
    }

    private static Point2D? Position(JToken? token)
    {
        if (token is JArray { Count: >= 2 } pair && Finite(pair[0]) is double x && Finite(pair[1]) is double y)
        {
            return new Point2D(x, y);
        }

        return null;
    }

    // uniqueId, else the middle part of revitKey ("model|uniqueId|n").
    private static string UniqueIdOf(JObject entry)
    {
        string uniqueId = Text(entry["uniqueId"]);
        if (uniqueId.Length > 0)
        {
            return uniqueId;
        }

        string[] parts = Text(entry["revitKey"]).Split('|');
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    private static string Text(JToken? token) => token is JValue { Value: not null } value ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    // A JSON number as a finite double: an integer too large for a long arrives as a BigInteger, and NaN or Infinity are not positions.
    private static double? Finite(JToken? token)
    {
        if (token is not JValue { Type: JTokenType.Integer or JTokenType.Float } value
            || !double.TryParse(Convert.ToString(value.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || double.IsNaN(number)
            || double.IsInfinity(number))
        {
            return null;
        }

        return number;
    }

    private static int Small(JToken? token) => Whole(token) is long number && Math.Abs(number) <= int.MaxValue ? (int)number : 0;

    // An id, ordinal or count: 0 when missing or out of range.
    private static long Whole(JToken? token) =>
        Finite(token) is double number && Math.Abs(number) <= 9007199254740992d ? (long)number : 0;

    private static bool Truthy(JToken? token) => token is JValue { Type: JTokenType.Boolean, Value: true };
}
