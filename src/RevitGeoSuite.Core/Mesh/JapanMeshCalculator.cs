using System;
using System.Globalization;

namespace RevitGeoSuite.Core.Mesh;

public sealed class JapanMeshCalculator : IMeshCalculator
{
    // Exact JIS X 0410 tertiary cell grid:
    // - Latitude: 30 arc-seconds = 1/120 degree per tertiary cell (10 per secondary, 80 per primary = 2/3 deg).
    // - Longitude: 45 arc-seconds = 1/80 degree per tertiary cell (10 per secondary, 80 per primary = 1 deg),
    //   origin at 100 degrees East (= 8000 tertiary cells).
    private const double TertiaryLatitudeCellsPerDegree = 120.0;
    private const double TertiaryLongitudeCellsPerDegree = 80.0;
    private const long TertiaryCellsPerPrimary = 80L;
    private const long TertiaryCellsPerSecondary = 10L;
    private const long LongitudeBaseCells = 8000L;

    // 1e-9 of a tertiary cell (~1 micrometer) absorbs IEEE-754 half-ULP truncation on exact cell corners
    // without moving any interior point across a boundary.
    private const double BoundaryEpsilonCells = 1e-9;

    public MeshCode Calculate(double latitude, double longitude, JapanMeshLevel level = JapanMeshLevel.Tertiary)
    {
        if (!JapanMeshDomain.IsFinite(latitude))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be a finite number.");
        }

        if (!JapanMeshDomain.IsFinite(longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be a finite number.");
        }

        long totalLatitudeCells = (long)Math.Floor((latitude * TertiaryLatitudeCellsPerDegree) + BoundaryEpsilonCells);
        long totalLongitudeCells = (long)Math.Floor(((longitude - 100.0) * TertiaryLongitudeCellsPerDegree) + BoundaryEpsilonCells);

        long primaryLatitudeIndex = FloorDiv(totalLatitudeCells, TertiaryCellsPerPrimary);
        long latitudeRemainder = totalLatitudeCells - (primaryLatitudeIndex * TertiaryCellsPerPrimary);
        long secondaryLatitudeIndex = latitudeRemainder / TertiaryCellsPerSecondary;
        long tertiaryLatitudeIndex = latitudeRemainder % TertiaryCellsPerSecondary;

        long primaryLongitudeIndex = FloorDiv(totalLongitudeCells, TertiaryCellsPerPrimary);
        long longitudeRemainder = totalLongitudeCells - (primaryLongitudeIndex * TertiaryCellsPerPrimary);
        long secondaryLongitudeIndex = longitudeRemainder / TertiaryCellsPerSecondary;
        long tertiaryLongitudeIndex = longitudeRemainder % TertiaryCellsPerSecondary;

        string primaryCode = string.Concat(
            primaryLatitudeIndex.ToString("00", CultureInfo.InvariantCulture),
            primaryLongitudeIndex.ToString("00", CultureInfo.InvariantCulture));

        if (level == JapanMeshLevel.Primary)
        {
            return new MeshCode { Value = primaryCode };
        }

        string secondaryCode = string.Concat(
            primaryCode,
            secondaryLatitudeIndex.ToString(CultureInfo.InvariantCulture),
            secondaryLongitudeIndex.ToString(CultureInfo.InvariantCulture));
        if (level == JapanMeshLevel.Secondary)
        {
            return new MeshCode { Value = secondaryCode };
        }

        string tertiaryCode = string.Concat(
            secondaryCode,
            tertiaryLatitudeIndex.ToString(CultureInfo.InvariantCulture),
            tertiaryLongitudeIndex.ToString(CultureInfo.InvariantCulture));
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

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                throw new ArgumentException("Mesh code must contain only ASCII digits.", nameof(meshCode));
            }
        }

        long primaryLatitudeIndex = ((value[0] - '0') * 10L) + (value[1] - '0');
        long primaryLongitudeIndex = ((value[2] - '0') * 10L) + (value[3] - '0');

        long southCells = primaryLatitudeIndex * TertiaryCellsPerPrimary;
        long westCells = primaryLongitudeIndex * TertiaryCellsPerPrimary;
        long spanCells = TertiaryCellsPerPrimary;

        if (value.Length >= 6)
        {
            long secondaryLatitudeIndex = value[4] - '0';
            long secondaryLongitudeIndex = value[5] - '0';
            if (secondaryLatitudeIndex > 7L || secondaryLongitudeIndex > 7L)
            {
                throw new ArgumentException("Secondary mesh digits must be between 0 and 7.", nameof(meshCode));
            }

            southCells += secondaryLatitudeIndex * TertiaryCellsPerSecondary;
            westCells += secondaryLongitudeIndex * TertiaryCellsPerSecondary;
            spanCells = TertiaryCellsPerSecondary;
        }

        if (value.Length == 8)
        {
            long tertiaryLatitudeIndex = value[6] - '0';
            long tertiaryLongitudeIndex = value[7] - '0';
            southCells += tertiaryLatitudeIndex;
            westCells += tertiaryLongitudeIndex;
            spanCells = 1L;
        }

        double southLatitude = southCells / TertiaryLatitudeCellsPerDegree;
        double northLatitude = (southCells + spanCells) / TertiaryLatitudeCellsPerDegree;
        double westLongitude = (LongitudeBaseCells + westCells) / TertiaryLongitudeCellsPerDegree;
        double eastLongitude = (LongitudeBaseCells + westCells + spanCells) / TertiaryLongitudeCellsPerDegree;

        return new MeshBounds(southLatitude, westLongitude, northLatitude, eastLongitude);
    }

    private static long FloorDiv(long value, long divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        if (remainder < 0L)
        {
            quotient--;
        }

        return quotient;
    }
}
