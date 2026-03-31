using RevitGeoSuite.MeshInspector;
using Xunit;

namespace RevitGeoSuite.MeshInspector.Tests;

public sealed class MeshInspectorModuleTests
{
    [Fact]
    public void Module_exposes_mesh_inspector_command_metadata()
    {
        MeshInspectorModule module = new MeshInspectorModule();
        var command = Assert.Single(module.GetCommands());

        Assert.Equal("mesh-inspector", module.ModuleId);
        Assert.Equal("Project Setup", module.PanelName);
        Assert.Equal("MeshInspector", command.CommandId);
        Assert.Equal("RevitGeoSuite.MeshInspector.MeshInspectorCommand", command.CommandClassName);
        Assert.EndsWith("RevitGeoSuite.MeshInspector.dll", command.AssemblyPath);
    }
}
