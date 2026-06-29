using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
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
/// origin (projected 0,0); the Project Base Point is the south-west corner of the selected grid extent.
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
/// <c>georeference.apply</c> — commits the split Survey Point / Project Base Point workflow to the active
/// Revit document. The Survey Point lands on the CRS origin; the Project Base Point keeps its local
/// position while its shared coordinates resolve to the selected grids' south-west corner.
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
        string[] meshCodes = obj?["selectedMeshCodes"]?.ToObject<string[]>() ?? Array.Empty<string>();
        bool confirmExistingSetup = obj?.Value<bool?>("confirmExistingSetup") ?? false;

        var registry = new CrsRegistry();
        CrsDefinition definition = GeoreferenceHandlerSupport.ResolveCrs(registry, crsCode);
        var transformer = new CoordinateTransformer(registry);
        var resolver = new PlateauGridProjectBasePointResolver(new JapanMeshCalculator(), transformer);

        if (confirmExistingSetup)
        {
            return ApplyConfirmExistingSetupAsync(definition, registry);
        }

        PlateauGridProjectBasePointSelection? selection = resolver.Resolve(meshCodes, definition.ToReference());
        if (selection is null || !selection.ProjectedCoordinate.HasValue)
        {
            throw new InvalidOperationException("Select at least one valid project grid before applying the georeference.");
        }

        GeographicCoordinate surveyOrigin = transformer.Unproject(new ProjectedCoordinate(0d, 0d), definition.ToReference());
        string setupSource = string.Format(
            CultureInfo.InvariantCulture,
            "Web georeference — {0} PLATEAU grid(s)",
            selection.SelectedMeshCodes.Count);

        var intent = new SplitSurveyProjectBasePointIntent
        {
            SelectedCrs = definition.ToReference(),
            SharedSurveyOrigin = new ProjectOrigin
            {
                Latitude = surveyOrigin.Latitude,
                Longitude = surveyOrigin.Longitude,
                ElevationMeters = 0d
            },
            SharedSurveyProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            LocalProjectBasePoint = new WorkingProjectBasePointReference
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
            },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = setupSource,
            ApplyMode = PlacementApplyMode.ProjectLocation
        };

        // The suite ties the canonical primary mesh code to the survey origin (CoordinateValidator
        // recomputes Calculate(origin) to detect staleness). ApplyCanonicalLocation clears it, so we
        // recompute the same value and persist it below — there is no Mesh Inspector in the web shell.
        MeshCode canonicalPrimaryMesh = new JapanMeshCalculator().Calculate(
            surveyOrigin.Latitude, surveyOrigin.Longitude, JapanMeshLevel.Tertiary);

        // Apply runs on the Revit API thread against the *current* active document (transaction-legal).
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var handle = new RevitDocumentHandle(document);
            CaptureUndoSnapshot(handle, $"Before applying {definition.Name}");

            var service = new SplitSurveyProjectBasePointService();
            PlacementApplyResult result = service.ApplyPlacement(handle, intent);

            // Persist the primary mesh code so export readiness ("Primary mesh key stored") passes.
            // The georeference is already committed, so a mesh-save hiccup must not fail the apply.
            try
            {
                new MeshCodePersistenceService().SavePrimaryMeshCode(handle, canonicalPrimaryMesh);
            }
            catch
            {
                // Recommended, non-blocking item — leave it unset rather than failing a committed apply.
            }

            return new { success = true, message = result.AuditSummary };
        });
    }

    private static Task<object?> ApplyConfirmExistingSetupAsync(CrsDefinition definition, CrsRegistry registry)
    {
        // Read the live document so we can reuse the same ProjectLocationReader the WPF view uses.
        // ProjectLocationWriter short-circuits in MetadataOnly mode, so Revit project location values
        // are preserved exactly while only the shared metadata (GeoProjectInfo + working PBP) is
        // persisted. This is the equivalent of the WPF BuildExistingSetupMetadataIntent path.
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var reader = new ProjectLocationReader(new GeoProjectInfoStorage());
            CurrentProjectStateSummary state = reader.Read(document);

            if (!state.SurveyPoint.HasEstimatedLocation || !state.ProjectBasePoint.HasEstimatedLocation)
            {
                throw new InvalidOperationException(
                    "Cannot confirm the existing setup: the Survey Point and Project Base Point do not have readable geographic coordinates. " +
                    "Set a Site Location in Revit or click an area on the map before confirming.");
            }

            const string setupSource = "Web georeference — confirm existing setup";

            var intent = new PlacementIntent
            {
                SelectedCrs = definition.ToReference(),
                SelectedOrigin = new ProjectOrigin
                {
                    Latitude = state.SurveyPoint.EstimatedLatitudeDegrees!.Value,
                    Longitude = state.SurveyPoint.EstimatedLongitudeDegrees!.Value,
                    ElevationMeters = 0d
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
                        ElevationMeters = 0d
                    },
                    ProjectedCoordinate = new ProjectedCoordinate(
                        (state.ProjectBasePoint.SharedEastWestFeet ?? 0d) * FeetToMeters,
                        (state.ProjectBasePoint.SharedNorthSouthFeet ?? 0d) * FeetToMeters),
                    Confidence = GeoConfidenceLevel.Verified,
                    SetupSource = setupSource
                }
            };

            var handle = new RevitDocumentHandle(document);
            CaptureUndoSnapshot(handle, $"Before confirming {definition.Name}");

            var service = new RevitGeoPlacementService();
            PlacementApplyResult result = service.ApplyPlacement(handle, intent);

            // Persist the primary mesh code so export readiness ("Primary mesh key stored") passes.
            try
            {
                MeshCode canonicalPrimaryMesh = new JapanMeshCalculator().Calculate(
                    state.SurveyPoint.EstimatedLatitudeDegrees!.Value,
                    state.SurveyPoint.EstimatedLongitudeDegrees!.Value,
                    JapanMeshLevel.Tertiary);
                new MeshCodePersistenceService().SavePrimaryMeshCode(handle, canonicalPrimaryMesh);
            }
            catch
            {
                // Non-blocking — the metadata is already committed.
            }

            return new { success = true, message = result.AuditSummary };
        });
    }

    private static void CaptureUndoSnapshot(RevitDocumentHandle handle, string summary)
    {
        try
        {
            var reader = new ProjectLocationReader(new GeoProjectInfoStorage());
            CurrentProjectStateSummary state = reader.Read(handle.Document);
            GeoProjectInfo? previousInfo = new GeoProjectInfoStorage().Load(handle);

            var snapshot = new GeoreferenceUndoSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                EastWestFeet = state.ProjectPosition.EastWestFeet,
                NorthSouthFeet = state.ProjectPosition.NorthSouthFeet,
                ElevationFeet = state.ProjectPosition.ElevationFeet,
                AngleRadians = state.ProjectPosition.AngleRadians,
                SiteLatitudeRadians = (state.SiteLatitudeDegrees ?? 0d) * (Math.PI / 180.0d),
                SiteLongitudeRadians = (state.SiteLongitudeDegrees ?? 0d) * (Math.PI / 180.0d),
                PreviousGeoInfo = previousInfo,
                Summary = summary
            };

            new GeoreferenceUndoStorage().Save(handle, snapshot);
        }
        catch
        {
            // Best-effort — don't fail the apply if snapshot capture fails.
        }
    }
}

