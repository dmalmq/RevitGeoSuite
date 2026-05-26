using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauAreaBounds
{
    public PlateauAreaBounds(double westDeg, double southDeg, double eastDeg, double northDeg)
    {
        WestDeg = westDeg;
        SouthDeg = southDeg;
        EastDeg = eastDeg;
        NorthDeg = northDeg;
    }

    public double WestDeg { get; }

    public double SouthDeg { get; }

    public double EastDeg { get; }

    public double NorthDeg { get; }
}

public sealed class PlateauAreaGeometryService
{
    private readonly IPlateauHttpClient httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<PlateauAreaBounds?>>> boundsCache;

    public PlateauAreaGeometryService(IPlateauHttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        boundsCache = new ConcurrentDictionary<string, Lazy<Task<PlateauAreaBounds?>>>(StringComparer.Ordinal);
    }

    public async Task<PlateauAreaBounds?> GetBoundsAsync(
        PlateauAreaOption area,
        PlateauCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        if (area is null) throw new ArgumentNullException(nameof(area));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        cancellationToken.ThrowIfCancellationRequested();
        Lazy<Task<PlateauAreaBounds?>> lazy = boundsCache.GetOrAdd(
            area.Code,
            _ => new Lazy<Task<PlateauAreaBounds?>>(
                () => LoadBoundsAsync(area, catalog, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            boundsCache.TryRemove(area.Code, out _);
            throw;
        }
        catch
        {
            boundsCache.TryRemove(area.Code, out _);
            throw;
        }
    }

    private async Task<PlateauAreaBounds?> LoadBoundsAsync(
        PlateauAreaOption area,
        PlateauCatalog catalog,
        CancellationToken cancellationToken)
    {
        PlateauDatasetEntry? dataset = SelectRepresentativeDataset(area, catalog);
        string? url = dataset?.PreferredUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? tilesetUri))
        {
            return null;
        }

        byte[] bytes = await httpClient.GetBytesAsync(tilesetUri, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            return null;
        }

        TilesetJson? tileset;
        try
        {
            string json = Encoding.UTF8.GetString(bytes);
            tileset = JsonConvert.DeserializeObject<TilesetJson>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        double[]? region = tileset?.Root?.BoundingVolume?.Region;
        if (region is null || region.Length < 4)
        {
            return null;
        }

        double west = RadiansToDegrees(region[0]);
        double south = RadiansToDegrees(region[1]);
        double east = RadiansToDegrees(region[2]);
        double north = RadiansToDegrees(region[3]);
        if (!IsFinite(west) || !IsFinite(south) || !IsFinite(east) || !IsFinite(north))
        {
            return null;
        }

        if (east <= west || north <= south)
        {
            return null;
        }

        return new PlateauAreaBounds(west, south, east, north);
    }

    private static PlateauDatasetEntry? SelectRepresentativeDataset(PlateauAreaOption area, PlateauCatalog catalog)
    {
        return catalog.Datasets
            .Where(dataset => PlateauCatalog.IsPlateau3dTilesDataset(dataset))
            .Where(dataset => PlateauDatasetSelector.AreaMatchesDataset(area, dataset))
            .Where(dataset => !string.IsNullOrWhiteSpace(dataset.PreferredUrl))
            .OrderBy(GetRepresentativeRank)
            .ThenBy(dataset => string.Equals(dataset.TypeEn, "bldg", StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(dataset => ParseLod(dataset.Lod))
            .ThenBy(dataset => dataset.Texture == false ? 0 : 1)
            .ThenByDescending(dataset => dataset.CatalogSource == PlateauCatalogSource.Latest ? 1 : 0)
            .ThenByDescending(GetDatasetYear)
            .ThenBy(dataset => dataset.PreferredUrl, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int GetRepresentativeRank(PlateauDatasetEntry dataset)
    {
        if (!string.Equals(dataset.TypeEn, "bldg", StringComparison.Ordinal))
        {
            return 3;
        }

        int lod = ParseLod(dataset.Lod);
        if (lod == 2 && dataset.Texture == false)
        {
            return 0;
        }

        if (lod == 2 && dataset.Texture == true)
        {
            return 1;
        }

        if (lod == 1)
        {
            return 2;
        }

        return 3;
    }

    private static int ParseLod(string? lod)
    {
        return int.TryParse(lod, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
    }

    private static int GetDatasetYear(PlateauDatasetEntry dataset)
    {
        return Math.Max(dataset.Year ?? -1, dataset.RegistrationYear ?? -1);
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
