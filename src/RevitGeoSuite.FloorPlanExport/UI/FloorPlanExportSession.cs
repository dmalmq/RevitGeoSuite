using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Preview;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.FloorPlanExport.UI;

public sealed class FloorPlanExportSession : IDisposable
{
    private readonly IReadOnlyList<ViewPlan> _views;
    private readonly IReadOnlyList<LinkSelectionItem> _availableLinks;
    private readonly Func<ExportDialogResult, Action<ExportProgressUpdate>?, FloorGeoPackageExportResult>? _runExportRequested;
    private readonly Action<ExportProfileScope, string, ExportDialogSettings>? _saveProfileRequested;
    private readonly Action<ExportProfile>? _deleteProfileRequested;
    private readonly Action<ExportDialogSettings>? _saveSettingsRequested;
    private readonly ModelCoordinateInfo? _coordinateInfo;
    private readonly WebRpcBridge _bridge;
    private ExporterSettingsPayload _currentPayload;
    private ExportDialogSettings _baseSettings;
    private List<ExportProfile> _profiles;
    private List<SchemaProfile> _schemaProfiles;
    private List<ValidationPolicyProfile> _validationPolicyProfiles;
    private ExportPreviewController? _previewController;
    private IReadOnlyList<ViewPlan> _previewViews = Array.Empty<ViewPlan>();
    private ViewPlan? _currentPreviewView;
    private PreviewInitialStateResponse? _previewState;
    private ExportResultInitialStateResponse? _lastResultPayload;
    private string _lastOutputDirectory = string.Empty;
    private ExecutionProgressPayload _latestProgress;

    public FloorPlanExportSession(
        WebRpcBridge bridge,
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        IReadOnlyList<LinkSelectionItem>? availableLinks = null,
        IReadOnlyList<ExportProfile>? profiles = null,
        Action<ExportDialogSettings>? saveSettingsRequested = null,
        Action<ExportProfileScope, string, ExportDialogSettings>? saveProfileRequested = null,
        Action<ExportProfile>? deleteProfileRequested = null,
        Func<ExportDialogResult, Action<ExportProgressUpdate>?, FloorGeoPackageExportResult>? runExportRequested = null,
        ModelCoordinateInfo? coordinateInfo = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _views = views ?? throw new ArgumentNullException(nameof(views));
        _baseSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _availableLinks = (availableLinks ?? Array.Empty<LinkSelectionItem>()).ToList();
        _profiles = (profiles ?? Array.Empty<ExportProfile>()).Select(CloneProfile).ToList();
        _saveSettingsRequested = saveSettingsRequested;
        _saveProfileRequested = saveProfileRequested;
        _deleteProfileRequested = deleteProfileRequested;
        _runExportRequested = runExportRequested;
        _coordinateInfo = coordinateInfo;
        _schemaProfiles = SchemaProfile.NormalizeProfiles(_baseSettings.SchemaProfiles).Select(profile => profile.Clone()).ToList();
        _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(_baseSettings.ValidationPolicyProfiles).Select(profile => profile.Clone()).ToList();
        _currentPayload = ToPayload(_baseSettings, selectedProfileName: null);
        _latestProgress = BuildInitialProgress(_baseSettings.UiLanguage);
    }

    public ExportDialogSettings BuildSettings()
    {
        return ToSettings(_currentPayload);
    }

    public void Dispose()
    {
        _previewController?.DiscardPendingChangesOnClose();
    }

    public void RegisterHandlers(WebRpcBridge bridge)
    {
        bridge.RegisterHandler(new GetInitialStateHandler(this));
        bridge.RegisterHandler(new PreparePreviewHandler(this));
        bridge.RegisterHandler(new PreviewGetInitialStateHandler(this));
        bridge.RegisterHandler(new PreviewLoadViewHandler(this));
        bridge.RegisterHandler(new PreviewAssignCategoryHandler(this));
        bridge.RegisterHandler(new PreviewClearAssignmentHandler(this));
        bridge.RegisterHandler(new PreviewSaveAssignmentsHandler(this));
        bridge.RegisterHandler(new PreviewDiscardAssignmentsHandler(this));
        bridge.RegisterHandler(new RunExportHandler(this));
        bridge.RegisterHandler(new ProgressInitialStateHandler(this));
        bridge.RegisterHandler(new ResultInitialStateHandler(this));
        bridge.RegisterHandler(new OpenOutputFolderHandler(this));
        bridge.RegisterHandler(new SaveProfileHandler(this));
        bridge.RegisterHandler(new DeleteProfileHandler(this));
        bridge.RegisterHandler(new CancelHandler(this));
    }

