using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauContextExportModule : IRevitGeoModule
{
    public string ModuleId => "plateau-context-export";

    public string ModuleName => "PLATEAU Context Export";

    public string ModuleVersion => "0.8.0-phase4";

    public string PanelName => "Export";

    public int SortOrder => 45;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "PlateauContextExport",
                ButtonText = "PLATEAU\nContext\nExport",
                ToolTip = "Export PLATEAU context, GSI Kiban data, and optionally Revit model footprints to Shapefile and/or DXF. Uses the same scan and tile selection workflow as PLATEAU Import.",
                CommandClassName = "RevitGeoSuite.PlateauImport.PlateauContextExportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                IconKind = RibbonIconKind.PlateauImport
            }
        };
    }
}
