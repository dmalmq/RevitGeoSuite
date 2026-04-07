using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.PlateauImport;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauImportModuleTests
{
    [Fact]
    public void Module_exposes_import_context_command_metadata()
    {
        PlateauImportModule module = new PlateauImportModule();
        var command = Assert.Single(module.GetCommands());

        Assert.Equal("plateau-import", module.ModuleId);
        Assert.Equal("PLATEAU", module.PanelName);
        Assert.Equal("PlateauImportContext", command.CommandId);
        Assert.Equal("RevitGeoSuite.PlateauImport.PlateauImportCommand", command.CommandClassName);
        Assert.Equal(RibbonIconKind.PlateauImport, command.IconKind);
        Assert.EndsWith("RevitGeoSuite.PlateauImport.dll", command.AssemblyPath);
    }
}
