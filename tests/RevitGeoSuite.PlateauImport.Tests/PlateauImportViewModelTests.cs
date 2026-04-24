using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauImportViewModelTests
{
    [Fact]
    public void Revit_project_base_point_uses_shared_coordinates_when_available()
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        CrsReference projectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" };
        GeographicCoordinate desiredLocation = new GeographicCoordinate(35.681236d, 139.767125d);
        ProjectedCoordinate desiredProjected = coordinateTransformer.Project(desiredLocation, projectCrs);

        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Sample Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot
                {
                    Name = "Project Base Point",
                    XFeet = 12d,
                    YFeet = 24d,
                    SharedEastWestFeet = desiredProjected.Easting / 0.3048d,
                    SharedNorthSouthFeet = desiredProjected.Northing / 0.3048d,
                    SharedElevationFeet = 0d,
                    EstimatedLatitudeDegrees = 35.000000d,
                    EstimatedLongitudeDegrees = 139.000000d
                },
                StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = projectCrs,
                    Origin = new ProjectOrigin { Latitude = 35.67916666666667, Longitude = 139.76875, ElevationMeters = 0d },
                    ProjectedCoordinate = new ProjectedCoordinate(150d, 200d),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Test"
                }
            },
            CreateGeoInfo());

        Assert.Equal(PlateauImportReferenceSource.WorkingProjectBasePoint, viewModel.SelectedReferenceSource);
        Assert.Equal("Revit Project Base Point", viewModel.ReferenceSourceTitle);
        Assert.Contains("shared coordinates", viewModel.ReferenceSourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.CurrentStateRows, row => row.Label == "Reference Location" && row.Value == $"{desiredLocation.Latitude:F6}, {desiredLocation.Longitude:F6}");
        Assert.Contains(viewModel.CurrentStateRows, row => row.Label == "Reference Projected" && row.Value == $"E {desiredProjected.Easting:F3} m, N {desiredProjected.Northing:F3} m");
    }

    [Fact]
    public void Previous_import_state_is_restored_on_startup()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportState importState = new PlateauImportState
        {
            LastImportedFolderPath = fixtureFolder,
            LastImportDateUtc = new DateTime(2026, 3, 31, 6, 1, 57, DateTimeKind.Utc),
            LastReferenceSource = PlateauImportReferenceSource.WorkingProjectBasePoint,
            LastGeometryImportMode = PlateauGeometryImportMode.DetailedDirectShape,
            LastImportedFeatureCount = 5,
            LastImportedGroupCount = 4,
            LastSelectedTileIds = new List<string> { "53394536" },
            LastSelectedFeatureTypes = new List<string> { nameof(PlateauFeatureType.Building), nameof(PlateauFeatureType.Road) },
            ImportedTileIds = new List<string> { "53394536" },
            LastImportSummary = "Imported 5 elements in 4 groups."
        };

        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Sample Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot
                {
                    Name = "Project Base Point",
                    EstimatedLatitudeDegrees = 35.681236d,
                    EstimatedLongitudeDegrees = 139.767125d
                },
                StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                    Origin = new ProjectOrigin { Latitude = 35.67916666666667, Longitude = 139.76875, ElevationMeters = 0d },
                    ProjectedCoordinate = new ProjectedCoordinate(150d, 200d),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = "Test"
                }
            },
            CreateGeoInfo(),
            importState);

        Assert.Equal(fixtureFolder, viewModel.SelectedFolderPath);
        Assert.True(viewModel.HasLastImportRows);
        Assert.Contains(viewModel.LastImportRows, row => row.Label == "Last Folder" && row.Value == fixtureFolder);
        Assert.Contains(viewModel.LastImportRows, row => row.Label == "Last Categories" && row.Value.Contains("Building"));
        Assert.Equal(PlateauGeometryImportMode.DetailedDirectShape, viewModel.SelectedGeometryImportMode);
        Assert.Contains(viewModel.LastImportRows, row => row.Label == "Last Geometry Mode" && row.Value.Contains("Detailed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("restored", viewModel.ActionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scan Folder", viewModel.ActionMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_folder_populates_filters_and_preview_respects_selected_category_and_tile()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Sample Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        bool scanned = viewModel.TryScanFolder();

        Assert.True(scanned);
        Assert.Equal(5, viewModel.FeatureTypeOptions.Count);
        Assert.Equal(3, viewModel.TileOptions.Count);
        Assert.True(viewModel.HasDetectedSourceFiles);
        Assert.Contains(viewModel.DetectedSourceFiles, path => path.Contains("53394536_bldg_sample.gml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.WarningMessages, message => message.Contains("unsupported_notes.xml"));

        foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
        {
            option.IsSelected = option.FeatureType == PlateauFeatureType.Building;
        }

        viewModel.ToggleTileSelection("53394536");

        bool loaded = viewModel.TryLoadPreview();

        Assert.True(loaded);
        Assert.Equal(2, viewModel.PreparedShapeCount);
        Assert.Contains(viewModel.PreviewRows, row => row.Label == "Selected Categories" && row.Value == "Buildings");
        Assert.Contains(viewModel.FeatureNames, name => name.Contains("Folder Building A"));
        Assert.DoesNotContain(viewModel.FeatureNames, name => name.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Scan_folder_succeeds_even_while_scanning_flag_is_set()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Sample Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;
        viewModel.IsScanning = true;

        bool scanned = viewModel.TryScanFolder();

        Assert.True(scanned);
        Assert.True(viewModel.HasScanRows);
        Assert.True(viewModel.HasDetectedSourceFiles);
    }

    [Fact]
    public void Scan_progress_updates_status_counts_and_resets_after_finish()
    {
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Progress Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedFolderPath = @"C:\plateau";

        bool started = viewModel.TryStartFolderScan(out string folderPath);
        viewModel.ReportScanProgress(new PlateauScanProgress(PlateauScanPhase.Parsing, 2, 6, @"C:\plateau\53394536_bldg_sample.gml"));

        Assert.True(started);
        Assert.Equal(@"C:\plateau", folderPath);
        Assert.True(viewModel.IsScanning);
        Assert.False(viewModel.IsScanProgressIndeterminate);
        Assert.Equal(2, viewModel.ScanProgressCurrent);
        Assert.Equal(6, viewModel.ScanProgressTotal);
        Assert.Equal(33.333333333333336d, viewModel.ScanProgressPercent, 6);
        Assert.Contains("2 of 6", viewModel.ScanProgressStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("53394536_bldg_sample.gml", viewModel.ScanProgressStatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.FinishFolderScan();

        Assert.False(viewModel.IsScanning);
        Assert.Equal(0, viewModel.ScanProgressCurrent);
        Assert.Equal(0, viewModel.ScanProgressTotal);
        Assert.Equal(0d, viewModel.ScanProgressPercent);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.ScanProgressStatusText));
    }

    [Fact]
    public void Scan_folder_starts_with_no_tiles_selected_by_default_and_builds_tile_preview_geojson()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Suggested Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        bool scanned = viewModel.TryScanFolder();

        Assert.True(scanned);
        Assert.Equal(0, viewModel.SelectedTileCount);
        Assert.False(viewModel.CanLoadPreview);
        Assert.True(viewModel.HasTilePreview);
        Assert.Contains("53394536", viewModel.TilePreviewGeoJson, StringComparison.Ordinal);
        Assert.Contains("54394536", viewModel.TilePreviewGeoJson, StringComparison.Ordinal);
        Assert.Contains("Click the grid cells you want to import", viewModel.TilePreviewStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toggle_tile_selection_updates_selected_count_and_allows_preview_loading()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Suggested Tiles Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        bool scanned = viewModel.TryScanFolder();
        bool toggledOn = viewModel.ToggleTileSelection("53394536");
        bool toggledOff = viewModel.ToggleTileSelection("53394536");

        Assert.True(scanned);
        Assert.True(toggledOn);
        Assert.True(toggledOff);
        Assert.Equal(0, viewModel.SelectedTileCount);
        Assert.False(viewModel.CanLoadPreview);

        viewModel.ToggleTileSelection("53394536");

        Assert.Equal(1, viewModel.SelectedTileCount);
        Assert.True(viewModel.CanLoadPreview);
        Assert.Contains("Selected 1 of", viewModel.TilePreviewStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_folder_uses_package_root_and_lists_detected_source_files()
    {
        string packageRoot = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "PackageRoot", "13104_shinjuku-ku_pref_2023_citygml_2_op");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Plateau Package Root Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = packageRoot;

        bool scanned = viewModel.TryScanFolder();

        Assert.True(scanned);
        Assert.Contains(viewModel.ScanRows, row => row.Label == "Scan Mode" && row.Value.Contains("udx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.DetectedSourceFiles, path => path.Contains("udx\\bldg\\53394536_bldg_sample.gml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.DetectedSourceFiles, path => path.Contains("udx\\brid\\54394536_brid_sample.gml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.DetectedSourceFiles, path => path.Contains("metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.FeatureTypeOptions, option => option.FeatureType == PlateauFeatureType.Bridge && option.SourceFileCount == 1);
    }

    [Fact]
    public void Read_only_document_can_preview_but_cannot_import()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Read Only Project",
                IsSupportedDocument = true,
                IsReadOnly = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point" }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        bool scanned = viewModel.TryScanFolder();
        viewModel.ToggleTileSelection("53394536");
        bool loaded = viewModel.TryLoadPreview();

        Assert.True(scanned);
        Assert.True(loaded);
        Assert.Equal(4, viewModel.PreparedShapeCount);
        Assert.False(viewModel.CanImport);
        Assert.Contains("read-only", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detailed_geometry_mode_updates_preview_rows_and_status_text()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Detailed Preview Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedGeometryImportModeOption = viewModel.GeometryImportModeOptions.Single(option => option.Mode == PlateauGeometryImportMode.DetailedDirectShape);
        viewModel.SelectedFolderPath = fixtureFolder;

        bool scanned = viewModel.TryScanFolder();
        viewModel.ToggleTileSelection("53394536");
        bool loaded = viewModel.TryLoadPreview();

        Assert.True(scanned);
        Assert.True(loaded);
        Assert.Equal(PlateauGeometryImportMode.DetailedDirectShape, viewModel.SelectedGeometryImportMode);
        Assert.Contains(viewModel.PreviewRows, row => row.Label == "Geometry Mode" && row.Value.Contains("Detailed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Detailed Geometry", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Preview_build_pipeline_matches_the_synchronous_preview_result_and_toggles_preview_state()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel syncViewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Synchronous Preview Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        PlateauImportViewModel asyncViewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Async Preview Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());

        syncViewModel.SelectedReferenceSourceOption = syncViewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        syncViewModel.SelectedFolderPath = fixtureFolder;
        asyncViewModel.SelectedReferenceSourceOption = asyncViewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        asyncViewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(syncViewModel.TryScanFolder());
        Assert.True(asyncViewModel.TryScanFolder());
        syncViewModel.ToggleTileSelection("53394536");
        asyncViewModel.ToggleTileSelection("53394536");

        bool loadedSynchronously = syncViewModel.TryLoadPreview();
        bool started = asyncViewModel.TryStartPreviewLoad(out PlateauImportViewModel.PreviewBuildRequest? request);
        PlateauImportViewModel.PreviewBuildResult result = asyncViewModel.BuildPreviewResult(request!);
        bool applied = asyncViewModel.ApplyPreviewResult(result);
        asyncViewModel.FinishPreviewLoad();

        Assert.True(loadedSynchronously);
        Assert.True(started);
        Assert.NotNull(request);
        Assert.True(applied);
        Assert.False(asyncViewModel.IsPreparingPreview);
        Assert.Equal(syncViewModel.PreparedShapeCount, asyncViewModel.PreparedShapeCount);
        Assert.Equal(syncViewModel.FeatureNames.ToArray(), asyncViewModel.FeatureNames.ToArray());
        Assert.Equal(syncViewModel.PreviewRows.Select(row => row.Value).ToArray(), asyncViewModel.PreviewRows.Select(row => row.Value).ToArray());
    }

    [Fact]
    public void Preview_build_results_are_discarded_when_the_selection_changes_mid_build()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Stale Preview Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(viewModel.TryScanFolder());
        viewModel.ToggleTileSelection("53394536");

        Assert.True(viewModel.TryStartPreviewLoad(out PlateauImportViewModel.PreviewBuildRequest? request));
        Assert.NotNull(request);
        Assert.True(viewModel.IsPreparingPreview);
        Assert.False(viewModel.CanLoadPreview);
        Assert.False(viewModel.CanImport);

        viewModel.ToggleTileSelection("53394536");
        PlateauImportViewModel.PreviewBuildResult result = viewModel.BuildPreviewResult(request!);
        bool applied = viewModel.ApplyPreviewResult(result);
        viewModel.FinishPreviewLoad();

        Assert.False(applied);
        Assert.False(viewModel.IsPreparingPreview);
        Assert.Null(viewModel.PreparedPlan);
        Assert.Equal(0, viewModel.PreparedShapeCount);
        Assert.False(viewModel.CanLoadPreview);
    }
    private static GeoProjectInfo CreateGeoInfo()
    {
        return new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 36d, Longitude = 139.833333333333d, ElevationMeters = 0d },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };
    }

    private static PlateauImportViewModel CreateViewModel(CurrentProjectStateSummary currentState, GeoProjectInfo info, PlateauImportState? importState = null)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new PlateauImportViewModel(
            currentState,
            info,
            importState,
            new PlateauImportReferenceResolver(coordinateTransformer),
            new RevitGeoSuite.Core.Plateau.Tiles.PlateauTileIndex(),
            new PlateauFolderScanService(new CityGmlParser()),
            new ContextGeometryBuilder());
    }
}
