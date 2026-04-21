using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Shell;

public sealed class RibbonBuilder
{
    public const string TabName = "Revit Geo Suite";

    public void Build(UIControlledApplication application, IReadOnlyCollection<IRevitGeoModule> modules)
    {
        EnsureRibbonTab(application);

        foreach (IRevitGeoModule module in modules)
        {
            RibbonPanel panel = GetOrCreatePanel(application, module.PanelName);

            foreach (RevitCommandDescriptor command in module.GetCommands())
            {
                PushButtonData buttonData = new PushButtonData(
                    command.CommandId,
                    command.ButtonText,
                    command.AssemblyPath,
                    command.CommandClassName)
                {
                    ToolTip = command.ToolTip,
                    Image = RibbonIconFactory.CreateSmall(command.IconKind),
                    LargeImage = RibbonIconFactory.CreateLarge(command.IconKind)
                };

                bool alreadyExists = panel.GetItems()
                    .OfType<PushButton>()
                    .Any(item => item.Name == command.CommandId);

                if (!alreadyExists)
                {
                    panel.AddItem(buttonData);
                }
            }
        }
    }

    private static void EnsureRibbonTab(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (System.Exception ex) when (IsDuplicateRibbonTabException(ex))
        {
        }
        catch (System.Exception ex)
        {
            Trace.WriteLine($"Failed to create ribbon tab '{TabName}': {ex}");
            throw;
        }
    }

    internal static bool IsDuplicateRibbonTabException(System.Exception exception)
    {
        return exception.GetType().Name.EndsWith("ArgumentException", System.StringComparison.Ordinal)
            && exception.Message.IndexOf("already exists", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
    {
        RibbonPanel? existingPanel = application
            .GetRibbonPanels(TabName)
            .FirstOrDefault(panel => panel.Name == panelName);

        return existingPanel ?? application.CreateRibbonPanel(TabName, panelName);
    }
}
