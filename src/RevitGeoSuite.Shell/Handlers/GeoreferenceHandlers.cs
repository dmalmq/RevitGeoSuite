using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.Georeference;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// Shared helpers for the georeference RPC handlers. These wrap the existing Georeference-module
/// services (<see cref="PlateauGridCandidateIndex"/>, <see cref="PlateauGridProjectBasePointResolver"/>,
/// <see cref="SplitSurveyProjectBasePointService"/>) so the web shell drives the exact same Survey
/// Point / Project Base Point workflow as the original WPF window. The Survey Point is always the CRS
/// origin (projected 0,0); the Project Base Point is either exact user-entered CRS coordinates or the
/// south-west corner of the selected grid extent.
/// </summary>
internal static class GeoreferenceHandlerSupport
{
    public static CrsDefinition ResolveCrs(ICrsRegistry registry, string? crsCode)
    {
        if (!TryParseEpsg(crsCode, out int epsgCode))
        {
            throw new InvalidOperationException($"Invalid CRS code '{crsCode}'. Expected a value like 'EPSG:6677'.");
        }

        if (!registry.TryGetByEpsgCode(epsgCode, out CrsDefinition? definition) || definition is null)
        {
            throw new InvalidOperationException($"Unknown coordinate reference system EPSG:{epsgCode}.");
        }

        return definition;
    }

    private static bool TryParseEpsg(string? crsCode, out int epsgCode)
    {
        epsgCode = 0;
        if (string.IsNullOrWhiteSpace(crsCode))
        {
            return false;
        }

        string text = crsCode!.Trim();
        int separator = text.LastIndexOf(':');
        if (separator >= 0)
        {
            text = text.Substring(separator + 1);
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out epsgCode);
    }

    public static ManualProjectBasePointSelection ResolveManualProjectBasePoint(
        CrsDefinition definition,
        CoordinateTransformer transformer,
        GeoreferenceManualProjectBasePoint? manualProjectBasePoint)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (transformer is null)
        {
            throw new ArgumentNullException(nameof(transformer));
        }

        if (manualProjectBasePoint is null)
        {
            throw new InvalidOperationException("Enter Project Base Point Easting and Northing before continuing.");
        }

        return new ManualProjectBasePointResolver(transformer).Resolve(
            manualProjectBasePoint.Easting,
            manualProjectBasePoint.Northing,
            definition.ToReference());
    }
}

/// <summary>
/// <c>georeference.getCrsOrigin</c> — returns the unprojected latitude/longitude at projected (0,0)
/// for the chosen CRS. This is the Survey Point and needs no user interaction.
/// </summary>
public sealed class GeoreferenceGetCrsOriginHandler : IRpcHandler
{
    public string Method => "georeference.getCrsOrigin";

    public Task<object?> HandleAsync(object? payload)
    {
        string? crsCode = (payload as JObject)?.Value<string>("crsCode");

        var registry = new CrsRegistry();
        CrsDefinition definition = GeoreferenceHandlerSupport.ResolveCrs(registry, crsCode);
        var transformer = new CoordinateTransformer(registry);

        GeographicCoordinate origin = transformer.Unproject(new ProjectedCoordinate(0d, 0d), definition.ToReference());
        return Task.FromResult<object?>(new { lat = origin.Latitude, lon = origin.Longitude });
    }
}

/// <summary>
/// <c>georeference.getGridCandidates</c> — given an area (lat/lon), returns the primary PLATEAU mesh
/// plus its eight neighbours and a ready-to-render selection overlay (all initially unselected).
/// </summary>
public sealed class GeoreferenceGetGridCandidatesHandler : IRpcHandler
{
    public string Method => "georeference.getGridCandidates";

