using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.PlateauImport.Online;

public sealed class PlateauOnlineSamplePoint
{
    public PlateauOnlineSamplePoint(double latitude, double longitude, double distanceMeters, double bearingDegrees)
    {
        Latitude = latitude;
        Longitude = longitude;
        DistanceMeters = distanceMeters;
        BearingDegrees = bearingDegrees;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public double DistanceMeters { get; }
    public double BearingDegrees { get; }
}

public sealed class PlateauOnlineSampleResult
{
    public PlateauOnlineSampleResult(PlateauOnlineSamplePoint point, string? municipalityCode)
    {
        Point = point ?? throw new ArgumentNullException(nameof(point));
        MunicipalityCode = municipalityCode;
    }

    public PlateauOnlineSamplePoint Point { get; }
    public string? MunicipalityCode { get; }
}

public sealed class PlateauOnlineNearbyArea
{
    public PlateauOnlineNearbyArea(PlateauAreaOption area, string displayLabel, string codeLabel, double nearestDistanceMeters)
    {
        Area = area ?? throw new ArgumentNullException(nameof(area));
        DisplayLabel = displayLabel ?? string.Empty;
        CodeLabel = codeLabel ?? string.Empty;
        NearestDistanceMeters = nearestDistanceMeters;
    }

    public PlateauAreaOption Area { get; }
    public string DisplayLabel { get; }
    public string CodeLabel { get; }
    public double NearestDistanceMeters { get; }
}

public static class PlateauOnlineNearbyAreaDetector
{
    private const double MetersPerDegreeLatitude = 111320.0;
    private static readonly double[] SampleDistancesMeters = { 500.0, 1000.0, 1500.0 };
    private const int DirectionCount = 16;

    public static PlateauOnlineSamplePoint[] GenerateSamplePoints(double latitude, double longitude)
    {
        double cosLat = Math.Cos(latitude * Math.PI / 180.0);
        if (Math.Abs(cosLat) < 1e-10)
        {
            return Array.Empty<PlateauOnlineSamplePoint>();
        }

        double metersPerDegreeLongitude = MetersPerDegreeLatitude * cosLat;
        List<PlateauOnlineSamplePoint> points = new List<PlateauOnlineSamplePoint>(SampleDistancesMeters.Length * DirectionCount);

        for (int dir = 0; dir < DirectionCount; dir++)
        {
            double bearingDeg = dir * (360.0 / DirectionCount);
            double bearingRad = bearingDeg * Math.PI / 180.0;
            double sinBearing = Math.Sin(bearingRad);
            double cosBearing = Math.Cos(bearingRad);

            foreach (double distance in SampleDistancesMeters)
            {
                double deltaLat = distance * cosBearing / MetersPerDegreeLatitude;
                double deltaLon = distance * sinBearing / metersPerDegreeLongitude;
                points.Add(new PlateauOnlineSamplePoint(
                    latitude + deltaLat,
                    longitude + deltaLon,
                    distance,
                    bearingDeg));
            }
        }

        return points.ToArray();
    }

    public static PlateauOnlineNearbyArea[] ResolveNearbyAreas(
        PlateauCatalog catalog,
        string? exactMunicipalityCode,
        PlateauOnlineSampleResult[] sampleResults,
        PlateauOnlineProjectPoint projectPoint)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (projectPoint is null) throw new ArgumentNullException(nameof(projectPoint));

        Dictionary<string, double> codeToNearestDistance = new Dictionary<string, double>(StringComparer.Ordinal);

        string? normalizedExact = PlateauCatalog.NormalizeCode(exactMunicipalityCode);
        if (normalizedExact is not null)
        {
            codeToNearestDistance[normalizedExact] = 0.0;
        }

        if (sampleResults is not null)
        {
            foreach (PlateauOnlineSampleResult result in sampleResults)
            {
                string? normalized = PlateauCatalog.NormalizeCode(result.MunicipalityCode);
                if (normalized is null) continue;

                if (!codeToNearestDistance.TryGetValue(normalized, out double existing) || result.Point.DistanceMeters < existing)
                {
                    codeToNearestDistance[normalized] = result.Point.DistanceMeters;
                }
            }
        }

        List<PlateauOnlineNearbyArea> nearbyAreas = new List<PlateauOnlineNearbyArea>();
        foreach (KeyValuePair<string, double> entry in codeToNearestDistance)
        {
            PlateauAreaOption? area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(entry.Key));
            if (area is null) continue;
            if (!PlateauOnlineSuggestionResolver.HasBuildingDataset(catalog, area)) continue;

            AreaSearchOption option = PlateauOnlineAreaSearch.BuildOption(area);
            nearbyAreas.Add(new PlateauOnlineNearbyArea(area, option.DisplayLabel, option.CodeLabel, entry.Value));
        }

        return nearbyAreas
            .OrderBy(a => string.Equals(a.Area.Code, normalizedExact, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(a => a.NearestDistanceMeters)
            .ToArray();
    }
}
