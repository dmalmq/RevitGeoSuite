using System;
using System.IO;

namespace RevitGeoSuite.FloorPlanExport.Core;

public static class FloorPlanExportAppDataPaths
{
    /// <summary>
    /// Points the per-PC store at another folder. %APPDATA% roams to the user's other PCs, so a
    /// test run that moves per-PC rules into a station sets this to a throwaway folder.
    /// </summary>
    public const string RootOverrideVariable = "KANIZU_REVIT_FLOORPLAN_DATA";

    /// <summary>Legacy environment variable honored as a fallback when <see cref="RootOverrideVariable"/> is unset.</summary>
    public const string LegacyRootOverrideVariable = "REVITGEOSUITE_FLOORPLAN_DATA";

    public static string CurrentBaseDirectory =>
        Path.Combine(Root, "KanizuRevit", "FloorPlanExport");

    public static string LegacyRevitGeoSuiteBaseDirectory =>
        Path.Combine(Root, "RevitGeoSuite", "FloorPlanExport");

    public static string LegacyBaseDirectory => Path.Combine(Root, "RevitGeoExporter");

    private static string Root
    {
        get
        {
            string? root = Environment.GetEnvironmentVariable(RootOverrideVariable);
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root!.Trim();
            }

            string? legacyRoot = Environment.GetEnvironmentVariable(LegacyRootOverrideVariable);
            if (!string.IsNullOrWhiteSpace(legacyRoot))
            {
                return legacyRoot!.Trim();
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
    }

    public static string CurrentSettingsFilePath => Path.Combine(CurrentBaseDirectory, "settings.json");

    public static string LegacyRevitGeoSuiteSettingsFilePath => Path.Combine(LegacyRevitGeoSuiteBaseDirectory, "settings.json");

    public static string LegacySettingsFilePath => Path.Combine(LegacyBaseDirectory, "settings.json");

    public static string CurrentProfilesFilePath => Path.Combine(CurrentBaseDirectory, "profiles.json");

    public static string LegacyRevitGeoSuiteProfilesFilePath => Path.Combine(LegacyRevitGeoSuiteBaseDirectory, "profiles.json");

    public static string LegacyProfilesFilePath => Path.Combine(LegacyBaseDirectory, "profiles.json");

    public static string CurrentMappingRulesDirectory => Path.Combine(CurrentBaseDirectory, "mapping-rules");

    public static string LegacyRevitGeoSuiteMappingRulesDirectory => Path.Combine(LegacyRevitGeoSuiteBaseDirectory, "mapping-rules");

    public static string LegacyMappingRulesBaseDirectory => LegacyBaseDirectory;

    public static string CurrentBaselinesDirectory => Path.Combine(CurrentBaseDirectory, "export-baselines");

    public static string LegacyRevitGeoSuiteBaselinesDirectory => Path.Combine(LegacyRevitGeoSuiteBaseDirectory, "export-baselines");

    public static string LegacyBaselinesDirectory => Path.Combine(LegacyBaseDirectory, "export-baselines");

    public static string CurrentNoticesFilePath => Path.Combine(CurrentBaseDirectory, "gis-standard-notices.json");

    public static string LegacyRevitGeoSuiteNoticesFilePath => Path.Combine(LegacyRevitGeoSuiteBaseDirectory, "gis-standard-notices.json");
}
