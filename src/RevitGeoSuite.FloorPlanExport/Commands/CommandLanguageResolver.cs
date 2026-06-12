using RevitGeoSuite.FloorPlanExport.UI;

namespace RevitGeoSuite.FloorPlanExport.Commands;

internal static class CommandLanguageResolver
{
    public static UiLanguage Resolve()
    {
        try
        {
            return new ExportDialogSettingsStore().Load().UiLanguage;
        }
        catch
        {
            return UiLanguage.English;
        }
    }
}