    public Task<object?> HandleAsync(object? payload)
    {
        var obj = payload as JObject;
        double? latitude = obj?.Value<double?>("lat");
        double? longitude = obj?.Value<double?>("lon");

        if (!latitude.HasValue || !longitude.HasValue)
        {
            throw new InvalidOperationException("A latitude/longitude area is required to load grid candidates.");
        }

        if (!JapanMeshDomain.IsSupportedCoordinate(latitude.Value, longitude.Value))
        {
            throw new InvalidOperationException(
                $"The selected area ({latitude.Value:F6}, {longitude.Value:F6}) falls outside the supported Japan mesh range.");
        }

        var meshCalculator = new JapanMeshCalculator();
        var candidateIndex = new PlateauGridCandidateIndex(meshCalculator);
        var overlayService = new PlateauGridSelectionOverlayService(meshCalculator);

        var candidates = candidateIndex.GetCandidateGrids(latitude.Value, longitude.Value).ToArray();
        var overlayItems = candidates
            .Select(candidate => new PlateauGridSelectionItem
            {
                TileId = candidate.TileId,
                IsSeedCandidate = candidate.IsPrimary,
                IsSelected = false
            })
            .ToArray();

        string overlayGeoJson = overlayService.CreateGeoJson(overlayItems);

        return Task.FromResult<object?>(new
        {
            candidates = candidates.Select(candidate => new
            {
                tileId = candidate.TileId,
                isPrimary = candidate.IsPrimary,
                isSeedCandidate = candidate.IsPrimary
            }).ToArray(),
            overlayGeoJson,
            centerLat = latitude.Value,
            centerLon = longitude.Value
        });
    }
}

/// <summary>
/// <c>georeference.resolveGridBasePoint</c> — resolves the Project Base Point (south-west corner of the
/// selected grid extent) and its projected easting/northing in the chosen CRS.
/// </summary>
public sealed class GeoreferenceResolveGridBasePointHandler : IRpcHandler
{
    public string Method => "georeference.resolveGridBasePoint";

    public Task<object?> HandleAsync(object? payload)
    {
        var obj = payload as JObject;
        string[] meshCodes = obj?["selectedMeshCodes"]?.ToObject<string[]>() ?? Array.Empty<string>();
        string? crsCode = obj?.Value<string>("crsCode");

        var registry = new CrsRegistry();
        CrsDefinition definition = GeoreferenceHandlerSupport.ResolveCrs(registry, crsCode);
        var transformer = new CoordinateTransformer(registry);
        var resolver = new PlateauGridProjectBasePointResolver(new JapanMeshCalculator(), transformer);

        PlateauGridProjectBasePointSelection? selection = resolver.Resolve(meshCodes, definition.ToReference());
        if (selection is null)
        {
            throw new InvalidOperationException("Select at least one valid project grid to resolve the Project Base Point.");
        }

        ProjectedCoordinate projected = selection.ProjectedCoordinate ?? new ProjectedCoordinate(0d, 0d);
        return Task.FromResult<object?>(new
        {
            selectedMeshCodes = selection.SelectedMeshCodes.ToArray(),
            lat = selection.AnchorLatitude,
            lon = selection.AnchorLongitude,
            easting = projected.Easting,
            northing = projected.Northing
        });
    }
}

/// <summary>
/// <c>georeference.resolveManualBasePoint</c> — resolves exact Project Base Point coordinates
/// entered as easting/northing in the selected CRS into a geographic point for preview/map display.
/// </summary>
public sealed class GeoreferenceResolveManualBasePointHandler : IRpcHandler
{
    public string Method => "georeference.resolveManualBasePoint";

    public Task<object?> HandleAsync(object? payload)
    {
        var obj = payload as JObject;
        string? crsCode = obj?.Value<string>("crsCode");
        GeoreferenceManualProjectBasePoint? manualProjectBasePoint =
            obj?["projectBasePoint"]?.ToObject<GeoreferenceManualProjectBasePoint>();

        var registry = new CrsRegistry();
        CrsDefinition definition = GeoreferenceHandlerSupport.ResolveCrs(registry, crsCode);
        var transformer = new CoordinateTransformer(registry);

        ManualProjectBasePointSelection resolved = GeoreferenceHandlerSupport.ResolveManualProjectBasePoint(
            definition,
            transformer,
            manualProjectBasePoint);

        return Task.FromResult<object?>(new
        {
            selectedMeshCodes = Array.Empty<string>(),
            lat = resolved.AnchorLatitude,
            lon = resolved.AnchorLongitude,
            easting = resolved.ProjectedCoordinate.Easting,
            northing = resolved.ProjectedCoordinate.Northing
        });
    }
}

