using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Answers the Windows dialogs that the web page cannot reach: the Open/Save/folder dialogs the
/// page's buttons open, and any message box left on screen. Runs on the pipe thread, because Revit's
/// UI thread is inside the dialog's modal loop while it is open.
/// </summary>
internal static class Win32Dialogs
{
    private const string DialogClass = "#32770";
    private const int FileNameComboId = 1148; // Open and folder dialogs: ComboBoxEx32 > ComboBox > Edit
    private const int SaveFileNameId = 1001;  // Save dialog: ComboBox > Edit
    private const int FolderNameId = 1152;    // Folder picker ("Select Folder"): the Folder: Edit itself
    private const int OkButtonId = 1;
    private const int CancelButtonId = 2;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SETTEXT = 0x000C;
    private const uint BM_CLICK = 0x00F5;

    /// <summary>Waits for a file or folder dialog of this process, types <paramref name="path"/> and presses its OK button.</summary>
    public static Dictionary<string, object?> FillFileDialog(string path, int timeoutMs)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            foreach (IntPtr dialog in Dialogs())
            {
                IntPtr edit = FileNameEdit(dialog);
                IntPtr ok = Descendants(dialog).FirstOrDefault(child => GetDlgCtrlID(child) == OkButtonId && ClassOf(child) == "Button");
                if (edit == IntPtr.Zero || ok == IntPtr.Zero)
                {
                    continue;
                }

                string title = TitleOf(dialog);
                SendMessage(edit, WM_SETTEXT, IntPtr.Zero, path);
                PostMessage(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                if (!WaitClosed(dialog, 10000))
                {
                    throw new InvalidOperationException($"The dialog '{title}' stayed open after OK. Still on screen: {Describe()}");
                }

                return new Dictionary<string, object?> { ["dialog"] = title, ["path"] = path };
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"No file dialog appeared within {timeoutMs} ms. On screen: {Describe()}");
    }

    /// <summary>
    /// Closes every dialog of this process, with its Cancel button when it has one and WM_CLOSE
    /// otherwise, so that a step that failed with a dialog open does not block the next step.
    /// </summary>
    public static Dictionary<string, object?> CloseAll(int timeoutMs)
    {
        var closed = new List<Dictionary<string, object?>>();
        Stopwatch clock = Stopwatch.StartNew();
        while (Dialogs().Count > 0 && clock.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(200); // a dialog a step just answered may still be on its way out
        }

        for (List<IntPtr> open = Dialogs(); open.Count > 0 && clock.ElapsedMilliseconds < timeoutMs; open = Dialogs())
        {
            IntPtr dialog = open[0];
            string title = TitleOf(dialog);
            string text = TextOf(dialog);
            IntPtr cancel = Descendants(dialog).FirstOrDefault(child => GetDlgCtrlID(child) == CancelButtonId && ClassOf(child) == "Button");
            if (cancel != IntPtr.Zero)
            {
                PostMessage(cancel, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            closed.Add(new Dictionary<string, object?> { ["title"] = title, ["text"] = text, ["by"] = cancel != IntPtr.Zero ? "Cancel" : "WM_CLOSE", ["closed"] = WaitClosed(dialog, 5000) });
        }

        return new Dictionary<string, object?> { ["closed"] = closed, ["stillOpen"] = Dialogs().Select(TitleOf).ToList() };
    }

    /// <summary>The visible dialogs of this process, with their title and the text a person would read.</summary>
    public static Dictionary<string, object?> List() => new()
    {
        ["windows"] = Dialogs().Select(dialog => new Dictionary<string, object?>
        {
            ["title"] = TitleOf(dialog),
            ["text"] = TextOf(dialog),
        }).ToList(),
    };

    /// <summary>Presses the button named <paramref name="button"/> on the first dialog that has one.</summary>
    public static Dictionary<string, object?> Press(string button, int timeoutMs)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            foreach (IntPtr dialog in Dialogs())
            {
                AutomationElement? target = AutomationElement.FromHandle(dialog).FindFirst(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                        new PropertyCondition(AutomationElement.NameProperty, button, PropertyConditionFlags.IgnoreCase)));
                if (target?.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
                {
                    string title = TitleOf(dialog);
                    string text = TextOf(dialog);
                    invoke.Invoke();
                    return new Dictionary<string, object?> { ["title"] = title, ["text"] = text };
                }
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"No dialog with a '{button}' button within {timeoutMs} ms. On screen: {Describe()}");
    }

    /// <summary>
    /// Invokes the first enabled button of Revit's main window named one of <paramref name="names"/>
    /// (the Quick Access Toolbar's Undo, for example). Throws, naming what it found, when none is enabled.
    /// </summary>
    public static Dictionary<string, object?> PressMainWindowButton(string[] names, string why)
    {
        IntPtr window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException($"{why} Revit's main window is not available to press {names[0]} instead.");
        }

        Condition named = new OrCondition(names.Select(name => (Condition)new PropertyCondition(AutomationElement.NameProperty, name)).Concat(new[] { Condition.FalseCondition }).ToArray());
        AutomationElementCollection found = AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants, named);
        var seen = new List<string>();
        foreach (AutomationElement element in found)
        {
            seen.Add($"{element.Current.ControlType.ProgrammaticName} '{element.Current.Name}' enabled={element.Current.IsEnabled}");
            if (element.Current.IsEnabled && element.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return new Dictionary<string, object?> { ["pressed"] = element.Current.Name, ["why"] = why };
            }
        }

