using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Shell.Handlers;

namespace RevitGeoSuite.Shell.Commands;

/// <summary>The GIS standard ribbon button: opens the page that audits the model and brings it to the standard.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class GisStandardCommand : IExternalCommand
{
    public const string Route = "/gis-standard";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document? document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null || document.IsFamilyDocument)
            {
                TaskDialog.Show("Kanizu GIS standard", "Open a project model to check it against the Kanizu GIS standard.");
                return Result.Cancelled;
            }

            OpenPage(commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    /// <summary>Shows the GIS standard page in the shared shell window, opening the window when needed.</summary>
    internal static void OpenPage(IntPtr owner)
    {
        var window = WebShellWindowManager.Instance.GetOrCreateWindow(new WebShellOptions
        {
            InitialRoute = Route,
            TitleKey = "GisStandard",
            OwnerHandle = owner,
            Handlers = Array.Empty<IRpcHandler>(),
            RegisterHandlers = WebShellHandlerRegistry.RegisterAll,
        });
        window.NavigateTo(Route);
        window.Show();
        window.Activate();
    }
}
