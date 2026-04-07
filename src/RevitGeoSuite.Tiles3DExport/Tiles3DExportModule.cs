using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportModule : IRevitGeoModule
{
    public string ModuleId => "tiles3d-export";

    public string ModuleName => "3D Tiles Export";

    public string ModuleVersion => "0.8.0-phase4";

    public string PanelName => "Export";

    public int SortOrder => 50;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "Tiles3DExport",
                ButtonText = "3D Tiles\nExport",
                ToolTip = "Prepare a viewer-oriented 3D Tiles export using the shared project geo metadata and save export preferences separately from GeoProjectInfo.",
                CommandClassName = "RevitGeoSuite.Tiles3DExport.Tiles3DExportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                IconKind = RibbonIconKind.Tiles3DExport
            }
        };
    }
}
