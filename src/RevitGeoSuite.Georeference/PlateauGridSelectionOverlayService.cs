using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.Georeference;

public sealed class PlateauGridSelectionOverlayService
{
    private readonly IMeshCalculator meshCalculator;

    public PlateauGridSelectionOverlayService(IMeshCalculator meshCalculator)
    {
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
    }

    public string CreateGeoJson(IReadOnlyCollection<PlateauGridSelectionItem> gridOptions)
    {
        PlateauGridSelectionItem[] grids = (gridOptions ?? Array.Empty<PlateauGridSelectionItem>())
            .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.TileId))
            .ToArray();

        StringBuilder builder = new StringBuilder();
        builder.Append("{\"type\":\"FeatureCollection\",\"features\":[");

        int emitted = 0;
        foreach (PlateauGridSelectionItem option in grids)
        {
            MeshBounds bounds;
            try
            {
                bounds = meshCalculator.GetBounds(new MeshCode { Value = option.TileId });
            }
            catch
            {
                continue;
            }

            if (emitted > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"type\":\"Feature\",\"properties\":{");
            builder.Append("\"featureId\":\"").Append(option.TileId).Append("\",");
            builder.Append("\"tileId\":\"").Append(option.TileId).Append("\",");
            builder.Append("\"isSelected\":").Append(option.IsSelected ? "true" : "false").Append(',');
            builder.Append("\"isSuggested\":").Append(option.IsSeedCandidate ? "true" : "false");
            builder.Append("},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");
            AppendCoordinate(builder, bounds.WestLongitude, bounds.SouthLatitude);
            builder.Append(',');
            AppendCoordinate(builder, bounds.EastLongitude, bounds.SouthLatitude);
            builder.Append(',');
            AppendCoordinate(builder, bounds.EastLongitude, bounds.NorthLatitude);
            builder.Append(',');
            AppendCoordinate(builder, bounds.WestLongitude, bounds.NorthLatitude);
            builder.Append(',');
            AppendCoordinate(builder, bounds.WestLongitude, bounds.SouthLatitude);
            builder.Append("]]}}");
            emitted++;
        }

        builder.Append("]}");
        return builder.ToString();
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

