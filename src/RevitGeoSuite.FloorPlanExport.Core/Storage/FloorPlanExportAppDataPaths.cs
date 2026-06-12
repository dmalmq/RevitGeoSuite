using System;
using System.IO;

namespace RevitGeoSuite.FloorPlanExport.Core;

public static class FloorPlanExportAppDataPaths
{
    public static string CurrentBaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitGeoSuite",
            "FloorPlanExport");

    public static string LegacyBaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitGeoExporter");

    public static string CurrentSettingsFilePath => Path.Combine(CurrentBaseDirectory, "settings.json");

    public static string LegacySettingsFilePath => Path.Combine(LegacyBaseDirectory, "settings.json");

    public static string CurrentProfilesFilePath => Path.Combine(CurrentBaseDirectory, "profiles.json");

    public static string LegacyProfilesFilePath => Path.Combine(LegacyBaseDirectory, "profiles.json");

    public static string CurrentMappingRulesDirectory => Path.Combine(CurrentBaseDirectory, "mapping-rules");

    public static string LegacyMappingRulesBaseDirectory => LegacyBaseDirectory;

    public static string CurrentBaselinesDirectory => Path.Combine(CurrentBaseDirectory, "export-baselines");

    public static string LegacyBaselinesDirectory => Path.Combine(LegacyBaseDirectory, "export-baselines");
}