/// <summary>
/// <c>georeference.apply</c> — commits the split Survey Point / Project Base Point workflow to the active
/// Revit document. The Survey Point lands on the CRS origin; the Project Base Point keeps its local
/// position while its shared coordinates resolve to exact CRS coordinates or the selected grids'
/// south-west corner.
///
/// When <c>confirmExistingSetup</c> is true the handler instead adopts the model&apos;s current Revit
/// shared coordinates (metadata-only): the <c>ProjectPosition</c> and base points are not modified,
/// only the GeoSuite <c>GeoProjectInfo</c> and <c>WorkingProjectBasePoint</c> are persisted. This
/// mirrors the WPF "Confirm existing setup" path so models that already have coordinates set in
/// Revit (but lack the shared metadata) are recognized rather than overwritten.
/// </summary>
public sealed class GeoreferenceApplyHandler : IRpcHandler
{
    private const double FeetToMeters = 0.3048d;

    public string Method => "georeference.apply";

    public Task<object?> HandleAsync(object? payload)
    {
        var obj = payload as JObject;
        string? crsCode = obj?.Value<string>("crsCode");
        string? expectedDocumentId = obj?.Value<string>("documentId")?.Trim();
        string[] meshCodes = obj?["selectedMeshCodes"]?.ToObject<string[]>() ?? Array.Empty<string>();
        GeoreferenceManualProjectBasePoint? manualProjectBasePoint =
            obj?["manualProjectBasePoint"]?.ToObject<GeoreferenceManualProjectBasePoint>();
        bool confirmExistingSetup = obj?.Value<bool?>("confirmExistingSetup") ?? false;

        if (string.IsNullOrWhiteSpace(expectedDocumentId))
        {
            throw new InvalidOperationException(
                "Document identity is required before applying georeference changes. Reload the Georeference page to review the active model first.");
        }

        var registry = new CrsRegistry();
        CrsDefinition definition = GeoreferenceHandlerSupport.ResolveCrs(registry, crsCode);
        var transformer = new CoordinateTransformer(registry);

        if (confirmExistingSetup)
        {
            return ApplyConfirmExistingSetupAsync(definition, registry, expectedDocumentId!);
        }

        GeographicCoordinate surveyOrigin = transformer.Unproject(new ProjectedCoordinate(0d, 0d), definition.ToReference());
        WorkingProjectBasePointReference localProjectBasePoint;
        string setupSource;

        if (manualProjectBasePoint is not null)
        {
            ManualProjectBasePointSelection resolved = GeoreferenceHandlerSupport.ResolveManualProjectBasePoint(
                definition,
                transformer,
                manualProjectBasePoint);
            setupSource = "Web georeference — exact Project Base Point coordinates";
            localProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = definition.ToReference(),
                Origin = new ProjectOrigin
                {
                    Latitude = resolved.AnchorLatitude,
                    Longitude = resolved.AnchorLongitude,
                    ElevationMeters = 0d
                },
                ProjectedCoordinate = resolved.ProjectedCoordinate,
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = setupSource
            };
        }
        else
        {
            var resolver = new PlateauGridProjectBasePointResolver(new JapanMeshCalculator(), transformer);
            PlateauGridProjectBasePointSelection? selection = resolver.Resolve(meshCodes, definition.ToReference());
            if (selection is null || !selection.ProjectedCoordinate.HasValue)
            {
                throw new InvalidOperationException("Select at least one valid project grid before applying the georeference.");
            }

            setupSource = string.Format(
                CultureInfo.InvariantCulture,
                "Web georeference — {0} PLATEAU grid(s)",
                selection.SelectedMeshCodes.Count);
            localProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = definition.ToReference(),
                Origin = new ProjectOrigin
                {
                    Latitude = selection.AnchorLatitude,
                    Longitude = selection.AnchorLongitude,
                    ElevationMeters = 0d
                },
                ProjectedCoordinate = selection.ProjectedCoordinate,
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = setupSource
            };
        }

        // The suite ties the canonical primary mesh code to the survey origin (CoordinateValidator
        // recomputes Calculate(origin) to detect staleness). Persist it atomically inside the apply
        // transaction so export readiness ("Primary mesh key stored") passes in one Revit undo step.
        MeshCode? canonicalPrimaryMesh = JapanMeshDomain.IsSupportedCoordinate(surveyOrigin.Latitude, surveyOrigin.Longitude)
            ? new JapanMeshCalculator().Calculate(surveyOrigin.Latitude, surveyOrigin.Longitude, JapanMeshLevel.Tertiary)
            : null;

        // Apply runs on the Revit API thread only if the active document still matches the reviewed model.
        return RevitContext.Instance.InvokeWithExpectedDocumentAsync<object?>(expectedDocumentId, "georeference setup", document =>
        {
            // Preserve the model's existing shared elevations instead of resetting them to 0.
            double preservedProjectBasePointElevationMeters = ReadSharedElevationMeters(document, BasePoint.GetProjectBasePoint(document));
            double preservedSurveyElevationMeters = ReadSharedElevationMeters(document, BasePoint.GetSurveyPoint(document));
            localProjectBasePoint.Origin!.ElevationMeters = preservedProjectBasePointElevationMeters;

            var intent = new SplitSurveyProjectBasePointIntent
            {
                SelectedCrs = definition.ToReference(),
                SharedSurveyOrigin = new ProjectOrigin
                {
                    Latitude = surveyOrigin.Latitude,
                    Longitude = surveyOrigin.Longitude,
                    ElevationMeters = preservedSurveyElevationMeters
                },
                SharedSurveyProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
                LocalProjectBasePoint = localProjectBasePoint,
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = setupSource,
                ApplyMode = PlacementApplyMode.ProjectLocation
            };

            var handle = new RevitDocumentHandle(document);
            GeoreferenceUndoSnapshot undoSnapshot = BuildUndoSnapshot(handle, $"Before applying {definition.Name}");

            var service = new SplitSurveyProjectBasePointService();
            PlacementApplyResult result = service.ApplyPlacement(handle, intent, undoSnapshot, canonicalPrimaryMesh);

            return new { success = true, message = result.AuditSummary };
        });
    }

    private static Task<object?> ApplyConfirmExistingSetupAsync(CrsDefinition definition, CrsRegistry registry, string expectedDocumentId)
    {
        // Read the live document so we can reuse the same ProjectLocationReader the WPF view uses.
        // ProjectLocationWriter short-circuits in MetadataOnly mode, so Revit project location values
        // are preserved exactly while only the shared metadata (GeoProjectInfo + working PBP) is
        // persisted. This is the equivalent of the WPF BuildExistingSetupMetadataIntent path.
        return RevitContext.Instance.InvokeWithExpectedDocumentAsync<object?>(expectedDocumentId, "confirming existing georeference setup", document =>
        {
            var reader = new ProjectLocationReader(new GeoProjectInfoStorage(), crsOverride: definition.ToReference());
            CurrentProjectStateSummary state = reader.Read(document);

            if (!state.SurveyPoint.HasEstimatedLocation || !state.ProjectBasePoint.HasEstimatedLocation)
            {
                throw new InvalidOperationException(
                    "Cannot confirm the existing setup: the Survey Point and Project Base Point do not have readable geographic coordinates. " +
                    "Set a Site Location in Revit or click an area on the map before confirming.");
            }

            double surveyElevationMeters = state.SurveyPoint.SharedElevationFeet.HasValue
                ? state.SurveyPoint.SharedElevationFeet.Value * FeetToMeters
                : ReadSharedElevationMeters(document, BasePoint.GetSurveyPoint(document));
            double projectBasePointElevationMeters = state.ProjectBasePoint.SharedElevationFeet.HasValue
                ? state.ProjectBasePoint.SharedElevationFeet.Value * FeetToMeters
                : ReadSharedElevationMeters(document, BasePoint.GetProjectBasePoint(document));

            const string setupSource = "Web georeference — confirm existing setup";
            double surveyLatitude = state.SurveyPoint.EstimatedLatitudeDegrees!.Value;
            double surveyLongitude = state.SurveyPoint.EstimatedLongitudeDegrees!.Value;

            var intent = new PlacementIntent
            {
                SelectedCrs = definition.ToReference(),
                SelectedOrigin = new ProjectOrigin
                {
                    Latitude = surveyLatitude,
                    Longitude = surveyLongitude,
                    ElevationMeters = surveyElevationMeters
                },
                SelectedProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = setupSource,
                ApplyMode = PlacementApplyMode.MetadataOnly,
                AnchorTarget = PlacementAnchorTarget.SurveyPoint,
                WorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = definition.ToReference(),
                    Origin = new ProjectOrigin
                    {
                        Latitude = state.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                        Longitude = state.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                        ElevationMeters = projectBasePointElevationMeters
                    },
                    ProjectedCoordinate = new ProjectedCoordinate(
                        (state.ProjectBasePoint.SharedEastWestFeet ?? 0d) * FeetToMeters,
                        (state.ProjectBasePoint.SharedNorthSouthFeet ?? 0d) * FeetToMeters),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = setupSource
                }
            };

            MeshCode? canonicalPrimaryMesh = JapanMeshDomain.IsSupportedCoordinate(surveyLatitude, surveyLongitude)
                ? new JapanMeshCalculator().Calculate(surveyLatitude, surveyLongitude, JapanMeshLevel.Tertiary)
                : null;

            var handle = new RevitDocumentHandle(document);
            GeoreferenceUndoSnapshot undoSnapshot = BuildUndoSnapshot(handle, $"Before confirming {definition.Name}");

            var service = new RevitGeoPlacementService();
            PlacementApplyResult result = service.ApplyPlacement(handle, intent, undoSnapshot, canonicalPrimaryMesh);

            return new { success = true, message = result.AuditSummary };
        });
    }

    private static double ReadSharedElevationMeters(Document document, BasePoint? basePoint)
    {
        if (basePoint is null || document.ActiveProjectLocation is null)
        {
            return 0d;
        }

        ProjectPosition? position = document.ActiveProjectLocation.GetProjectPosition(basePoint.Position);
        return position is null ? 0d : position.Elevation * FeetToMeters;
    }

    private static GeoreferenceUndoSnapshot BuildUndoSnapshot(RevitDocumentHandle handle, string summary)
    {
        Document document = handle.Document;
        ProjectLocation projectLocation = document.ActiveProjectLocation
            ?? throw new InvalidOperationException("The active Revit document does not expose a readable project location.");
        ProjectPosition projectPosition = projectLocation.GetProjectPosition(XYZ.Zero);
        SiteLocation? siteLocation = document.SiteLocation;
        BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
        XYZ surveyPosition = surveyPoint.Position;

        GeoProjectInfo? previousInfo = new GeoProjectInfoStorage().Load(handle);
        GeoreferenceModuleState? previousModuleState = new ModuleStateStorage()
            .Load<GeoreferenceModuleState>(handle, ModuleStateIds.Georeference);

        return new GeoreferenceUndoSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            EastWestFeet = projectPosition.EastWest,
            NorthSouthFeet = projectPosition.NorthSouth,
            ElevationFeet = projectPosition.Elevation,
            AngleRadians = projectPosition.Angle,
            SiteLatitudeRadians = siteLocation?.Latitude ?? 0d,
            SiteLongitudeRadians = siteLocation?.Longitude ?? 0d,
            SurveyPointXFeet = surveyPosition.X,
            SurveyPointYFeet = surveyPosition.Y,
            SurveyPointZFeet = surveyPosition.Z,
            SurveyPointClipped = surveyPoint.Clipped,
            SurveyPointPinned = surveyPoint.Pinned,
            PreviousGeoInfo = previousInfo,
            PreviousModuleState = previousModuleState,
            Summary = summary
        };
    }
}

