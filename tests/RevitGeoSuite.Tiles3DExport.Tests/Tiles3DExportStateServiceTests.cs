using System.Collections.Generic;
using Moq;
using RevitGeoSuite.Core.Storage;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExportStateServiceTests
{
    [Fact]
    public void Load_and_save_delegate_to_module_state_store()
    {
        Mock<IDocumentHandle> document = new Mock<IDocumentHandle>(MockBehavior.Strict);
        Tiles3DExportState expectedState = new Tiles3DExportState
        {
            LastExportPath = @"C:\\temp\\tiles",
            LastLodSetting = "Medium",
            LastScopeMode = Tiles3DExportScopeMode.Selected3DView,
            LastViewUniqueId = "view-1",
            LastViewName = "Export View",
            LastSelectedLinkUniqueIds = new List<string> { "link-1" },
            LastSelectedLinkNames = new List<string> { "Architectural Link" },
            LastExportedElementCount = 4,
            LastExportedTriangleCount = 12
        };

        Mock<IModuleStateStore> store = new Mock<IModuleStateStore>(MockBehavior.Strict);
        store.Setup(x => x.Load<Tiles3DExportState>(document.Object, ModuleStateIds.Tiles3DExport)).Returns(expectedState);
        store.Setup(x => x.Save(document.Object, ModuleStateIds.Tiles3DExport, expectedState));

        Tiles3DExportStateService service = new Tiles3DExportStateService(store.Object);
        Tiles3DExportState? loaded = service.Load(document.Object);
        service.Save(document.Object, expectedState);

        Assert.Same(expectedState, loaded);
        store.VerifyAll();
    }
}