public sealed class GeoreferenceRevertHandler : IRpcHandler
{
    public string Method => "georeference.revert";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var handle = new RevitDocumentHandle(document);
            var undoStorage = new GeoreferenceUndoStorage();

            GeoreferenceUndoSnapshot? snapshot = undoStorage.Load(handle);
            if (snapshot is null)
            {
                throw new InvalidOperationException("No undo snapshot is available. Nothing to revert.");
            }

            using Transaction tx = new Transaction(document, "Revert Georeference");
            tx.Start();
            try
            {
                SiteLocation siteLocation = document.SiteLocation;
                if (siteLocation is not null)
                {
                    siteLocation.Latitude = snapshot.SiteLatitudeRadians;
                    siteLocation.Longitude = snapshot.SiteLongitudeRadians;
                }

                XYZ anchor = BasePoint.GetSurveyPoint(document).Position;
                ProjectPosition position = new ProjectPosition(
                    snapshot.EastWestFeet,
                    snapshot.NorthSouthFeet,
                    snapshot.ElevationFeet,
                    snapshot.AngleRadians);
                document.ActiveProjectLocation.SetProjectPosition(anchor, position);

                var geoStore = new GeoProjectInfoStorage();
                if (snapshot.PreviousGeoInfo is not null)
                {
                    geoStore.Save(handle, snapshot.PreviousGeoInfo);
                }

                undoStorage.Clear(handle);
                tx.Commit();

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
                tx.RollBack();
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
            return MapToContract(state);
        });
    }

    private static GeoreferenceCurrentStateResponse MapToContract(CurrentProjectStateSummary state)
    {
        return new GeoreferenceCurrentStateResponse
        {
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
