using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Plateau.Tiles;

public sealed class PlateauTileIndex
{
    private readonly IMeshCalculator meshCalculator;
    private readonly MeshNeighborResolver neighborResolver;

    public PlateauTileIndex(IMeshCalculator? meshCalculator = null)
    {
        this.meshCalculator = meshCalculator ?? new JapanMeshCalculator();
        neighborResolver = new MeshNeighborResolver(this.meshCalculator);
    }

    public IReadOnlyCollection<PlateauTileCandidate> GetCandidateTiles(ProjectOrigin origin, bool includeNeighbors = true)
    {
        if (origin is null)
        {
            throw new ArgumentNullException(nameof(origin));
        }

        return GetCandidateTiles(origin.Latitude, origin.Longitude, includeNeighbors);
    }

    public IReadOnlyCollection<PlateauTileCandidate> GetCandidateTiles(double latitude, double longitude, bool includeNeighbors = true)
    {
        MeshCode primaryMesh = meshCalculator.Calculate(latitude, longitude, JapanMeshLevel.Tertiary);
        return GetCandidateTiles(primaryMesh, includeNeighbors);
    }

    public IReadOnlyCollection<PlateauTileCandidate> GetCandidateTiles(MeshCode primaryMesh, bool includeNeighbors = true)
    {
        if (primaryMesh is null)
        {
            throw new ArgumentNullException(nameof(primaryMesh));
        }

        List<PlateauTileCandidate> candidates = new List<PlateauTileCandidate>
        {
            new PlateauTileCandidate
            {
                TileId = primaryMesh.Value,
                IsPrimary = true,
                Source = "primary"
            }
        };

        if (includeNeighbors)
        {
            candidates.AddRange(neighborResolver.GetNeighbors(primaryMesh).Select(code => new PlateauTileCandidate
            {
                TileId = code.Value,
                IsPrimary = false,
                Source = "neighbor"
            }));
        }

        return candidates
            .GroupBy(candidate => candidate.TileId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(candidate => candidate.IsPrimary)
            .ThenBy(candidate => candidate.TileId, StringComparer.Ordinal)
            .ToArray();
    }
}
