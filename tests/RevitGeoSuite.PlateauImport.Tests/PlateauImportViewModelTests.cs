using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    public void Scan_folder_reuses_cached_plateau_scan_and_updates_status()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string tempFolder = CopyFixtureToTemp(fixtureFolder);
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Plateau Cached Scan Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = tempFolder;

            Assert.True(viewModel.TryScanFolder());
            Assert.DoesNotContain("cached", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

            Assert.True(viewModel.TryScanFolder());

            Assert.Contains("cached", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
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
    [Fact]
    public void BuildOutlineFeatures_returns_one_outline_per_selected_feature_on_the_correct_layer()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Outline Export Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(viewModel.TryScanFolder());
        foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
        {
            option.IsSelected = true;
        }
        viewModel.ToggleTileSelection("53394536");

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = viewModel.BuildOutlineFeatures(out IReadOnlyList<string> warnings);

        Assert.NotEmpty(outlines);
        Assert.All(outlines, outline => Assert.True(outline.VerticesMetres.Count >= 3));

        HashSet<string> layers = new HashSet<string>(outlines.Select(o => o.Layer));
        Assert.Contains("PLATEAU_BUILDINGS", layers);
        Assert.Contains("PLATEAU_ROADS", layers);
        Assert.Contains("PLATEAU_VEGETATION", layers);
        // brid sample sits on tile 54394536 which is not selected here, so no PLATEAU_BRIDGES.
        Assert.DoesNotContain("PLATEAU_BRIDGES", layers);
        Assert.NotNull(warnings);
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_exports_roads_as_filled_areas_not_outline_polylines()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Road Fill Export Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(viewModel.TryScanFolder());
        foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
        {
            option.IsSelected = true;
        }
        viewModel.ToggleTileSelection("53394536");

        PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(out IReadOnlyList<string> warnings);

        Assert.NotEmpty(package.RoadAreas);
        Assert.DoesNotContain(package.Features, outline => string.Equals(outline.Layer, "PLATEAU_ROADS", StringComparison.Ordinal));
        Assert.All(package.RoadAreas, roadArea => Assert.Equal("PLATEAU_ROADS", roadArea.Layer));
        Assert.NotNull(warnings);
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_includes_selected_tile_gsi_kiban_lines()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Line Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;

            Assert.True(viewModel.TryScanKibanFolder());
            PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(out IReadOnlyList<string> warnings);

            Assert.NotNull(warnings);
            // Sidewalks are polygonized into KibanPolygonFeatures; KibanLineFeatures now
            // only carries railways.
            Assert.DoesNotContain(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiSidewalksLayer);
            Assert.Contains(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiRailwaysLayer && feature.Visibility == "非表示");
            Assert.All(package.KibanLineFeatures, feature => Assert.Equal("533945", feature.MeshCode));
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanKibanFolder_reuses_cached_scan_and_updates_status()
    {
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Cached Scan Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            PlateauImportViewModel.KibanScanRequest request = new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "533945" });

            PlateauImportViewModel.KibanScanResult first = viewModel.ScanKibanFolder(request);
            PlateauImportViewModel.KibanScanResult second = viewModel.ScanKibanFolder(request);

            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.True(viewModel.ApplyKibanScanResult(second));
            Assert.Contains("cached", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanKibanFolder_uses_secondary_mesh_codes_in_cache_key()
    {
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Mesh Cache Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());

            PlateauImportViewModel.KibanScanResult first = viewModel.ScanKibanFolder(new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "533945" }));
            PlateauImportViewModel.KibanScanResult second = viewModel.ScanKibanFolder(new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "543945" }));

            Assert.False(first.IsFromCache);
            Assert.False(second.IsFromCache);
            Assert.Empty(second.Features);
            Assert.Empty(second.PolygonFeatures);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanKibanFolder_uses_optional_land_use_tokens_in_cache_key()
    {
        string kibanFolder = CreateTempKibanFolder("53394536", includeLandUse: true);
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Token Cache Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());

            PlateauImportViewModel.KibanScanResult first = viewModel.ScanKibanFolder(new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "533945" },
                additionalGreenLandUseTokens: null));
            PlateauImportViewModel.KibanScanResult second = viewModel.ScanKibanFolder(new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "533945" },
                new[] { "公園" }));

            Assert.False(first.IsFromCache);
            Assert.False(second.IsFromCache);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanKibanFolder_invalidates_cache_when_file_timestamp_changes()
    {
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Invalidation Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            PlateauImportViewModel.KibanScanRequest request = new PlateauImportViewModel.KibanScanRequest(
                kibanFolder,
                new[] { "533945" });
            string changedFile = Directory.EnumerateFiles(kibanFolder, "FG-GML-*.xml", SearchOption.AllDirectories).Single();

            PlateauImportViewModel.KibanScanResult first = viewModel.ScanKibanFolder(request);
            PlateauImportViewModel.KibanScanResult second = viewModel.ScanKibanFolder(request);
            File.SetLastWriteTimeUtc(changedFile, DateTime.UtcNow.AddMinutes(5));
            PlateauImportViewModel.KibanScanResult third = viewModel.ScanKibanFolder(request);

            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.False(third.IsFromCache);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void Export_mode_restores_last_kiban_folder_path_from_session_cache()
    {
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel firstViewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Folder Remember Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo(),
                isExportMode: true);
            firstViewModel.KibanFolderPath = kibanFolder;

            PlateauImportViewModel secondViewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Folder Remember Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo(),
                isExportMode: true);

            Assert.Equal(kibanFolder, secondViewModel.KibanFolderPath);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_auto_scans_kiban_folder_for_export_request()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Auto Kiban Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;
            Assert.Empty(viewModel.KibanFeatureOptions);

            Assert.True(viewModel.TryStartShapefileExport(out PlateauImportViewModel.ShapefileExportRequest? request));
            try
            {
                PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(request!, out IReadOnlyList<string> warnings);

                Assert.NotNull(warnings);
                Assert.DoesNotContain(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiSidewalksLayer);
                Assert.Contains(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiRailwaysLayer);
            }
            finally
            {
                viewModel.FinishShapefileExport();
            }
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_honours_selected_kiban_layers_for_export_request()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394536");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Filtered Kiban Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;
            Assert.True(viewModel.TryScanKibanFolder());
            viewModel.KibanFeatureOptions.Single(option => option.LayerName == PlateauContextOutlinesDxfWriter.GsiRailwaysLayer).IsSelected = false;

            Assert.True(viewModel.TryStartShapefileExport(out PlateauImportViewModel.ShapefileExportRequest? request));
            try
            {
                PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(request!, out IReadOnlyList<string> warnings);

                Assert.NotNull(warnings);
                Assert.Empty(package.KibanLineFeatures);
                Assert.DoesNotContain(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiRailwaysLayer);
                Assert.DoesNotContain(package.KibanLineFeatures, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiSidewalksLayer);
            }
            finally
            {
                viewModel.FinishShapefileExport();
            }
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_includes_selected_tile_gsi_kiban_land_use_polygons()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394536", includeLandUse: true);
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Kiban Land Use Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;

            Assert.True(viewModel.TryScanKibanFolder());
            Assert.Contains(viewModel.KibanFeatureOptions, option => option.LayerName == KibanGmlParser.LandUseLayer && option.IsSelected);
            PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(out IReadOnlyList<string> warnings);

            Assert.NotNull(warnings);
            KibanPolygonExportFeature landUse = Assert.Single(package.KibanPolygonFeatures, feature => feature.Layer == KibanGmlParser.LandUseLayer);
            Assert.Equal("緑地", landUse.FeatureType);
            Assert.Equal("533945", landUse.MeshCode);
            Assert.True(landUse.ExteriorRingMetres.Count >= 3);
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void WriteShapefilesStreaming_writes_plateau_and_kiban_category_files()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394536", includeLandUse: true);
        string outputDirectory = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteStreamingShpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "Streaming Shapefile Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;
            Assert.True(viewModel.TryScanKibanFolder());

            Assert.True(viewModel.TryStartShapefileExport(out PlateauImportViewModel.ShapefileExportRequest? request));
            PlateauContextShapefileWriter.WriteResult result;
            try
            {
                result = viewModel.WriteShapefilesStreaming(
                    Path.Combine(outputDirectory, "context.shp"),
                    request!,
                    includePlateauContext: true,
                    includeKibanData: true,
                    includeRevitModel: false,
                    Array.Empty<RevitModelFootprintFeature>());
            }
            finally
            {
                viewModel.FinishShapefileExport();
            }

            Assert.True(result.FeatureCount > 0);
            Assert.True(result.FootprintFeatureCount > 0);
            Assert.True(result.RoadFeatureCount > 0);
            Assert.True(result.RailwayFeatureCount > 0);
            Assert.True(result.KibanLandUseFeatureCount > 0);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "context.shp")));
            AssertShapefileExists(Path.Combine(outputDirectory, "context_plateau_buildings.shp"));
            AssertShapefileExists(Path.Combine(outputDirectory, "context_plateau_roads.shp"));
            AssertShapefileExists(Path.Combine(outputDirectory, "context_gsi_railways.shp"));
            AssertShapefileExists(Path.Combine(outputDirectory, "context_gsi_landuse.shp"));
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_warns_when_kiban_folder_has_no_lines_for_selected_tile()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string kibanFolder = CreateTempKibanFolder("53394537");
        try
        {
            PlateauImportViewModel viewModel = CreateViewModel(
                new CurrentProjectStateSummary
                {
                    DocumentTitle = "No Kiban Intersection Export Project",
                    IsSupportedDocument = true,
                    HasStoredGeoInfo = true,
                    ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
                },
                CreateGeoInfo());
            viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
            viewModel.SelectedFolderPath = fixtureFolder;

            Assert.True(viewModel.TryScanFolder());
            foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
            {
                option.IsSelected = true;
            }
            viewModel.ToggleTileSelection("53394536");
            viewModel.KibanFolderPath = kibanFolder;

            Assert.True(viewModel.TryStartShapefileExport(out PlateauImportViewModel.ShapefileExportRequest? request));
            try
            {
                PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(request!, out IReadOnlyList<string> warnings);

                Assert.Empty(package.KibanLineFeatures);
                Assert.True(package.Features.Count > 0 || package.RoadAreas.Count > 0);
                Assert.Contains(warnings, warning => warning.IndexOf("no sidewalk or railway lines intersected", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                viewModel.FinishShapefileExport();
            }
        }
        finally
        {
            Directory.Delete(kibanFolder, recursive: true);
        }
    }

    [Fact]
    public void BuildOutlineFeatures_honours_feature_type_filter_when_relief_is_unchecked()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Outline Export Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(viewModel.TryScanFolder());
        foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
        {
            option.IsSelected = option.FeatureType != PlateauFeatureType.Relief;
        }
        // dem tile 53394537 carries the Relief features in the fixture, but with Relief
        // unchecked it should not contribute outlines.
        viewModel.ToggleTileSelection("53394536");
        viewModel.ToggleTileSelection("53394537");

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = viewModel.BuildOutlineFeatures(out _);

        Assert.DoesNotContain(outlines, outline => string.Equals(outline.Layer, "PLATEAU_RELIEF", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildOutlineFeatures_returns_empty_when_no_scan_has_run()
    {
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Empty Project",
                IsSupportedDocument = true,
                ProjectBasePoint = new BasePointSnapshot { Name = "Project Base Point", EstimatedLatitudeDegrees = 35.681236d, EstimatedLongitudeDegrees = 139.767125d }
            },
            CreateGeoInfo());

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = viewModel.BuildOutlineFeatures(out IReadOnlyList<string> warnings);

        Assert.Empty(outlines);
        Assert.Empty(warnings);
    }

    [Fact]
    public void BuildOutlineDxfExportPackage_uses_survey_point_origin_and_shared_axes_when_local_basis_is_rotated()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauImportViewModel viewModel = CreateViewModel(
            new CurrentProjectStateSummary
            {
                DocumentTitle = "Rotated Outline Export Project",
                IsSupportedDocument = true,
                HasStoredGeoInfo = true,
                SurveyPoint = new BasePointSnapshot
                {
                    Name = "Survey Point",
                    XFeet = 1000d,
                    YFeet = 2000d,
                    ZFeet = 0d,
                    SharedEastWestFeet = 0d,
                    SharedNorthSouthFeet = 0d,
                    SharedElevationFeet = 0d
                },
                ProjectBasePoint = new BasePointSnapshot
                {
                    Name = "Project Base Point",
                    XFeet = 1100d,
                    YFeet = 2200d,
                    ZFeet = 0d,
                    SharedEastWestFeet = 10d / 0.3048d,
                    SharedNorthSouthFeet = 20d / 0.3048d,
                    SharedElevationFeet = 0d
                }
            },
            CreateGeoInfo(),
            localBasisProvider: new NinetyDegreeLocalBasisProvider());
        viewModel.SelectedReferenceSourceOption = viewModel.ReferenceSourceOptions.Single(option => option.Source == PlateauImportReferenceSource.CanonicalOrigin);
        viewModel.SelectedFolderPath = fixtureFolder;

        Assert.True(viewModel.TryScanFolder());
        foreach (PlateauFeatureSelectionItem option in viewModel.FeatureTypeOptions)
        {
            option.IsSelected = option.FeatureType == PlateauFeatureType.Building;
        }
        viewModel.ToggleTileSelection("53394536");

        PlateauOutlineDxfExportPackage package = viewModel.BuildOutlineDxfExportPackage(out IReadOnlyList<string> warnings);
        PlateauContextOutlinesDxfWriter.OutlineFeature buildingOutline = package.Features.Single(outline => outline.SourceId == "bldg-folder-001");

        Assert.NotNull(warnings);
        Assert.Equal(6677, package.ProjectCrs.EpsgCode);
        Assert.Equal(0d, package.OriginOffsetMetres.X, 6);
        Assert.Equal(0d, package.OriginOffsetMetres.Y, 6);
        Assert.Equal(10d, package.ProjectBasePointMarkerMetres.X, 6);
        Assert.Equal(20d, package.ProjectBasePointMarkerMetres.Y, 6);

        Assert.Equal(100d, buildingOutline.VerticesMetres[0].X, 6);
        Assert.Equal(150d, buildingOutline.VerticesMetres[0].Y, 6);
        Assert.Equal(140d, buildingOutline.VerticesMetres[1].X, 6);
        Assert.Equal(150d, buildingOutline.VerticesMetres[1].Y, 6);
        Assert.Equal(140d, buildingOutline.VerticesMetres[2].X, 6);
        Assert.Equal(190d, buildingOutline.VerticesMetres[2].Y, 6);

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.Write(
            writer,
            package.Features,
            package.RoadAreas,
            package.ProjectBasePointMarkerMetres,
            package.OriginOffsetMetres);
        string dxf = writer.ToString();
        int circleIndex = dxf.IndexOf("\nCIRCLE\n", StringComparison.Ordinal);
        Assert.True(circleIndex >= 0);
        string markerEntity = dxf.Substring(circleIndex);
        Assert.Contains("\n10\n10.0\n", markerEntity);
        Assert.Contains("\n20\n20.0\n", markerEntity);
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

    private static string CreateTempKibanFolder(string selectedTileId, bool includeLandUse = false)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshBounds bounds = calculator.GetBounds(new MeshCode { Value = selectedTileId });
        double centerLatitude = bounds.CenterLatitude;
        double westLongitude = bounds.WestLongitude + (bounds.LongitudeSpan * 0.25d);
        double eastLongitude = bounds.WestLongitude + (bounds.LongitudeSpan * 0.75d);
        string secondaryMeshCode = selectedTileId.Substring(0, 6);

        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteKibanVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string meshDirectory = Path.Combine(directory, $"FG-GML-{secondaryMeshCode}-ALL-20260401");
        Directory.CreateDirectory(meshDirectory);
        string filePath = Path.Combine(meshDirectory, $"FG-GML-{secondaryMeshCode}-RdCompt-20260401-0001.xml");
        File.WriteAllText(filePath, BuildKibanFixtureXml(centerLatitude, westLongitude, eastLongitude, includeLandUse), Encoding.UTF8);
        return directory;
    }

    private static string BuildKibanFixtureXml(double latitude, double westLongitude, double eastLongitude, bool includeLandUse)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<Dataset xmlns:gml=""http://www.opengis.net/gml/3.2"" xmlns=""http://fgd.gsi.go.jp/spec/2008/FGD_GMLSchema"" gml:id=""Dataset1"">
  <RdCompt gml:id=""sidewalk-id"">
    <fid>sidewalk-fid</fid>
    <vis>表示</vis>
    <loc><gml:Curve gml:id=""sidewalk-g""><gml:segments><gml:LineStringSegment><gml:posList>
{0} {1}
{0} {2}
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>歩道</type>
  </RdCompt>
  <RailCL gml:id=""rail-id"">
    <fid>rail-fid</fid>
    <vis>非表示</vis>
    <loc><gml:Curve gml:id=""rail-g""><gml:segments><gml:LineStringSegment><gml:posList>
{0} {1}
{0} {2}
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>トンネル内の鉄道</type>
  </RailCL>
