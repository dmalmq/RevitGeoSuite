using Autodesk.Revit.UI;

namespace RevitGeoSuite.Shell;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        RibbonBuilder ribbonBuilder = new RibbonBuilder();
        ribbonBuilder.Build(application, ModuleRegistry.CreateModules());
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
