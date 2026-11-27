using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;

namespace RevitGeoSuite.FloorPlanExport.Showcase;

/// <summary>Summary of a completed <see cref="ShowcaseCompanionBuilder"/> run.</summary>
public sealed class ShowcaseCompanionBuildResult
{
    public ShowcaseCompanionBuildResult(
        Document? document,
        string? savedFilePath,
        string snapshotScope,
        string snapshotContentHash,
        int createdElementCount,
        int skippedElementCount,
        int boundParameterCount,
        int overflowFeatureCount,
        IReadOnlyList<string> createdApplicationDataIds,
        IReadOnlyList<string> warnings,
        int updatedElementCount = 0,
        int replacedElementCount = 0,
        int deletedElementCount = 0,
        int poiMarkerCount = 0,
        int attachmentMarkerCount = 0,
        int networkLinkCount = 0,
        int verticalTubeCount = 0,
        int findingPuckCount = 0,
        int estimatedTriangleCount = 0,
        int unchangedElementCount = 0,
        ShowcaseRefreshSummary? refreshSummary = null,
        FormaPublishViewSetPlan? viewSetPlan = null,
        IReadOnlyList<string>? configuredViewNames = null,
        int deletedStaleViewCount = 0,
        bool publishViewSetConfigured = false,
        ShowcaseBudgetReport? budgetReport = null)
    {
        Document = document;
        SavedFilePath = savedFilePath;
        SnapshotScope = snapshotScope ?? throw new ArgumentNullException(nameof(snapshotScope));
        SnapshotContentHash = snapshotContentHash ?? throw new ArgumentNullException(nameof(snapshotContentHash));
        CreatedElementCount = createdElementCount;
        UnchangedElementCount = unchangedElementCount;
        UpdatedElementCount = updatedElementCount;
        ReplacedElementCount = replacedElementCount;
        DeletedElementCount = deletedElementCount;
        SkippedElementCount = skippedElementCount;
        BoundParameterCount = boundParameterCount;
        OverflowFeatureCount = overflowFeatureCount;
        CreatedApplicationDataIds = createdApplicationDataIds ?? Array.Empty<string>();
        Warnings = warnings ?? Array.Empty<string>();
        PoiMarkerCount = poiMarkerCount;
        AttachmentMarkerCount = attachmentMarkerCount;
        NetworkLinkCount = networkLinkCount;
        VerticalTubeCount = verticalTubeCount;
        FindingPuckCount = findingPuckCount;
        EstimatedTriangleCount = estimatedTriangleCount;
        RefreshSummary = refreshSummary;
        ViewSetPlan = viewSetPlan;
        ConfiguredViewNames = configuredViewNames ?? Array.Empty<string>();
        DeletedStaleViewCount = deletedStaleViewCount;
        PublishViewSetConfigured = publishViewSetConfigured;
        BudgetReport = budgetReport;
    }

    /// <summary>The companion document (or <c>null</c> if <see cref="ShowcaseCompanionBuildOptions.CloseDocumentAfterSave"/> closed it).</summary>
    public Document? Document { get; }

    public string? SavedFilePath { get; }

    public string SnapshotScope { get; }

    public string SnapshotContentHash { get; }

    public int CreatedElementCount { get; }

    /// <summary>Existing showcase elements skipped completely (<c>SkipUnchanged</c>) with zero Revit writes.</summary>
    public int UnchangedElementCount { get; }

    /// <summary>Existing showcase elements updated in-place while preserving Revit <c>UniqueId</c>s.</summary>
    public int UpdatedElementCount { get; }

    /// <summary>Alias matching F5 refresh terminology for in-place updated elements.</summary>
    public int UpdatedInPlaceElementCount => UpdatedElementCount;

    public int ReplacedElementCount { get; }

    public int DeletedElementCount { get; }

    public int SkippedElementCount { get; }

    /// <summary>Count of surviving existing elements whose Revit <c>UniqueId</c> (and attached Forma comments) stayed intact.</summary>
    public int PreservedUniqueIdCount => RefreshSummary?.PreservedUniqueIdCount ?? (UnchangedElementCount + UpdatedElementCount);

    /// <summary>Fraction (<c>0.0..1.0</c>) of surviving elements whose Revit <c>UniqueId</c> stayed intact.</summary>
    public double UniqueIdPreservationRate
    {
        get
        {
            if (RefreshSummary != null)
            {
                return RefreshSummary.UniqueIdPreservationRate;
            }

            int surviving = UnchangedElementCount + UpdatedElementCount + ReplacedElementCount;
            return surviving == 0 ? 1.0d : (double)(UnchangedElementCount + UpdatedElementCount) / surviving;
        }
    }

    public int BoundParameterCount { get; }

    public int OverflowFeatureCount { get; }

    public IReadOnlyList<string> CreatedApplicationDataIds { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int PoiMarkerCount { get; }

    public int AttachmentMarkerCount { get; }

    public int NetworkLinkCount { get; }

    public int VerticalTubeCount { get; }

    public int FindingPuckCount { get; }

    public int EstimatedTriangleCount { get; }

    /// <summary>Complete F5 refresh audit ledger recording prior/new snapshot hashes and <c>UniqueId</c> preservation.</summary>
    public ShowcaseRefreshSummary? RefreshSummary { get; }

    /// <summary>Planned Forma Publish Settings 3D views (Overview, Network &amp; POIs, per-floor section boxes).</summary>
    public FormaPublishViewSetPlan? ViewSetPlan { get; }

    /// <summary>Names of Forma 3D views materialized in the companion document.</summary>
    public IReadOnlyList<string> ConfiguredViewNames { get; }

    public int DeletedStaleViewCount { get; }

    public bool PublishViewSetConfigured { get; }

    /// <summary>F5 triangle, element, marker, parameter, and <c>.rvt</c> file-size budget evaluation report.</summary>
    public ShowcaseBudgetReport? BudgetReport { get; }
}
