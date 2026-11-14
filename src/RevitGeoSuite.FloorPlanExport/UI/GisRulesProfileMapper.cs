using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.Validation;
using RevitGeoSuite.FloorPlanExport.Export;

namespace RevitGeoSuite.FloorPlanExport.UI;

/// <summary>
/// Maps between the export dialog's settings and the station's <see cref="GisRulesProfile"/>,
/// which holds only the settings that shape the output. The selected link instances are the host
/// model's, so they go to its gis-model.json instead. What stays per PC (output folder,
/// language, diagnostics and QGIS side outputs, post-export actions, debug views, packaging and
/// output format) is neither written to the station nor taken from it.
/// </summary>
internal static class GisRulesProfileMapper
{
    private static readonly (ExportFeatureType Flag, string Name)[] FeatureNames =
    {
        (ExportFeatureType.Unit, "unit"),
        (ExportFeatureType.Detail, "detail"),
        (ExportFeatureType.Opening, "opening"),
        (ExportFeatureType.Level, "level"),
        (ExportFeatureType.Fixture, "fixture"),
    };

    public static GisRulesProfile FromSettings(string name, ExportDialogSettings settings)
    {
        UnitGeometrySource geometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
        UnitAttributeSource attributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, geometrySource, settings.UnitAttributeSource);
        List<SchemaProfile> schemaProfiles = SchemaProfile.NormalizeProfiles(settings.SchemaProfiles).Select(profile => profile.Clone()).ToList();
        List<ValidationPolicyProfile> policies = ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles).Select(policy => policy.Clone()).ToList();
        return new GisRulesProfile
        {
            Name = name?.Trim() ?? string.Empty,
            TargetEpsg = settings.TargetEpsg > 0 ? settings.TargetEpsg : GisRulesProfile.DefaultTargetEpsg,
            CoordinateMode = settings.CoordinateMode,
            FeatureTypes = ToNames(settings.FeatureTypes),
            UnitGeometrySource = geometrySource,
            UnitAttributeSource = attributeSource,
            RoomCategoryParameter = string.IsNullOrWhiteSpace(settings.RoomCategoryParameterName) ? "Name" : settings.RoomCategoryParameterName.Trim(),
            IncludeLinkedModels = settings.LinkExportOptions?.IncludeLinkedModels ?? false,
            UnitCategories = (settings.UnitCategories ?? new List<string>())
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SimplifyStairUnits = settings.SimplifyStairUnits,
            SimplifyEscalatorUnits = settings.SimplifyEscalatorUnits,
            Use3DSectionBox = settings.Use3DSectionBoxExport,
            SectionBoxAboveFloorMeters = settings.SectionBoxAboveFloorMeters,
            SectionBoxBelowFloorMeters = settings.SectionBoxBelowFloorMeters,
            GeometryRepair = settings.GeometryRepairOptions?.Clone() ?? new Core.Geometry.GeometryRepairOptions(),
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfile = SchemaProfile.ResolveActiveName(schemaProfiles, settings.ActiveSchemaProfileName),
            ValidationPolicies = policies,
            ActiveValidationPolicy = ValidationPolicyProfile.ResolveActiveName(policies, settings.ActiveValidationPolicyProfileName),
        };
    }

    public static GisRulesProfile FromResult(string name, ExportDialogResult request)
    {
        ExportDialogSettings settings = new()
        {
            TargetEpsg = request.TargetEpsg,
            FeatureTypes = request.FeatureTypes,
            CoordinateMode = request.CoordinateMode,
            UnitSource = request.UnitSource,
            UnitGeometrySource = request.UnitGeometrySource,
            UnitAttributeSource = request.UnitAttributeSource,
            RoomCategoryParameterName = request.RoomCategoryParameterName,
            LinkExportOptions = request.LinkExportOptions,
            SimplifyStairUnits = request.SimplifyStairUnits,
            SimplifyEscalatorUnits = request.SimplifyEscalatorUnits,
            UnitCategories = request.UnitCategories.ToList(),
            Use3DSectionBoxExport = request.Use3DSectionBoxExport,
            SectionBoxAboveFloorMeters = request.SectionBoxAboveFloorMeters,
            SectionBoxBelowFloorMeters = request.SectionBoxBelowFloorMeters,
            GeometryRepairOptions = request.GeometryRepairOptions,
            SchemaProfiles = new List<SchemaProfile> { request.ActiveSchemaProfile },
            ActiveSchemaProfileName = request.ActiveSchemaProfile.Name,
            ValidationPolicyProfiles = new List<ValidationPolicyProfile> { request.ActiveValidationPolicyProfile },
            ActiveValidationPolicyProfileName = request.ActiveValidationPolicyProfile.Name,
        };
        return FromSettings(name, settings);
    }

    /// <summary>A profile for the dialog's list: the station's settings, with this PC's own settings at their defaults.</summary>
    public static ExportProfile ToExportProfile(GisRulesProfile profile)
    {
        ExportDialogSettings settings = new();
        ApplyTo(profile, settings);
        return ExportProfile.FromSettings(profile.Name, ExportProfileScope.Project, settings);
    }

    public static void ApplyTo(GisRulesProfile profile, ExportDialogSettings settings)
    {
        settings.TargetEpsg = profile.TargetEpsg;
        settings.CoordinateMode = profile.CoordinateMode;
        settings.FeatureTypes = ToFeatureTypes(profile.FeatureTypes);
        settings.UnitGeometrySource = profile.UnitGeometrySource;
        settings.UnitAttributeSource = profile.UnitAttributeSource;
        settings.UnitSource = UnitExportSettingsResolver.ToLegacy(profile.UnitGeometrySource, profile.UnitAttributeSource);
        settings.RoomCategoryParameterName = profile.RoomCategoryParameter;
        settings.LinkExportOptions = new LinkExportOptions { IncludeLinkedModels = profile.IncludeLinkedModels };
        settings.UnitCategories = profile.UnitCategories.ToList();
        settings.SimplifyStairUnits = profile.SimplifyStairUnits;
        settings.SimplifyEscalatorUnits = profile.SimplifyEscalatorUnits;
        settings.Use3DSectionBoxExport = profile.Use3DSectionBox;
        settings.SectionBoxAboveFloorMeters = profile.SectionBoxAboveFloorMeters;
        settings.SectionBoxBelowFloorMeters = profile.SectionBoxBelowFloorMeters;
        settings.GeometryRepairOptions = profile.GeometryRepair.Clone();
        settings.SchemaProfiles = profile.SchemaProfiles.Select(schema => schema.Clone()).ToList();
        settings.ActiveSchemaProfileName = profile.ActiveSchemaProfile;
        settings.ValidationPolicyProfiles = profile.ValidationPolicies.Select(policy => policy.Clone()).ToList();
        settings.ActiveValidationPolicyProfileName = profile.ActiveValidationPolicy;
    }

    /// <summary>
    /// <paramref name="request"/> with the station profile's settings in place of its own, for an
    /// export into a station. Every selected view is exported, with no incremental baseline. The
    /// linked models stay the request's: their ids belong to this host model, not to the station.
    /// </summary>
    public static ExportDialogResult Overlay(ExportDialogResult request, GisRulesProfile profile)
    {
        ExportDialogSettings settings = new();
        ApplyTo(profile, settings);
        settings.LinkExportOptions.SelectedLinkInstanceIds = request.LinkExportOptions?.SelectedLinkInstanceIds?.Distinct().ToList() ?? new List<long>();
        SchemaProfile schema = SchemaProfile.ResolveActive(settings.SchemaProfiles, settings.ActiveSchemaProfileName);
        ValidationPolicyProfile policy = ValidationPolicyProfile
            .NormalizeProfiles(settings.ValidationPolicyProfiles)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                ValidationPolicyProfile.ResolveActiveName(settings.ValidationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
                StringComparison.OrdinalIgnoreCase))
            ?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
        return new ExportDialogResult(
            request.SelectedViews,
            request.OutputDirectory,
            settings.TargetEpsg,
            settings.FeatureTypes,
            IncrementalExportMode.AllSelectedViews,
            request.GenerateDiagnosticsReport,
            request.GeneratePackageOutput,
            request.IncludePackageLegend,
            request.PackagingMode,
            request.ValidateAfterWrite,
            request.GenerateQgisArtifacts,
            request.PostExportActions,
            settings.GeometryRepairOptions,
            request.SelectedProfileName,
            request.UiLanguage,
            settings.CoordinateMode,
            settings.UnitSource,
            settings.UnitGeometrySource,
            settings.UnitAttributeSource,
            settings.RoomCategoryParameterName,
            settings.SimplifyStairUnits,
            settings.SimplifyEscalatorUnits,
            settings.LinkExportOptions,
            schema,
            policy,
            settings.Use3DSectionBoxExport,
            settings.SectionBoxAboveFloorMeters,
            settings.SectionBoxBelowFloorMeters,
            request.Keep3DTempViewsForDebug,
            settings.UnitCategories)
        {
            OutputFormat = request.OutputFormat,
        };
    }

    private static List<string> ToNames(ExportFeatureType types)
    {
        ExportFeatureType effective = types == ExportFeatureType.None ? ExportFeatureType.All : types;
        return FeatureNames.Where(entry => effective.HasFlag(entry.Flag)).Select(entry => entry.Name).ToList();
    }

    private static ExportFeatureType ToFeatureTypes(IEnumerable<string> names)
    {
        ExportFeatureType types = ExportFeatureType.None;
        foreach (string name in names ?? Array.Empty<string>())
        {
            foreach ((ExportFeatureType flag, string featureName) in FeatureNames)
            {
                if (string.Equals(featureName, name?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    types |= flag;
                }
            }
        }

        return types == ExportFeatureType.None ? ExportFeatureType.All : types;
    }
}
