using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportModule : IRevitGeoModule
{
    public string ModuleId => "plateau-import";

    public string ModuleName => "PLATEAU Context Import";

    public string ModuleVersion => "0.8.0-phase4";

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
                ToolTip = "Scan a PLATEAU folder, preview selected categories and grid tiles, and import grouped lightweight or detailed PLATEAU context geometry into the active Revit project.",
                CommandClassName = "RevitGeoSuite.PlateauImport.PlateauImportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                IconKind = RibbonIconKind.PlateauImport
            },
            new RevitCommandDescriptor
            {
                CommandId = "PlateauImportOnline",
                ButtonText = "Import\nPLATEAU\nOnline",
                ToolTip = "Download PLATEAU 3D Tiles from the public datacatalog API for a chosen prefecture/municipality and import them as LOD2 DirectShape buildings, or export their footprints to DXF.",
                CommandClassName = "RevitGeoSuite.PlateauImport.Online.PlateauOnlineImportCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                IconKind = RibbonIconKind.PlateauImport
            }
        };
    }
}
