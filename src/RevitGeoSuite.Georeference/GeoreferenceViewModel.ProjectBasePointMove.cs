using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.Georeference;

public sealed partial class GeoreferenceViewModel
{
    private const double ActualProjectBasePointMoveToleranceMeters = 0.01d;

    public bool HasEstablishedProjectBasePointMoveContext => CurrentState.HasStoredGeoInfo || CurrentState.ExistingSetupDetected;

    public bool CanShowActualProjectBasePointMoveSection => HasEstablishedProjectBasePointMoveContext && !IsSplitWorkflowMode;

    public bool HasActualProjectBasePointMoveTarget => WorkingProjectBasePoint is not null;

    public bool HasMeaningfulCurrentProjectBasePointSetup => Math.Abs(CurrentState.ProjectBasePoint.XFeet) > 1e-6d
        || Math.Abs(CurrentState.ProjectBasePoint.YFeet) > 1e-6d
        || CurrentState.ProjectBasePoint.HasSharedPosition
        || CurrentState.ProjectBasePoint.HasEstimatedLocation;

    public double ActualProjectBasePointMoveRequiredPlanDistanceFeet
    {
        get
        {
            if (WorkingProjectBasePoint is null || !CurrentState.ProjectBasePoint.HasSharedPosition)
            {
                return 0d;
            }

            double deltaEastFeet = (WorkingProjectBasePoint.ProjectedCoordinate.Easting / 0.3048d) - CurrentState.ProjectBasePoint.SharedEastWestFeet!.Value;
            double deltaNorthFeet = (WorkingProjectBasePoint.ProjectedCoordinate.Northing / 0.3048d) - CurrentState.ProjectBasePoint.SharedNorthSouthFeet!.Value;
            return ProjectBasePointMoveMath.CalculatePlanDistance(deltaEastFeet, deltaNorthFeet);
        }
    }

    public double ActualProjectBasePointMoveRequiredPlanDistanceMeters => ActualProjectBasePointMoveRequiredPlanDistanceFeet * 0.3048d;

    public bool DoesActualProjectBasePointMoveExceedDistanceLimit => ActualProjectBasePointMoveRequiredPlanDistanceFeet > ProjectBasePointMoveMath.MaximumSupportedPlanMoveFeet;

    public bool IsActualProjectBasePointMoveNoOp
    {
        get
        {
            if (WorkingProjectBasePoint is null || !CurrentState.ProjectBasePoint.HasSharedPosition)
            {
                return false;
            }

            double toleranceFeet = ActualProjectBasePointMoveToleranceMeters / 0.3048d;
            double deltaEastFeet = (WorkingProjectBasePoint.ProjectedCoordinate.Easting / 0.3048d) - CurrentState.ProjectBasePoint.SharedEastWestFeet!.Value;
            double deltaNorthFeet = (WorkingProjectBasePoint.ProjectedCoordinate.Northing / 0.3048d) - CurrentState.ProjectBasePoint.SharedNorthSouthFeet!.Value;
            return Math.Abs(deltaEastFeet) < toleranceFeet && Math.Abs(deltaNorthFeet) < toleranceFeet;
        }
    }

    public bool CanMoveActualProjectBasePoint => CurrentState.IsSupportedDocument
        && !CurrentState.IsReadOnly
        && HasEstablishedProjectBasePointMoveContext
        && HasActualProjectBasePointMoveTarget
        && CurrentState.ProjectBasePoint.HasSharedPosition
        && !DoesActualProjectBasePointMoveExceedDistanceLimit
        && !IsActualProjectBasePointMoveNoOp;

    public bool HasActualProjectBasePointMoveWarningMessage => !string.IsNullOrWhiteSpace(ActualProjectBasePointMoveWarningMessage);

    public string ActualProjectBasePointMoveWarningMessage => HasMeaningfulCurrentProjectBasePointSetup
        ? "The current Project Base Point already has a meaningful local setup. This advanced local-only alignment will overwrite that actual Project Base Point position while keeping survey/shared coordinates fixed."
        : string.Empty;

