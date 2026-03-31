using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class RevitGeoPlacementService : IRevitGeoPlacementService
{
    private readonly ProjectLocationWriter projectLocationWriter;
    private readonly IGeoProjectInfoStore geoProjectInfoStore;
    private readonly PlacementAuditStorage placementAuditStorage;
    private readonly IModuleStateStore moduleStateStore;

    public RevitGeoPlacementService(
        ProjectLocationWriter? projectLocationWriter = null,
        IGeoProjectInfoStore? geoProjectInfoStore = null,
        PlacementAuditStorage? placementAuditStorage = null,
        IModuleStateStore? moduleStateStore = null)
    {
        this.projectLocationWriter = projectLocationWriter ?? new ProjectLocationWriter();
        this.geoProjectInfoStore = geoProjectInfoStore ?? new GeoProjectInfoStorage();
        this.placementAuditStorage = placementAuditStorage ?? new PlacementAuditStorage();
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public PlacementApplyResult ApplyPlacement(IDocumentHandle document, PlacementIntent intent)
    {
        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Georeference apply requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;
        EnsureSupportedDocument(revitDocument);

        double resolvedTrueNorthAngle = ResolveTrueNorthAngleDegrees(revitDocument, intent);
        GeoProjectInfo geoProjectInfo = BuildGeoProjectInfo(intent, resolvedTrueNorthAngle);
        PlacementAuditRecord auditRecord = BuildAuditRecord(revitDocument, intent, resolvedTrueNorthAngle);

        using Transaction transaction = new Transaction(revitDocument, "Apply Georeference");
        transaction.Start();

        try
        {
            projectLocationWriter.Apply(revitDocument, intent, resolvedTrueNorthAngle);
            geoProjectInfoStore.Save(handle, geoProjectInfo);
            SaveGeoreferenceModuleState(handle, intent);
            placementAuditStorage.Save(handle, auditRecord);

            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the georeference transaction.");
            }
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw new InvalidOperationException("Georeference apply failed. Revit rolled back the transaction. " + ex.Message, ex);
        }

        return new PlacementApplyResult
        {
            SavedGeoProjectInfo = geoProjectInfo,
            AuditRecord = auditRecord,
            AuditSummary = auditRecord.Summary
        };
    }

    private static void EnsureSupportedDocument(Document document)
    {
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Family documents are not supported by the georeference apply workflow.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. Apply requires an editable project.");
        }

        if (document.IsModifiable)
        {
            throw new InvalidOperationException("Another Revit transaction is already active. Finish it before applying georeference changes.");
        }
    }

    private static double ResolveTrueNorthAngleDegrees(Document document, PlacementIntent intent)
    {
        double currentAngleDegrees = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle * (180.0d / Math.PI);
        return intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle
            ? intent.TrueNorthAngle ?? currentAngleDegrees
            : currentAngleDegrees;
    }

    private static GeoProjectInfo BuildGeoProjectInfo(PlacementIntent intent, double resolvedTrueNorthAngle)
    {
        if (intent.SelectedOrigin is null)
        {
            throw new InvalidOperationException("Placement intent is missing a selected origin.");
        }

        GeoProjectInfo info = new GeoProjectInfo
        {
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource,
            GeoSetupDate = DateTime.UtcNow
        };

        info.ApplyCanonicalLocation(intent.SelectedCrs, intent.SelectedOrigin, resolvedTrueNorthAngle);
        return info;
    }

    private void SaveGeoreferenceModuleState(IDocumentHandle document, PlacementIntent intent)
    {
        WorkingProjectBasePointReference? workingProjectBasePoint = WorkingProjectBasePointResolver.Resolve(intent);
        if (workingProjectBasePoint is null)
        {
            return;
        }

        GeoreferenceModuleState state = moduleStateStore.Load<GeoreferenceModuleState>(document, ModuleStateIds.Georeference)
            ?? new GeoreferenceModuleState();
        state.WorkingProjectBasePoint = workingProjectBasePoint;
        state.LastUpdatedUtc = DateTime.UtcNow;
        moduleStateStore.Save(document, ModuleStateIds.Georeference, state);
    }

    private static PlacementAuditRecord BuildAuditRecord(Document document, PlacementIntent intent, double resolvedTrueNorthAngle)
    {
        PlacementAnchorTarget anchorTarget = ProjectLocationWriter.ResolveAnchorTarget(intent.AnchorTarget);
        WorkingProjectBasePointReference? workingProjectBasePoint = WorkingProjectBasePointResolver.Resolve(intent);
        string summary = BuildAuditSummary(document, intent, anchorTarget, resolvedTrueNorthAngle, workingProjectBasePoint);

        return new PlacementAuditRecord
        {
            AppliedAtUtc = DateTime.UtcNow,
            DocumentTitle = document.Title,
            ApplyMode = intent.ApplyMode,
            AnchorTarget = anchorTarget,
            ProjectCrs = intent.SelectedCrs,
            Origin = intent.SelectedOrigin,
            ProjectedCoordinate = intent.SelectedProjectedCoordinate,
            WorkingProjectBasePoint = workingProjectBasePoint,
            TrueNorthAngle = resolvedTrueNorthAngle,
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource,
            Summary = summary
        };
    }

    private static string BuildAuditSummary(
        Document document,
        PlacementIntent intent,
        PlacementAnchorTarget anchorTarget,
        double resolvedTrueNorthAngle,
        WorkingProjectBasePointReference? workingProjectBasePoint)
    {
        string crsText = intent.SelectedCrs is null
            ? "unknown CRS"
            : $"EPSG:{intent.SelectedCrs.EpsgCode}";
        string coordinateText = intent.SelectedProjectedCoordinate.HasValue
            ? $"E {intent.SelectedProjectedCoordinate.Value.Easting:F3} m, N {intent.SelectedProjectedCoordinate.Value.Northing:F3} m"
            : "projected coordinates not captured";
        string originText = intent.SelectedOrigin is null
            ? "unknown origin"
            : $"Lat {intent.SelectedOrigin.Latitude:F6}, Lon {intent.SelectedOrigin.Longitude:F6}";
        string workingPointText = workingProjectBasePoint?.IsValid == true
            ? $" Working Project Base Point saved as E {workingProjectBasePoint.ProjectedCoordinate!.Value.Easting:F3} m, N {workingProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m in EPSG:{workingProjectBasePoint.ProjectCrs!.EpsgCode}."
            : string.Empty;

        return $"Applied {intent.ApplyMode} to '{document.Title}' using {crsText}, {originText}, {coordinateText}, anchor {anchorTarget}, true north {resolvedTrueNorthAngle:F3}°, confidence {intent.Confidence}, source '{intent.SetupSource}'.{workingPointText}";
    }
}
