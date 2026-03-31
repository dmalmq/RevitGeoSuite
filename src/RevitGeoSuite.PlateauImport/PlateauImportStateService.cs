using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportStateService
{
    private readonly IModuleStateStore moduleStateStore;

    public PlateauImportStateService(IModuleStateStore? moduleStateStore = null)
    {
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public PlateauImportState? Load(IDocumentHandle document)
    {
        return moduleStateStore.Load<PlateauImportState>(document, ModuleStateIds.PlateauImport);
    }

    public void Save(IDocumentHandle document, PlateauImportState state)
    {
        moduleStateStore.Save(document, ModuleStateIds.PlateauImport, state);
    }
}
