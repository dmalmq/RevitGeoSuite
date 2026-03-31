using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.MeshInspector;

public sealed class MeshInspectorService
{
    private readonly IMeshCalculator meshCalculator;
    private readonly MeshNeighborResolver neighborResolver;
    private readonly MeshOverlayService meshOverlayService;

    public MeshInspectorService(
        IMeshCalculator meshCalculator,
        MeshNeighborResolver? neighborResolver = null,
        MeshOverlayService? meshOverlayService = null)
    {
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
        this.neighborResolver = neighborResolver ?? new MeshNeighborResolver(meshCalculator);
        this.meshOverlayService = meshOverlayService ?? new MeshOverlayService(meshCalculator);
    }

    public MeshInspectorSummary BuildSummary(CurrentProjectStateSummary currentState, GeoProjectInfo? info, MeshReferenceSource referenceSource = MeshReferenceSource.SurveyPoint)
    {
        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        MeshInspectorSummary summary = new MeshInspectorSummary
        {
            DocumentTitle = string.IsNullOrWhiteSpace(currentState.DocumentTitle) ? "Active Revit Project" : currentState.DocumentTitle,
            ReferenceSourceTitle = GetReferenceSourceTitle(referenceSource),
            ReferenceSourceDescription = GetReferenceSourceDescription(referenceSource)
        };

        if (!currentState.IsSupportedDocument)
        {
            summary.StatusMessage = string.IsNullOrWhiteSpace(currentState.StatusMessage)
                ? "Mesh Inspector is not available for this Revit document."
                : currentState.StatusMessage;
            summary.DetailRows = new[]
            {
                new DetailRow("Document", summary.DocumentTitle),
                new DetailRow("Reference Source", summary.ReferenceSourceTitle),
                new DetailRow("Stored Geo Metadata", currentState.HasStoredGeoInfo ? "Yes" : "No")
            };
            return summary;
        }

        if (info?.ProjectCrs is null || info.Origin is null)
        {
            summary.StatusMessage = "Shared geo metadata is missing or incomplete. Run Georeference Setup before using Mesh Inspector.";
            summary.DetailRows = new[]
            {
                new DetailRow("Document", summary.DocumentTitle),
                new DetailRow("Reference Source", summary.ReferenceSourceTitle),
                new DetailRow("Stored Geo Metadata", currentState.HasStoredGeoInfo ? "Yes" : "No"),
                new DetailRow("Stored CRS", info?.ProjectCrs is null ? "Not stored" : $"EPSG:{info.ProjectCrs.EpsgCode}  {info.ProjectCrs.NameSnapshot}"),
                new DetailRow("Stored Origin", info?.Origin is null ? "Not stored" : $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6}")
            };
            return summary;
        }

        MeshReferenceContext? referenceContext = ResolveReferenceContext(currentState, info, referenceSource);
        if (referenceContext is null)
        {
            summary.StatusMessage = "Project Base Point location is not available yet. Save a working Project Base Point in Georeference Setup or establish a readable Revit Project Base Point location first.";
            summary.DetailRows = new[]
            {
                new DetailRow("Document", summary.DocumentTitle),
                new DetailRow("Reference Source", summary.ReferenceSourceTitle),
                new DetailRow("Stored CRS", $"EPSG:{info.ProjectCrs.EpsgCode}  {info.ProjectCrs.NameSnapshot}"),
                new DetailRow("Stored Origin", $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6}, elev {info.Origin.ElevationMeters:F3} m"),
                new DetailRow("Working Project Base Point", currentState.StoredWorkingProjectBasePoint?.IsValid == true ? "Available" : "Not stored"),
                new DetailRow("Revit Project Base Point Estimate", currentState.ProjectBasePoint.HasEstimatedLocation ? "Available" : "Not available")
            };
            return summary;
        }

        MeshCode calculatedPrimaryMesh = meshCalculator.Calculate(referenceContext.Latitude, referenceContext.Longitude, JapanMeshLevel.Tertiary);
        IReadOnlyCollection<MeshCode> neighbors = neighborResolver.GetNeighbors(calculatedPrimaryMesh);
        bool storedMatches = referenceSource == MeshReferenceSource.SurveyPoint
            && string.Equals(info.PrimaryMeshCode?.Value, calculatedPrimaryMesh.Value, StringComparison.Ordinal);
        string meshState = referenceSource == MeshReferenceSource.SurveyPoint
            ? info.PrimaryMeshCode is null
                ? "Not stored yet"
                : storedMatches
                    ? "Stored value is current"
                    : "Stored value is stale"
            : "Inspection only for Project Base Point workflows";

        summary.PrimaryMeshCode = calculatedPrimaryMesh.Value;
        summary.CanSavePrimaryMeshCode = referenceContext.CanPersistPrimaryMeshCode && (info.PrimaryMeshCode is null || !storedMatches);
        summary.CenterLatitude = referenceContext.Latitude;
        summary.CenterLongitude = referenceContext.Longitude;
        summary.OverlayGeoJson = meshOverlayService.CreateGeoJson(calculatedPrimaryMesh, neighbors);
        summary.NeighborMeshCodes = neighbors.Select(code => code.Value).ToArray();
        summary.DetailRows = new[]
        {
            new DetailRow("Document", summary.DocumentTitle),
            new DetailRow("Reference Source", referenceContext.Title),
            new DetailRow("Reference Location", $"{referenceContext.Latitude:F6}, {referenceContext.Longitude:F6}"),
            new DetailRow("Reference Note", referenceContext.Description),
            new DetailRow("Stored CRS", $"EPSG:{info.ProjectCrs.EpsgCode}  {info.ProjectCrs.NameSnapshot}"),
            new DetailRow("Stored Origin", $"{info.Origin.Latitude:F6}, {info.Origin.Longitude:F6}, elev {info.Origin.ElevationMeters:F3} m"),
            new DetailRow("Stored Primary Mesh", info.PrimaryMeshCode?.Value ?? "Not stored"),
            new DetailRow("Calculated Mesh", calculatedPrimaryMesh.Value),
            new DetailRow("Mesh Status", meshState),
            new DetailRow("Neighbor Count", neighbors.Count.ToString(CultureInfo.InvariantCulture)),
            new DetailRow("Confidence", info.Confidence.ToString()),
            new DetailRow("Setup Source", string.IsNullOrWhiteSpace(info.SetupSource) ? "Not recorded" : info.SetupSource)
        };

        if (referenceSource == MeshReferenceSource.ProjectBasePoint)
        {
            summary.StatusMessage = "Mesh codes were derived from the selected Project Base Point reference. This view is inspection-only because GeoProjectInfo.PrimaryMeshCode stays tied to the canonical survey/origin state.";
        }
        else if (currentState.IsReadOnly)
        {
            summary.StatusMessage = "Mesh codes were derived from the stored geo metadata. This project is read-only, so saving the primary mesh code back into GeoProjectInfo is disabled.";
        }
        else if (storedMatches)
        {
            summary.StatusMessage = "Primary and neighbor mesh codes were derived from the current shared geo metadata. The stored primary mesh is already current.";
        }
        else
        {
            summary.StatusMessage = "Primary and neighbor mesh codes were derived from the current shared geo metadata. Save the primary mesh code if you want it persisted in GeoProjectInfo.";
        }

        return summary;
    }

