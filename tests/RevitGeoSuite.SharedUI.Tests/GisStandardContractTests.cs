using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

/// <summary>The GIS standard page's requests and replies, through the bridge as the page sends them.</summary>
public sealed class GisStandardContractTests
{
    private sealed class FixHandler : IRpcHandler
    {
        public long[] Received { get; private set; } = Array.Empty<long>();

        public string Method => "gisStandard.fixFloorTypes";

        public Task<object?> HandleAsync(object? payload)
        {
            // The same parse as GisFixFloorTypesHandler.
            Received = ((payload as JObject)?.ToObject<GisFixFloorTypesRequest>() ?? new GisFixFloorTypesRequest()).FloorTypeIds;
            return Task.FromResult<object?>(new GisStandardApplyResponse
            {
                Log = new[] { "j 店舗_床 (old): gis_category retail" },
                Skipped = new[] { "Floor type 999 is no longer in the model." },
            });
        }
    }

    private sealed class FailingHandler : IRpcHandler
    {
        public string Method => "gisStandard.setup";

        public Task<object?> HandleAsync(object? payload) =>
            throw new InvalidOperationException("Nothing was changed: 1 element(s) are borrowed by other users.");
    }

    private static async Task<JObject> Dispatch(WebRpcBridge bridge, string method, JObject payload) =>
        JObject.Parse(await bridge.DispatchRequestAsync(new JObject
        {
            ["kind"] = "req",
            ["id"] = "1",
            ["method"] = method,
            ["payload"] = payload,
        }.ToString()));

    [Fact]
    public async Task FixFloorTypesReadsTheAcceptedIdsAndRepliesWithLogAndSkipped()
    {
        WebRpcBridge bridge = new();
        FixHandler handler = new();
        bridge.RegisterHandler(handler);

        JObject response = await Dispatch(bridge, "gisStandard.fixFloorTypes", new JObject { ["floorTypeIds"] = new JArray(201, 202) });

        Assert.Equal(new long[] { 201, 202 }, handler.Received);
        Assert.Equal(new[] { "j 店舗_床 (old): gis_category retail" }, response["payload"]!["log"]!.Values<string>());
        Assert.Equal(new[] { "Floor type 999 is no longer in the model." }, response["payload"]!["skipped"]!.Values<string>());
    }

    [Fact]
    public async Task AFailedApplyReachesThePageAsTheErrorMessage()
    {
        WebRpcBridge bridge = new();
        bridge.RegisterHandler(new FailingHandler());

        JObject response = await Dispatch(bridge, "gisStandard.setup", new JObject());

        Assert.Equal("HANDLER_ERROR", (string)response["error"]!["code"]!);
        Assert.StartsWith("Nothing was changed", (string)response["error"]!["message"]!);
    }
}
