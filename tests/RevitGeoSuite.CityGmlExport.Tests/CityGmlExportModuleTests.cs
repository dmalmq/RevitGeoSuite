using RevitGeoSuite.Core.Modules;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class CityGmlExportModuleTests
{
    [Fact]
    public void Module_exposes_citygml_export_command_metadata()
    {
        CityGmlExportModule module = new CityGmlExportModule();
        RevitCommandDescriptor command = Assert.Single(module.GetCommands());

        Assert.Equal("citygml-export", module.ModuleId);
        Assert.Equal("Export", module.PanelName);
        Assert.Equal("CityGmlExport", command.CommandId);
        Assert.Equal("RevitGeoSuite.CityGmlExport.CityGmlExportCommand", command.CommandClassName);
        Assert.Equal(RibbonIconKind.CityGmlExport, command.IconKind);
        Assert.EndsWith("RevitGeoSuite.CityGmlExport.dll", command.AssemblyPath);
    }
}
