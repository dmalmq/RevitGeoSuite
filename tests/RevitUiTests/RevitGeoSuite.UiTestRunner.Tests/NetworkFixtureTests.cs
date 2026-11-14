using System.Text.Json.Nodes;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class NetworkFixtureTests
{
    private static JsonObject Fitted() =>
        NetworkFixture.Rewrite(NetworkFixture.Read(NetworkFixture.SamplePath), "abcd-model", "L-3F", "L-4F", 6669, (100.0, 200.0));

    [Fact]
    public void Fits_the_sample_to_the_model_levels_crs_and_position()
    {
        JsonObject network = Fitted();

        Assert.Equal("abcd-model", network["model"]!.GetValue<string>());
        Assert.Equal(6669, network["epsg"]!.GetValue<int>());
        Assert.Equal(new[] { "lvl-b1", "L-3F", "L-4F" }, network["levels"]!.AsArray().Select(level => level!["levelId"]!.GetValue<string>()));
        JsonArray first = network["links"]![0]!["line"]!.AsArray();
        Assert.Equal(new[] { 100.0, 200.0 }, first[0]!.AsArray().Select(value => value!.GetValue<double>()));
        Assert.Equal(new[] { 110.0, 200.0 }, first[1]!.AsArray().Select(value => value!.GetValue<double>()));
        JsonObject stairs = network["connectors"]![0]!.AsObject();
        Assert.Equal("L-3F", stairs["from"]!["levelId"]!.GetValue<string>());
        Assert.Equal("L-4F", stairs["to"]!["levelId"]!.GetValue<string>());
        Assert.Equal(new[] { 105.0, 215.0 }, stairs["to"]!["at"]!.AsArray().Select(value => value!.GetValue<double>()));
        Assert.Equal("abcd-model|stair-uid|0", stairs["revitKey"]!.GetValue<string>());
        JsonObject finding = network["findings"]![0]!.AsObject();
        Assert.Equal("abcd-model|stair2-uid|0", finding["revitKey"]!.GetValue<string>());
        Assert.Equal(new[] { 140.0, 220.0 }, finding["at"]!.AsArray().Select(value => value!.GetValue<double>()));
        Assert.Equal(new[] { 100.0, 180.0, 140.0, 220.0 }, ToArray(NetworkFixture.Extent(network)));
    }

    [Fact]
    public void Expects_the_counts_the_c8_script_names_when_b1_matches_no_level()
    {
        NetworkCounts counts = NetworkFixture.Expect(Fitted(), new HashSet<string> { "L-3F", "L-4F", "L-5F" });

        Assert.Equal("3 link(s), 3 level connector(s) and 1 finding(s) on a floor.", counts.CountsLine);
        Assert.Equal("3 item(s) sit on a level_id no level of this model has, so they are not drawn.", counts.UnknownLine);
    }

    [Fact]
    public void Expects_everything_drawn_when_every_level_matches()
    {
        NetworkCounts counts = NetworkFixture.Expect(Fitted(), new HashSet<string> { "lvl-b1", "L-3F", "L-4F" });

        Assert.Equal(new NetworkCounts(4, 5, 1, 0), counts);
    }

    [Fact]
    public void Edits_return_copies()
    {
        JsonObject network = Fitted();

        JsonObject dropped = NetworkFixture.WithoutLink(network, 3);
        JsonObject newer = NetworkFixture.With(network, "version", 2);

        Assert.Equal(new[] { 1, 2, 4 }, dropped["links"]!.AsArray().Select(link => link!["id"]!.GetValue<int>()));
        Assert.Equal(4, network["links"]!.AsArray().Count);
        Assert.Equal(2, newer["version"]!.GetValue<int>());
        Assert.Equal(1, network["version"]!.GetValue<int>());
        Assert.Equal("abcd-model.gis-network.json", NetworkFixture.FileName("abcd-model"));
    }

    private static double[] ToArray((double MinX, double MinY, double MaxX, double MaxY) box) => new[] { box.MinX, box.MinY, box.MaxX, box.MaxY };
}
