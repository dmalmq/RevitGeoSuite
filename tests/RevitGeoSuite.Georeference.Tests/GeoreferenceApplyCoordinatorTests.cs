using System;
using System.Linq;
using Moq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class GeoreferenceApplyCoordinatorTests
{
    [Fact]
    public void Apply_forwards_existing_setup_preview_intent_to_revit_placement_service()
    {
        GeoreferenceViewModel viewModel = GeoreferenceViewModelTestsAccessor.CreatePreviewReadyExistingSetupViewModel();
        PlacementApplyResult expectedResult = new PlacementApplyResult
        {
            AuditSummary = "Applied test summary"
        };
        Mock<IRevitGeoPlacementService> placementService = new Mock<IRevitGeoPlacementService>(MockBehavior.Strict);
        placementService
            .Setup(service => service.ApplyPlacement(It.IsAny<IDocumentHandle>(), It.IsAny<PlacementIntent>()))
            .Returns(expectedResult);

        GeoreferenceApplyCoordinator coordinator = new GeoreferenceApplyCoordinator(placementService.Object);
        FakeDocumentHandle document = new FakeDocumentHandle("SampleProject");

        PlacementApplyResult result = coordinator.Apply(document, viewModel);

        Assert.Same(expectedResult, result);
        placementService.Verify(service => service.ApplyPlacement(
            document,
            It.Is<PlacementIntent>(intent =>
                intent.ApplyMode == PlacementApplyMode.MetadataOnly &&
                intent.SelectedCrs!.EpsgCode == 6677 &&
                intent.WorkingProjectBasePoint != null &&
                Math.Abs(intent.WorkingProjectBasePoint.ProjectedCoordinate!.Value.Easting - 25.0d) < 0.001d &&
                Math.Abs(intent.WorkingProjectBasePoint.ProjectedCoordinate.Value.Northing + 12.5d) < 0.001d)), Times.Once);
    }

    [Fact]
    public void Apply_blocks_when_preview_is_not_ready()
    {
        GeoreferenceViewModel viewModel = GeoreferenceViewModelTestsAccessor.CreateChooseCrsViewModel();
        GeoreferenceApplyCoordinator coordinator = new GeoreferenceApplyCoordinator(Mock.Of<IRevitGeoPlacementService>());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => coordinator.Apply(new FakeDocumentHandle("SampleProject"), viewModel));

        Assert.Contains("Generate a valid preview", error.Message);
    }

    private sealed class FakeDocumentHandle : IDocumentHandle
    {
        public FakeDocumentHandle(string documentKey)
        {
            DocumentKey = documentKey;
        }

        public string DocumentKey { get; }
    }

    private static class GeoreferenceViewModelTestsAccessor
    {
        public static GeoreferenceViewModel CreateChooseCrsViewModel()
        {
            GeoreferenceViewModel viewModel = CreateBaseViewModel();
            viewModel.GoNext();
            return viewModel;
        }

        public static GeoreferenceViewModel CreatePreviewReadyExistingSetupViewModel()
        {
            GeoreferenceViewModel viewModel = CreateBaseViewModel();
            viewModel.GoNext();
            viewModel.SelectedCrs = viewModel.AvailableCrs.Single(definition => definition.EpsgCode == 6677);
            viewModel.ConfirmExistingSetup = true;
            viewModel.GoNext();
            return viewModel;
        }

        private static GeoreferenceViewModel CreateBaseViewModel()
        {
            CrsRegistry registry = new CrsRegistry();
            CoordinateTransformer transformer = new CoordinateTransformer(registry);
            CoordinateValidator validator = new CoordinateValidator(registry, transformer, new JapanMeshCalculator());
            return new GeoreferenceViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Sample Project",
                    IsSupportedDocument = true,
                    ExistingSetupDetected = true,
                    ProjectPosition = new ProjectPositionSnapshot(),
                    SurveyPoint = new BasePointSnapshot
                    {
                        Name = "Survey Point",
                        SharedEastWestFeet = 0d,
                        SharedNorthSouthFeet = 0d,
                        SharedElevationFeet = 0d,
                        EstimatedLatitudeDegrees = 35.681236,
                        EstimatedLongitudeDegrees = 139.767125
                    },
                    ProjectBasePoint = new BasePointSnapshot
                    {
                        Name = "Project Base Point",
                        SharedEastWestFeet = 25d / 0.3048d,
                        SharedNorthSouthFeet = -12.5d / 0.3048d,
                        SharedElevationFeet = 0d,
                        EstimatedLatitudeDegrees = 35.680910,
                        EstimatedLongitudeDegrees = 139.768015
                    }
                },
                registry.GetAvailableDefinitions(),
                transformer,
                new PlacementPreviewService(validator),
                new SplitSurveyProjectBasePointPreviewService(validator));
        }
    }
}
