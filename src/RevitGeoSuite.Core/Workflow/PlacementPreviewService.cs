using System;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementPreviewService
{
    private readonly CoordinateValidator coordinateValidator;
    private readonly PlacementIntentValidator intentValidator;

    public PlacementPreviewService(CoordinateValidator coordinateValidator, PlacementIntentValidator? intentValidator = null)
    {
        this.coordinateValidator = coordinateValidator ?? throw new ArgumentNullException(nameof(coordinateValidator));
        this.intentValidator = intentValidator ?? new PlacementIntentValidator();
    }

    public PlacementPreview CreatePreview(PlacementCurrentState currentState, PlacementIntent intent)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        WorkingProjectBasePointReference? proposedWorkingProjectBasePoint = WorkingProjectBasePointResolver.Resolve(intent);
        PlacementPreview preview = new PlacementPreview();
        PlacementIntentValidationResult validationResult = intentValidator.Validate(intent);
        foreach (string error in validationResult.Errors)
        {
            preview.Warnings.Add(error);
        }

        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "CRS",
            CurrentValue = FormatCrs(currentState.CurrentCrs),
            ProposedValue = FormatCrs(intent.SelectedCrs)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Origin",
            CurrentValue = FormatOrigin(currentState.CurrentOrigin),
            ProposedValue = FormatOrigin(intent.SelectedOrigin)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Projected Coordinate",
            CurrentValue = "Not derived from the current project state",
            ProposedValue = FormatProjectedCoordinate(intent.SelectedProjectedCoordinate)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "True North Angle",
            CurrentValue = FormatAngle(currentState.CurrentTrueNorthAngleDegrees),
            ProposedValue = FormatAngle(ResolveProposedAngle(currentState, intent))
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Apply Mode",
            CurrentValue = "No pending change",
            ProposedValue = FormatApplyMode(intent.ApplyMode)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Anchor Target",
            CurrentValue = "Not captured",
            ProposedValue = FormatAnchorTarget(intent.AnchorTarget, includeDefaultLabel: true)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Working Project Base Point",
            CurrentValue = FormatWorkingProjectBasePoint(currentState.CurrentWorkingProjectBasePoint),
            ProposedValue = FormatWorkingProjectBasePoint(proposedWorkingProjectBasePoint)
        });
        preview.Fields.Add(new PlacementPreviewField
        {
            Label = "Confidence / Source",
            CurrentValue = FormatConfidenceAndSource(currentState.CurrentConfidence, currentState.CurrentSetupSource),
            ProposedValue = FormatConfidenceAndSource(intent.Confidence, intent.SetupSource)
        });

        if (!currentState.HasStoredGeoMetadata)
        {
            preview.Warnings.Add("Current canonical geo metadata is not stored yet. Current values are inferred from the active Revit location state.");
        }

        if (currentState.ExistingSetupDetected)
        {
            preview.Warnings.Add("Existing coordinate setup was detected. Review the preview carefully before apply overwrites it.");
        }

        if (!string.IsNullOrWhiteSpace(currentState.CurrentOriginSource))
        {
            preview.Warnings.Add($"Current origin is based on: {currentState.CurrentOriginSource}.");
        }

        GeoProjectInfo candidateInfo = new GeoProjectInfo
        {
            ProjectCrs = intent.SelectedCrs,
            Origin = intent.SelectedOrigin,
            TrueNorthAngle = ResolveProposedAngle(currentState, intent),
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource
        };

        foreach (ValidationResult result in coordinateValidator.Validate(candidateInfo))
        {
            preview.Warnings.Add(result.Message);
        }

        PopulateChangeStatements(preview, currentState, intent, proposedWorkingProjectBasePoint);
        preview.PersistenceSummary = BuildPersistenceSummary(intent, proposedWorkingProjectBasePoint);
        preview.ChangeImpactSummary = BuildImpactSummary(intent, proposedWorkingProjectBasePoint);
        preview.ConfidenceSummary = BuildConfidenceSummary(intent, proposedWorkingProjectBasePoint);
        preview.IsReadyToApply = validationResult.IsValid;
        return preview;
    }

    private static void PopulateChangeStatements(
        PlacementPreview preview,
        PlacementCurrentState currentState,
        PlacementIntent intent,
        WorkingProjectBasePointReference? proposedWorkingProjectBasePoint)
    {
        preview.WhatWillChange.Add($"Canonical metadata will use {FormatCrs(intent.SelectedCrs)} and {FormatOrigin(intent.SelectedOrigin)}.");
        preview.WhatWillChange.Add($"Projected placement target will use {FormatProjectedCoordinate(intent.SelectedProjectedCoordinate)}.");
        preview.WhatWillChange.Add($"Confidence and provenance will be recorded as {FormatConfidenceAndSource(intent.Confidence, intent.SetupSource)}.");
        preview.WhatWillChange.Add($"The apply step will interpret the selected coordinates as the {FormatAnchorTarget(intent.AnchorTarget)} anchor.");

        if (proposedWorkingProjectBasePoint is not null)
        {
            preview.WhatWillChange.Add($"A working Project Base Point reference will be saved for later suite workflows: {FormatWorkingProjectBasePoint(proposedWorkingProjectBasePoint)}.");
        }
        else if (currentState.CurrentWorkingProjectBasePoint is not null)
        {
            preview.WhatWillNotChange.Add("The previously saved working Project Base Point reference will remain unchanged.");
        }
        else
        {
            preview.WhatWillNotChange.Add("No separate working Project Base Point reference will be stored in this run.");
        }

        switch (intent.ApplyMode)
        {
            case PlacementApplyMode.MetadataOnly:
                preview.WhatWillChange.Add("Only shared geo metadata will change.");
                preview.WhatWillNotChange.Add("Project location offsets in Revit will remain unchanged.");
                preview.WhatWillNotChange.Add("True north angle will remain unchanged.");
                break;
            case PlacementApplyMode.ProjectLocation:
                preview.WhatWillChange.Add("Project location offsets, anchor coordinates, and elevation will update during apply.");
                preview.WhatWillNotChange.Add("True north angle will remain at the current value.");
                break;
            case PlacementApplyMode.ProjectLocationAndAngle:
                preview.WhatWillChange.Add("Project location offsets, anchor coordinates, elevation, and true north angle will update during apply.");
                preview.WhatWillChange.Add($"True north angle preview target: {FormatAngle(ResolveProposedAngle(currentState, intent))}.");
                preview.WhatWillNotChange.Add("Building geometry rotation is still out of scope for V1.");
                break;
        }

        if (intent.ApplyMode != PlacementApplyMode.ProjectLocationAndAngle)
        {
            preview.WhatWillNotChange.Add("Building geometry rotation will not be changed.");
        }
    }

    private static double ResolveProposedAngle(PlacementCurrentState currentState, PlacementIntent intent)
    {
        return intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle
            ? intent.TrueNorthAngle ?? currentState.CurrentTrueNorthAngleDegrees
            : currentState.CurrentTrueNorthAngleDegrees;
    }

    private static string BuildPersistenceSummary(PlacementIntent intent, WorkingProjectBasePointReference? proposedWorkingProjectBasePoint)
    {
        string summary = $"Apply will persist EPSG:{intent.SelectedCrs?.EpsgCode.ToString() ?? "unknown"}, the selected geographic origin, true north angle, confidence {intent.Confidence}, setup source, and timestamp in GeoProjectInfo. Anchor target remains workflow-only and is captured in the audit log rather than GeoProjectInfo.";
        if (proposedWorkingProjectBasePoint is not null)
        {
            summary += " The working Project Base Point reference will be saved in georeference module state for later suite workflows.";
        }

        return summary;
    }

    private static string BuildImpactSummary(PlacementIntent intent, WorkingProjectBasePointReference? proposedWorkingProjectBasePoint)
    {
        string impact = intent.ApplyMode switch
        {
            PlacementApplyMode.MetadataOnly => "Apply will save shared geo metadata only. No Revit project location values change in this mode.",
            PlacementApplyMode.ProjectLocation => $"Apply will update the {FormatAnchorTarget(intent.AnchorTarget)} coordinates and project location values without changing true north.",
            PlacementApplyMode.ProjectLocationAndAngle => $"Apply will update the {FormatAnchorTarget(intent.AnchorTarget)} coordinates, project location values, and true north. Geometry rotation is still excluded.",
            _ => string.Empty
        };

        if (proposedWorkingProjectBasePoint is not null)
        {
            impact += " The same transaction will also save the working Project Base Point reference for later suite workflows.";
        }

        return impact;
    }

    private static string BuildConfidenceSummary(PlacementIntent intent, WorkingProjectBasePointReference? proposedWorkingProjectBasePoint)
    {
        string summary = $"Primary anchor: {intent.Confidence} / {intent.SetupSource}";
        if (proposedWorkingProjectBasePoint is not null)
        {
            summary += $" Working Project Base Point: {proposedWorkingProjectBasePoint.Confidence} / {proposedWorkingProjectBasePoint.SetupSource}";
        }

        return summary;
    }

    private static string FormatCrs(Coordinates.CrsReference? crs)
    {
        return crs is null
            ? "Not stored"
            : $"EPSG:{crs.EpsgCode}  {crs.NameSnapshot}";
    }

    private static string FormatOrigin(ProjectOrigin? origin)
    {
        return origin is null
            ? "Unknown"
            : $"Lat {origin.Latitude:F6}, Lon {origin.Longitude:F6}, Elev {origin.ElevationMeters:F3} m";
    }

    private static string FormatProjectedCoordinate(Coordinates.ProjectedCoordinate? projectedCoordinate)
    {
        return projectedCoordinate.HasValue
            ? $"E {projectedCoordinate.Value.Easting:F4} m, N {projectedCoordinate.Value.Northing:F4} m"
            : "Not captured";
    }

    private static string FormatWorkingProjectBasePoint(WorkingProjectBasePointReference? workingProjectBasePoint)
    {
        if (workingProjectBasePoint?.IsValid != true)
        {
            return "Not stored";
        }

        return $"{FormatCrs(workingProjectBasePoint.ProjectCrs)} / {FormatProjectedCoordinate(workingProjectBasePoint.ProjectedCoordinate)} / {FormatOrigin(workingProjectBasePoint.Origin)}";
    }

    private static string FormatAngle(double angleDegrees)
    {
        return $"{angleDegrees:F3}°";
    }

    private static string FormatApplyMode(PlacementApplyMode applyMode)
    {
        return applyMode switch
        {
            PlacementApplyMode.MetadataOnly => "Metadata Only",
            PlacementApplyMode.ProjectLocation => "Project Location",
            PlacementApplyMode.ProjectLocationAndAngle => "Project Location + True North",
            _ => applyMode.ToString()
        };
    }

    private static string FormatAnchorTarget(PlacementAnchorTarget anchorTarget, bool includeDefaultLabel = false)
    {
        PlacementAnchorTarget resolvedTarget = ResolveAnchorTarget(anchorTarget);
        string title = resolvedTarget switch
        {
            PlacementAnchorTarget.SurveyPoint => "Survey Point",
            PlacementAnchorTarget.ProjectBasePoint => "Project Base Point",
            _ => "Survey Point"
        };

        return anchorTarget == PlacementAnchorTarget.Unspecified && includeDefaultLabel
            ? $"{title} (default)"
            : title;
    }

    private static string FormatConfidenceAndSource(GeoConfidenceLevel? confidence, string setupSource)
    {
        string confidenceText = confidence?.ToString() ?? "Unknown";
        string sourceText = string.IsNullOrWhiteSpace(setupSource) ? "No source note" : setupSource;
        return $"{confidenceText} / {sourceText}";
    }

    private static PlacementAnchorTarget ResolveAnchorTarget(PlacementAnchorTarget anchorTarget)
    {
        return anchorTarget == PlacementAnchorTarget.Unspecified
            ? PlacementAnchorTarget.SurveyPoint
            : anchorTarget;
    }
}