    public ExporterInitialStateResponse BuildInitialState(ExporterSettingsPayload? payload = null)
    {
        ExporterSettingsPayload nextPayload = ClonePayload(payload ?? _currentPayload);
        return new ExporterInitialStateResponse
        {
            DocumentName = _views.FirstOrDefault()?.Document?.Title ?? ProjectInfo.Name,
            Version = ProjectInfo.VersionTag,
            CoordinateStatus = BuildCoordinateStatus(),
            CoordinateDetail = BuildCoordinateDetail(),
            Settings = nextPayload,
            Views = _views
                .Select(view =>
                {
                    string levelName = view.GenLevel?.Name ?? "<no level>";
                    return new ExporterViewOption
                    {
                        Id = view.Id.Value,
                        Name = view.Name,
                        LevelName = levelName,
                        DisplayName = $"{view.Name} [Level: {levelName}]",
                    };
                })
                .ToList(),
            Links = _availableLinks
                .Select(link => new ExporterLinkOption
                {
                    Id = link.LinkInstanceId,
                    DisplayName = link.DisplayName,
                    SourceDocumentName = link.SourceDocumentName ?? string.Empty,
                })
                .ToList(),
            Profiles = _profiles
                .OrderBy(profile => profile.Scope)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new ExporterProfileOption
                {
                    Name = profile.Name,
                    Scope = ToScopeString(profile.Scope),
                    DisplayName = $"[{profile.Scope}] {profile.Name}",
                    Settings = ToPayload(profile.ToSettings(), profile.Name),
                })
                .ToList(),
            SchemaProfiles = _schemaProfiles.Select(profile => new ExporterNamedOption { Name = profile.Name }).ToList(),
            ValidationPolicies = _validationPolicyProfiles.Select(profile => new ExporterNamedOption { Name = profile.Name }).ToList(),
            CrsPresetGroups = CrsPresetCatalog.GetAllGroups()
                .Select(group => new ExporterCrsPresetGroup
                {
                    Region = group.Region,
                    Entries = group.Entries
                        .Select(entry => new ExporterCrsPreset
                        {
                            Epsg = entry.Epsg,
                            DisplayName = entry.DisplayName,
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }

    private ExporterSettingsPayload ToPayload(ExportDialogSettings settings, string? selectedProfileName)
    {
        ExportFeatureType featureTypes = settings.FeatureTypes == ExportFeatureType.None
            ? ExportFeatureType.All
            : settings.FeatureTypes;
        UnitGeometrySource geometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
        UnitAttributeSource attributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, geometrySource, settings.UnitAttributeSource);
        LinkExportOptions linkOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();
        List<long> selectedViewIds = (settings.SelectedViewIds == null || settings.SelectedViewIds.Count == 0)
            ? _views.Select(view => view.Id.Value).ToList()
            : settings.SelectedViewIds.Distinct().ToList();
        string outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : settings.OutputDirectory.Trim();

        return new ExporterSettingsPayload
        {
            OutputDirectory = outputDirectory,
            TargetEpsg = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs
                ? settings.TargetEpsg
                : (_coordinateInfo?.ResolvedSourceEpsg ?? ProjectInfo.DefaultTargetEpsg),
            CoordinateMode = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs ? "convert" : "shared",
            OutputFormat = settings.OutputFormat == ExportFormat.Shapefile ? "shapefile" : "geopackage",
            IncrementalExportMode = settings.IncrementalExportMode == IncrementalExportMode.ChangedViewsOnly ? "changed" : "all",
            PackagingMode = settings.PackagingMode switch
            {
                PackagingMode.PerViewGeoPackage => "perView",
                PackagingMode.PerLevelGeoPackage => "perLevel",
                PackagingMode.PerBuildingGeoPackage => "perBuilding",
                _ => "perFeature",
            },
            SelectedViewIds = selectedViewIds,
            IncludeLinkedModels = linkOptions.IncludeLinkedModels,
            SelectedLinkIds = linkOptions.SelectedLinkInstanceIds?.Distinct().ToList() ?? new List<long>(),
            Unit = featureTypes.HasFlag(ExportFeatureType.Unit),
            Detail = featureTypes.HasFlag(ExportFeatureType.Detail),
            Opening = featureTypes.HasFlag(ExportFeatureType.Opening),
            Level = featureTypes.HasFlag(ExportFeatureType.Level),
            Fixture = featureTypes.HasFlag(ExportFeatureType.Fixture),
            GenerateDiagnosticsReport = settings.GenerateDiagnosticsReport,
            GeneratePackageOutput = settings.GeneratePackageOutput,
            IncludePackageLegend = settings.IncludePackageLegend,
            ValidateAfterWrite = settings.ValidateAfterWrite,
            GenerateQgisArtifacts = settings.GenerateQgisArtifacts,
            OpenOutputFolder = settings.PostExportActions?.OpenOutputFolder == true,
            LaunchQgis = settings.PostExportActions?.LaunchQgis == true,
            UnitGeometrySource = geometrySource == UnitGeometrySource.Rooms ? "rooms" : "floors",
            UnitAttributeSource = attributeSource switch
            {
                UnitAttributeSource.Rooms => "rooms",
                UnitAttributeSource.Hybrid => "hybrid",
                _ => "floors",
            },
            RoomCategoryParameterName = string.IsNullOrWhiteSpace(settings.RoomCategoryParameterName)
                ? "Name"
                : settings.RoomCategoryParameterName.Trim(),
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(settings.SchemaProfiles, settings.ActiveSchemaProfileName),
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(settings.ValidationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
            SimplifyStairUnits = settings.SimplifyStairUnits,
            SimplifyEscalatorUnits = settings.SimplifyEscalatorUnits,
            Use3DSectionBoxExport = settings.Use3DSectionBoxExport,
            SectionBoxAboveFloorMeters = settings.SectionBoxAboveFloorMeters > 0d
                ? settings.SectionBoxAboveFloorMeters
                : Temp3DViewScope.DefaultAboveFloorMeters,
            SectionBoxBelowFloorMeters = double.IsNaN(settings.SectionBoxBelowFloorMeters)
                ? Temp3DViewScope.DefaultBelowFloorMeters
                : settings.SectionBoxBelowFloorMeters,
            Keep3DTempViewsForDebug = settings.Keep3DTempViewsForDebug,
            SelectedProfileName = selectedProfileName,
        };
    }

    private ExportDialogSettings ToSettings(ExporterSettingsPayload payload)
    {
        ExportDialogSettings profileBase = ResolveSelectedProfileSettings(payload);
        List<SchemaProfile> schemaProfiles = SchemaProfile.NormalizeProfiles(profileBase.SchemaProfiles)
            .Select(profile => profile.Clone())
            .ToList();
        List<ValidationPolicyProfile> validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(profileBase.ValidationPolicyProfiles)
            .Select(profile => profile.Clone())
            .ToList();

        if (payload.SelectedProfileName == null)
        {
            schemaProfiles = _schemaProfiles.Select(profile => profile.Clone()).ToList();
            validationPolicyProfiles = _validationPolicyProfiles.Select(profile => profile.Clone()).ToList();
        }

        UnitGeometrySource geometrySource = payload.UnitGeometrySource == "rooms"
            ? UnitGeometrySource.Rooms
            : UnitGeometrySource.Floors;
        UnitAttributeSource attributeSource = payload.UnitAttributeSource switch
        {
            "rooms" => UnitAttributeSource.Rooms,
            "hybrid" when geometrySource != UnitGeometrySource.Rooms => UnitAttributeSource.Hybrid,
            _ => UnitAttributeSource.Floors,
        };
        UnitSource unitSource = UnitExportSettingsResolver.ToLegacy(geometrySource, attributeSource);

        return new ExportDialogSettings
        {
            OutputDirectory = payload.OutputDirectory?.Trim() ?? string.Empty,
            TargetEpsg = payload.TargetEpsg > 0 ? payload.TargetEpsg : ProjectInfo.DefaultTargetEpsg,
            FeatureTypes = ToFeatureTypes(payload),
            SelectedViewIds = payload.SelectedViewIds?.Distinct().ToList() ?? new List<long>(),
            IncrementalExportMode = payload.IncrementalExportMode == "changed"
                ? IncrementalExportMode.ChangedViewsOnly
                : IncrementalExportMode.AllSelectedViews,
            GenerateDiagnosticsReport = payload.GenerateDiagnosticsReport,
            GeneratePackageOutput = payload.GeneratePackageOutput,
            IncludePackageLegend = payload.IncludePackageLegend,
            PackagingMode = payload.PackagingMode switch
            {
                "perView" => PackagingMode.PerViewGeoPackage,
                "perLevel" => PackagingMode.PerLevelGeoPackage,
                "perBuilding" => PackagingMode.PerBuildingGeoPackage,
                _ => PackagingMode.PerViewPerFeatureFiles,
            },
            ValidateAfterWrite = payload.ValidateAfterWrite,
            GenerateQgisArtifacts = payload.GenerateQgisArtifacts,
            PostExportActions = new PostExportActionOptions
            {
                OpenOutputFolder = payload.OpenOutputFolder,
                LaunchQgis = payload.LaunchQgis,
            },
            GeometryRepairOptions = new GeometryRepairOptions(),
            UiLanguage = _baseSettings.UiLanguage,
            CoordinateMode = payload.CoordinateMode == "convert"
                ? CoordinateExportMode.ConvertToTargetCrs
                : CoordinateExportMode.SharedCoordinates,
            UnitSource = unitSource,
            UnitGeometrySource = geometrySource,
            UnitAttributeSource = attributeSource,
            RoomCategoryParameterName = string.IsNullOrWhiteSpace(payload.RoomCategoryParameterName)
                ? "Name"
                : payload.RoomCategoryParameterName.Trim(),
            LinkExportOptions = new LinkExportOptions
            {
                IncludeLinkedModels = payload.IncludeLinkedModels,
                SelectedLinkInstanceIds = payload.SelectedLinkIds?.Distinct().ToList() ?? new List<long>(),
            },
            SimplifyStairUnits = payload.SimplifyStairUnits,
            SimplifyEscalatorUnits = payload.SimplifyEscalatorUnits,
            Use3DSectionBoxExport = payload.Use3DSectionBoxExport,
            SectionBoxAboveFloorMeters = payload.SectionBoxAboveFloorMeters > 0d
                ? payload.SectionBoxAboveFloorMeters
                : Temp3DViewScope.DefaultAboveFloorMeters,
            SectionBoxBelowFloorMeters = double.IsNaN(payload.SectionBoxBelowFloorMeters)
                ? Temp3DViewScope.DefaultBelowFloorMeters
                : payload.SectionBoxBelowFloorMeters,
            Keep3DTempViewsForDebug = payload.Keep3DTempViewsForDebug,
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(schemaProfiles, payload.ActiveSchemaProfileName),
            ValidationPolicyProfiles = validationPolicyProfiles,
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(validationPolicyProfiles, payload.ActiveValidationPolicyProfileName),
            PreviewBasemapUrlTemplate = _baseSettings.PreviewBasemapUrlTemplate,
            PreviewBasemapAttribution = _baseSettings.PreviewBasemapAttribution,
            OutputFormat = payload.OutputFormat == "shapefile" ? ExportFormat.Shapefile : ExportFormat.GeoPackage,
        };
    }

    private ExportDialogSettings ResolveSelectedProfileSettings(ExporterSettingsPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.SelectedProfileName))
        {
            ExportProfile? profile = _profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, payload.SelectedProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile != null)
            {
                return profile.ToSettings();
            }
        }

        return _baseSettings;
    }

    private ExportDialogResult ToResult(ExporterSettingsPayload payload)
    {
        ExportDialogSettings settings = ToSettings(payload);
        List<ViewPlan> selectedViews = ResolveSelectedViews(settings.SelectedViewIds);
        SchemaProfile activeSchemaProfile = SchemaProfile.ResolveActive(settings.SchemaProfiles, settings.ActiveSchemaProfileName);
        ValidationPolicyProfile activeValidationPolicyProfile = ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles)
            .FirstOrDefault(profile => string.Equals(
                profile.Name,
                ValidationPolicyProfile.ResolveActiveName(settings.ValidationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
                StringComparison.OrdinalIgnoreCase))
            ?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();

        return new ExportDialogResult(
            selectedViews,
            settings.OutputDirectory,
            settings.TargetEpsg,
            settings.FeatureTypes,
            settings.IncrementalExportMode,
            settings.GenerateDiagnosticsReport,
            settings.GeneratePackageOutput,
            settings.IncludePackageLegend,
            settings.PackagingMode,
            settings.ValidateAfterWrite,
            settings.GenerateQgisArtifacts,
            settings.PostExportActions,
            settings.GeometryRepairOptions,
            payload.SelectedProfileName,
            settings.UiLanguage,
            settings.CoordinateMode,
            settings.UnitSource,
            settings.UnitGeometrySource,
            settings.UnitAttributeSource,
            settings.RoomCategoryParameterName,
            settings.SimplifyStairUnits,
            settings.SimplifyEscalatorUnits,
            settings.LinkExportOptions,
            activeSchemaProfile,
            activeValidationPolicyProfile,
            settings.Use3DSectionBoxExport,
            settings.SectionBoxAboveFloorMeters,
            settings.SectionBoxBelowFloorMeters,
            settings.Keep3DTempViewsForDebug)
        {
            OutputFormat = settings.OutputFormat,
        };
    }

    private ExportPreviewRequest ToPreviewRequest(ExporterSettingsPayload payload)
    {
        ExportDialogSettings settings = ToSettings(payload);
        return new ExportPreviewRequest(
            ResolveSelectedViews(settings.SelectedViewIds),
            settings.FeatureTypes,
            new GeometryRepairOptions(),
            settings.UiLanguage,
            settings.CoordinateMode,
            settings.TargetEpsg,
            _coordinateInfo?.ResolvedSourceEpsg,
            _coordinateInfo?.SiteCoordinateSystemId,
            _coordinateInfo?.SiteCoordinateSystemDefinition,
            _coordinateInfo?.SurveyPointSharedCoordinates,
            settings.UnitSource,
            settings.UnitGeometrySource,
            settings.UnitAttributeSource,
            settings.RoomCategoryParameterName,
            settings.LinkExportOptions,
            SchemaProfile.ResolveActive(settings.SchemaProfiles, settings.ActiveSchemaProfileName),
            _baseSettings.PreviewBasemapUrlTemplate,
            _baseSettings.PreviewBasemapAttribution,
            settings.SimplifyStairUnits,
            settings.SimplifyEscalatorUnits,
            settings.Use3DSectionBoxExport,
            settings.SectionBoxAboveFloorMeters,
            settings.SectionBoxBelowFloorMeters,
            settings.Keep3DTempViewsForDebug);
    }

    public PreviewInitialStateResponse PreparePreview(ExporterSettingsPayload payload)
    {
        string? validationError = Validate(payload);
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        _currentPayload = ClonePayload(payload);
        ExportPreviewRequest request = ToPreviewRequest(_currentPayload);
        _previewViews = request.SelectedViews.ToList();
        if (_previewViews.Count == 0)
        {
            throw new InvalidOperationException("Select at least one view.");
        }

        ExportPreviewService previewService = new(
            request.SelectedViews.First().Document,
            request.UnitSource,
            request.UnitGeometrySource,
            request.UnitAttributeSource,
            request.RoomCategoryParameterName,
            request.GeometryRepairOptions,
            request.LinkExportOptions,
            request.ActiveSchemaProfile,
            request.SimplifyStairUnits,
            request.SimplifyEscalatorUnits,
            request.Use3DSectionBoxExport,
            request.SectionBoxAboveFloorMeters,
            request.SectionBoxBelowFloorMeters,
            request.Keep3DTempViewsForDebug);

        _previewController = new ExportPreviewController(request, previewService);

        int warningCount = 0;
        int unassignedFloorTypeCount = 0;
        List<PreviewViewData> assignmentViews = new();
        foreach (ViewPlan view in _previewViews)
        {
            PreviewDisplayViewState displayState = _previewController.LoadView(view);
            assignmentViews.Add(displayState.SourceViewData);
            warningCount += _previewController.GetWarnings().Count;
            unassignedFloorTypeCount += _previewController.GetUnassignedFloors().Count;
        }

        _currentPreviewView = _previewViews[0];
        PreviewViewPayload currentView = WebPreviewPayloadMapper.ToPayload(
            _previewController,
            _previewController.LoadView(_currentPreviewView));
        _previewState = WebPreviewPayloadMapper.BuildInitialState(
            _previewController,
            _previewViews,
            currentView,
            warningCount,
            unassignedFloorTypeCount,
            WebPreviewPayloadMapper.BuildAssignmentSummary(_previewController, assignmentViews));
        return _previewState;
    }

    public PreviewInitialStateResponse GetPreviewInitialState()
    {
        return _previewState ?? PreparePreview(_currentPayload);
    }

    public PreviewViewPayload LoadPreviewView(long viewId)
    {
        ExportPreviewController controller = EnsurePreviewController();
        ViewPlan view = _previewViews.FirstOrDefault(candidate => candidate.Id.Value == viewId) ??
                        _previewViews.FirstOrDefault() ??
                        throw new InvalidOperationException("No preview views are available.");
        _currentPreviewView = view;
        return WebPreviewPayloadMapper.ToPayload(controller, controller.LoadView(view));
    }

    public PreviewViewPayload AssignPreviewCategory(PreviewAssignmentRequest request)
    {
        ExportPreviewController controller = EnsurePreviewController();
        IReadOnlyList<string> floorTypeNames = WebPreviewPayloadMapper.CleanFloorTypeNames(request.FloorTypeNames);
        if (floorTypeNames.Count > 0 && !string.IsNullOrWhiteSpace(request.Category))
        {
            controller.StageCategoryOverride(floorTypeNames, request.Category.Trim());
            RefreshPreviewReadiness();
        }

        return CurrentPreviewPayload();
    }

    public PreviewViewPayload ClearPreviewAssignment(PreviewClearAssignmentRequest request)
    {
        ExportPreviewController controller = EnsurePreviewController();
        IReadOnlyList<string> floorTypeNames = WebPreviewPayloadMapper.CleanFloorTypeNames(request.FloorTypeNames);
        if (floorTypeNames.Count > 0)
        {
            controller.ClearCategoryOverride(floorTypeNames);
            RefreshPreviewReadiness();
        }

        return CurrentPreviewPayload();
    }

    public PreviewViewPayload SavePreviewAssignments()
    {
        ExportPreviewController controller = EnsurePreviewController();
        controller.SavePendingAssignments();
        RefreshPreviewReadiness();
        return CurrentPreviewPayload();
    }

    public PreviewViewPayload DiscardPreviewAssignments()
    {
        ExportPreviewController controller = EnsurePreviewController();
        controller.DiscardPendingAssignments();
        RefreshPreviewReadiness();
        return CurrentPreviewPayload();
    }

    private PreviewViewPayload CurrentPreviewPayload()
    {
        ExportPreviewController controller = EnsurePreviewController();
        if (controller.CurrentDisplayState != null)
        {
            return WebPreviewPayloadMapper.ToPayload(controller, controller.CurrentDisplayState);
        }

        if (_currentPreviewView != null)
        {
            return LoadPreviewView(_currentPreviewView.Id.Value);
        }

        ViewPlan? firstView = _previewViews.FirstOrDefault();
        if (firstView == null)
        {
            throw new InvalidOperationException("No preview views are available.");
        }

        return LoadPreviewView(firstView.Id.Value);
    }

    private void RefreshPreviewReadiness()
    {
        ExportPreviewController controller = EnsurePreviewController();
        if (_previewViews.Count == 0)
        {
            return;
        }

        long currentViewId = _currentPreviewView?.Id.Value ?? _previewViews[0].Id.Value;
        int warningCount = 0;
        int unassignedFloorTypeCount = 0;
        List<PreviewViewData> assignmentViews = new();
        foreach (ViewPlan view in _previewViews)
        {
            PreviewDisplayViewState displayState = controller.LoadView(view);
            assignmentViews.Add(displayState.SourceViewData);
            warningCount += controller.GetWarnings().Count;
            unassignedFloorTypeCount += controller.GetUnassignedFloors().Count;
        }

        PreviewViewPayload currentView = LoadPreviewView(currentViewId);
        _previewState = WebPreviewPayloadMapper.BuildInitialState(
            controller,
            _previewViews,
            currentView,
            warningCount,
            unassignedFloorTypeCount,
            WebPreviewPayloadMapper.BuildAssignmentSummary(controller, assignmentViews));
    }

    private ExportPreviewController EnsurePreviewController()
    {
        return _previewController ?? throw new InvalidOperationException("Preview has not been prepared.");
    }

    public ExporterRunResponse RunExport(ExporterSettingsPayload payload)
    {
        string? validationError = Validate(payload);
        if (validationError != null)
        {
            return new ExporterRunResponse { Success = false, Error = validationError };
        }

        if (_runExportRequested == null)
        {
            return new ExporterRunResponse
            {
                Success = false,
                Error = "Export is not available in this window.",
            };
        }

        _currentPayload = ClonePayload(payload);
        ExportDialogResult request = ToResult(_currentPayload);
        _lastOutputDirectory = request.OutputDirectory;
        _latestProgress = BuildInitialProgress(request.UiLanguage);
        SendProgressEvent();

        try
        {
            _saveSettingsRequested?.Invoke(BuildSettings());
            FloorGeoPackageExportResult result = _runExportRequested(request, UpdateProgress);
            _lastResultPayload = WebExportResultPayloadBuilder.Build(result, request.OutputDirectory, request.UiLanguage);
            return new ExporterRunResponse
            {
                Success = true,
                Result = _lastResultPayload,
            };
        }
        catch (OperationCanceledException)
        {
            return new ExporterRunResponse
            {
                Success = false,
                Error = UiLanguageText.Get(request.UiLanguage, "Command.ExportCancelled", "Export was cancelled. Partial output may have been written to the output directory."),
            };
        }
        catch (Exception ex)
        {
            return new ExporterRunResponse
            {
                Success = false,
                Error = ex.Message,
            };
        }
    }

    public ExecutionProgressInitialStateResponse BuildProgressInitialState()
    {
        UiLanguage language = ToSettings(_currentPayload).UiLanguage;
        return new ExecutionProgressInitialStateResponse
        {
            Language = language == UiLanguage.Japanese ? "japanese" : "english",
            Progress = _latestProgress,
        };
    }

    public ExportResultInitialStateResponse BuildResultInitialState()
    {
        return _lastResultPayload ?? new ExportResultInitialStateResponse
        {
            Language = ToSettings(_currentPayload).UiLanguage == UiLanguage.Japanese ? "japanese" : "english",
            Title = "Export Results",
            Message = "No export has completed in this window.",
            OutputDirectory = _lastOutputDirectory,
            CanOpenOutputDirectory = false,
        };
    }

    public ExecutionActionResponse OpenLastOutputDirectory()
    {
        UiLanguage language = ToSettings(_currentPayload).UiLanguage;
        string outputDirectory = _lastResultPayload?.OutputDirectory ?? _lastOutputDirectory;
        return WebExportResultPayloadBuilder.OpenOutputDirectory(outputDirectory, language);
    }

    private void UpdateProgress(ExportProgressUpdate update)
    {
        if (update is null)
        {
            return;
        }

        int total = Math.Max(1, update.TotalSteps);
        int completed = Math.Max(0, Math.Min(update.CompletedSteps, total));
        _latestProgress = new ExecutionProgressPayload
        {
            StatusText = string.IsNullOrWhiteSpace(update.StatusText)
                ? "Exporting..."
                : update.StatusText,
            CompletedSteps = completed,
            TotalSteps = total,
            IsCancelling = false,
            StartedAtUtc = string.IsNullOrWhiteSpace(_latestProgress.StartedAtUtc)
                ? DateTimeOffset.UtcNow.ToString("O")
                : _latestProgress.StartedAtUtc,
        };

        SendProgressEvent();
        PumpWindow();
    }

    private void SendProgressEvent()
    {
        void Send()
        {
            _bridge.SendEvent("floorplan.execution.progress.updated", _latestProgress);
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke((Action)Send);
            return;
        }

        Send();
    }

    private void PumpWindow()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private static ExecutionProgressPayload BuildInitialProgress(UiLanguage language)
    {
        return new ExecutionProgressPayload
        {
            StatusText = UiLanguageText.Select(language, "Preparing export...", "出力を準備中..."),
            CompletedSteps = 0,
            TotalSteps = 1,
            StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
    }

    private List<ViewPlan> ResolveSelectedViews(IEnumerable<long>? selectedViewIds)
    {
        HashSet<long> selectedIds = new(selectedViewIds ?? Array.Empty<long>());
        return _views
            .Where(view => selectedIds.Contains(view.Id.Value))
            .ToList();
    }

    public ExporterInitialStateResponse SaveProfile(ExporterSaveProfileRequest request)
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        ExportProfileScope scope = FromScopeString(request.Scope);
        ExportDialogSettings settings = ToSettings(request.Settings);
        _saveProfileRequested?.Invoke(scope, name, settings);

        ExportProfile profile = ExportProfile.FromSettings(name, scope, settings);
        int existingIndex = _profiles.FindIndex(candidate =>
            candidate.Scope == scope &&
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _profiles[existingIndex] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }

        _currentPayload = ClonePayload(request.Settings);
        _currentPayload.SelectedProfileName = name;
        return BuildInitialState(_currentPayload);
    }

    public ExporterInitialStateResponse DeleteProfile(ExporterDeleteProfileRequest request)
    {
        ExportProfileScope scope = FromScopeString(request.Scope);
        string name = request.Name?.Trim() ?? string.Empty;
        ExportProfile? profile = _profiles.FirstOrDefault(candidate =>
            candidate.Scope == scope &&
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile != null)
        {
            _deleteProfileRequested?.Invoke(profile);
            _profiles.Remove(profile);
        }

        _currentPayload.SelectedProfileName = null;
        return BuildInitialState(_currentPayload);
    }

    public void Cancel()
    {
        _previewController?.DiscardPendingChangesOnClose();
    }

    private string? Validate(ExporterSettingsPayload payload)
    {
        if (payload.SelectedViewIds == null || payload.SelectedViewIds.Count == 0)
        {
            return "Select at least one view.";
        }

        if (string.IsNullOrWhiteSpace(payload.OutputDirectory))
        {
            return "Select an output folder.";
        }

        if (ToFeatureTypes(payload) == ExportFeatureType.None)
        {
            return "Select at least one feature type.";
        }

        if (payload.TargetEpsg <= 0)
        {
            return "Enter a valid EPSG code.";
        }

        if (payload.CoordinateMode == "convert" && _coordinateInfo?.CanConvert != true)
        {
            return "Conversion requires a recognizable shared/site coordinate system in the current Revit model.";
        }

        return null;
    }

    private static ExportFeatureType ToFeatureTypes(ExporterSettingsPayload payload)
    {
        ExportFeatureType result = ExportFeatureType.None;
        if (payload.Unit) result |= ExportFeatureType.Unit;
        if (payload.Detail) result |= ExportFeatureType.Detail;
        if (payload.Opening) result |= ExportFeatureType.Opening;
        if (payload.Level) result |= ExportFeatureType.Level;
        if (payload.Fixture) result |= ExportFeatureType.Fixture;
        return result;
    }

    private string BuildCoordinateStatus()
    {
        if (_coordinateInfo == null)
        {
            return "No coordinate information";
        }

        string status = _coordinateInfo.CanConvert
            ? $"Source EPSG:{_coordinateInfo.ResolvedSourceEpsg?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"
            : "Shared coordinates";

        // Fold the friendly coordinate-system name into the one-line status; the verbose
        // WKT/XML definition is surfaced separately via BuildCoordinateDetail().
        return string.IsNullOrWhiteSpace(_coordinateInfo.SiteCoordinateSystemId)
            ? status
            : $"{status} · {_coordinateInfo.SiteCoordinateSystemId}";
    }

    private string BuildCoordinateDetail()
    {
        // Only the raw coordinate-system definition (WKT/XML); the UI shows it collapsed.
        return _coordinateInfo?.SiteCoordinateSystemDefinition ?? string.Empty;
    }

    private static ExporterSettingsPayload ClonePayload(ExporterSettingsPayload payload)
    {
        return new ExporterSettingsPayload
        {
            OutputDirectory = payload.OutputDirectory,
            TargetEpsg = payload.TargetEpsg,
            CoordinateMode = payload.CoordinateMode,
            OutputFormat = payload.OutputFormat,
            IncrementalExportMode = payload.IncrementalExportMode,
            PackagingMode = payload.PackagingMode,
            SelectedViewIds = payload.SelectedViewIds?.ToList() ?? new List<long>(),
            SelectedLinkIds = payload.SelectedLinkIds?.ToList() ?? new List<long>(),
            IncludeLinkedModels = payload.IncludeLinkedModels,
            Unit = payload.Unit,
            Detail = payload.Detail,
            Opening = payload.Opening,
            Level = payload.Level,
            Fixture = payload.Fixture,
            GenerateDiagnosticsReport = payload.GenerateDiagnosticsReport,
            GeneratePackageOutput = payload.GeneratePackageOutput,
            IncludePackageLegend = payload.IncludePackageLegend,
            ValidateAfterWrite = payload.ValidateAfterWrite,
            GenerateQgisArtifacts = payload.GenerateQgisArtifacts,
            OpenOutputFolder = payload.OpenOutputFolder,
            LaunchQgis = payload.LaunchQgis,
            UnitGeometrySource = payload.UnitGeometrySource,
            UnitAttributeSource = payload.UnitAttributeSource,
            RoomCategoryParameterName = payload.RoomCategoryParameterName,
            ActiveSchemaProfileName = payload.ActiveSchemaProfileName,
            ActiveValidationPolicyProfileName = payload.ActiveValidationPolicyProfileName,
            SimplifyStairUnits = payload.SimplifyStairUnits,
            SimplifyEscalatorUnits = payload.SimplifyEscalatorUnits,
            Use3DSectionBoxExport = payload.Use3DSectionBoxExport,
            SectionBoxAboveFloorMeters = payload.SectionBoxAboveFloorMeters,
            SectionBoxBelowFloorMeters = payload.SectionBoxBelowFloorMeters,
            Keep3DTempViewsForDebug = payload.Keep3DTempViewsForDebug,
            SelectedProfileName = payload.SelectedProfileName,
        };
    }

    private static string ToScopeString(ExportProfileScope scope)
    {
        return scope == ExportProfileScope.Project ? "project" : "global";
    }

    private static ExportProfileScope FromScopeString(string? scope)
    {
        return string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase)
            ? ExportProfileScope.Project
            : ExportProfileScope.Global;
    }

