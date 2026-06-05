using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.SharedUI.Shell;

public sealed class WebRpcBridge
{
    // The frontend (rpc.ts) and generated contracts read camelCase fields (envelope.kind/id/payload
    // and camelCase payload props). Newtonsoft defaults to PascalCase, so every response/event must
    // be serialized with the camelCase resolver or the browser drops it and request() hangs forever.
    // ProcessDictionaryKeys is OFF: dictionary keys (e.g. localization keys like "Export.Title")
    // are data the web looks up verbatim, not C# property names, so they must NOT be camel-cased.
    private static readonly JsonSerializerSettings WireSettings = new JsonSerializerSettings
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy
            {
                ProcessDictionaryKeys = false,
                OverrideSpecifiedNames = true
            }
        }
    };

    private readonly Dictionary<string, IRpcHandler> handlers = new();
    private CoreWebView2? webView;

    public WebRpcBridge(IEnumerable<IRpcHandler>? handlers = null)
    {
        if (handlers != null)
        {
            foreach (var handler in handlers)
            {
                RegisterHandler(handler);
            }
        }
    }

    public IReadOnlyDictionary<string, IRpcHandler> Handlers => handlers;

    public void RegisterHandler(IRpcHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        handlers[handler.Method] = handler;
    }

    public void UnregisterHandler(string method)
    {
        handlers.Remove(method);
    }

    public void Attach(CoreWebView2 webView)
    {
        this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    public void Detach()
    {
        if (webView != null)
        {
            webView.WebMessageReceived -= OnWebMessageReceived;
            webView = null;
        }
    }

    public void SendEvent(string method, object? payload = null)
    {
        if (webView == null) return;

        var envelope = new RpcEnvelope
        {
            Kind = "evt",
            Method = method,
            Payload = payload
        };

        string json = JsonConvert.SerializeObject(envelope, WireSettings);
        webView.PostWebMessageAsJson(json);
    }

    public async Task<string> DispatchRequestAsync(string requestJson)
    {
        RpcEnvelope? request;
        try
        {
            request = JsonConvert.DeserializeObject<RpcEnvelope>(requestJson);
        }
        catch
        {
            return string.Empty;
        }

        if (request == null || request.Kind != "req")
        {
            return string.Empty;
        }

        var response = await BuildResponseAsync(request);
        return JsonConvert.SerializeObject(response, WireSettings);
    }

    private async Task<RpcEnvelope> BuildResponseAsync(RpcEnvelope request)
    {
        var response = new RpcEnvelope
        {
            Kind = "res",
            Id = request.Id,
            Method = request.Method
        };

        if (!handlers.TryGetValue(request.Method, out var handler))
        {
            response.Error = new RpcError
            {
                Code = "METHOD_NOT_FOUND",
                Message = $"No handler registered for method: {request.Method}"
            };
        }
        else
        {
            try
            {
                response.Payload = await handler.HandleAsync(request.Payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response.Error = new RpcError
                {
                    Code = "HANDLER_ERROR",
                    Message = ex.Message
                };
            }
        }

        return response;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _ = HandleMessageAsync(e.WebMessageAsJson);
    }

    private async Task HandleMessageAsync(string json)
    {
        if (webView == null) return;

        string responseJson = await DispatchRequestAsync(json);
        if (!string.IsNullOrEmpty(responseJson))
        {
            webView.PostWebMessageAsJson(responseJson);
        }
    }
}
