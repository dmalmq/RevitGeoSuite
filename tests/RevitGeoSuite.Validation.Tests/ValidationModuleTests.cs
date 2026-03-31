using RevitGeoSuite.Validation;
using Xunit;

namespace RevitGeoSuite.Validation.Tests;

public sealed class ValidationModuleTests
{
    [Fact]
    public void Module_exposes_check_project_command_metadata()
    {
        ValidationModule module = new ValidationModule();
        var command = Assert.Single(module.GetCommands());

        Assert.Equal("validation", module.ModuleId);
        Assert.Equal("Project Setup", module.PanelName);
        Assert.Equal("CheckProject", command.CommandId);
        Assert.Equal("RevitGeoSuite.Validation.ValidationCommand", command.CommandClassName);
        Assert.EndsWith("RevitGeoSuite.Validation.dll", command.AssemblyPath);
    }
}