    private static ExportProfile CloneProfile(ExportProfile profile)
    {
        return ExportProfile.FromSettings(profile.Name, profile.Scope, profile.ToSettings());
    }

    private sealed class GetInitialStateHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public GetInitialStateHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildInitialState());
    }

    private sealed class PreparePreviewHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreparePreviewHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preparePreview";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.PreparePreview(PayloadReader.Read<ExporterSettingsPayload>(payload)));
    }

    private sealed class PreviewGetInitialStateHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewGetInitialStateHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.GetPreviewInitialState());
    }

    private sealed class PreviewLoadViewHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewLoadViewHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.loadView";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.LoadPreviewView(PayloadReader.Read<PreviewLoadViewRequest>(payload).ViewId));
    }

    private sealed class PreviewAssignCategoryHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewAssignCategoryHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.assignCategory";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.AssignPreviewCategory(PayloadReader.Read<PreviewAssignmentRequest>(payload)));
    }

    private sealed class PreviewClearAssignmentHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewClearAssignmentHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.clearAssignment";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.ClearPreviewAssignment(PayloadReader.Read<PreviewClearAssignmentRequest>(payload)));
    }

    private sealed class PreviewSaveAssignmentsHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewSaveAssignmentsHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.saveAssignments";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.SavePreviewAssignments());
    }

    private sealed class PreviewDiscardAssignmentsHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public PreviewDiscardAssignmentsHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.preview.discardAssignments";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.DiscardPreviewAssignments());
    }

    private sealed class RunExportHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public RunExportHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.run";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.RunExport(PayloadReader.Read<ExporterSettingsPayload>(payload)));
    }

    private sealed class ProgressInitialStateHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public ProgressInitialStateHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.execution.progress.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildProgressInitialState());
    }

    private sealed class ResultInitialStateHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public ResultInitialStateHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.execution.result.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildResultInitialState());
    }

    private sealed class OpenOutputFolderHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public OpenOutputFolderHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.execution.result.openOutputFolder";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.OpenLastOutputDirectory());
    }

    private sealed class SaveProfileHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public SaveProfileHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.saveProfile";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.SaveProfile(PayloadReader.Read<ExporterSaveProfileRequest>(payload)));
    }

    private sealed class DeleteProfileHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public DeleteProfileHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.deleteProfile";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.DeleteProfile(PayloadReader.Read<ExporterDeleteProfileRequest>(payload)));
    }

    private sealed class CancelHandler : IRpcHandler
    {
        private readonly FloorPlanExportSession _dialog;

        public CancelHandler(FloorPlanExportSession dialog) => _dialog = dialog;

        public string Method => "floorplan.cancel";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.Cancel();
            return Task.FromResult<object?>(null);
        }
    }

    private static class PayloadReader
    {
        public static T Read<T>(object? payload) where T : new()
        {
            if (payload is Newtonsoft.Json.Linq.JObject jobj)
            {
                return jobj.ToObject<T>() ?? new T();
            }

            if (payload == null)
            {
                return new T();
            }

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json) ?? new T();
        }
    }
}
