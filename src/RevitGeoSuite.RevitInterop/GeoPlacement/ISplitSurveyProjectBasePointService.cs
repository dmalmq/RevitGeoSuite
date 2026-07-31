using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public interface ISplitSurveyProjectBasePointService
{
    PlacementApplyResult ApplyPlacement(IDocumentHandle document, SplitSurveyProjectBasePointIntent intent);
}
