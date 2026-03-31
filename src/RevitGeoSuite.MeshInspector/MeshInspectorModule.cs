using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.MeshInspector;

public sealed class MeshInspectorModule : IRevitGeoModule
{
    public string ModuleId => "mesh-inspector";

    public string ModuleName => "Mesh Inspector";

    public string ModuleVersion => "0.6.0-phase6";

    public string PanelName => "Project Setup";

    public int SortOrder => 20;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "MeshInspector",
                ButtonText = "Mesh\nInspector",
                ToolTip = "Inspect the primary tertiary mesh, surrounding mesh cells, and the saved shared mesh key.",
                CommandClassName = "RevitGeoSuite.MeshInspector.MeshInspectorCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location
            }
        };
    }
}
