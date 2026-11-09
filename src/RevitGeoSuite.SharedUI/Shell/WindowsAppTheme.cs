using System;
using Microsoft.Win32;

namespace RevitGeoSuite.SharedUI.Shell;

/// <summary>
/// Reads the Windows app theme so the shell window opens on the same frame colour the web UI
/// paints (Kanizu's Framed sheet tokens), instead of flashing a dark window before a light page.
/// The web UI follows the Windows theme unless the user pins one.
/// </summary>
public static class WindowsAppTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Framed sheet frame colour, light: #E9EEEC.</summary>
    public static readonly (byte R, byte G, byte B) LightFrame = (0xE9, 0xEE, 0xEC);

    /// <summary>Framed sheet frame colour, dark: #121514.</summary>
    public static readonly (byte R, byte G, byte B) DarkFrame = (0x12, 0x15, 0x14);

    /// <summary>True when Windows apps use the dark theme (AppsUseLightTheme = 0).</summary>
    public static bool AppsUseDarkTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The frame colour for the current Windows app theme.</summary>
    public static (byte R, byte G, byte B) FrameColor() => AppsUseDarkTheme() ? DarkFrame : LightFrame;
}
