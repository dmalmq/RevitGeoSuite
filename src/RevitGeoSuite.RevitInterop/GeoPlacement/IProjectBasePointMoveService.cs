using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public interface IProjectBasePointMoveService
{
    ProjectBasePointMovePreview CreatePreview(IDocumentHandle document, WorkingProjectBasePointReference targetReference);

    ProjectBasePointMoveResult MoveProjectBasePoint(IDocumentHandle document, ProjectBasePointMovePreview preview);
}
