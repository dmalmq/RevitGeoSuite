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
        if (meshCode is null || string.IsNullOrEmpty(meshCode.Value) || meshCode.Value.Length != (int)JapanMeshLevel.Tertiary)
        {
            return false;
        }

        string value = meshCode.Value;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return false;
            }
        }

        return value[4] <= '7' && value[5] <= '7';
    }

    public static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
