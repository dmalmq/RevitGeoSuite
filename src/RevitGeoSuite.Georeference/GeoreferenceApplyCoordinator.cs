using System;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

public sealed class GeoreferenceApplyCoordinator
{
    private readonly IRevitGeoPlacementService placementService;

    public GeoreferenceApplyCoordinator(IRevitGeoPlacementService placementService)
    {
        this.placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
    }

    public PlacementApplyResult Apply(IDocumentHandle document, GeoreferenceViewModel viewModel)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (viewModel is null)
        {
            throw new ArgumentNullException(nameof(viewModel));
        }

        if (!viewModel.CanApply)
        {
            throw new InvalidOperationException("Generate a valid preview in an editable project before applying georeference changes.");
        }

        PlacementIntent intent = viewModel.GetApplyIntent();
        return placementService.ApplyPlacement(document, intent);
    }
}
