using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Shell.Handlers;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Shell.Handlers;

namespace RevitGeoSuite.Shell.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;
            var mainWindowHandle = uiApp.MainWindowHandle;

            var options = new WebShellOptions
            {
                InitialRoute = "/import",
                TitleKey = "Import",
                OwnerHandle = mainWindowHandle,
                Handlers = Array.Empty<IRpcHandler>(),
                RegisterHandlers = WebShellHandlerRegistry.RegisterAll
            };

            // Reuses the single long-lived shell window if one is already open, and registers every
            // route's handlers so rail navigation between GEO/IMP/EXP works regardless of entry point.
            var window = WebShellWindowManager.Instance.GetOrCreateWindow(options);

            window.NavigateTo("/import");
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
