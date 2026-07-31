using System;
using System.Globalization;

namespace RevitGeoSuite.Core.Mesh;

public sealed class JapanMeshCalculator : IMeshCalculator
{
    private const double PrimaryLatitudeSpan = 2.0 / 3.0;
    private const double PrimaryLongitudeSpan = 1.0;
    private const double SecondaryLatitudeSpan = 5.0 / 60.0;
    private const double SecondaryLongitudeSpan = 7.5 / 60.0;
    private const double TertiaryLatitudeSpan = 30.0 / 3600.0;
    private const double TertiaryLongitudeSpan = 45.0 / 3600.0;

    public MeshCode Calculate(double latitude, double longitude, JapanMeshLevel level = JapanMeshLevel.Tertiary)
    {
        int primaryLatitudeIndex = (int)Math.Floor(latitude / PrimaryLatitudeSpan);
        int primaryLongitudeIndex = (int)Math.Floor(longitude) - 100;

        double latitudeRemainder = latitude - (primaryLatitudeIndex * PrimaryLatitudeSpan);
        double longitudeRemainder = longitude - (primaryLongitudeIndex + 100.0);

        int secondaryLatitudeIndex = ClampIndex((int)Math.Floor(latitudeRemainder / SecondaryLatitudeSpan), 8);
        int secondaryLongitudeIndex = ClampIndex((int)Math.Floor(longitudeRemainder / SecondaryLongitudeSpan), 8);

        double secondaryLatitudeBase = primaryLatitudeIndex * PrimaryLatitudeSpan + (secondaryLatitudeIndex * SecondaryLatitudeSpan);
        double secondaryLongitudeBase = primaryLongitudeIndex + 100.0 + (secondaryLongitudeIndex * SecondaryLongitudeSpan);

        int tertiaryLatitudeIndex = ClampIndex((int)Math.Floor((latitude - secondaryLatitudeBase) / TertiaryLatitudeSpan), 10);
        int tertiaryLongitudeIndex = ClampIndex((int)Math.Floor((longitude - secondaryLongitudeBase) / TertiaryLongitudeSpan), 10);

        string primaryCode = string.Concat(
            primaryLatitudeIndex.ToString("00", CultureInfo.InvariantCulture),
            primaryLongitudeIndex.ToString("00", CultureInfo.InvariantCulture));

        if (level == JapanMeshLevel.Primary)
        {
            return new MeshCode { Value = primaryCode };
        }

        string secondaryCode = primaryCode + secondaryLatitudeIndex.ToString(CultureInfo.InvariantCulture) + secondaryLongitudeIndex.ToString(CultureInfo.InvariantCulture);
        if (level == JapanMeshLevel.Secondary)
        {
            return new MeshCode { Value = secondaryCode };
        }

        string tertiaryCode = secondaryCode + tertiaryLatitudeIndex.ToString(CultureInfo.InvariantCulture) + tertiaryLongitudeIndex.ToString(CultureInfo.InvariantCulture);
        return new MeshCode { Value = tertiaryCode };
    }

    public MeshCode CalculatePrimaryMesh(double latitude, double longitude)
    {
        return Calculate(latitude, longitude, JapanMeshLevel.Tertiary);
    }

    public MeshBounds GetBounds(MeshCode meshCode)
    {
        if (meshCode is null)
        {
            throw new ArgumentNullException(nameof(meshCode));
        }

        string value = meshCode.Value ?? string.Empty;
        if (value.Length != 4 && value.Length != 6 && value.Length != 8)
        {
            throw new ArgumentException("Mesh code must be 4, 6, or 8 digits long.", nameof(meshCode));
        }

        if (!long.TryParse(value, out _))
        {
            throw new ArgumentException("Mesh code must contain only digits.", nameof(meshCode));
        }

        int primaryLatitudeIndex = int.Parse(value.Substring(0, 2), CultureInfo.InvariantCulture);
        int primaryLongitudeIndex = int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture);

        double southLatitude = primaryLatitudeIndex * PrimaryLatitudeSpan;
        double westLongitude = primaryLongitudeIndex + 100.0;
        double latitudeSpan = PrimaryLatitudeSpan;
        double longitudeSpan = PrimaryLongitudeSpan;

        if (value.Length >= 6)
        {
            int secondaryLatitudeIndex = int.Parse(value.Substring(4, 1), CultureInfo.InvariantCulture);
            int secondaryLongitudeIndex = int.Parse(value.Substring(5, 1), CultureInfo.InvariantCulture);
            southLatitude += secondaryLatitudeIndex * SecondaryLatitudeSpan;
            westLongitude += secondaryLongitudeIndex * SecondaryLongitudeSpan;
            latitudeSpan = SecondaryLatitudeSpan;
            longitudeSpan = SecondaryLongitudeSpan;
        }

        if (value.Length == 8)
        {
            int tertiaryLatitudeIndex = int.Parse(value.Substring(6, 1), CultureInfo.InvariantCulture);
            int tertiaryLongitudeIndex = int.Parse(value.Substring(7, 1), CultureInfo.InvariantCulture);
            southLatitude += tertiaryLatitudeIndex * TertiaryLatitudeSpan;
            westLongitude += tertiaryLongitudeIndex * TertiaryLongitudeSpan;
            latitudeSpan = TertiaryLatitudeSpan;
            longitudeSpan = TertiaryLongitudeSpan;
        }

        return new MeshBounds(southLatitude, westLongitude, southLatitude + latitudeSpan, westLongitude + longitudeSpan);
    }

    private static int ClampIndex(int value, int maxExclusive)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value >= maxExclusive)
        {
            return maxExclusive - 1;
        }

        return value;
    }
}
