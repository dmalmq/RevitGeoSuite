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
        public (long FloorTypeId, string StandardTypeName)[] Received { get; private set; } = Array.Empty<(long, string)>();

        public string Method => "gisStandard.fixFloorTypes";

        public Task<object?> HandleAsync(object? payload)
        {
            // The same parse as GisFixFloorTypesHandler.
            Received = ((payload as JObject)?.ToObject<GisFixFloorTypesRequest>() ?? new GisFixFloorTypesRequest()).Accepted
                .Select(entry => (entry.FloorTypeId, entry.StandardTypeName))
                .ToArray();
            return Task.FromResult<object?>(new GisStandardApplyResponse
            {
                Log = new[] { "j ラチ外店舗_床 (old): gis_category retail" },
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
    public async Task FixFloorTypesReadsTheAcceptedPairsAndRepliesWithLogAndSkipped()
    {
        WebRpcBridge bridge = new();
        FixHandler handler = new();
        bridge.RegisterHandler(handler);

        JObject response = await Dispatch(bridge, "gisStandard.fixFloorTypes", new JObject
        {
            ["accepted"] = new JArray(
                new JObject { ["floorTypeId"] = 201, ["standardTypeName"] = "j ラチ外店舗_床" },
                new JObject { ["floorTypeId"] = 202, ["standardTypeName"] = "j ラチ内店舗_床" }),
        });

        Assert.Equal(new[] { (201L, "j ラチ外店舗_床"), (202L, "j ラチ内店舗_床") }, handler.Received);
        Assert.Equal(new[] { "j ラチ外店舗_床 (old): gis_category retail" }, response["payload"]!["log"]!.Values<string>());
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
