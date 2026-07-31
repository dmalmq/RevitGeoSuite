using Moq;
using RevitGeoSuite.Core.Storage;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauImportStateServiceTests
{
    [Fact]
    public void Load_and_save_use_plateau_import_module_state_key()
    {
        FakeDocumentHandle document = new FakeDocumentHandle("plateau-test");
        PlateauImportState expectedState = new PlateauImportState
        {
            LastImportedFilePath = "sample-origin-context.gml",
            LastImportedFeatureCount = 2,
            LastReferenceSource = PlateauImportReferenceSource.WorkingProjectBasePoint
        };

        Mock<IModuleStateStore> moduleStateStore = new Mock<IModuleStateStore>(MockBehavior.Strict);
        moduleStateStore
            .Setup(store => store.Load<PlateauImportState>(document, ModuleStateIds.PlateauImport))
            .Returns(expectedState);
        moduleStateStore
            .Setup(store => store.Save(document, ModuleStateIds.PlateauImport, expectedState));

        PlateauImportStateService service = new PlateauImportStateService(moduleStateStore.Object);

        PlateauImportState? loadedState = service.Load(document);
        service.Save(document, expectedState);

        Assert.Same(expectedState, loadedState);
        moduleStateStore.Verify(store => store.Load<PlateauImportState>(document, ModuleStateIds.PlateauImport), Times.Once);
        moduleStateStore.Verify(store => store.Save(document, ModuleStateIds.PlateauImport, expectedState), Times.Once);
    }

    private sealed class FakeDocumentHandle : IDocumentHandle
    {
        public FakeDocumentHandle(string documentKey)
        {
            DocumentKey = documentKey;
        }

        public string DocumentKey { get; }
    }
}
