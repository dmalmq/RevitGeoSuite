using System.Collections.Generic;

namespace RevitGeoSuite.Core.Modules;

public interface IRevitGeoModule
{
    string ModuleId { get; }

    string ModuleName { get; }

    string ModuleVersion { get; }

    string PanelName { get; }

    int SortOrder { get; }

    IReadOnlyCollection<RevitCommandDescriptor> GetCommands();
}
