using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.Tiles3DExport;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExportModuleTests
{
    [Fact]
    public void Module_exposes_3d_tiles_export_command_metadata()
    {
        Tiles3DExportModule module = new Tiles3DExportModule();
        var command = Assert.Single(module.GetCommands());

        Assert.Equal("tiles3d-export", module.ModuleId);
        Assert.Equal("Export", module.PanelName);
        Assert.Equal("Tiles3DExport", command.CommandId);
        Assert.Equal("RevitGeoSuite.Tiles3DExport.Tiles3DExportCommand", command.CommandClassName);
        Assert.Equal(RibbonIconKind.Tiles3DExport, command.IconKind);
        Assert.EndsWith("RevitGeoSuite.Tiles3DExport.dll", command.AssemblyPath);
    }
}
