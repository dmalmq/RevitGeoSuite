using System.Linq;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.Shell;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class GeoreferenceModuleTests
{
    [Fact]
    public void Module_exposes_georeference_command_metadata()
    {
        GeoreferenceModule module = new GeoreferenceModule();
        var command = Assert.Single(module.GetCommands());

        Assert.Equal("georeference", module.ModuleId);
        Assert.Equal("Project Setup", module.PanelName);
        Assert.Equal("GeoreferenceSetup", command.CommandId);
        Assert.Equal("RevitGeoSuite.Georeference.GeoreferenceCommand", command.CommandClassName);
        Assert.EndsWith("RevitGeoSuite.Georeference.dll", command.AssemblyPath);
    }

    [Fact]
    public void Shell_registry_statically_registers_current_modules_in_order()
    {
        var modules = ModuleRegistry.CreateModules();
        string[] moduleIds = modules.Select(module => module.ModuleId).ToArray();

        Assert.Equal(new[] { "georeference", "mesh-inspector", "validation", "plateau-import" }, moduleIds);
    }
}
