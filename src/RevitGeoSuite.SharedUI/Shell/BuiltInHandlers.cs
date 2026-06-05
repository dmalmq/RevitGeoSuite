using System;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.SharedUI.Shell;

internal sealed class WindowCloseHandler : IRpcHandler
{
    private readonly Func<Window> getWindow;

    public WindowCloseHandler(Func<Window> getWindow)
    {
        this.getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
    }

    public string Method => "window.close";

    public Task<object?> HandleAsync(object? payload)
    {
        // Dispatch on the window itself: Application.Current is null in the Revit host (no WPF
        // Application), so Application.Current.Dispatcher threw and the close button did nothing.
        var window = getWindow();
        window?.Dispatcher.Invoke(window.Close);
        return Task.FromResult<object?>(null);
    }
}

internal sealed class WindowNavigateHandler : IRpcHandler
{
    private readonly Func<Window> getWindow;
    private readonly Action<string> onNavigate;

    public WindowNavigateHandler(Func<Window> getWindow, Action<string> onNavigate)
    {
        this.getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
        this.onNavigate = onNavigate ?? throw new ArgumentNullException(nameof(onNavigate));
    }

    public string Method => "window.navigate";

    public Task<object?> HandleAsync(object? payload)
    {
        string route = string.Empty;
        if (payload is JObject jobj)
        {
            route = jobj.Value<string>("route") ?? string.Empty;
        }
        else if (payload != null)
        {
            var json = JsonConvert.SerializeObject(payload);
            var obj = JObject.Parse(json);
            route = obj.Value<string>("route") ?? string.Empty;
        }

        // Dispatch on the window itself: Application.Current is null in the Revit host.
        var window = getWindow();
        window?.Dispatcher.Invoke(() => onNavigate(route));
        return Task.FromResult<object?>(null);
    }
}
