using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DExportViewModelTests
{
    [Fact]
    public void Working_project_base_point_is_preferred_for_reference_context_when_available()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" },
                StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                    Origin = new ProjectOrigin { Latitude = 35.67916666666667, Longitude = 139.76875, ElevationMeters = 0d },
                    ProjectedCoordinate = new ProjectedCoordinate(150d, 200d),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Test"
                }
            },
            CreateGeoProjectInfo());

        Assert.Equal(Tiles3DExportReferenceSource.WorkingProjectBasePoint, viewModel.SelectedReferenceSource);
        Assert.Equal("Working Project Base Point", viewModel.ReferenceSourceTitle);
    }

    [Fact]
    public void Previous_export_state_is_restored_on_startup()
    {
        Tiles3DExportState exportState = new Tiles3DExportState
        {
            LastExportPath = @"C:\\exports\\tiles",
            LastLodSetting = Tiles3DLevelOfDetail.Fine.ToString(),
            LastExportDateUtc = new DateTime(2026, 3, 31, 8, 0, 0, DateTimeKind.Utc),
            LastReferenceSource = Tiles3DExportReferenceSource.CanonicalOrigin,
            LastExportedElementCount = 3,
            LastExportedTriangleCount = 18
        };

        Tiles3DExportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            CreateGeoProjectInfo(),
            exportState);

        Assert.Equal(exportState.LastExportPath, viewModel.OutputDirectory);
        Assert.True(viewModel.HasLastExportRows);
        Assert.Contains(viewModel.LastExportRows, row => row.Label == "Last LOD" && row.Value == "Fine");
        Assert.Contains("Prepare Export", viewModel.ActionMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepared_package_enables_export_when_output_directory_is_set()
    {
        Tiles3DExportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            CreateGeoProjectInfo());

        viewModel.MarkPrepared(new Tiles3DExportPreparationResult
        {
            Package = new Tiles3DExportPackage
            {
                ReferenceContext = new Tiles3DExportReferenceContext
                {
                    Title = "Canonical Origin",
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" }
                },
                Meshes = new List<Tiles3DMeshPrimitive>
                {
                    new Tiles3DMeshPrimitive
                    {
                        Name = "Triangle",
                        Triangles = new List<Tiles3DTriangle>
                        {
                            new Tiles3DTriangle(new Tiles3DPoint(0d,0d,0d), new Tiles3DPoint(1d,0d,0d), new Tiles3DPoint(0d,1d,0d))
                        }
                    }
                }
            },
            PreparedRows = new[] { new DetailRow("Exportable Elements", "1") },
            FeatureNames = new[] { "Triangle" },
            StatusMessage = "Prepared."
        });

        Assert.False(viewModel.CanExport);
        viewModel.OutputDirectory = @"C:\\exports\\tiles";
        Assert.True(viewModel.CanExport);
    }

    private static Tiles3DExportViewModel CreateViewModel(CurrentProjectStateSummary currentState, GeoProjectInfo info, Tiles3DExportState? state = null)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new Tiles3DExportViewModel(
            currentState,
            info,
            state,
            new Tiles3DExportReferenceResolver(coordinateTransformer));
    }

    private static GeoProjectInfo CreateGeoProjectInfo()
    {
        return new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };
    }
}