        throw new InvalidOperationException($"{why} No enabled '{string.Join("' or '", names)}' button to press instead (found: {(seen.Count == 0 ? "none" : string.Join("; ", seen))}).");
    }

    /// <summary>Saves a PNG of Revit's main window as it is on screen.</summary>
    public static void CaptureMainWindow(string path)
    {
        IntPtr window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == IntPtr.Zero || !GetWindowRect(window, out RECT rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            throw new InvalidOperationException("Revit's main window is not on screen.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static string Describe()
    {
        List<string> open = Dialogs().Select(dialog => $"'{TitleOf(dialog)}'").ToList();
        return open.Count == 0 ? "no dialog" : string.Join(", ", open);
    }

    private static IntPtr FileNameEdit(IntPtr dialog)
    {
        foreach (IntPtr child in Descendants(dialog).Where(child => ClassOf(child) == "Edit"))
        {
            if (GetDlgCtrlID(child) == FolderNameId && IsWindowVisible(child))
            {
                return child;
            }

            for (IntPtr parent = GetParent(child); parent != IntPtr.Zero && parent != dialog; parent = GetParent(parent))
            {
                int id = GetDlgCtrlID(parent);
                if (id == FileNameComboId || id == SaveFileNameId)
                {
                    return child;
                }
            }
        }

        return IntPtr.Zero;
    }

    private static List<IntPtr> Dialogs()
    {
        uint process = (uint)Process.GetCurrentProcess().Id;
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == process && IsWindowVisible(window) && ClassOf(window) == DialogClass)
            {
                found.Add(window);
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static List<IntPtr> Descendants(IntPtr parent)
    {
        var found = new List<IntPtr>();
        EnumChildWindows(parent, (child, _) => { found.Add(child); return true; }, IntPtr.Zero);
        return found;
    }

    private static bool WaitClosed(IntPtr window, int timeoutMs)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (!IsWindow(window) || !IsWindowVisible(window))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    // TaskDialog text lives in a DirectUIHWND, which plain window text cannot read; UI Automation can.
    private static string TextOf(IntPtr dialog)
    {
        try
        {
            AutomationElementCollection texts = AutomationElement.FromHandle(dialog).FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            return string.Join("\n", texts.Cast<AutomationElement>().Select(text => text.Current.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
