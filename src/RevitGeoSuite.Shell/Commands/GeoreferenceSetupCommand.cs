using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.SharedUI.Shell;

namespace RevitGeoSuite.Shell.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class GeoreferenceSetupCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;
            var mainWindowHandle = uiApp.MainWindowHandle;

            var options = new WebShellOptions
            {
                InitialRoute = "/georeference",
                TitleKey = "GeoreferenceSetup",
                OwnerHandle = mainWindowHandle,
                Handlers = Array.Empty<IRpcHandler>(),
                RegisterHandlers = WebShellHandlerRegistry.RegisterAll
            };

            var window = WebShellWindowManager.Instance.GetOrCreateWindow(options);

            window.NavigateTo("/georeference");
            window.Show();
            window.Activate();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
