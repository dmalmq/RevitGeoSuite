using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost.Loader;

/// <summary>
/// Loads the test host (RevitGeoSuite.TestHost.dll, next to this DLL) only when Revit was started for
/// a UI test run, and hands it Revit's start and shutdown. Revit's add-in security prompt covers the
/// DLL its manifest names, which is this one; the test host it loads can change with every build.
/// </summary>
public sealed class LoaderApp : IExternalApplication
{
    private const string EnableVariable = "REVITGEOSUITE_UITEST";
    private const string LogicAssembly = "RevitGeoSuite.TestHost.dll";
    private const string LogicType = "RevitGeoSuite.TestHost.TestHostApp";

    private IExternalApplication? host;

    public Result OnStartup(UIControlledApplication application)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return Result.Succeeded;
        }

        string folder = Path.GetDirectoryName(typeof(LoaderApp).Assembly.Location) ?? string.Empty;
        Assembly logic = Assembly.LoadFrom(Path.Combine(folder, LogicAssembly));
        host = (IExternalApplication)Activator.CreateInstance(logic.GetType(LogicType, throwOnError: true));
        return host.OnStartup(application);
    }

    public Result OnShutdown(UIControlledApplication application) => host?.OnShutdown(application) ?? Result.Succeeded;
}
