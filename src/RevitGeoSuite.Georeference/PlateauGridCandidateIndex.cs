using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.Georeference;

public sealed class PlateauGridCandidateIndex
{
    private readonly IMeshCalculator meshCalculator;
    private readonly MeshNeighborResolver neighborResolver;

    public PlateauGridCandidateIndex(IMeshCalculator? meshCalculator = null)
    {
        this.meshCalculator = meshCalculator ?? new JapanMeshCalculator();
        neighborResolver = new MeshNeighborResolver(this.meshCalculator);
    }

    public IReadOnlyCollection<PlateauGridCandidate> GetCandidateGrids(double latitude, double longitude, bool includeNeighbors = true)
    {
        MeshCode primaryMesh = meshCalculator.Calculate(latitude, longitude, JapanMeshLevel.Tertiary);
        return GetCandidateGrids(primaryMesh, includeNeighbors);
    }

    public IReadOnlyCollection<PlateauGridCandidate> GetCandidateGrids(MeshCode primaryMesh, bool includeNeighbors = true)
    {
        if (primaryMesh is null)
        {
            throw new ArgumentNullException(nameof(primaryMesh));
        }

        List<PlateauGridCandidate> candidates = new List<PlateauGridCandidate>
        {
            new PlateauGridCandidate
            {
                TileId = primaryMesh.Value,
                IsPrimary = true,
                Source = "primary"
            }
        };

        if (includeNeighbors)
        {
            candidates.AddRange(neighborResolver.GetNeighbors(primaryMesh).Select(code => new PlateauGridCandidate
            {
                TileId = code.Value,
                IsPrimary = false,
                Source = "neighbor"
            }));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.TileId))
            .GroupBy(candidate => candidate.TileId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(candidate => candidate.IsPrimary)
            .ThenBy(candidate => candidate.TileId, StringComparer.Ordinal)
            .ToArray();
    }
}
