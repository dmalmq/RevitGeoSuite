using System.Collections.Generic;

namespace RevitGeoSuite.Core.Coordinates;

public interface ICrsRegistry
{
    IReadOnlyCollection<CrsDefinition> GetAvailableDefinitions();

    IReadOnlyCollection<CrsReference> GetAvailableReferences();

    IReadOnlyCollection<CrsDefinition> Search(string? query);

    bool TryGetByEpsgCode(int epsgCode, out CrsDefinition? definition);
}
