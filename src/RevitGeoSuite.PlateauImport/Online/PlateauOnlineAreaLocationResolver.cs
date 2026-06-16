using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.PlateauImport.Online;

public sealed class PlateauOnlineAreaLocation
{
    public PlateauOnlineAreaLocation(string areaCode, double latitude, double longitude, int zoom)
    {
        AreaCode = areaCode ?? string.Empty;
        Latitude = latitude;
        Longitude = longitude;
        Zoom = zoom;
    }

    public string AreaCode { get; }

    public double Latitude { get; }

    public double Longitude { get; }

    public int Zoom { get; }
}

public static class PlateauOnlineAreaLocationResolver
{
    public static async Task<PlateauOnlineAreaLocation?> ResolveAsync(
        PlateauCatalog catalog,
        PlateauAreaGeometryService geometryService,
        string? areaCode,
        CancellationToken cancellationToken = default)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (geometryService is null) throw new ArgumentNullException(nameof(geometryService));

        string code = (areaCode ?? string.Empty).Trim();
        if (code.Length == 0) return null;

        PlateauAreaOption? area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(code));
        if (area is null) return null;

        PlateauAreaBounds? bounds = await geometryService
            .GetBoundsAsync(area, catalog, cancellationToken)
            .ConfigureAwait(false);
        if (bounds is not null)
        {
            return new PlateauOnlineAreaLocation(
                area.Code,
                latitude: (bounds.SouthDeg + bounds.NorthDeg) / 2d,
                longitude: (bounds.WestDeg + bounds.EastDeg) / 2d,
                zoom: PlateauOnlineAreaSearch.PickZoomForBounds(bounds));
        }

        if (MunicipalityCentroids.TryGet(area.Code, out double lat, out double lon))
        {
            return new PlateauOnlineAreaLocation(area.Code, lat, lon, zoom: 12);
        }

        return null;
    }
}
