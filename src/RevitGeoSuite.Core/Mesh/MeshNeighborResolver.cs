using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Core.Mesh;

public sealed class MeshNeighborResolver
{
    private static readonly (int LatOffset, int LonOffset)[] NeighborOffsets =
    {
        (1, -1),
        (1, 0),
        (1, 1),
        (0, -1),
        (0, 1),
        (-1, -1),
        (-1, 0),
        (-1, 1)
    };

    private readonly IMeshCalculator meshCalculator;

    public MeshNeighborResolver(IMeshCalculator meshCalculator)
    {
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
    }

    public IReadOnlyCollection<MeshCode> GetNeighbors(MeshCode tertiaryMeshCode)
    {
        if (tertiaryMeshCode is null)
        {
            throw new ArgumentNullException(nameof(tertiaryMeshCode));
        }

        if (tertiaryMeshCode.Value.Length != (int)JapanMeshLevel.Tertiary)
        {
            throw new ArgumentException("Neighbor resolution requires an 8-digit tertiary mesh code.", nameof(tertiaryMeshCode));
        }

        MeshBounds bounds = meshCalculator.GetBounds(tertiaryMeshCode);
        List<MeshCode> neighbors = new List<MeshCode>(NeighborOffsets.Length);

        foreach ((int latOffset, int lonOffset) in NeighborOffsets)
        {
            double latitude = bounds.CenterLatitude + (latOffset * bounds.LatitudeSpan);
            double longitude = bounds.CenterLongitude + (lonOffset * bounds.LongitudeSpan);
            neighbors.Add(meshCalculator.Calculate(latitude, longitude, JapanMeshLevel.Tertiary));
        }

        return neighbors;
    }
}
