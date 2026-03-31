using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public interface IRevitGeoPlacementService
{
    PlacementApplyResult ApplyPlacement(IDocumentHandle document, PlacementIntent intent);
}
