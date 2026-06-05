using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

public class WebRpcBridgeTests
{
    [Fact]
    public void RegisterHandler_AddsHandlerToDictionary()
    {
        var bridge = new WebRpcBridge();
        var handler = new EchoHandler();

        bridge.RegisterHandler(handler);

        Assert.Single(bridge.Handlers);
        Assert.True(bridge.Handlers.ContainsKey("echo"));
    }

    [Fact]
    public void RegisterHandler_ThrowsOnNull()
    {
        var bridge = new WebRpcBridge();

        Assert.Throws<ArgumentNullException>(() => bridge.RegisterHandler(null!));
    }

    [Fact]
    public void RegisterHandler_OverwritesExistingHandler()
    {
        var bridge = new WebRpcBridge();
        var handler1 = new EchoHandler();
        var handler2 = new EchoHandler();

        bridge.RegisterHandler(handler1);
        bridge.RegisterHandler(handler2);

        Assert.Single(bridge.Handlers);
    }

    [Fact]
    public void UnregisterHandler_RemovesHandler()
    {
        var bridge = new WebRpcBridge();
        bridge.RegisterHandler(new EchoHandler());

        bridge.UnregisterHandler("echo");

        Assert.Empty(bridge.Handlers);
    }

    [Fact]
    public async Task DispatchRequest_ReturnsMethodNotFound_WhenNoHandler()
    {
        var bridge = new WebRpcBridge();
        var request = new RpcEnvelope
        {
            Kind = "req",
            Id = "test-1",
            Method = "nonexistent",
            Payload = null
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(request));
        var response = JsonConvert.DeserializeObject<RpcEnvelope>(responseJson);

        Assert.NotNull(response);
        Assert.Equal("res", response!.Kind);
        Assert.Equal("test-1", response.Id);
        Assert.NotNull(response.Error);
        Assert.Equal("METHOD_NOT_FOUND", response.Error!.Code);
    }

    [Fact]
    public async Task DispatchRequest_EchoHandler_ReturnsPayload()
    {
        var bridge = new WebRpcBridge();
        bridge.RegisterHandler(new EchoHandler());

        var request = new RpcEnvelope
        {
            Kind = "req",
            Id = "test-2",
            Method = "echo",
            Payload = new { message = "hello" }
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(request));
        var response = JsonConvert.DeserializeObject<RpcEnvelope>(responseJson);

        Assert.NotNull(response);
        Assert.Equal("res", response!.Kind);
        Assert.Equal("test-2", response.Id);
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }

    [Fact]
    public async Task DispatchRequest_ReturnsEmpty_WhenInvalidJson()
    {
        var bridge = new WebRpcBridge();

        string responseJson = await bridge.DispatchRequestAsync("not valid json");

        Assert.Equal(string.Empty, responseJson);
    }

    [Fact]
    public async Task DispatchRequest_ReturnsEmpty_WhenNotRequest()
    {
        var bridge = new WebRpcBridge();
        var envelope = new RpcEnvelope
        {
            Kind = "evt",
            Method = "some.event"
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(envelope));

        Assert.Equal(string.Empty, responseJson);
    }

    [Fact]
    public async Task DispatchRequestAsync_HandlesAsyncHandler()
    {
        var bridge = new WebRpcBridge();
        bridge.RegisterHandler(new AsyncTestHandler());

        var request = new RpcEnvelope
        {
            Kind = "req",
            Id = "test-3",
            Method = "async.test",
            Payload = "input"
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(request));
        var response = JsonConvert.DeserializeObject<RpcEnvelope>(responseJson);

        Assert.NotNull(response);
        Assert.Equal("res", response!.Kind);
        Assert.Null(response.Error);
        Assert.Equal("input-async", response.Payload?.ToString());
    }

    [Fact]
    public async Task DispatchRequest_ReturnsHandlerError_WhenHandlerThrows()
    {
        var bridge = new WebRpcBridge();
        bridge.RegisterHandler(new ThrowingHandler());

        var request = new RpcEnvelope
        {
            Kind = "req",
            Id = "test-4",
            Method = "throwing",
            Payload = null
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(request));
        var response = JsonConvert.DeserializeObject<RpcEnvelope>(responseJson);

        Assert.NotNull(response);
        Assert.Equal("res", response!.Kind);
        Assert.NotNull(response.Error);
        Assert.Equal("HANDLER_ERROR", response.Error!.Code);
        Assert.Contains("test exception", response.Error.Message);
    }

    [Fact]
    public async Task DispatchRequest_SerializesEnvelopeInCamelCase()
    {
        // The frontend reads envelope.kind/id/method/payload (camelCase). PascalCase output would be
        // silently dropped by rpc.ts and every request() would hang. Guard the wire casing here.
        var bridge = new WebRpcBridge();
        bridge.RegisterHandler(new EchoHandler());

        var request = new RpcEnvelope
        {
            Kind = "req",
            Id = "camel-1",
            Method = "echo",
            Payload = new { message = "hi" }
        };

        string responseJson = await bridge.DispatchRequestAsync(JsonConvert.SerializeObject(request));

        Assert.Contains("\"kind\":\"res\"", responseJson);
        Assert.Contains("\"id\":\"camel-1\"", responseJson);
        Assert.Contains("\"method\":\"echo\"", responseJson);
        Assert.DoesNotContain("\"Kind\":", responseJson);
        Assert.DoesNotContain("\"Id\":", responseJson);
    }

    [Fact]
    public void Constructor_WithHandlers_RegistersAll()
    {
        var handlers = new IRpcHandler[]
        {
            new EchoHandler(),
            new AsyncTestHandler()
        };

        var bridge = new WebRpcBridge(handlers);

        Assert.Equal(2, bridge.Handlers.Count);
        Assert.True(bridge.Handlers.ContainsKey("echo"));
        Assert.True(bridge.Handlers.ContainsKey("async.test"));
    }

    private class AsyncTestHandler : IRpcHandler
    {
        public string Method => "async.test";

        public async Task<object?> HandleAsync(object? payload)
        {
            await Task.Yield();
            return $"{payload}-async";
        }
    }

    private class ThrowingHandler : IRpcHandler
    {
        public string Method => "throwing";

        public Task<object?> HandleAsync(object? payload)
        {
            throw new InvalidOperationException("test exception");
        }
    }
}
