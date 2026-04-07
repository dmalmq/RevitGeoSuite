using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.PlateauImport;

public sealed class ProjectedFootprintOverlayGeoJsonBuilder
{
    private readonly ICoordinateTransformer coordinateTransformer;

    public ProjectedFootprintOverlayGeoJsonBuilder(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public string CreateGeoJson(
        IReadOnlyCollection<ProjectedCoordinate> points,
        CrsReference crs,
        string featureId,
        string title,
        int elementCount)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (crs is null)
        {
            throw new ArgumentNullException(nameof(crs));
        }

        List<ProjectedCoordinate> hull = BuildConvexHull(points);
        if (hull.Count < 3)
        {
            return string.Empty;
        }

        List<GeographicCoordinate> geographicHull = hull
            .Select(point => coordinateTransformer.Unproject(point, crs))
            .ToList();

        StringBuilder builder = new StringBuilder();
        builder.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        builder.Append("{\"type\":\"Feature\",\"properties\":{");
        builder.Append("\"featureId\":\"").Append(featureId).Append("\",");
        builder.Append("\"title\":\"").Append(title).Append("\",");
        builder.Append("\"elementCount\":").Append(elementCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");

        for (int index = 0; index < geographicHull.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendCoordinate(builder, geographicHull[index].Longitude, geographicHull[index].Latitude);
        }

        builder.Append(',');
        AppendCoordinate(builder, geographicHull[0].Longitude, geographicHull[0].Latitude);
        builder.Append("]]}}]}");
        return builder.ToString();
    }

    private static List<ProjectedCoordinate> BuildConvexHull(IEnumerable<ProjectedCoordinate> sourcePoints)
    {
        List<ProjectedCoordinate> points = sourcePoints
            .Where(point => point.IsFinite)
            .GroupBy(point => new
            {
                Easting = Math.Round(point.Easting, 3, MidpointRounding.AwayFromZero),
                Northing = Math.Round(point.Northing, 3, MidpointRounding.AwayFromZero)
            })
            .Select(group => group.First())
            .OrderBy(point => point.Easting)
            .ThenBy(point => point.Northing)
            .ToList();

        if (points.Count < 3)
        {
            return points;
        }

        List<ProjectedCoordinate> lower = new List<ProjectedCoordinate>();
        foreach (ProjectedCoordinate point in points)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0d)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        List<ProjectedCoordinate> upper = new List<ProjectedCoordinate>();
        for (int index = points.Count - 1; index >= 0; index--)
        {
            ProjectedCoordinate point = points[index];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0d)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(ProjectedCoordinate origin, ProjectedCoordinate first, ProjectedCoordinate second)
    {
        return ((first.Easting - origin.Easting) * (second.Northing - origin.Northing))
            - ((first.Northing - origin.Northing) * (second.Easting - origin.Easting));
    }

    private static void AppendCoordinate(StringBuilder builder, double longitude, double latitude)
    {
        builder.Append('[')
            .Append(longitude.ToString("0.########", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(latitude.ToString("0.########", CultureInfo.InvariantCulture))
            .Append(']');
    }
}

