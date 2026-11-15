using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Shell.Handlers;

namespace RevitGeoSuite.Shell.Commands;

/// <summary>
/// The Send to Kanizu ribbon button: opens the Send page, which audits the model and, when the
/// audit has no errors, writes the GIS package to the remembered station folder at once.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class SendToKanizuCommand : IExternalCommand
{
    /// <summary>/send/now starts sending as soon as the page opens; /send, from the sidebar, waits for a click.</summary>
    public const string Route = "/send/now";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document? document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null || document.IsFamilyDocument)
            {
                TaskDialog.Show("Send to Kanizu", "Open a project model to send it to Kanizu.");
                return Result.Cancelled;
            }

            var window = WebShellWindowManager.Instance.GetOrCreateWindow(new WebShellOptions
            {
                InitialRoute = Route,
                TitleKey = "Send",
                OwnerHandle = commandData.Application.MainWindowHandle,
                Handlers = Array.Empty<IRpcHandler>(),
                RegisterHandlers = WebShellHandlerRegistry.RegisterAll,
            });
            window.NavigateTo(Route);
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
