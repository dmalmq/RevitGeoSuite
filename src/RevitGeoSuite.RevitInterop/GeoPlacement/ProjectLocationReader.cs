using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ProjectLocationReader : IProjectLocationReader
{
    private const double FeetToMeters = 0.3048d;
    private readonly ExistingSetupDetector existingSetupDetector;
    private readonly IGeoProjectInfoStore geoProjectInfoStore;
    private readonly IModuleStateStore moduleStateStore;

    public ProjectLocationReader(
        IGeoProjectInfoStore geoProjectInfoStore,
        ExistingSetupDetector? existingSetupDetector = null,
        IModuleStateStore? moduleStateStore = null)
    {
        this.geoProjectInfoStore = geoProjectInfoStore ?? throw new ArgumentNullException(nameof(geoProjectInfoStore));
        this.existingSetupDetector = existingSetupDetector ?? new ExistingSetupDetector();
        this.moduleStateStore = moduleStateStore ?? new ModuleStateStorage();
    }

    public CurrentProjectStateSummary Read(Document? document)
    {
        if (document is null)
        {
            return new CurrentProjectStateSummary
            {
                IsSupportedDocument = false,
                StatusMessage = "No active Revit document is available."
            };
        }

        if (document.IsFamilyDocument)
        {
            return new CurrentProjectStateSummary
            {
                DocumentTitle = document.Title,
                IsSupportedDocument = false,
                StatusMessage = "Family documents are not supported by the georeference workflow."
            };
        }

        try
        {
            BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
            BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(document);
            ProjectLocation projectLocation = document.ActiveProjectLocation;
            ProjectPosition projectPosition = projectLocation.GetProjectPosition(XYZ.Zero);
            SiteLocation? siteLocation = document.SiteLocation;

            RevitDocumentHandle documentHandle = new RevitDocumentHandle(document);
            bool hasStoredGeoInfo = geoProjectInfoStore.HasData(documentHandle);
            GeoProjectInfo? storedGeoInfo = hasStoredGeoInfo ? geoProjectInfoStore.Load(documentHandle) : null;
            GeoreferenceModuleState? moduleState = moduleStateStore.Load<GeoreferenceModuleState>(documentHandle, ModuleStateIds.Georeference);

            ProjectPositionSnapshot positionSnapshot = new ProjectPositionSnapshot
            {
                EastWestFeet = projectPosition.EastWest,
                NorthSouthFeet = projectPosition.NorthSouth,
                ElevationFeet = projectPosition.Elevation,
                AngleRadians = projectPosition.Angle
            };

            ExistingSetupDetectionResult detection = existingSetupDetector.Detect(positionSnapshot, hasStoredGeoInfo);
            GeographicCoordinate? siteCoordinate = siteLocation is null
                ? null
                : new GeographicCoordinate(
                    siteLocation.Latitude * (180.0 / Math.PI),
                    siteLocation.Longitude * (180.0 / Math.PI));

            return new CurrentProjectStateSummary
            {
                DocumentTitle = document.Title,
                IsSupportedDocument = true,
                IsReadOnly = document.IsReadOnly,
                StatusMessage = document.IsReadOnly
                    ? "This project is read-only. Preview is still available, but apply requires an editable model."
                    : string.Empty,
                ExistingSetupDetected = detection.Detected,
                ExistingSetupMessage = detection.Message,
                HasStoredGeoInfo = hasStoredGeoInfo,
                StoredCrs = storedGeoInfo?.ProjectCrs,
                StoredOrigin = storedGeoInfo?.Origin,
                StoredTrueNorthAngle = storedGeoInfo?.TrueNorthAngle,
                StoredConfidence = storedGeoInfo?.Confidence,
                SetupSource = storedGeoInfo?.SetupSource ?? string.Empty,
                StoredWorkingProjectBasePoint = moduleState?.WorkingProjectBasePoint,
                SiteLatitudeDegrees = siteCoordinate?.Latitude,
                SiteLongitudeDegrees = siteCoordinate?.Longitude,
                SiteTimeZoneHours = siteLocation?.TimeZone,
                ProjectPosition = positionSnapshot,
                SurveyPoint = CreateBasePointSnapshot("Survey Point", surveyPoint, projectLocation, siteCoordinate),
                ProjectBasePoint = CreateBasePointSnapshot("Project Base Point", projectBasePoint, projectLocation, siteCoordinate)
            };
        }
        catch (Exception ex)
        {
            return new CurrentProjectStateSummary
            {
                DocumentTitle = document.Title,
                IsSupportedDocument = false,
                StatusMessage = "Current project location could not be read safely: " + ex.Message
            };
        }
    }

    private static BasePointSnapshot CreateBasePointSnapshot(
        string name,
        BasePoint basePoint,
        ProjectLocation projectLocation,
        GeographicCoordinate? siteCoordinate)
    {
        XYZ position = basePoint.Position;
        BasePointSnapshot snapshot = new BasePointSnapshot
        {
            Name = name,
            XFeet = position.X,
            YFeet = position.Y,
            ZFeet = position.Z
        };

        if (siteCoordinate is null)
        {
            return snapshot;
        }

        ProjectPosition pointPosition = projectLocation.GetProjectPosition(position);
        GeographicCoordinate estimatedCoordinate = LocalCoordinateOffsetProjector.Offset(
            siteCoordinate.Value,
            pointPosition.EastWest * FeetToMeters,
            pointPosition.NorthSouth * FeetToMeters);

        snapshot.EstimatedLatitudeDegrees = estimatedCoordinate.Latitude;
        snapshot.EstimatedLongitudeDegrees = estimatedCoordinate.Longitude;
        return snapshot;
    }
}
