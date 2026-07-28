using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;

namespace RevitGeoSuite.FloorPlanExport.Commands;

/// <summary>
/// Runs a floor-plan GIS export from a saved <see cref="ExportProfile"/> without any WPF dialogs,
/// so background jobs (the combined "Export to Cesium" flow) can drive it with a plain progress
/// callback. Post-export actions (open folder, launch QGIS) are intentionally not executed here.
/// </summary>
public sealed class FloorPlanHeadlessExporter
{
    private readonly Document _document;
    private readonly UIDocument? _uiDocument;

    public FloorPlanHeadlessExporter(Document document, UIDocument? uiDocument)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _uiDocument = uiDocument;
    }

    /// <param name="outputDirectoryOverride">
    /// Target directory for the artifacts (e.g. the package's <c>gis/</c> folder). Falls back to
    /// the profile's own output directory when null.
    /// </param>
    /// <param name="packagingModeOverride">Optional packaging override (the Cesium flow uses PerBuildingGeoPackage).</param>
    /// <param name="outputFormatOverride">Optional format override (the Cesium flow forces GeoPackage).</param>
    public FloorGeoPackageExportResult Export(
        ExportProfile profile,
        ModelCoordinateInfo coordinateInfo,
        string? outputDirectoryOverride = null,
        PackagingMode? packagingModeOverride = null,
        ExportFormat? outputFormatOverride = null,
        Action<ExportProgressUpdate>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (coordinateInfo is null)
        {
            throw new ArgumentNullException(nameof(coordinateInfo));
        }

        ExportDialogSettings settings = profile.ToSettings();
        string outputDirectory = string.IsNullOrWhiteSpace(outputDirectoryOverride)
            ? settings.OutputDirectory
            : outputDirectoryOverride!.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(
                $"Profile '{profile.Name}' has no output directory and no override was provided.");
        }

        IReadOnlyList<ViewPlan> selectedViews = ResolveSelectedViews(settings.SelectedViewIds);
        if (selectedViews.Count == 0)
        {
            throw new InvalidOperationException(
                $"Profile '{profile.Name}' selects no exportable plan views in the current document.");
        }

        SchemaProfile activeSchemaProfile = SchemaProfile.ResolveActive(
            settings.SchemaProfiles, settings.ActiveSchemaProfileName);
        ValidationPolicyProfile activeValidationPolicyProfile = ValidationPolicyProfile
            .NormalizeProfiles(settings.ValidationPolicyProfiles)
            .FirstOrDefault(policy => string.Equals(
                policy.Name,
                ValidationPolicyProfile.ResolveActiveName(
                    settings.ValidationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
                StringComparison.OrdinalIgnoreCase))
            ?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();

        // Headless runs never open folders or external tools afterwards.
        var postExportActions = new PostExportActionOptions();

        var request = new ExportDialogResult(
            selectedViews,
            outputDirectory,
            settings.TargetEpsg,
            settings.FeatureTypes,
            settings.IncrementalExportMode,
            settings.GenerateDiagnosticsReport,
            settings.GeneratePackageOutput,
            settings.IncludePackageLegend,
            packagingModeOverride ?? settings.PackagingMode,
            settings.ValidateAfterWrite,
            settings.GenerateQgisArtifacts,
            postExportActions,
            settings.GeometryRepairOptions,
            profile.Name,
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
            settings.Keep3DTempViewsForDebug,
            settings.UnitCategories)
        {
            OutputFormat = outputFormatOverride ?? settings.OutputFormat,
        };

        string projectKey = DocumentProjectKeyBuilder.Create(_document);
        var workflow = new ExportWorkflowCoordinator(
            _document,
            _uiDocument,
            projectKey,
            GetAvailableFloorTypeNames(),
            new ExportProfileStore());

        return workflow.RunExportCore(request, coordinateInfo, progressCallback, cancellationToken);
    }

    private IReadOnlyList<ViewPlan> ResolveSelectedViews(IReadOnlyList<long>? selectedViewIds)
    {
        IReadOnlyList<ViewPlan> views = new ViewCollector().GetExportablePlanViews(_document);
        var selectedIds = new HashSet<long>(selectedViewIds ?? (IReadOnlyList<long>)Array.Empty<long>());
        if (selectedIds.Count == 0)
        {
            // A profile without stored views means "everything" for the one-click flow.
            return views;
        }

        return views.Where(view => selectedIds.Contains(view.Id.Value)).ToList();
    }

    private IReadOnlyList<string> GetAvailableFloorTypeNames()
    {
        return new FilteredElementCollector(_document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => type.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
