using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportModule : IRevitGeoModule
{
    public string ModuleId => "plateau-import";

    public string ModuleName => "PLATEAU Context Import";

    public string ModuleVersion => "0.7.0-phase3";

    public string PanelName => "PLATEAU";

    public int SortOrder => 40;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "PlateauImportContext",
                ButtonText = "Import\nContext",
                ToolTip = "Load a lightweight PLATEAU CityGML file, preview the resolved context geometry, and import it into the active Revit project.",
                CommandClassName = "RevitGeoSuite.PlateauImport.PlateauImportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location
            }
        };
    }
}
