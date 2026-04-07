using Moq;
using RevitGeoSuite.Core.Storage;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class CityGmlExportStateServiceTests
{
    [Fact]
    public void Load_and_save_delegate_to_module_state_store()
    {
        Mock<IDocumentHandle> document = new Mock<IDocumentHandle>(MockBehavior.Strict);
        CityGmlExportState expectedState = new CityGmlExportState
        {
            LastExportPath = @"C:\\temp\\citygml",
            TargetSchemaVersion = CityGmlExportProfile.LightweightCityGml20,
            LastExportedFeatureCount = 4
        };

        Mock<IModuleStateStore> store = new Mock<IModuleStateStore>(MockBehavior.Strict);
        store.Setup(x => x.Load<CityGmlExportState>(document.Object, ModuleStateIds.CityGmlExport)).Returns(expectedState);
        store.Setup(x => x.Save(document.Object, ModuleStateIds.CityGmlExport, expectedState));

        CityGmlExportStateService service = new CityGmlExportStateService(store.Object);
        CityGmlExportState? loaded = service.Load(document.Object);
        service.Save(document.Object, expectedState);

        Assert.Same(expectedState, loaded);
        store.VerifyAll();
    }
}
