using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class SplitSurveyProjectBasePointService : ISplitSurveyProjectBasePointService
{
    private const double MetersToFeet = 1.0d / 0.3048d;
    private readonly IGeoProjectInfoStore geoProjectInfoStore;
    private readonly PlacementAuditStorage placementAuditStorage;
    private readonly IModuleStateStore moduleStateStore;
    private readonly GeoreferenceUndoStorage undoStorage;

    public SplitSurveyProjectBasePointService(
        IGeoProjectInfoStore? geoProjectInfoStore = null,
        PlacementAuditStorage? placementAuditStorage = null,
        IModuleStateStore? moduleStateStore = null,
        GeoreferenceUndoStorage? undoStorage = null)
    {
        this.geoProjectInfoStore = geoProjectInfoStore ?? new GeoProjectInfoStorage();
        this.placementAuditStorage = placementAuditStorage ?? new PlacementAuditStorage();
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
        this.undoStorage = undoStorage ?? new GeoreferenceUndoStorage();
    }

    public PlacementApplyResult ApplyPlacement(IDocumentHandle document, SplitSurveyProjectBasePointIntent intent)
    {
        return ApplyPlacement(document, intent, undoSnapshot: null, primaryMeshCode: null);
    }

    public PlacementApplyResult ApplyPlacement(
        IDocumentHandle document,
        SplitSurveyProjectBasePointIntent intent,
        GeoreferenceUndoSnapshot? undoSnapshot,
        MeshCode? primaryMeshCode = null)
    {
        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        ValidateIntent(intent);

        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Split georeference apply requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;
        EnsureSupportedDocument(revitDocument);

        double resolvedTrueNorthAngle = ResolveTrueNorthAngleDegrees(revitDocument, intent);
        GeoProjectInfo geoProjectInfo = BuildGeoProjectInfo(intent, resolvedTrueNorthAngle, primaryMeshCode);
        PlacementAuditRecord auditRecord = BuildAuditRecord(revitDocument, intent, resolvedTrueNorthAngle);

        using Transaction transaction = new Transaction(revitDocument, "Apply Split Georeference");
        transaction.Start();

        try
        {
            ApplySplitWorkflow(revitDocument, intent, resolvedTrueNorthAngle);
            geoProjectInfoStore.Save(handle, geoProjectInfo);
            SaveGeoreferenceModuleState(handle, intent.LocalProjectBasePoint!);
            placementAuditStorage.Save(handle, auditRecord);
            if (undoSnapshot is not null)
            {
                undoStorage.Save(handle, undoSnapshot);
            }

            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the split georeference transaction.");
            }
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw new InvalidOperationException("Split georeference apply failed. Revit rolled back the transaction. " + ex.Message, ex);
        }

        return new PlacementApplyResult
        {
            SavedGeoProjectInfo = geoProjectInfo,
            AuditRecord = auditRecord,
            AuditSummary = auditRecord.Summary
        };
    }

    private static void ApplySplitWorkflow(Document document, SplitSurveyProjectBasePointIntent intent, double resolvedTrueNorthAngleDegrees)
    {
        ProjectLocation projectLocation = document.ActiveProjectLocation
            ?? throw new InvalidOperationException("The active Revit document does not expose a writable project location.");
        SiteLocation siteLocation = document.SiteLocation
            ?? throw new InvalidOperationException("The active Revit document does not expose a writable site location.");
        BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(document);
        BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
        XYZ actualProjectBasePoint = projectBasePoint.Position;

        siteLocation.Latitude = DegreesToRadians(intent.SharedSurveyOrigin!.Latitude);
        siteLocation.Longitude = DegreesToRadians(intent.SharedSurveyOrigin.Longitude);

        ProjectPosition projectBasePointSharedPosition = new ProjectPosition(
            intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting * MetersToFeet,
            intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing * MetersToFeet,
            intent.LocalProjectBasePoint.Origin!.ElevationMeters * MetersToFeet,
            DegreesToRadians(resolvedTrueNorthAngleDegrees));

        projectLocation.SetProjectPosition(actualProjectBasePoint, projectBasePointSharedPosition);

        ProjectPosition currentSharedPosition = projectLocation.GetProjectPosition(actualProjectBasePoint);
        ProjectPosition plusX = projectLocation.GetProjectPosition(actualProjectBasePoint + XYZ.BasisX);
        ProjectPosition plusY = projectLocation.GetProjectPosition(actualProjectBasePoint + XYZ.BasisY);
        ProjectPosition plusZ = projectLocation.GetProjectPosition(actualProjectBasePoint + XYZ.BasisZ);

        double targetSurveyEastFeet = intent.SharedSurveyProjectedCoordinate!.Value.Easting * MetersToFeet;
        double targetSurveyNorthFeet = intent.SharedSurveyProjectedCoordinate.Value.Northing * MetersToFeet;
        double targetSurveyElevationFeet = intent.SharedSurveyOrigin.ElevationMeters * MetersToFeet;
        double deltaEastFeet = targetSurveyEastFeet - currentSharedPosition.EastWest;
        double deltaNorthFeet = targetSurveyNorthFeet - currentSharedPosition.NorthSouth;

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
            throw new InvalidOperationException("The current project location basis could not be inverted to position the Survey Point.");
        }

        double verticalBasis = plusZ.Elevation - currentSharedPosition.Elevation;
        if (Math.Abs(verticalBasis) < 1e-6d)
        {
            throw new InvalidOperationException("The current project location basis could not be inverted vertically to position the Survey Point.");
        }

        double deltaZFeet = (targetSurveyElevationFeet - currentSharedPosition.Elevation) / verticalBasis;
        XYZ desiredSurveyPoint = actualProjectBasePoint + new XYZ(deltaXFeet, deltaYFeet, deltaZFeet);
        MoveSurveyPointLocally(document, surveyPoint, desiredSurveyPoint);
    }

    public static void MoveSurveyPointLocally(
        Document document,
        BasePoint surveyPoint,
        XYZ desiredSurveyPoint,
        bool? restoreClipped = null,
        bool? restorePinned = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (surveyPoint is null)
        {
            throw new ArgumentNullException(nameof(surveyPoint));
        }

        if (desiredSurveyPoint is null)
        {
            throw new ArgumentNullException(nameof(desiredSurveyPoint));
        }

        bool targetClipped = restoreClipped ?? surveyPoint.Clipped;
        bool targetPinned = restorePinned ?? surveyPoint.Pinned;

        try
        {
            if (surveyPoint.Pinned)
            {
                surveyPoint.Pinned = false;
            }

            if (surveyPoint.Clipped)
            {
                surveyPoint.Clipped = false;
            }

            XYZ delta = desiredSurveyPoint - surveyPoint.Position;
            if (delta.GetLength() > 1e-6d)
            {
                ElementTransformUtils.MoveElement(document, surveyPoint.Id, delta);
            }
        }
        finally
        {
            if (surveyPoint.IsValidObject)
            {
                if (surveyPoint.Clipped != targetClipped)
                {
                    surveyPoint.Clipped = targetClipped;
                }

                if (surveyPoint.Pinned != targetPinned)
                {
                    surveyPoint.Pinned = targetPinned;
                }
            }
        }
    }

    private static void ValidateIntent(SplitSurveyProjectBasePointIntent intent)
    {
        if (intent.SelectedCrs is null)
        {
            throw new InvalidOperationException("Select a coordinate reference system before applying the split georeference workflow.");
        }

        if (intent.SharedSurveyOrigin is null || !intent.SharedSurveyProjectedCoordinate.HasValue || !intent.SharedSurveyProjectedCoordinate.Value.IsFinite)
        {
            throw new InvalidOperationException("Capture a valid shared Survey Point target before applying the split georeference workflow.");
        }

        if (intent.LocalProjectBasePoint?.IsValid != true)
        {
            throw new InvalidOperationException("Capture or confirm a valid local Project Base Point before applying the split georeference workflow.");
        }

        if (intent.ApplyMode == PlacementApplyMode.MetadataOnly)
        {
            throw new InvalidOperationException("Split georeference requires a Project Location apply mode. Metadata-only apply is not supported for this workflow.");
        }
    }

    private static void EnsureSupportedDocument(Document document)
    {
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Family documents are not supported by the split georeference workflow.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. Apply requires an editable project.");
        }

        if (document.IsModifiable)
        {
            throw new InvalidOperationException("Another Revit transaction is already active. Finish it before applying split georeference changes.");
        }
    }

    private static double ResolveTrueNorthAngleDegrees(Document document, SplitSurveyProjectBasePointIntent intent)
    {
        double currentAngleDegrees = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle * (180.0d / Math.PI);
        return intent.ApplyMode == PlacementApplyMode.ProjectLocationAndAngle
            ? intent.TrueNorthAngle ?? currentAngleDegrees
            : currentAngleDegrees;
    }

    private static GeoProjectInfo BuildGeoProjectInfo(
        SplitSurveyProjectBasePointIntent intent,
        double resolvedTrueNorthAngle,
        MeshCode? primaryMeshCode)
    {
        GeoProjectInfo info = new GeoProjectInfo
        {
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource,
            GeoSetupDate = DateTime.UtcNow
        };

        info.ApplyCanonicalLocation(intent.SelectedCrs!, intent.SharedSurveyOrigin!, resolvedTrueNorthAngle);
        if (primaryMeshCode is not null && !string.IsNullOrWhiteSpace(primaryMeshCode.Value))
        {
            info.PrimaryMeshCode = new MeshCode { Value = primaryMeshCode.Value };
        }

        return info;
    }

    private void SaveGeoreferenceModuleState(IDocumentHandle document, WorkingProjectBasePointReference workingProjectBasePoint)
    {
        GeoreferenceModuleState state = moduleStateStore.Load<GeoreferenceModuleState>(document, ModuleStateIds.Georeference)
            ?? new GeoreferenceModuleState();
        state.WorkingProjectBasePoint = workingProjectBasePoint;
        state.LastUpdatedUtc = DateTime.UtcNow;
        moduleStateStore.Save(document, ModuleStateIds.Georeference, state);
    }

    private static PlacementAuditRecord BuildAuditRecord(Document document, SplitSurveyProjectBasePointIntent intent, double resolvedTrueNorthAngle)
    {
        string summary = BuildAuditSummary(document, intent, resolvedTrueNorthAngle);
        return new PlacementAuditRecord
        {
            AppliedAtUtc = DateTime.UtcNow,
            DocumentTitle = document.Title,
            ApplyMode = intent.ApplyMode,
            AnchorTarget = PlacementAnchorTarget.SurveyPoint,
            ProjectCrs = intent.SelectedCrs,
            Origin = intent.SharedSurveyOrigin,
            ProjectedCoordinate = intent.SharedSurveyProjectedCoordinate,
            WorkingProjectBasePoint = intent.LocalProjectBasePoint,
            TrueNorthAngle = resolvedTrueNorthAngle,
            Confidence = intent.Confidence,
            SetupSource = intent.SetupSource,
            Summary = summary
        };
    }

    private static string BuildAuditSummary(Document document, SplitSurveyProjectBasePointIntent intent, double resolvedTrueNorthAngle)
    {
        return $"Applied split local Project Base Point + shared Survey workflow to '{document.Title}' using EPSG:{intent.SelectedCrs!.EpsgCode}, shared survey origin Lat {intent.SharedSurveyOrigin!.Latitude:F6}, Lon {intent.SharedSurveyOrigin.Longitude:F6}, E {intent.SharedSurveyProjectedCoordinate!.Value.Easting:F3} m, N {intent.SharedSurveyProjectedCoordinate.Value.Northing:F3} m. The actual Project Base Point stayed local while its shared coordinates resolved to E {intent.LocalProjectBasePoint!.ProjectedCoordinate!.Value.Easting:F3} m, N {intent.LocalProjectBasePoint.ProjectedCoordinate.Value.Northing:F3} m. True north {resolvedTrueNorthAngle:F3}°, confidence {intent.Confidence}, source '{intent.SetupSource}'.";
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0d);
    }
}
