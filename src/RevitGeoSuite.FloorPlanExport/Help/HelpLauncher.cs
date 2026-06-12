using System;
using System.Windows.Forms;
using RevitGeoSuite.FloorPlanExport.UI;

namespace RevitGeoSuite.FloorPlanExport.Help;

public static class HelpLauncher
{
    public static void Show(IntPtr ownerHandle, HelpTopic topic, UiLanguage language, string? contextLabel = null)
    {
        Show(ownerHandle == IntPtr.Zero ? null : new Win32WindowOwner(ownerHandle), topic, language, contextLabel);
    }

    public static void Show(IWin32Window? owner, HelpTopic topic, UiLanguage language, string? contextLabel = null)
    {
        HelpContentProvider provider = new();
        HelpLanguage helpLanguage = HelpContentProvider.FromUiLanguage(language);

        using WebHelpViewerDialog webViewer = new(provider, topic, helpLanguage, contextLabel, owner?.Handle ?? IntPtr.Zero);
        _ = webViewer.ShowDialog();
    }

    private sealed class Win32WindowOwner : IWin32Window
    {
        public Win32WindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
