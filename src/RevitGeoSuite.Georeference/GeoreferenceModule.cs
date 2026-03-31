using System.Collections.Generic;
using System.Reflection;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Georeference;

public sealed class GeoreferenceModule : IRevitGeoModule
{
    public string ModuleId => "georeference";

    public string ModuleName => "Georeference";

    public string ModuleVersion => "0.3.0-phase3";

    public string PanelName => "Project Setup";

    public int SortOrder => 10;

    public IReadOnlyCollection<RevitCommandDescriptor> GetCommands()
    {
        return new[]
        {
            new RevitCommandDescriptor
            {
                CommandId = "GeoreferenceSetup",
                ButtonText = "Georeference\nSetup",
                ToolTip = "Open the read-only georeference workflow for current-state review, CRS selection, and map point capture.",
                CommandClassName = "RevitGeoSuite.Georeference.GeoreferenceCommand",
                AssemblyPath = Assembly.GetExecutingAssembly().Location
            }
        };
    }
}
