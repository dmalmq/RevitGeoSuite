using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauTileOverlayService
{
    private readonly IMeshCalculator meshCalculator;

    public PlateauTileOverlayService(IMeshCalculator? meshCalculator = null)
    {
        this.meshCalculator = meshCalculator ?? new JapanMeshCalculator();
    }

    public string CreateGeoJson(IReadOnlyCollection<PlateauTileSelectionItem> tiles)
    {
        IReadOnlyCollection<PlateauTileSelectionItem> sourceTiles = tiles ?? Array.Empty<PlateauTileSelectionItem>();
        List<PlateauTileSelectionItem> validTiles = sourceTiles
            .Where(tile => tile is not null && !string.IsNullOrWhiteSpace(tile.TileId))
            .ToList();

        StringBuilder builder = new StringBuilder();
        builder.Append("{\"type\":\"FeatureCollection\",\"features\":[");

        int emitted = 0;
        foreach (PlateauTileSelectionItem tile in validTiles)
        {
            MeshBounds bounds;
            try
            {
                bounds = meshCalculator.GetBounds(new MeshCode { Value = tile.TileId });
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
            builder.Append("\"featureId\":\"").Append(tile.TileId).Append("\",");
            builder.Append("\"tileId\":\"").Append(tile.TileId).Append("\",");
            builder.Append("\"isSelected\":").Append(tile.IsSelected ? "true" : "false").Append(',');
            builder.Append("\"isSuggested\":").Append(tile.IsSuggested ? "true" : "false").Append(',');
            builder.Append("\"featureCount\":").Append(tile.FeatureCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append("\"sourceFileCount\":").Append(tile.SourceFileCount.ToString(CultureInfo.InvariantCulture));
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
