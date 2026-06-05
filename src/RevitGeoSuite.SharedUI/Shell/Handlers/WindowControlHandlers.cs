using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Shell;

namespace RevitGeoSuite.SharedUI.Shell.Handlers;

public sealed class WindowMinimizeHandler : IRpcHandler
{
    private readonly Func<Window> getWindow;

    public WindowMinimizeHandler(Func<Window> getWindow)
    {
        this.getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
    }

    public string Method => "window.minimize";

    public Task<object?> HandleAsync(object? payload)
    {
        // Use the window's own Dispatcher rather than Application.Current.Dispatcher: inside Revit
        // the add-in is hosted without a WPF Application, so Application.Current is null and the
        // dereference threw a NullReferenceException that the bridge swallowed as a HANDLER_ERROR —
        // which is why the title-bar buttons appeared dead. Every Window is a DispatcherObject.
        var window = getWindow();
        window?.Dispatcher.Invoke(() => window.WindowState = WindowState.Minimized);
        return Task.FromResult<object?>(null);
    }
}

public sealed class WindowMaximizeHandler : IRpcHandler
{
    private readonly Func<Window> getWindow;

    public WindowMaximizeHandler(Func<Window> getWindow)
    {
        this.getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
    }

    public string Method => "window.maximize";

    public Task<object?> HandleAsync(object? payload)
    {
        // See WindowMinimizeHandler: Application.Current is null in the Revit host, so dispatch on
        // the window itself.
        var window = getWindow();
        window?.Dispatcher.Invoke(() =>
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        });
        return Task.FromResult<object?>(null);
    }
}

/// <summary>
/// Starts the native Win32 edge/corner resize loop. WebView2 is hosted in a child HWND that covers
/// the WPF client area, including WindowChrome's resize border, so DOM resize grips call this on
/// mousedown and let the OS handle the rest of the gesture.
/// </summary>
public sealed class WindowResizeHandler : IRpcHandler
{
    private const int WmNcButtonDown = 0x00A1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly Func<Window> getWindow;

    public WindowResizeHandler(Func<Window> getWindow)
    {
        this.getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
    }

    public string Method => "window.resize";

    public Task<object?> HandleAsync(object? payload)
    {
        string direction = ReadDirection(payload);
        if (!TryGetHitTest(direction, out int hitTest))
        {
            return Task.FromResult<object?>(null);
        }

        var window = getWindow();
        window?.Dispatcher.Invoke(() =>
        {
            if (window.WindowState == WindowState.Maximized)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(hwnd, WmNcButtonDown, new IntPtr(hitTest), IntPtr.Zero);
        });

        return Task.FromResult<object?>(null);
    }

    private static string ReadDirection(object? payload)
    {
        if (payload is JObject jobj)
        {
            return jobj.Value<string>("direction") ?? string.Empty;
        }

        if (payload != null)
        {
            var json = JsonConvert.SerializeObject(payload);
            var obj = JObject.Parse(json);
            return obj.Value<string>("direction") ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryGetHitTest(string direction, out int hitTest)
    {
        switch (direction)
        {
            case "left":
                hitTest = HtLeft;
                return true;
            case "right":
                hitTest = HtRight;
                return true;
            case "top":
                hitTest = HtTop;
                return true;
            case "top-left":
                hitTest = HtTopLeft;
                return true;
            case "top-right":
                hitTest = HtTopRight;
                return true;
            case "bottom":
                hitTest = HtBottom;
                return true;
            case "bottom-left":
                hitTest = HtBottomLeft;
                return true;
            case "bottom-right":
                hitTest = HtBottomRight;
                return true;
            default:
                hitTest = 0;
                return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