public sealed class GeoreferenceRevertHandler : IRpcHandler
{
    public string Method => "georeference.revert";

    public Task<object?> HandleAsync(object? payload)
    {
        string? expectedDocumentId = (payload as JObject)?.Value<string>("documentId")?.Trim();
        if (string.IsNullOrWhiteSpace(expectedDocumentId))
        {
            throw new InvalidOperationException(
                "Document identity is required before reverting georeference changes. Reload the Georeference page to review the active model first.");
        }

        return RevitContext.Instance.InvokeWithExpectedDocumentAsync<object?>(expectedDocumentId, "reverting georeference", document =>
        {
            if (document.IsFamilyDocument)
            {
                throw new InvalidOperationException("Family documents are not supported by the georeference revert workflow.");
            }

            if (document.IsReadOnly)
            {
                throw new InvalidOperationException("This Revit document is read-only. Revert requires an editable project.");
            }

            if (document.IsModifiable)
            {
                throw new InvalidOperationException("Another Revit transaction is already active. Finish it before reverting georeference changes.");
            }

            ProjectLocation projectLocation = document.ActiveProjectLocation
                ?? throw new InvalidOperationException("The active Revit document does not expose a writable project location.");

            var handle = new RevitDocumentHandle(document);
            var undoStorage = new GeoreferenceUndoStorage();

            GeoreferenceUndoSnapshot? snapshot = undoStorage.Load(handle);
            if (snapshot is null)
            {
                throw new InvalidOperationException("No undo snapshot is available. Nothing to revert.");
            }

            GeoreferenceUndoStorage.ValidateForRestore(snapshot);
            double normalizedAngleRadians = GeoreferenceUndoStorage.NormalizeAngleRadians(snapshot.AngleRadians);

            using Transaction tx = new Transaction(document, "Revert Georeference");
            tx.Start();
            try
            {
                SiteLocation? siteLocation = document.SiteLocation;
                if (siteLocation is not null)
                {
                    siteLocation.Latitude = snapshot.SiteLatitudeRadians;
                    siteLocation.Longitude = snapshot.SiteLongitudeRadians;
                }

                // Snapshot ProjectPosition was captured at Internal Origin (XYZ.Zero); restore at XYZ.Zero
                // first so shared coordinates match the pre-apply transform regardless of Survey Point offset.
                ProjectPosition position = new ProjectPosition(
                    snapshot.EastWestFeet,
                    snapshot.NorthSouthFeet,
                    snapshot.ElevationFeet,
                    normalizedAngleRadians);
                projectLocation.SetProjectPosition(XYZ.Zero, position);

                // Move Survey Point unclipped back to its captured local position so shared coordinates
                // restored above stay intact while Survey Point placement is faithfully reverted.
                if (snapshot.HasSurveyPointPosition())
                {
                    BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
                    XYZ originalSurveyPoint = new XYZ(
                        snapshot.SurveyPointXFeet!.Value,
                        snapshot.SurveyPointYFeet!.Value,
                        snapshot.SurveyPointZFeet!.Value);
                    SplitSurveyProjectBasePointService.MoveSurveyPointLocally(
                        document,
                        surveyPoint,
                        originalSurveyPoint,
                        snapshot.SurveyPointClipped,
                        snapshot.SurveyPointPinned);
                }

                var geoStore = new GeoProjectInfoStorage();
                if (snapshot.PreviousGeoInfo is not null)
                {
                    geoStore.Save(handle, snapshot.PreviousGeoInfo);
                }
                else
                {
                    geoStore.Clear(handle);
                }

                var moduleStateStore = new ModuleStateStorage();
                if (snapshot.PreviousModuleState is not null)
                {
                    moduleStateStore.Save(handle, ModuleStateIds.Georeference, snapshot.PreviousModuleState);
                }
                else
                {
                    moduleStateStore.Remove(handle, ModuleStateIds.Georeference);
                }

                undoStorage.Clear(handle);

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException("Revit did not commit the georeference revert transaction.");
                }

                return new GeoreferenceRevertResponse
                {
                    Success = true,
                    Summary = string.Format(
                        CultureInfo.InvariantCulture,
                        "Reverted to state from {0:yyyy-MM-dd HH:mm} UTC. {1}",
                        snapshot.CapturedAtUtc,
                        snapshot.Summary)
                };
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                {
                    tx.RollBack();
                }

                throw;
            }
        });
    }
}

