using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportModule : IRevitGeoModule
{
    public string ModuleId => "citygml-export";

    public string ModuleName => "CityGML Export";

    public string ModuleVersion => "0.9.0-phase5";

    public string PanelName => "Export";

    public int SortOrder => 60;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "CityGmlExport",
                ButtonText = "CityGML\nExport",
                ToolTip = "Prepare a lightweight CityGML export using the shared project geo metadata, semantic mapping, attribute mapping, and separate module-specific export state.",
                CommandClassName = "RevitGeoSuite.CityGmlExport.CityGmlExportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                IconKind = RibbonIconKind.CityGmlExport
            }
        };
    }
}
