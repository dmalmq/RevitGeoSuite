using System;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Modules;
using RevitGeoSuite.Shell.Commands;

namespace RevitGeoSuite.Shell;

public sealed class App : IExternalApplication
{
    internal const string TabName = "Revit Geo Suite";

    public Result OnStartup(UIControlledApplication application)
    {
        // Create the shared ExternalEvent marshaller while we're in a valid Revit API context.
        RevitContext.Initialize();

        EnsureRibbonTab(application);
        AddWebShellButtons(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    // Adds the three web-shell entry points to a single panel in the order Georeference,
    // Import, Export. Buttons render in add order, so this yields GEO -> IMP -> EXP. Each
    // opens the shared WebView2 shell on its route; icons mirror the in-app nav rail.
    private static void AddWebShellButtons(UIControlledApplication application)
    {
        try
        {
            RibbonPanel panel = GetOrCreatePanel(application, "Geo Suite");

            panel.AddItem(CreateButton(
                "GeoreferenceSetup",
                "Georef",
                typeof(GeoreferenceSetupCommand),
                RibbonIconKind.WebGeoreference,
                "Open the georeference workflow"));

            panel.AddItem(CreateButton(
                "PlateauImportNew",
                "Import",
                typeof(ImportCommand),
                RibbonIconKind.WebImport,
                "Open the PLATEAU import workflow"));

            panel.AddItem(CreateButton(
                "ExportNew",
                "Export",
                typeof(ExportCommand),
                RibbonIconKind.WebExport,
                "Open the export workflow"));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to add web shell buttons: {ex}");
        }
    }

    private static PushButtonData CreateButton(
        string commandId,
        string buttonText,
        Type commandType,
        RibbonIconKind iconKind,
        string toolTip)
    {
        return new PushButtonData(
            commandId,
            buttonText,
            commandType.Assembly.Location,
            commandType.FullName)
        {
            ToolTip = toolTip,
            Image = RibbonIconFactory.CreateSmall(iconKind),
            LargeImage = RibbonIconFactory.CreateLarge(iconKind)
        };
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
    {
        RibbonPanel? existing = application
            .GetRibbonPanels(TabName)
            .FirstOrDefault(panel => panel.Name == panelName);

        return existing ?? application.CreateRibbonPanel(TabName, panelName);
    }

    private static void EnsureRibbonTab(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Exception ex) when (IsDuplicateRibbonTabException(ex))
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to create ribbon tab '{TabName}': {ex}");
            throw;
        }
    }

    internal static bool IsDuplicateRibbonTabException(Exception exception)
    {
        return exception.GetType().Name.EndsWith("ArgumentException", StringComparison.Ordinal)
            && exception.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
