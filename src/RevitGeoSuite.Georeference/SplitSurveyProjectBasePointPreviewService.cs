using System;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

public sealed class SplitSurveyProjectBasePointPreviewService
{
    private readonly CoordinateValidator coordinateValidator;

    public SplitSurveyProjectBasePointPreviewService(CoordinateValidator coordinateValidator)
    {
        this.coordinateValidator = coordinateValidator ?? throw new ArgumentNullException(nameof(coordinateValidator));
    }

    public PlacementPreview CreatePreview(CurrentProjectStateSummary currentState, SplitSurveyProjectBasePointIntent intent)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        PlacementPreview preview = new PlacementPreview();
        double currentSurveyEastMeters = (currentState.SurveyPoint.SharedEastWestFeet ?? 0d) * 0.3048d;
        double currentSurveyNorthMeters = (currentState.SurveyPoint.SharedNorthSouthFeet ?? 0d) * 0.3048d;
        double currentProjectBasePointEastMeters = (currentState.ProjectBasePoint.SharedEastWestFeet ?? 0d) * 0.3048d;
        double currentProjectBasePointNorthMeters = (currentState.ProjectBasePoint.SharedNorthSouthFeet ?? 0d) * 0.3048d;
        double proposedTrueNorthAngle = intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle
            ? intent.TrueNorthAngle ?? currentState.ProjectPosition.AngleDegrees
            : currentState.ProjectPosition.AngleDegrees;

        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Workflow",
            CurrentValue = "Standard shared/project location setup",
            ProposedValue = "Local Project Base Point + Shared Survey"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "CRS",
            CurrentValue = FormatStoredCrs(currentState),
            ProposedValue = FormatCrs(intent)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Shared Survey Target",
            CurrentValue = FormatSharedSurvey(currentState),
            ProposedValue = $"Lat {intent.SharedSurveyOrigin!.Latitude:F6}, Lon {intent.SharedSurveyOrigin.Longitude:F6} / E {intent.SharedSurveyProjectedCoordinate!.Value.Easting:F3} m, N {intent.SharedSurveyProjectedCoordinate.Value.Northing:F3} m"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Actual Project Base Point (local)",
            CurrentValue = FormatLocalPoint(currentState.ProjectBasePoint),
            ProposedValue = $"{FormatLocalPoint(currentState.ProjectBasePoint)} (kept local)"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Actual Project Base Point (shared)",
            CurrentValue = $"E {currentProjectBasePointEastMeters:F3} m, N {currentProjectBasePointNorthMeters:F3} m",
            ProposedValue = $"E {intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting:F3} m, N {intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Working Project Base Point",
            CurrentValue = currentState.StoredWorkingProjectBasePoint?.IsValid == true
                ? $"E {currentState.StoredWorkingProjectBasePoint.ProjectedCoordinate!.Value.Easting:F3} m, N {currentState.StoredWorkingProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m"
                : "Not stored",
            ProposedValue = $"E {intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting:F3} m, N {intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Survey Point (shared)",
            CurrentValue = $"E {currentSurveyEastMeters:F3} m, N {currentSurveyNorthMeters:F3} m",
            ProposedValue = $"E {intent.SharedSurveyProjectedCoordinate!.Value.Easting:F3} m, N {intent.SharedSurveyProjectedCoordinate.Value.Northing:F3} m"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "True North Angle",
            CurrentValue = $"{currentState.ProjectPosition.AngleDegrees:F3}°",
            ProposedValue = $"{proposedTrueNorthAngle:F3}°"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Apply Mode",
            CurrentValue = "No pending change",
            ProposedValue = intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle
                ? "Project Location + True North"
                : "Project Location"
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Confidence / Source",
            CurrentValue = FormatConfidence(currentState.StoredConfidence, currentState.SetupSource),
            ProposedValue = FormatConfidence(intent.Confidence, intent.SetupSource)
        });

        preview.WhatWillChange.Add($"Shared survey coordinates will use {FormatCrs(intent)} and the selected survey origin at E {intent.SharedSurveyProjectedCoordinate!.Value.Easting:F3} m, N {intent.SharedSurveyProjectedCoordinate.Value.Northing:F3} m.");
        preview.WhatWillChange.Add("The actual Revit Survey Point will be repositioned so that it represents the selected shared survey target in the model.");
        preview.WhatWillChange.Add($"The actual Revit Project Base Point will remain in its current local/model position, but its shared coordinates will resolve to E {intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting:F3} m, N {intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m.");
        preview.WhatWillChange.Add("The same transaction will save the local Project Base Point as the suite Working Project Base Point for PLATEAU and export workflows.");

        preview.WhatWillNotChange.Add("The actual Revit Project Base Point local X/Y/Z position will not move in this split workflow.");
        preview.WhatWillNotChange.Add("Building geometry will not be rotated or moved.");
        if (intent.ApplyMode != PlacementApplyMode.ProjectLocationAndAngle)
        {
            preview.WhatWillNotChange.Add("True north will remain unchanged.");
        }

        if (currentState.ExistingSetupDetected)
        {
            preview.Warnings.Add("Existing coordinate setup was detected. Review the split workflow preview carefully before apply overwrites the current shared setup.");
        }

        if (currentState.HasStoredGeoInfo)
        {
            preview.Warnings.Add("Stored suite georeference metadata already exists. This split workflow will replace the canonical stored origin with the selected shared survey target.");
        }

        GeoProjectInfo candidate = new GeoProjectInfo
        {
            ProjectCrs = intent.SelectedCrs,
            Origin = intent.SharedSurveyOrigin,
            TrueNorthAngle = proposedTrueNorthAngle,
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource
        };

        foreach (ValidationResult result in coordinateValidator.Validate(candidate))
        {
            preview.Warnings.Add(result.Message);
        }

        preview.PersistenceSummary = "Apply will persist the selected shared survey origin as the canonical GeoProjectInfo location, save the working/local Project Base Point in georeference module state, and write an audit record describing the split Project Base Point / Survey Point workflow.";
        preview.ChangeImpactSummary = "Apply will keep the actual Revit Project Base Point local near the model, update project location/shared coordinates from that local point, and then reposition the actual Survey Point to the selected shared survey target.";
        preview.ConfidenceSummary = $"Shared survey target: {intent.Confidence} / {intent.SetupSource}. Local Project Base Point: {intent.LocalProjectBasePoint!.Confidence} / {intent.LocalProjectBasePoint.SetupSource}.";
        preview.IsReadyToApply = true;
        return preview;
    }

    private static string FormatStoredCrs(CurrentProjectStateSummary currentState)
    {
        return currentState.StoredCrs is null
            ? "Not stored"
            : $"EPSG:{currentState.StoredCrs.EpsgCode}  {currentState.StoredCrs.NameSnapshot}";
    }

    private static string FormatCrs(SplitSurveyProjectBasePointIntent intent)
    {
        return intent.SelectedCrs is null
            ? "Unknown"
            : $"EPSG:{intent.SelectedCrs.EpsgCode}  {intent.SelectedCrs.NameSnapshot}";
    }

    private static string FormatSharedSurvey(CurrentProjectStateSummary currentState)
    {
        if (currentState.SurveyPoint.HasEstimatedLocation && currentState.SurveyPoint.HasSharedPosition)
        {
            return $"Lat {currentState.SurveyPoint.EstimatedLatitudeDegrees!.Value:F6}, Lon {currentState.SurveyPoint.EstimatedLongitudeDegrees!.Value:F6} / E {(currentState.SurveyPoint.SharedEastWestFeet!.Value * 0.3048d):F3} m, N {(currentState.SurveyPoint.SharedNorthSouthFeet!.Value * 0.3048d):F3} m";
        }

        if (currentState.StoredOrigin is not null)
        {
            return $"Lat {currentState.StoredOrigin.Latitude:F6}, Lon {currentState.StoredOrigin.Longitude:F6}";
        }

        return "Not stored";
    }

    private static string FormatLocalPoint(BasePointSnapshot point)
    {
        return $"X {point.XFeet:F3} ft, Y {point.YFeet:F3} ft, Z {point.ZFeet:F3} ft";
    }

    private static string FormatConfidence(GeoConfidenceLevel? confidence, string setupSource)
    {
        string confidenceText = confidence?.ToString() ?? "Unknown";
        string sourceText = string.IsNullOrWhiteSpace(setupSource) ? "No source note" : setupSource;
        return $"{confidenceText} / {sourceText}";
    }
}
