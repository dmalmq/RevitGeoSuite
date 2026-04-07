using System;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

public sealed class ProjectBasePointMoveCoordinator
{
    private readonly IProjectBasePointMoveService moveService;

    public ProjectBasePointMoveCoordinator(IProjectBasePointMoveService moveService)
    {
        this.moveService = moveService ?? throw new ArgumentNullException(nameof(moveService));
    }

    public ProjectBasePointMovePreview Preview(IDocumentHandle document, GeoreferenceViewModel viewModel)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (viewModel is null)
        {
            throw new ArgumentNullException(nameof(viewModel));
        }

        WorkingProjectBasePointReference targetReference = viewModel.GetActualProjectBasePointMoveTarget();
        return moveService.CreatePreview(document, targetReference);
    }

    public ProjectBasePointMoveResult Apply(IDocumentHandle document, ProjectBasePointMovePreview preview)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        return moveService.MoveProjectBasePoint(document, preview);
    }
}
