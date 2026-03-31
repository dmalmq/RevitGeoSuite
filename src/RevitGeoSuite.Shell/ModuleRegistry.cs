using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.MeshInspector;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.Validation;

namespace RevitGeoSuite.Shell;

public static class ModuleRegistry
{
    public static IReadOnlyList<IRevitGeoModule> CreateModules()
    {
        return new IRevitGeoModule[]
        {
            new GeoreferenceModule(),
            new MeshInspectorModule(),
            new ValidationModule(),
            new PlateauImportModule()
        }
        .OrderBy(module => module.SortOrder)
        .ToArray();
    }
}