</Dataset>",
            latitude,
            westLongitude,
            eastLongitude)
            .Replace("</Dataset>", includeLandUse
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    @"  <GreenArea gml:id=""green-id"">
    <fid>green-fid</fid>
    <area><gml:Surface gml:id=""green-g""><gml:patches><gml:PolygonPatch><gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>
{0} {1}
{0} {2}
{3} {2}
{3} {1}
{0} {1}
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></area>
    <type>緑地</type>
  </GreenArea>
</Dataset>",
                    latitude - 0.00005d,
                    westLongitude,
                    eastLongitude,
                    latitude + 0.00005d)
                : "</Dataset>");
    }

    private static void AssertShapefileExists(string shapefilePath)
    {
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".shp")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".shx")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".dbf")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".prj")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".cpg")));
    }

    private static string CopyFixtureToTemp(string sourceFolder)
    {
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuitePlateauVmCacheTests", Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceFolder, tempFolder);
        return tempFolder;
    }

    private static void CopyDirectory(string sourceFolder, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (string filePath in Directory.EnumerateFiles(sourceFolder))
        {
            File.Copy(filePath, Path.Combine(targetFolder, Path.GetFileName(filePath)));
        }

        foreach (string sourceSubfolder in Directory.EnumerateDirectories(sourceFolder))
        {
            CopyDirectory(sourceSubfolder, Path.Combine(targetFolder, Path.GetFileName(sourceSubfolder)));
        }
    }

    private static PlateauImportViewModel CreateViewModel(
        CurrentProjectStateSummary currentState,
        GeoProjectInfo info,
        PlateauImportState? importState = null,
        IPlateauImportLocalBasisProvider? localBasisProvider = null,
        bool isExportMode = false)
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        return new PlateauImportViewModel(
            currentState,
            info,
            importState,
            new PlateauImportReferenceResolver(coordinateTransformer, localBasisProvider),
            new RevitGeoSuite.Core.Plateau.Tiles.PlateauTileIndex(),
            new PlateauFolderScanService(new CityGmlParser()),
            new ContextGeometryBuilder(),
            kibanCoordinateTransformer: coordinateTransformer,
            isExportMode: isExportMode);
    }

    private sealed class NinetyDegreeLocalBasisProvider : IPlateauImportLocalBasisProvider
    {
        public void Apply(PlateauImportReferenceContext context)
        {
            context.SharedEastToLocalX = 0d;
            context.SharedEastToLocalY = 1d;
            context.SharedNorthToLocalX = -1d;
            context.SharedNorthToLocalY = 0d;
        }
    }
}
