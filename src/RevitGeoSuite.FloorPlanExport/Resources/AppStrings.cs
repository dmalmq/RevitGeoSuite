using System.Resources;

namespace RevitGeoSuite.FloorPlanExport.Resources;

internal static class AppStrings
{
    private static readonly ResourceManager ResourceManagerInstance = new("RevitGeoSuite.FloorPlanExport.Resources.AppStrings", typeof(AppStrings).Assembly);

    public static ResourceManager ResourceManager => ResourceManagerInstance;
}
