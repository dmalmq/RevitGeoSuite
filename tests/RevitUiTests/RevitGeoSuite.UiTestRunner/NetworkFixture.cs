using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C8 walking network file (kanizu.gis-network version 1) made fit for the test model: the sample's
/// 1F and 2F levels become two of the model's levels, its B1 level stays unmatched, and every position
/// moves by one offset so the network lies over the model.
/// </summary>
internal static class NetworkFixture
{
    public const string LowerSampleLevel = "lvl-1f";
    public const string UpperSampleLevel = "lvl-2f";

    // The sample's first link starts here (export CRS metres); the rewrite moves this point onto the model.
    public static readonly (double X, double Y) SampleAnchor = (-7130.0, -34560.0);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "network-sample.gis-network.json");

    public static string FileName(string modelKey) => modelKey + ".gis-network.json";

    /// <summary>
    /// A copy of <paramref name="sample"/> for the model <paramref name="modelKey"/>: lvl-1f is
    /// <paramref name="lower"/>, lvl-2f is <paramref name="upper"/>, epsg is <paramref name="epsg"/>,
    /// and every position is moved so the sample anchor lands on <paramref name="anchor"/>.
    /// </summary>
    public static JsonObject Rewrite(JsonObject sample, string modelKey, string lower, string upper, int epsg, (double X, double Y) anchor)
    {
        JsonObject network = sample.DeepClone().AsObject();
        string sampleModel = network["model"]?.GetValue<string>() ?? string.Empty;
        string sampleKeyPrefix = sampleModel.Split('-')[0] + "|";
        network["model"] = modelKey;
        network["epsg"] = epsg;
        (double dx, double dy) = (anchor.X - SampleAnchor.X, anchor.Y - SampleAnchor.Y);
        var levels = new Dictionary<string, string> { [LowerSampleLevel] = lower, [UpperSampleLevel] = upper };
        Walk(network, node =>
        {
            if (node is JsonObject item)
            {
                if (item["levelId"] is JsonValue level && level.GetValue<string>() is string id && levels.TryGetValue(id, out string? mapped))
                {
                    item["levelId"] = mapped;
                }

                if (item["revitKey"] is JsonValue key && key.GetValue<string>() is string revitKey && revitKey.StartsWith(sampleKeyPrefix, StringComparison.Ordinal))
                {
                    item["revitKey"] = modelKey + "|" + revitKey[sampleKeyPrefix.Length..];
                }

                if (item["at"] is JsonArray point)
                {
                    item["at"] = Move(point, dx, dy);
                }

                if (item["line"] is JsonArray line)
                {
                    item["line"] = new JsonArray(line.Select(point => (JsonNode?)Move(point!.AsArray(), dx, dy)).ToArray());
                }
            }
        });
        return network;
    }

    /// <summary>What the Network tab should say for <paramref name="network"/> on a model with <paramref name="modelLevels"/>.</summary>
    public static NetworkCounts Expect(JsonObject network, IReadOnlySet<string> modelLevels)
    {
        bool Known(string? level) => level != null && modelLevels.Contains(level);
        bool Unknown(string? level) => level != null && !modelLevels.Contains(level);
        JsonObject[] links = Items(network, "links");
        JsonObject[] connectors = Items(network, "connectors");
        JsonObject[] findings = Items(network, "findings");
        string?[] Ends(JsonObject connector) => new[] { Level(connector["from"]), Level(connector["to"]) };
        return new NetworkCounts(
            links.Count(link => Known(Level(link))),
            connectors.Count(connector => Ends(connector).All(Known)),
            findings.Count(finding => Known(Level(finding))),
            links.Count(link => Unknown(Level(link)))
                + connectors.Count(connector => Ends(connector).Any(Unknown))
                + findings.Count(finding => Unknown(Level(finding))));
    }

    /// <summary>The network without the link <paramref name="id"/>.</summary>
    public static JsonObject WithoutLink(JsonObject network, int id)
    {
        JsonObject copy = network.DeepClone().AsObject();
        JsonArray links = copy["links"]!.AsArray();
        links.Remove(links.First(link => link!["id"]!.GetValue<int>() == id));
        return copy;
    }

    public static JsonObject With(JsonObject network, string property, JsonNode value)
    {
        JsonObject copy = network.DeepClone().AsObject();
        copy[property] = value;
        return copy;
    }

    /// <summary>The bounding box of every position in the file (export CRS metres).</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) Extent(JsonObject network)
    {
        var points = new List<(double X, double Y)>();
        Walk(network, node =>
        {
            if (node is JsonObject item)
            {
                if (item["at"] is JsonArray point)
                {
                    points.Add(Point(point));
                }

                if (item["line"] is JsonArray line)
                {
                    points.AddRange(line.Select(vertex => Point(vertex!.AsArray())));
                }
            }
        });
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    public static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    public static void Write(string path, JsonObject network)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, network.ToJsonString(Indented));
    }

    private static JsonObject[] Items(JsonObject network, string name) =>
        network[name]?.AsArray().Select(item => item!.AsObject()).ToArray() ?? Array.Empty<JsonObject>();

    private static string? Level(JsonNode? item) => item?["levelId"]?.GetValue<string>();

    private static (double X, double Y) Point(JsonArray point) => (point[0]!.GetValue<double>(), point[1]!.GetValue<double>());

    private static JsonArray Move(JsonArray point, double dx, double dy)
    {
        (double x, double y) = Point(point);
        return new JsonArray(x + dx, y + dy);
    }

    private static void Walk(JsonNode? node, Action<JsonNode> visit)
    {
        if (node == null)
        {
            return;
        }

        visit(node);
        IEnumerable<JsonNode?> children = node switch
        {
            JsonObject item => item.Select(pair => pair.Value).ToList(),
            JsonArray array => array.ToList(),
            _ => Enumerable.Empty<JsonNode?>(),
        };
        foreach (JsonNode? child in children)
        {
            Walk(child, visit);
        }
    }
}

/// <summary>The Network tab's counts line and its unknown-level note.</summary>
internal sealed record NetworkCounts(int Links, int Connectors, int Findings, int UnknownLevel)
{
    public string CountsLine => $"{Links} link(s), {Connectors} level connector(s) and {Findings} finding(s) on a floor.";

    public string UnknownLine => $"{UnknownLevel} item(s) sit on a level_id no level of this model has, so they are not drawn.";
}
