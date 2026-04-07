using System;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

public sealed class SplitSurveyProjectBasePointApplyCoordinator
{
    private readonly ISplitSurveyProjectBasePointService placementService;

    public SplitSurveyProjectBasePointApplyCoordinator(ISplitSurveyProjectBasePointService placementService)
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
            throw new InvalidOperationException("Generate a valid split-workflow preview in an editable project before applying georeference changes.");
        }

        SplitSurveyProjectBasePointIntent intent = viewModel.GetSplitApplyIntent();
        return placementService.ApplyPlacement(document, intent);
    }
}