    public string ActualProjectBasePointMoveStatusMessage
    {
        get
        {
            if (!HasEstablishedProjectBasePointMoveContext)
            {
                return "Complete and save the normal georeference setup first, then use this advanced Project Base Point alignment.";
            }

            if (CurrentState.IsReadOnly)
            {
                return "This document is read-only. The actual Project Base Point cannot be moved until the model is editable.";
            }

            if (!HasActualProjectBasePointMoveTarget)
            {
                return "Capture a Working Project Base Point in this run to enable the advanced Project Base Point alignment.";
            }

            if (!CurrentState.ProjectBasePoint.HasSharedPosition)
            {
                return "The current Project Base Point shared coordinates are not readable yet, so the advanced alignment cannot be previewed safely.";
            }

            if (DoesActualProjectBasePointMoveExceedDistanceLimit)
            {
                double requiredKilometers = ActualProjectBasePointMoveRequiredPlanDistanceMeters / 1000d;
                double limitKilometers = (ProjectBasePointMoveMath.MaximumSupportedPlanMoveFeet * 0.3048d) / 1000d;
                return $"The captured Working Project Base Point is about {requiredKilometers:F1} km away in plan. Revit only allows the actual Project Base Point to stay within roughly {limitKilometers:F1} km of its local startup area. Keep the saved Working Project Base Point for PLATEAU and export workflows instead of moving the actual Project Base Point.";
            }

            if (IsActualProjectBasePointMoveNoOp)
            {
                return "The current actual Project Base Point already matches the captured working point within tolerance.";
            }

            return "This advanced local-only alignment updates the actual Revit Project Base Point when the captured working point is still within Revit's local distance limit. Survey/shared coordinates stay fixed, and the current Project Base Point elevation is preserved.";
        }
    }

    public IEnumerable<SummaryRow> ActualProjectBasePointMoveRows => BuildActualProjectBasePointMoveRows();

    public WorkingProjectBasePointReference GetActualProjectBasePointMoveTarget()
    {
        if (SelectedCrs is null || WorkingProjectBasePoint is null)
        {
            throw new InvalidOperationException("Capture a Working Project Base Point in the selected CRS before aligning the actual Revit Project Base Point.");
        }

        return new WorkingProjectBasePointReference
        {
            ProjectCrs = SelectedCrs.ToReference(),
            Origin = new RevitGeoSuite.Core.ProjectMetadata.ProjectOrigin
            {
                Latitude = WorkingProjectBasePoint.Latitude,
                Longitude = WorkingProjectBasePoint.Longitude,
                ElevationMeters = 0d
            },
            ProjectedCoordinate = WorkingProjectBasePoint.ProjectedCoordinate,
            Confidence = WorkingProjectBasePoint.ConfidenceLevel,
            SetupSource = WorkingProjectBasePoint.SourceLabel
        };
    }

    private IEnumerable<SummaryRow> BuildActualProjectBasePointMoveRows()
    {
        yield return new SummaryRow("Current Project Base Point", FormatPoint(CurrentState.ProjectBasePoint));

        if (CurrentState.ProjectBasePoint.HasSharedPosition)
        {
            yield return new SummaryRow(
                "Current Shared Position",
                $"E {CurrentState.ProjectBasePoint.SharedEastWestFeet!.Value * 0.3048:F3} m, N {CurrentState.ProjectBasePoint.SharedNorthSouthFeet!.Value * 0.3048:F3} m");
        }

        if (WorkingProjectBasePoint is not null && SelectedCrs is not null)
        {
            yield return new SummaryRow("Captured Move Target", $"Lat {WorkingProjectBasePoint.Latitude:F6}, Lon {WorkingProjectBasePoint.Longitude:F6}");
            yield return new SummaryRow(
                "Target Projected Position",
                $"EPSG:{SelectedCrs.EpsgCode} / E {WorkingProjectBasePoint.ProjectedCoordinate.Easting:F3} m, N {WorkingProjectBasePoint.ProjectedCoordinate.Northing:F3} m");
        }
        else
        {
            yield return new SummaryRow("Captured Move Target", "Capture a Working Project Base Point first");
        }

        if (CurrentState.ProjectBasePoint.HasSharedPosition && WorkingProjectBasePoint is not null)
        {
            yield return new SummaryRow("Required Local Plan Move", $"{ActualProjectBasePointMoveRequiredPlanDistanceMeters:F1} m");
            yield return new SummaryRow(
                "Revit Local Limit",
                $"Approx. {(ProjectBasePointMoveMath.MaximumSupportedPlanMoveFeet * 0.3048d) / 1000d:F1} km from the local startup area");
            yield return new SummaryRow(
                "Move Feasibility",
                DoesActualProjectBasePointMoveExceedDistanceLimit ? "Exceeds Revit actual Project Base Point limit" : "Within Revit actual Project Base Point limit");
        }

        yield return new SummaryRow("Shared Coordinate Behavior", "Survey Point, Project Location, and True North stay unchanged");
        yield return new SummaryRow("Elevation Behavior", $"Keep current Project Base Point elevation Z {CurrentState.ProjectBasePoint.ZFeet:F3} ft");
        yield return new SummaryRow("Workflow Guidance", "For far-away real-world targets, keep using the saved Working Project Base Point for PLATEAU and export workflows.");
    }
}

