using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportStateService
{
    private readonly IModuleStateStore moduleStateStore;

    public CityGmlExportStateService(IModuleStateStore? moduleStateStore = null)
    {
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public CityGmlExportState? Load(IDocumentHandle document)
    {
        return moduleStateStore.Load<CityGmlExportState>(document, ModuleStateIds.CityGmlExport);
    }

    public void Save(IDocumentHandle document, CityGmlExportState state)
    {
        moduleStateStore.Save(document, ModuleStateIds.CityGmlExport, state);
    }
}
