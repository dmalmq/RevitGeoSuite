using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Validation;

public sealed class ValidationModule : IRevitGeoModule
{
    public string ModuleId => "validation";

    public string ModuleName => "Validation";

    public string ModuleVersion => "0.6.0-phase6";

    public string PanelName => "Project Setup";

    public int SortOrder => 30;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "CheckProject",
                ButtonText = "Check\nProject",
                ToolTip = "Run a read-only health check against the shared geo metadata and derived readiness rules.",
                CommandClassName = "RevitGeoSuite.Validation.ValidationCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location
            }
        };
    }
}
