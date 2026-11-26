using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

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
        int deletedElementCount = 0)
    {
        Document = document;
        SavedFilePath = savedFilePath;
        SnapshotScope = snapshotScope ?? throw new ArgumentNullException(nameof(snapshotScope));
        SnapshotContentHash = snapshotContentHash ?? throw new ArgumentNullException(nameof(snapshotContentHash));
        CreatedElementCount = createdElementCount;
        UpdatedElementCount = updatedElementCount;
        ReplacedElementCount = replacedElementCount;
        DeletedElementCount = deletedElementCount;
        SkippedElementCount = skippedElementCount;
        BoundParameterCount = boundParameterCount;
        OverflowFeatureCount = overflowFeatureCount;
        CreatedApplicationDataIds = createdApplicationDataIds ?? Array.Empty<string>();
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>The companion document (or <c>null</c> if <see cref="ShowcaseCompanionBuildOptions.CloseDocumentAfterSave"/> closed it).</summary>
    public Document? Document { get; }

    public string? SavedFilePath { get; }

    public string SnapshotScope { get; }

    public string SnapshotContentHash { get; }

    public int CreatedElementCount { get; }

    public int UpdatedElementCount { get; }

    public int ReplacedElementCount { get; }

    public int DeletedElementCount { get; }

    public int SkippedElementCount { get; }

    public int BoundParameterCount { get; }

    public int OverflowFeatureCount { get; }

    public IReadOnlyList<string> CreatedApplicationDataIds { get; }

    public IReadOnlyList<string> Warnings { get; }
}