public sealed class GeoreferenceHasUndoSnapshotHandler : IRpcHandler
{
    public string Method => "georeference.hasUndoSnapshot";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var handle = new RevitDocumentHandle(document);
            var undoStorage = new GeoreferenceUndoStorage();
            GeoreferenceUndoSnapshot? snapshot = undoStorage.Load(handle);

            return new GeoreferenceHasUndoResponse
            {
                HasSnapshot = snapshot is not null,
                Summary = snapshot?.Summary ?? string.Empty
            };
        });
    }
}

/// <summary>
/// <c>georeference.getCurrentState</c> — reads the current Revit project location state so the web
/// shell can detect models that already have coordinates set (via "Acquire Coordinates" or
/// "Specify Coordinates at a Point") and offer a "Confirm existing setup" workflow instead of
/// overwriting meaningful values. Mirrors the WPF <c>ProjectLocationReader</c> + <c>ExistingSetupDetector</c>.
/// </summary>
public sealed class GeoreferenceGetCurrentStateHandler : IRpcHandler
{
    public string Method => "georeference.getCurrentState";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var reader = new ProjectLocationReader(new GeoProjectInfoStorage());
            CurrentProjectStateSummary state = reader.Read(document);
            return MapToContract(state, DocumentIdentity.Create(document));
        });
    }

    private static GeoreferenceCurrentStateResponse MapToContract(CurrentProjectStateSummary state, string documentId)
    {
        return new GeoreferenceCurrentStateResponse
        {
            DocumentId = documentId,
            DocumentTitle = state.DocumentTitle,
            IsSupportedDocument = state.IsSupportedDocument,
            IsReadOnly = state.IsReadOnly,
            StatusMessage = state.StatusMessage,
            ExistingSetupDetected = state.ExistingSetupDetected,
            ExistingSetupMessage = state.ExistingSetupMessage,
            HasDetectedExistingPointSetup = state.SurveyPoint.HasSharedPosition && state.ProjectBasePoint.HasSharedPosition,
            HasStoredGeoInfo = state.HasStoredGeoInfo,
            StoredCrsEpsgCode = state.StoredCrs?.EpsgCode,
            StoredOriginLatitude = state.StoredOrigin?.Latitude,
            StoredOriginLongitude = state.StoredOrigin?.Longitude,
            StoredTrueNorthAngleDegrees = state.StoredTrueNorthAngle,
            SiteLatitudeDegrees = state.SiteLatitudeDegrees,
            SiteLongitudeDegrees = state.SiteLongitudeDegrees,
            ProjectPosition = new GeoreferenceProjectPositionSnapshot
            {
                EastWestFeet = state.ProjectPosition.EastWestFeet,
                NorthSouthFeet = state.ProjectPosition.NorthSouthFeet,
                ElevationFeet = state.ProjectPosition.ElevationFeet,
                AngleDegrees = state.ProjectPosition.AngleDegrees
            },
            SurveyPoint = MapBasePoint(state.SurveyPoint),
            ProjectBasePoint = MapBasePoint(state.ProjectBasePoint)
        };
    }

    private static GeoreferenceBasePointSnapshot MapBasePoint(BasePointSnapshot snapshot)
    {
        return new GeoreferenceBasePointSnapshot
        {
            Name = snapshot.Name,
            XFeet = snapshot.XFeet,
            YFeet = snapshot.YFeet,
            ZFeet = snapshot.ZFeet,
            SharedEastWestFeet = snapshot.SharedEastWestFeet,
            SharedNorthSouthFeet = snapshot.SharedNorthSouthFeet,
            SharedElevationFeet = snapshot.SharedElevationFeet,
            EstimatedLatitudeDegrees = snapshot.EstimatedLatitudeDegrees,
            EstimatedLongitudeDegrees = snapshot.EstimatedLongitudeDegrees
        };
    }
}

public sealed class GeoreferenceGetAvailableCrsHandler : IRpcHandler
{
    public string Method => "georeference.getAvailableCrs";

    public Task<object?> HandleAsync(object? payload)
    {
        var registry = new CrsRegistry();
        var definitions = registry.GetAvailableDefinitions();

        var groups = definitions
            .GroupBy(d => string.IsNullOrEmpty(d.RegionGroup) ? "Other" : d.RegionGroup)
            .OrderBy(g => RegionSortOrder(g.Key))
            .Select(g => new CrsOptionGroup
            {
                Region = g.Key,
                Entries = g.OrderBy(d => d.EpsgCode)
                    .Select(d => new CrsOptionEntry
                    {
                        EpsgCode = d.EpsgCode,
                        Name = d.Name,
                        AreaSummary = d.AreaSummary
                    })
                    .ToArray()
            })
            .ToArray();

        return Task.FromResult<object?>(new AvailableCrsResponse { Groups = groups });
    }

    private static int RegionSortOrder(string region) => region switch
    {
        "Japan" => 0,
        "Europe" => 1,
        "North America" => 2,
        "Asia-Pacific" => 3,
        _ => 99
    };
}