    private static MeshReferenceContext? ResolveReferenceContext(CurrentProjectStateSummary currentState, GeoProjectInfo info, MeshReferenceSource referenceSource)
    {
        if (referenceSource == MeshReferenceSource.SurveyPoint)
        {
            return new MeshReferenceContext(
                "Survey Point / Canonical Origin",
                "Uses the canonical GeoProjectInfo origin. This remains the shared source of truth for suite-wide mesh state.",
                info.Origin!.Latitude,
                info.Origin.Longitude,
                !currentState.IsReadOnly);
        }

        if (currentState.StoredWorkingProjectBasePoint?.IsValid == true)
        {
            return new MeshReferenceContext(
                "Working Project Base Point",
                "Uses the saved Working Project Base Point from georeference module state. This is the preferred local reference for later import or export workflows.",
                currentState.StoredWorkingProjectBasePoint.Origin!.Latitude,
                currentState.StoredWorkingProjectBasePoint.Origin.Longitude,
                false);
        }

        if (currentState.ProjectBasePoint.HasEstimatedLocation)
        {
            return new MeshReferenceContext(
                "Revit Project Base Point",
                "Uses the Revit Project Base Point estimate derived from the current project location because no saved Working Project Base Point exists yet.",
                currentState.ProjectBasePoint.EstimatedLatitudeDegrees!.Value,
                currentState.ProjectBasePoint.EstimatedLongitudeDegrees!.Value,
                false);
        }

        return null;
    }

    private static string GetReferenceSourceTitle(MeshReferenceSource referenceSource)
    {
        return referenceSource switch
        {
            MeshReferenceSource.ProjectBasePoint => "Project Base Point",
            _ => "Survey Point / Canonical Origin"
        };
    }

    private static string GetReferenceSourceDescription(MeshReferenceSource referenceSource)
    {
        return referenceSource switch
        {
            MeshReferenceSource.ProjectBasePoint => "Inspect mesh around the saved Working Project Base Point when available, otherwise fall back to the Revit Project Base Point estimate.",
            _ => "Inspect mesh around the canonical survey/origin state that drives GeoProjectInfo and the shared suite mesh key."
        };
    }

    private sealed class MeshReferenceContext
    {
        public MeshReferenceContext(string title, string description, double latitude, double longitude, bool canPersistPrimaryMeshCode)
        {
            Title = title;
            Description = description;
            Latitude = latitude;
            Longitude = longitude;
            CanPersistPrimaryMeshCode = canPersistPrimaryMeshCode;
        }

        public string Title { get; }

        public string Description { get; }

        public double Latitude { get; }

        public double Longitude { get; }

        public bool CanPersistPrimaryMeshCode { get; }
    }
}
