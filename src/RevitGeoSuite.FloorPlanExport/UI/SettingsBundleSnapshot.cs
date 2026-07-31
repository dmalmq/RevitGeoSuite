using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;

namespace RevitGeoSuite.FloorPlanExport.UI;

public sealed class SettingsBundleSnapshot
{
    public ExportDialogSettings GlobalSettings { get; set; } = new();

    public IReadOnlyList<ExportProfile> Profiles { get; set; } = new List<ExportProfile>();

    public ProjectMappingRules ProjectMappings { get; set; } = ProjectMappingRules.Empty;

    public IReadOnlyList<SettingsStatusEntry> StatusEntries { get; set; } = new List<SettingsStatusEntry>();
}
