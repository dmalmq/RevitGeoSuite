using System.Linq;
using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.PlateauImport;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauImportModuleTests
{
    [Fact]
    public void Module_exposes_both_import_commands()
    {
        PlateauImportModule module = new PlateauImportModule();
        var commands = module.GetCommands();

        Assert.Equal("plateau-import", module.ModuleId);
        Assert.Equal("PLATEAU", module.PanelName);
        Assert.Equal(2, commands.Count);

        var localCommand = Assert.Single(commands, c => c.CommandId == "PlateauImportContext");
        Assert.Equal("RevitGeoSuite.PlateauImport.PlateauImportCommand", localCommand.CommandClassName);
        Assert.Equal(RibbonIconKind.PlateauImport, localCommand.IconKind);
        Assert.EndsWith("RevitGeoSuite.PlateauImport.dll", localCommand.AssemblyPath);

        var onlineCommand = Assert.Single(commands, c => c.CommandId == "PlateauImportOnline");
        Assert.Equal("RevitGeoSuite.PlateauImport.Online.PlateauOnlineImportCommand", onlineCommand.CommandClassName);
        Assert.EndsWith("RevitGeoSuite.PlateauImport.dll", onlineCommand.AssemblyPath);
    }
}
