namespace RevitGeoSuite.PlateauImport;

public static class PlateauGeometryImportModeExtensions
{
    public static string GetDisplayName(this PlateauGeometryImportMode mode)
    {
        switch (mode)
        {
            case PlateauGeometryImportMode.DetailedDirectShape:
                return "Detailed Geometry";
            case PlateauGeometryImportMode.LightweightExtrusion:
            default:
                return "Lightweight Geometry";
        }
    }
}
