using System.Windows.Media;
using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.SharedUI.Controls;

namespace RevitGeoSuite.Shell;

internal static class RibbonIconFactory
{
    public static ImageSource CreateLarge(RibbonIconKind iconKind)
    {
        return ModuleIconFactory.CreateLarge(iconKind);
    }

    public static ImageSource CreateSmall(RibbonIconKind iconKind)
    {
        return ModuleIconFactory.CreateSmall(iconKind);
    }
}
