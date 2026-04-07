using System;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class ModuleNavigationRequestedEventArgs : EventArgs
{
    public ModuleNavigationRequestedEventArgs(string moduleKey, string moduleTitle)
    {
        ModuleKey = moduleKey ?? throw new ArgumentNullException(nameof(moduleKey));
        ModuleTitle = moduleTitle ?? string.Empty;
    }

    public string ModuleKey { get; }

    public string ModuleTitle { get; }
}
