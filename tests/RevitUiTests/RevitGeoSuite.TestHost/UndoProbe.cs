using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// What decides whether Revit offers Undo: the command state Revit reports, which window is in front,
/// and RevitGeoSuite's shell window. Also the hand moves a person makes before Ctrl+Z: click in the
/// view, minimise or close the shell window, press Ctrl+Z.
/// </summary>
internal static class UndoProbe
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint WmClose = 0x0010;

    /// <summary>Revit's own answer, read on the API thread.</summary>
    public static Dictionary<string, object?> Commands(UIApplication app)
    {
        bool Can(string id) => RevitCommandId.LookupCommandId(id) is RevitCommandId command && app.CanPostCommand(command);
        UIDocument? ui = app.ActiveUIDocument;
        return new Dictionary<string, object?>
        {
            ["canUndo"] = Can("ID_EDIT_UNDO"),
            ["canRedo"] = Can("ID_EDIT_REDO"),
            ["canDefault3D"] = Can("ID_VIEW_DEFAULT_3DVIEW"),
            ["view"] = ui?.ActiveView?.Name,
            ["modifiable"] = ui?.Document.IsModifiable,
        };
    }

    /// <summary>The windows, read off the API thread.</summary>
    public static Dictionary<string, object?> Windows()
    {
        IntPtr main = Process.GetCurrentProcess().MainWindowHandle;
        IntPtr front = GetForegroundWindow();
        return new Dictionary<string, object?>
        {
            ["foreground"] = front == main ? "Revit main window" : $"'{Title(front)}' ({Class(front)}){(Ours(front) ? ", Revit's process" : ", another program")}",
            ["shell"] = ShellWindows().Select(window => $"'{Title(window)}' visible {IsWindowVisible(window)}, minimised {IsIconic(window)}, in front {window == front}").ToArray(),
        };
    }

    public static string Shell(string action)
    {
        IntPtr[] windows = ShellWindows();
        foreach (IntPtr window in windows)
        {
            switch (action)
            {
                case "minimize": ShowWindow(window, SwMinimize); break;
                case "restore": ShowWindow(window, SwRestore); break;
                case "hide": ShowWindow(window, SwHide); break;
                case "show": ShowWindow(window, SwShow); break;
                case "close": PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero); break;
                default: throw new ArgumentException($"Unknown shell window action '{action}'.");
            }
        }

        Thread.Sleep(1000);
        return $"{action} on {windows.Length} shell window(s)";
    }

    /// <summary>The active view's rectangle on screen, read on the API thread.</summary>
    public static (int Left, int Top, int Right, int Bottom)? ViewRectangle(UIApplication app)
    {
        UIDocument? ui = app.ActiveUIDocument;
        UIView? view = ui?.GetOpenUIViews().FirstOrDefault(open => open.ViewId == ui.ActiveView.Id);
        if (view == null)
        {
            return null;
        }

        Rectangle rectangle = view.GetWindowRectangle();
        return (rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    /// <summary>
    /// Brings Revit forward and clicks near the view's lower-left corner, then Escape to clear any selection.
    /// Clicks nothing unless Revit's main window is in front, so the click cannot land in another program.
    /// </summary>
    public static string ClickView((int Left, int Top, int Right, int Bottom) rectangle)
    {
        if (!RevitInFront(out string front))
        {
            return $"{front}; clicked nothing";
        }

        int x = rectangle.Left + 20, y = rectangle.Bottom - 20;
        SetCursorPos(x, y);
        if (!Send(Mouse(0x0002), Mouse(0x0004)))
        {
            return $"{front}; Windows did not take the click at ({x}, {y})";
        }

        Thread.Sleep(300);
        bool escaped = Send(Key(0x1B, false), Key(0x1B, true));
        Thread.Sleep(500);
        return $"{front}; clicked the view at ({x}, {y}){(escaped ? "" : ", Windows did not take Escape")}";
    }

    /// <summary>Ctrl+Z as a person types it, into Revit's main window brought forward.</summary>
    public static Typing CtrlZ() => Control('Z');

    /// <summary>
    /// Ctrl+&lt;letter&gt; typed into Revit's main window. On the build PC Revit reports Undo as unavailable
    /// (CanPostCommand false, the toolbar button greyed) in every window state, yet Ctrl+Z typed into its
    /// main window undoes a RevitGeoSuite Apply (undo-probe, bbf2917), so typing is what a person does.
    /// Types nothing unless Revit's main window is in front, so the keys cannot land in another program,
    /// another Revit or a dialog; Typed says whether Windows took all four key events.
    /// </summary>
    public static Typing Control(char letter)
    {
        if (!RevitInFront(out string front))
        {
            return new Typing(false, $"{front}; typed nothing");
        }

        ushort key = letter;
        if (!Send(Key(0x11, false), Key(key, false), Key(key, true), Key(0x11, true)))
        {
            return new Typing(false, $"{front}; Windows did not take Ctrl+{letter}");
        }

        Thread.Sleep(1500);
        return new Typing(true, $"{front}; typed Ctrl+{letter}");
    }

    public sealed class Typing(bool typed, string detail)
    {
        public bool Typed { get; } = typed;

        public string Detail { get; } = detail;

        public Dictionary<string, object?> Reply() => new() { ["typed"] = Typed, ["typing"] = Detail };
    }

    private static bool RevitInFront(out string front)
    {
        front = Win32Dialogs.ActivateMainWindow();
        IntPtr main = Process.GetCurrentProcess().MainWindowHandle;
        return main != IntPtr.Zero && GetForegroundWindow() == main;
    }

    private static IntPtr[] ShellWindows()
    {
        IntPtr main = Process.GetCurrentProcess().MainWindowHandle;
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (window != main && Ours(window) && Title(window).IndexOf("RevitGeoSuite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found.Add(window);
            }

            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    private static bool Ours(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint process);
        return process == (uint)Process.GetCurrentProcess().Id;
    }

    private static string Title(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static string Class(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private static bool Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == inputs.Length;

    private static Input Mouse(uint flags) => new() { Type = 0, Union = new InputUnion { Mouse = new MouseInput { Flags = flags } } };

    private static Input Key(ushort key, bool up) => new() { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = up ? 0x0002u : 0u } } };

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
}
