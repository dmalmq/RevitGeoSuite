using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ProjectBasePointMoveService : IProjectBasePointMoveService
{
    private const double MetersToFeet = 1.0d / 0.3048d;
    private const double FeetToMeters = 0.3048d;
    private readonly IModuleStateStore moduleStateStore;

    public ProjectBasePointMoveService(IModuleStateStore? moduleStateStore = null)
    {
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public ProjectBasePointMovePreview CreatePreview(IDocumentHandle document, WorkingProjectBasePointReference targetReference)
    {
        if (targetReference is null)
        {
            throw new ArgumentNullException(nameof(targetReference));
        }

        if (!targetReference.IsValid)
        {
            throw new InvalidOperationException("A valid Working Project Base Point reference is required before moving the actual Revit Project Base Point.");
        }

        RevitDocumentHandle handle = RequireHandle(document);
        Document revitDocument = handle.Document;
        EnsureSupportedDocument(revitDocument);

        BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(revitDocument);
        ProjectLocation projectLocation = revitDocument.ActiveProjectLocation
            ?? throw new InvalidOperationException("The active Revit document does not expose a writable project location.");

        XYZ currentPoint = projectBasePoint.Position;
        ProjectPosition currentSharedPosition = projectLocation.GetProjectPosition(currentPoint);
        ProjectPosition plusX = projectLocation.GetProjectPosition(new XYZ(currentPoint.X + 1d, currentPoint.Y, currentPoint.Z));
        ProjectPosition plusY = projectLocation.GetProjectPosition(new XYZ(currentPoint.X, currentPoint.Y + 1d, currentPoint.Z));

        double deltaEastFeet = (targetReference.ProjectedCoordinate!.Value.Easting * MetersToFeet) - currentSharedPosition.EastWest;
        double deltaNorthFeet = (targetReference.ProjectedCoordinate.Value.Northing * MetersToFeet) - currentSharedPosition.NorthSouth;
        bool exceedsPlanMoveLimit = ProjectBasePointMoveMath.ExceedsMaximumSupportedPlanMove(deltaEastFeet, deltaNorthFeet, out double requiredPlanMoveFeet);

        if (!ProjectBasePointMoveMath.TrySolvePlanOffset(
                deltaEastFeet,
                deltaNorthFeet,
                plusX.EastWest - currentSharedPosition.EastWest,
                plusX.NorthSouth - currentSharedPosition.NorthSouth,
                plusY.EastWest - currentSharedPosition.EastWest,
                plusY.NorthSouth - currentSharedPosition.NorthSouth,
                out double deltaXFeet,
                out double deltaYFeet))
        {
            throw new InvalidOperationException("The current project location basis could not be inverted to move the actual Project Base Point.");
        }

        XYZ proposedPoint = new XYZ(currentPoint.X + deltaXFeet, currentPoint.Y + deltaYFeet, currentPoint.Z);
        ProjectPosition proposedSharedPosition = projectLocation.GetProjectPosition(proposedPoint);

        bool hasMeaningfulCurrentSetup = Math.Abs(currentPoint.X) > 1e-6d
            || Math.Abs(currentPoint.Y) > 1e-6d
            || Math.Abs(currentSharedPosition.EastWest) > 1e-6d
            || Math.Abs(currentSharedPosition.NorthSouth) > 1e-6d;
        bool isNoOp = Math.Abs(deltaEastFeet) < 0.0328084d && Math.Abs(deltaNorthFeet) < 0.0328084d;

        return new ProjectBasePointMovePreview
        {
            TargetReference = targetReference,
            CurrentLocalXFeet = currentPoint.X,
            CurrentLocalYFeet = currentPoint.Y,
            CurrentLocalZFeet = currentPoint.Z,
            ProposedLocalXFeet = proposedPoint.X,
            ProposedLocalYFeet = proposedPoint.Y,
            ProposedLocalZFeet = proposedPoint.Z,
            CurrentSharedEastWestFeet = currentSharedPosition.EastWest,
            CurrentSharedNorthSouthFeet = currentSharedPosition.NorthSouth,
            CurrentSharedElevationFeet = currentSharedPosition.Elevation,
            ProposedSharedEastWestFeet = proposedSharedPosition.EastWest,
            ProposedSharedNorthSouthFeet = proposedSharedPosition.NorthSouth,
            ProposedSharedElevationFeet = proposedSharedPosition.Elevation,
            DeltaXFeet = deltaXFeet,
            DeltaYFeet = deltaYFeet,
            RequiredPlanMoveFeet = requiredPlanMoveFeet,
            ExceedsPlanMoveLimit = exceedsPlanMoveLimit,
            RequiresOverwriteWarning = hasMeaningfulCurrentSetup,
            IsNoOp = isNoOp,
            WarningMessage = hasMeaningfulCurrentSetup
                ? "The current Project Base Point already has a meaningful local setup. This advanced local-only alignment will overwrite that actual Project Base Point position while keeping survey/shared coordinates fixed."
                : string.Empty,
            BlockingMessage = exceedsPlanMoveLimit ? BuildPlanMoveLimitMessage(requiredPlanMoveFeet) : string.Empty
        };
    }

    public ProjectBasePointMoveResult MoveProjectBasePoint(IDocumentHandle document, ProjectBasePointMovePreview preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (preview.TargetReference?.IsValid != true)
        {
            throw new InvalidOperationException("A valid Project Base Point move preview is required before applying the advanced move.");
        }

        if (preview.ExceedsPlanMoveLimit)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(preview.BlockingMessage)
                ? BuildPlanMoveLimitMessage(preview.RequiredPlanMoveFeet)
                : preview.BlockingMessage);
        }

        RevitDocumentHandle handle = RequireHandle(document);
        Document revitDocument = handle.Document;
        EnsureSupportedDocument(revitDocument);

        using Transaction transaction = new Transaction(revitDocument, "Move Project Base Point");
        transaction.Start();

        try
        {
            BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(revitDocument);

            bool wasPinned = projectBasePoint.Pinned;
            if (wasPinned)
            {
                projectBasePoint.Pinned = false;
            }

            SetBasePointParameter(projectBasePoint, BuiltInParameter.BASEPOINT_EASTWEST_PARAM, preview.ProposedLocalXFeet, "E/W");
            SetBasePointParameter(projectBasePoint, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM, preview.ProposedLocalYFeet, "N/S");
            SetBasePointParameter(projectBasePoint, BuiltInParameter.BASEPOINT_ELEVATION_PARAM, preview.ProposedLocalZFeet, "Elevation");

            if (wasPinned)
            {
                projectBasePoint.Pinned = true;
            }

            SaveGeoreferenceModuleState(handle, preview.TargetReference);

            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the Project Base Point move transaction.");
            }
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw new InvalidOperationException("Project Base Point move failed. Revit rolled back the transaction. " + ex.Message, ex);
        }

        return new ProjectBasePointMoveResult
        {
            Preview = preview,
            Summary = $"Aligned the actual Project Base Point locally to X {preview.ProposedLocalXFeet:F3} ft, Y {preview.ProposedLocalYFeet:F3} ft while preserving elevation Z {preview.ProposedLocalZFeet:F3} ft. Survey/shared coordinates remained fixed, and the saved Working Project Base Point remains available for suite workflows."
        };
    }

    private void SaveGeoreferenceModuleState(IDocumentHandle document, WorkingProjectBasePointReference workingProjectBasePoint)
    {
        GeoreferenceModuleState state = moduleStateStore.Load<GeoreferenceModuleState>(document, ModuleStateIds.Georeference)
            ?? new GeoreferenceModuleState();
        state.WorkingProjectBasePoint = workingProjectBasePoint;
        state.LastUpdatedUtc = DateTime.UtcNow;
        moduleStateStore.Save(document, ModuleStateIds.Georeference, state);
    }

    private static string BuildPlanMoveLimitMessage(double requiredPlanMoveFeet)
    {
        double requiredPlanMoveKilometers = (requiredPlanMoveFeet * FeetToMeters) / 1000d;
        double limitKilometers = (ProjectBasePointMoveMath.MaximumSupportedPlanMoveFeet * FeetToMeters) / 1000d;
        return $"The captured Working Project Base Point would require the actual Revit Project Base Point to move about {requiredPlanMoveKilometers:F1} km in plan. Revit limits the actual Project Base Point to roughly {limitKilometers:F1} km from its local startup area. Keep the saved Working Project Base Point for PLATEAU and export workflows instead of moving the actual Revit Project Base Point.";
    }

    private static RevitDocumentHandle RequireHandle(IDocumentHandle document)
    {
        return document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Project Base Point move requires a RevitDocumentHandle.");
    }

    private static void SetBasePointParameter(BasePoint projectBasePoint, BuiltInParameter parameterId, double valueFeet, string label)
    {
        Parameter parameter = projectBasePoint.get_Parameter(parameterId)
            ?? throw new InvalidOperationException($"The Revit Project Base Point does not expose a writable {label} parameter.");

        if (parameter.IsReadOnly)
        {
            throw new InvalidOperationException($"The Revit Project Base Point {label} parameter is read-only in this model.");
        }

        if (!parameter.Set(valueFeet))
        {
            throw new InvalidOperationException($"Revit rejected the Project Base Point {label} value.");
        }
    }

    private static void EnsureSupportedDocument(Document document)
    {
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Family documents are not supported by the Project Base Point move workflow.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. Moving the actual Project Base Point requires an editable project.");
        }

        if (document.IsModifiable)
        {
            throw new InvalidOperationException("Another Revit transaction is already active. Finish it before moving the actual Project Base Point.");
        }
    }
}
