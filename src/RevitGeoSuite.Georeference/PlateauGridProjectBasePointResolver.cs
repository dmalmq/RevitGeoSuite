using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;

namespace RevitGeoSuite.Georeference;

public sealed class PlateauGridProjectBasePointResolver
{
    private readonly IMeshCalculator meshCalculator;
    private readonly ICoordinateTransformer coordinateTransformer;

    public PlateauGridProjectBasePointResolver(IMeshCalculator meshCalculator, ICoordinateTransformer coordinateTransformer)
    {
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public PlateauGridProjectBasePointSelection? Resolve(IReadOnlyCollection<string> selectedMeshCodes, CrsReference? projectCrs)
    {
        string[] meshCodes = (selectedMeshCodes ?? Array.Empty<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (meshCodes.Length == 0)
        {
            return null;
        }

        double? southLatitude = null;
        double? westLongitude = null;
        List<string> validMeshCodes = new List<string>();
        foreach (string meshCode in meshCodes)
        {
            MeshBounds bounds;
            try
            {
                bounds = meshCalculator.GetBounds(new MeshCode { Value = meshCode });
            }
            catch
            {
                continue;
            }

            southLatitude = !southLatitude.HasValue || bounds.SouthLatitude < southLatitude.Value
                ? bounds.SouthLatitude
                : southLatitude.Value;
            westLongitude = !westLongitude.HasValue || bounds.WestLongitude < westLongitude.Value
                ? bounds.WestLongitude
                : westLongitude.Value;
            validMeshCodes.Add(meshCode);
        }

        if (!southLatitude.HasValue || !westLongitude.HasValue || validMeshCodes.Count == 0)
        {
            return null;
        }

        ProjectedCoordinate? projectedCoordinate = null;
        if (projectCrs is not null)
        {
            projectedCoordinate = coordinateTransformer.Project(
                new GeographicCoordinate(southLatitude.Value, westLongitude.Value),
                projectCrs);
        }

        return new PlateauGridProjectBasePointSelection
        {
            SelectedMeshCodes = validMeshCodes.ToArray(),
            AnchorLatitude = southLatitude.Value,
            AnchorLongitude = westLongitude.Value,
            ProjectedCoordinate = projectedCoordinate
        };
    }
}
