using System;
using System.IO;

namespace RevitGeoSuite.FloorPlanExport.Core;

public static class FloorPlanExportAppDataPaths
{
    /// <summary>
    /// Points the per-PC store at another folder. %APPDATA% roams to the user's other PCs, so a
    /// test run that moves per-PC rules into a station sets this to a throwaway folder.
    /// </summary>
    public const string RootOverrideVariable = "REVITGEOSUITE_FLOORPLAN_DATA";

    public static string CurrentBaseDirectory =>
        Path.Combine(Root, "RevitGeoSuite", "FloorPlanExport");

    public static string LegacyBaseDirectory => Path.Combine(Root, "RevitGeoExporter");

    private static string Root
    {
        get
        {
            string? root = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(root)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : root!.Trim();
        }
    }

    public static string CurrentSettingsFilePath => Path.Combine(CurrentBaseDirectory, "settings.json");

    public static string LegacySettingsFilePath => Path.Combine(LegacyBaseDirectory, "settings.json");

    public static string CurrentProfilesFilePath => Path.Combine(CurrentBaseDirectory, "profiles.json");

    public static string LegacyProfilesFilePath => Path.Combine(LegacyBaseDirectory, "profiles.json");

    public static string CurrentMappingRulesDirectory => Path.Combine(CurrentBaseDirectory, "mapping-rules");

    public static string LegacyMappingRulesBaseDirectory => LegacyBaseDirectory;

    public static string CurrentBaselinesDirectory => Path.Combine(CurrentBaseDirectory, "export-baselines");

    public static string LegacyBaselinesDirectory => Path.Combine(LegacyBaseDirectory, "export-baselines");
}
