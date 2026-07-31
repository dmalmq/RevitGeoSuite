using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportStateService
{
    private readonly IModuleStateStore moduleStateStore;

    public Tiles3DExportStateService(IModuleStateStore? moduleStateStore = null)
    {
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public Tiles3DExportState? Load(IDocumentHandle document)
    {
        return moduleStateStore.Load<Tiles3DExportState>(document, ModuleStateIds.Tiles3DExport);
    }

    public void Save(IDocumentHandle document, Tiles3DExportState state)
    {
        moduleStateStore.Save(document, ModuleStateIds.Tiles3DExport, state);
    }
}
