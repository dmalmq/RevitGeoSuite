using System.Linq;

namespace RevitGeoSuite.Core.Mesh;

public static class JapanMeshDomain
{
    public const double MinLatitude = 20.0;
    public const double MaxLatitude = 46.0;
    public const double MinLongitude = 122.0;
    public const double MaxLongitude = 154.0;

    public static bool IsSupportedCoordinate(double latitude, double longitude)
    {
        return IsFinite(latitude)
            && IsFinite(longitude)
            && latitude >= MinLatitude
            && latitude <= MaxLatitude
            && longitude >= MinLongitude
            && longitude <= MaxLongitude;
    }

    public static bool IsValidTertiaryMeshCode(MeshCode? meshCode)
    {
        return meshCode is not null
            && !string.IsNullOrWhiteSpace(meshCode.Value)
            && meshCode.Value.Length == (int)JapanMeshLevel.Tertiary
            && meshCode.Value.All(char.IsDigit);
    }

    public static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
