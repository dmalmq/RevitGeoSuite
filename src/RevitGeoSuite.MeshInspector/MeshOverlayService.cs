using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.MeshInspector;

public sealed class MeshOverlayService
{
    private readonly IMeshCalculator meshCalculator;

    public MeshOverlayService(IMeshCalculator meshCalculator)
    {
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
    }

    public string CreateGeoJson(MeshCode primaryMeshCode, IReadOnlyCollection<MeshCode> neighborMeshCodes)
    {
        if (primaryMeshCode is null)
        {
            throw new ArgumentNullException(nameof(primaryMeshCode));
        }

        List<MeshCode> distinctNeighbors = (neighborMeshCodes ?? Array.Empty<MeshCode>())
            .Where(code => code is not null && !string.IsNullOrWhiteSpace(code.Value))
            .Distinct()
            .Where(code => !code.Equals(primaryMeshCode))
            .ToList();

        List<(MeshCode Code, bool IsPrimary)> meshes = new List<(MeshCode Code, bool IsPrimary)> { (primaryMeshCode, true) };
        meshes.AddRange(distinctNeighbors.Select(code => (code, false)));

        StringBuilder builder = new StringBuilder();
        builder.Append("{\"type\":\"FeatureCollection\",\"features\":[");

        for (int index = 0; index < meshes.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            MeshBounds bounds = meshCalculator.GetBounds(meshes[index].Code);
            builder.Append("{\"type\":\"Feature\",\"properties\":{");
            builder.Append("\"meshCode\":\"").Append(meshes[index].Code.Value).Append("\",");
            builder.Append("\"isPrimary\":").Append(meshes[index].IsPrimary ? "true" : "false");
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
