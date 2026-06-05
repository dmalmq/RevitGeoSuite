using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport.Online;

public static class PlateauOnlineSuggestionResolver
{
    private const double FeetToMeters = 0.3048d;

    public static PlateauOnlineProjectPoint? ResolveProjectPoint(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo? info,
        ICoordinateTransformer coordinateTransformer)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (coordinateTransformer is null)
        {
            throw new ArgumentNullException(nameof(coordinateTransformer));
        }

        if (currentState.ProjectBasePoint.HasSharedPosition && info?.ProjectCrs is not null)
        {
            ProjectedCoordinate projected = new ProjectedCoordinate(
                currentState.ProjectBasePoint.SharedEastWestFeet!.Value * FeetToMeters,
                currentState.ProjectBasePoint.SharedNorthSouthFeet!.Value * FeetToMeters);
            GeographicCoordinate geographic = coordinateTransformer.Unproject(projected, info.ProjectCrs);
            return new PlateauOnlineProjectPoint(
                geographic.Latitude,
                geographic.Longitude,
                "projectBasePoint",
                "Suggested from Project Base Point.");
        }

        if (currentState.ProjectBasePoint.HasEstimatedLocation)
        {
            return new PlateauOnlineProjectPoint(
                currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                "projectBasePoint",
                "Suggested from Project Base Point.");
        }

        if (currentState.StoredWorkingProjectBasePoint?.IsValid == true)
        {
            ProjectOrigin origin = currentState.StoredWorkingProjectBasePoint.Origin!;
            return new PlateauOnlineProjectPoint(
                origin.Latitude,
                origin.Longitude,
                "workingProjectBasePoint",
                "Suggested from saved Working Project Base Point.");
        }

        return null;
    }

    public static PlateauOnlineSuggestedArea? ResolveSuggestedArea(
        PlateauCatalog catalog,
        string? municipalityCode,
        PlateauOnlineProjectPoint? projectPoint)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (projectPoint is null)
        {
            return null;
        }

        return ResolveSuggestedAreas(
                catalog,
                new[] { new PlateauOnlineMunicipalitySample(municipalityCode, 0d, 0) },
                projectPoint)
            .FirstOrDefault();
    }

    public static IReadOnlyList<PlateauOnlineSuggestedArea> ResolveSuggestedAreas(
        PlateauCatalog catalog,
        IEnumerable<PlateauOnlineMunicipalitySample> municipalitySamples,
        PlateauOnlineProjectPoint? projectPoint)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (municipalitySamples is null)
        {
            throw new ArgumentNullException(nameof(municipalitySamples));
        }

        if (projectPoint is null)
        {
            return Array.Empty<PlateauOnlineSuggestedArea>();
        }

        Dictionary<string, PlateauOnlineMunicipalitySample> bestSamples =
            new Dictionary<string, PlateauOnlineMunicipalitySample>(StringComparer.Ordinal);

        foreach (PlateauOnlineMunicipalitySample sample in municipalitySamples)
        {
            string? normalizedCode = PlateauCatalog.NormalizeCode(sample.MunicipalityCode);
            if (normalizedCode is null)
            {
                continue;
            }

            PlateauOnlineMunicipalitySample normalizedSample = new PlateauOnlineMunicipalitySample(
                normalizedCode,
                sample.NearestDistanceMeters,
                sample.Sequence);

            if (!bestSamples.TryGetValue(normalizedCode, out PlateauOnlineMunicipalitySample existing)
                || normalizedSample.NearestDistanceMeters < existing.NearestDistanceMeters
                || (Math.Abs(normalizedSample.NearestDistanceMeters - existing.NearestDistanceMeters) < 1e-6
                    && normalizedSample.Sequence < existing.Sequence))
            {
                bestSamples[normalizedCode] = normalizedSample;
            }
        }

        List<PlateauOnlineSuggestedArea> result = new List<PlateauOnlineSuggestedArea>();
        foreach (PlateauOnlineMunicipalitySample sample in bestSamples.Values
                     .OrderBy(sample => sample.NearestDistanceMeters)
                     .ThenBy(sample => sample.Sequence))
        {
            PlateauAreaOption? area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(sample.MunicipalityCode));
            if (area is null || !HasBuildingDataset(catalog, area))
            {
                continue;
            }

            AreaSearchOption option = PlateauOnlineAreaSearch.BuildOption(area);
            result.Add(new PlateauOnlineSuggestedArea(
                area,
                option.DisplayLabel,
                option.CodeLabel,
                projectPoint.Source,
                projectPoint.Message,
                projectPoint.Latitude,
                projectPoint.Longitude,
                sample.NearestDistanceMeters));
        }

        return result;
    }

    public static bool HasBuildingDataset(PlateauCatalog catalog, PlateauAreaOption area)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (area is null)
        {
            throw new ArgumentNullException(nameof(area));
        }

        return catalog.Datasets.Any(dataset =>
            string.Equals(dataset.TypeEn, "bldg", StringComparison.Ordinal)
            && PlateauDatasetSelector.AreaMatchesDataset(area, dataset));
    }
}

public sealed class PlateauOnlineMunicipalitySample
{
    public PlateauOnlineMunicipalitySample(string? municipalityCode, double nearestDistanceMeters, int sequence)
    {
        MunicipalityCode = municipalityCode ?? string.Empty;
        NearestDistanceMeters = Math.Max(0d, nearestDistanceMeters);
        Sequence = sequence;
    }

    public string MunicipalityCode { get; }

    public double NearestDistanceMeters { get; }

    public int Sequence { get; }
}

public sealed class PlateauOnlineProjectPoint
{
    public PlateauOnlineProjectPoint(double latitude, double longitude, string source, string message)
    {
        Latitude = latitude;
        Longitude = longitude;
        Source = source ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public string Source { get; }

    public string Message { get; }
}

public sealed class PlateauOnlineSuggestedArea
{
    public PlateauOnlineSuggestedArea(
        PlateauAreaOption area,
        string displayLabel,
        string codeLabel,
        string source,
        string message,
        double latitude,
        double longitude,
        double nearestDistanceMeters = 0d)
    {
        Area = area ?? throw new ArgumentNullException(nameof(area));
        DisplayLabel = displayLabel ?? string.Empty;
        CodeLabel = codeLabel ?? string.Empty;
        Source = source ?? string.Empty;
        Message = message ?? string.Empty;
        Latitude = latitude;
        Longitude = longitude;
        NearestDistanceMeters = Math.Max(0d, nearestDistanceMeters);
    }

    public PlateauAreaOption Area { get; }

    public string DisplayLabel { get; }

    public string CodeLabel { get; }

    public string Source { get; }

    public string Message { get; }

    public double Latitude { get; }

    public double Longitude { get; }

    public double NearestDistanceMeters { get; }
}
