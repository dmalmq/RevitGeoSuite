using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportCoordinator
{
    private readonly Tiles3DGeometryExtractor geometryExtractor;
    private readonly Tiles3DGeometrySimplifier geometrySimplifier;
    private readonly Tiles3DPackageWriter packageWriter;
    private readonly Tiles3DExportStateService stateService;
    private readonly Tiles3DLevelGrouper levelGrouper;
    private readonly Tiles3DPreciseCrsAnchorRebaser? preciseCrsRebaser;

    public Tiles3DExportCoordinator(
        Tiles3DGeometryExtractor? geometryExtractor = null,
        Tiles3DGeometrySimplifier? geometrySimplifier = null,
        Tiles3DPackageWriter? packageWriter = null,
        Tiles3DExportStateService? stateService = null,
        Tiles3DLevelGrouper? levelGrouper = null,
        ICoordinateTransformer? coordinateTransformer = null)
    {
        this.geometryExtractor = geometryExtractor ?? new Tiles3DGeometryExtractor();
        this.geometrySimplifier = geometrySimplifier ?? new Tiles3DGeometrySimplifier();
        this.packageWriter = packageWriter ?? new Tiles3DPackageWriter();
        this.stateService = stateService ?? new Tiles3DExportStateService();
        this.levelGrouper = levelGrouper ?? new Tiles3DLevelGrouper();
        preciseCrsRebaser = coordinateTransformer is not null
            ? new Tiles3DPreciseCrsAnchorRebaser(coordinateTransformer)
            : null;
    }

    public Tiles3DExportPreparationResult Prepare(
        IDocumentHandle document,
        Tiles3DExportReferenceContext referenceContext,
        Tiles3DExportScopeSelection scope,
        Tiles3DLevelOfDetail levelOfDetail = Tiles3DLevelOfDetail.Fine,
        bool usePreciseCrsProjection = false,
        double? geoidHeightOffsetMeters = null)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("3D Tiles export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("3D Tiles export is not supported in family documents.");
        }

        if (scope.ScopeMode == Tiles3DExportScopeMode.Selected3DView && !scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a non-template 3D view before preparing 3D Tiles export from a selected view.");
        }

        double resolvedGeoidHeightOffsetMeters = ResolveGeoidOffset(
            geoidHeightOffsetMeters,
            usePreciseCrsProjection,
            referenceContext.AnchorLatitude,
            referenceContext.AnchorLongitude);
        Tiles3DGeoidHeightOffsetValidator.ValidateOrThrow(resolvedGeoidHeightOffsetMeters);

        List<string> extractionWarnings = new List<string>();
        IReadOnlyCollection<Tiles3DMeshPrimitive> extracted = geometryExtractor.Extract(revitDocument, referenceContext, scope, extractionWarnings);
        IReadOnlyCollection<Tiles3DMeshPrimitive> simplified = geometrySimplifier.Simplify(extracted, levelOfDetail);
        if (simplified.Count == 0)
        {
            string warningText = extractionWarnings.Count == 0 ? string.Empty : $" Last warning: {extractionWarnings[0]}";
            throw new InvalidOperationException($"No exportable model geometry was found for the selected 3D Tiles scope.{warningText}");
        }

        List<Tiles3DMeshPrimitive> meshList = simplified.ToList();
        IReadOnlyList<Tiles3DLevelGroup> levelGroups = levelGrouper.Group(meshList);
        Tiles3DExportPackage package = BuildPackage(referenceContext, meshList, levelOfDetail, resolvedGeoidHeightOffsetMeters);

        if (usePreciseCrsProjection && preciseCrsRebaser is not null)
        {
            preciseCrsRebaser.Rebase(package);
        }

        return new Tiles3DExportPreparationResult
        {
            Package = package,
            PreparedRows = BuildPreparedRows(package, scope, levelGroups),
            FeatureNames = BuildFeatureNames(package),
            Warnings = extractionWarnings,
            StatusMessage = BuildStatusMessage(package, scope)
        };
    }

    internal static double ResolveGeoidOffset(double? requestedGeoidOffsetMeters, bool usePreciseCrsProjection, double anchorLatitudeDegrees, double anchorLongitudeDegrees)
    {
        return requestedGeoidOffsetMeters
            ?? (usePreciseCrsProjection
                ? Egm2008Geoid.GetUndulationMeters(anchorLatitudeDegrees, anchorLongitudeDegrees)
                : 0d);
    }

    internal static Tiles3DExportPackage BuildPackage(
        Tiles3DExportReferenceContext referenceContext,
        IReadOnlyCollection<Tiles3DMeshPrimitive> meshes,
        Tiles3DLevelOfDetail levelOfDetail,
        double geoidHeightOffsetMeters)
    {
        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        if (meshes is null)
        {
            throw new ArgumentNullException(nameof(meshes));
        }

        Tiles3DGeoidHeightOffsetValidator.ValidateOrThrow(geoidHeightOffsetMeters);
        List<Tiles3DMeshPrimitive> meshList = meshes.ToList();
        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            ReferenceContext = referenceContext.Copy(),
            LevelOfDetail = levelOfDetail,
            Meshes = meshList,
            ElementCount = meshList.Count,
            TriangleCount = meshList.Sum(mesh => mesh.Triangles.Count),
            GeometricError = CalculateGeometricError(meshList, levelOfDetail),
            BoundingBox = BuildBoundingBox(meshList),
            GeoidHeightOffsetMeters = geoidHeightOffsetMeters
        };

        // Apply the geoid undulation correction to the package-owned context only.
        // This lifts orthometric height (above sea level) to WGS84 ellipsoidal height.
        if (geoidHeightOffsetMeters != 0d)
        {
            package.ReferenceContext.AnchorElevationMeters += geoidHeightOffsetMeters;
        }

        return package;
    }

    public Tiles3DExportResult Export(
        IDocumentHandle document,
        Tiles3DExportPackage package,
        string outputDirectory,
        Tiles3DExportReferenceSource referenceSource,
        Tiles3DExportScopeSelection scope,
        Tiles3DExportState? existingState)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("3D Tiles export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("3D Tiles export is not supported in family documents.");
        }

        if (scope.ScopeMode == Tiles3DExportScopeMode.Selected3DView && !scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a non-template 3D view before exporting 3D Tiles from a selected view.");
        }

        (string tilesetPath, string contentPath) = packageWriter.Write(outputDirectory, package);
        Tiles3DExportState updatedState = BuildUpdatedState(existingState, outputDirectory, package, referenceSource, scope);
        bool statePersisted = false;

        if (!revitDocument.IsReadOnly)
        {
            using Transaction transaction = new Transaction(revitDocument, "Save 3D Tiles Export State");
            transaction.Start();
            stateService.Save(handle, updatedState);
            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("3D Tiles export completed, but Revit did not commit the export state transaction.");
            }

            statePersisted = true;
        }

        return new Tiles3DExportResult
        {
            UpdatedState = updatedState,
            TilesetPath = tilesetPath,
            ContentPath = contentPath,
            StatePersisted = statePersisted,
            SummaryMessage = BuildSummaryMessage(updatedState, package, statePersisted)
        };
    }

    internal static Tiles3DExportState BuildUpdatedState(
        Tiles3DExportState? existingState,
        string outputDirectory,
        Tiles3DExportPackage package,
        Tiles3DExportReferenceSource referenceSource,
        Tiles3DExportScopeSelection scope)
    {
        return new Tiles3DExportState
        {
            LastExportPath = outputDirectory,
            LastLodSetting = package.LevelOfDetail.ToString(),
            LastExportDateUtc = DateTime.UtcNow,
            LastReferenceSource = referenceSource,
            LastScopeMode = scope.ScopeMode,
            LastViewUniqueId = scope.SelectedView?.UniqueId ?? existingState?.LastViewUniqueId ?? string.Empty,
            LastViewName = scope.SelectedView?.Title ?? existingState?.LastViewName ?? string.Empty,
            LastSelectedLinkUniqueIds = scope.SelectedLinkedModels.Select(option => option.UniqueId).ToList(),
            LastSelectedLinkNames = scope.SelectedLinkedModels.Select(option => option.Title).ToList(),
            LastExportedElementCount = package.ElementCount,
            LastExportedTriangleCount = package.TriangleCount,
            LastSplitByLevel = false,
            LastExportedLevelCount = 0,
            LastUsedPreciseCrsProjection = package.UsedPreciseCrsProjection,
            LastGeoidHeightOffsetMeters = package.GeoidHeightOffsetMeters
        };
    }

    private static string BuildSummaryMessage(Tiles3DExportState state, Tiles3DExportPackage package, bool statePersisted)
    {
        string persistenceText = statePersisted
            ? "The export state was saved in module storage separately from GeoProjectInfo."
            : "The export state was not saved because the Revit document is read-only.";
        return $"Exported {package.ElementCount} elements and {package.TriangleCount} triangles to '{state.LastExportPath}' using {FormatReferenceSource(state.LastReferenceSource)} from {BuildScopeSummary(state.LastScopeMode, state.LastViewName)} with {BuildLinkSummary(state.LastSelectedLinkNames)}. Wrote per-object metadata and {package.LevelManifestFileName}. {persistenceText}";
    }

    private static string BuildStatusMessage(Tiles3DExportPackage package, Tiles3DExportScopeSelection scope)
    {
        return $"Prepared {package.ElementCount} exportable elements and {package.TriangleCount} triangles with per-object metadata for 3D Tiles export from {BuildScopeSummary(scope.ScopeMode, scope.SelectedView?.Title)} with {BuildLinkSummary(scope.SelectedLinkedModelNames)} using {package.ReferenceContext.Title}.";
    }

    private static string FormatGeoidOffset(double offsetMeters)
    {
        return offsetMeters == 0d ? "0 m (not set)" : $"{offsetMeters:+0.###;-0.###} m";
    }

    private static string FormatReferenceSource(Tiles3DExportReferenceSource referenceSource)
    {
        return referenceSource == Tiles3DExportReferenceSource.WorkingProjectBasePoint
            ? "Working Project Base Point"
            : "Canonical Origin";
    }

    private static string FormatScopeMode(Tiles3DExportScopeMode scopeMode)
    {
        return scopeMode == Tiles3DExportScopeMode.Selected3DView
            ? "Selected 3D View"
            : "Whole Host Model";
    }

    private static string BuildScopeSummary(Tiles3DExportScopeMode scopeMode, string? viewName)
    {
        if (scopeMode != Tiles3DExportScopeMode.Selected3DView)
        {
            return "the whole host model";
        }

        return string.IsNullOrWhiteSpace(viewName)
            ? "the selected 3D view"
            : $"view '{viewName}'";
    }

    private static string BuildLinkSummary(IEnumerable<string> linkNames)
    {
        string[] names = linkNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            return "host model only";
        }

        if (names.Length <= 3)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.Take(3)) + $" (+{names.Length - 3} more)";
    }

    private static double CalculateGeometricError(IReadOnlyCollection<Tiles3DMeshPrimitive> meshes, Tiles3DLevelOfDetail levelOfDetail)
    {
        double[] bounds = BuildBoundingBox(meshes);
        double maxExtent = Math.Max(bounds[3] * 2d, Math.Max(bounds[7] * 2d, bounds[11] * 2d));
        int stride = Tiles3DGeometrySimplifier.GetTriangleStride(levelOfDetail);
        return Math.Round(maxExtent / stride, 3);
    }

    private static double[] BuildBoundingBox(IReadOnlyCollection<Tiles3DMeshPrimitive> meshes)
    {
        IEnumerable<Tiles3DPoint> points = meshes.SelectMany(mesh => mesh.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }));
        double minX = points.Min(point => point.X);
        double minY = points.Min(point => point.Y);
        double minZ = points.Min(point => point.Z);
        double maxX = points.Max(point => point.X);
        double maxY = points.Max(point => point.Y);
        double maxZ = points.Max(point => point.Z);
        double centerX = (minX + maxX) / 2d;
        double centerY = (minY + maxY) / 2d;
        double centerZ = (minZ + maxZ) / 2d;
        double halfX = Math.Max((maxX - minX) / 2d, 0.01d);
        double halfY = Math.Max((maxY - minY) / 2d, 0.01d);
        double halfZ = Math.Max((maxZ - minZ) / 2d, 0.01d);

        return new[]
        {
            centerX, centerY, centerZ,
            halfX, 0d, 0d,
            0d, halfY, 0d,
            0d, 0d, halfZ
        };
    }

    private static IReadOnlyCollection<DetailRow> BuildPreparedRows(
        Tiles3DExportPackage package,
        Tiles3DExportScopeSelection scope,
        IReadOnlyList<Tiles3DLevelGroup> levelGroups)
    {
        List<DetailRow> rows = new List<DetailRow>
        {
            new DetailRow("Export Reference", package.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{package.ReferenceContext.ProjectCrs.EpsgCode}  {package.ReferenceContext.ProjectCrs.NameSnapshot}"),
            new DetailRow("Anchor Location", $"{package.ReferenceContext.AnchorLatitude:F6}, {package.ReferenceContext.AnchorLongitude:F6}, elev {package.ReferenceContext.AnchorElevationMeters:F3} m"),
            new DetailRow("Source Scope", FormatScopeMode(scope.ScopeMode)),
            new DetailRow("Source View", scope.ScopeMode == Tiles3DExportScopeMode.Selected3DView ? scope.SelectedView?.Title ?? "Not selected" : "Not required"),
            new DetailRow("Linked Models", BuildLinkSummary(scope.SelectedLinkedModelNames)),
            new DetailRow("Exportable Elements", package.ElementCount.ToString()),
            new DetailRow("Exportable Triangles", package.TriangleCount.ToString()),
            new DetailRow("Level Groups", levelGroups.Count.ToString()),
            new DetailRow("Object Metadata", "Enabled"),
            new DetailRow("Geometric Error", package.GeometricError.ToString("F3")),
            new DetailRow("Geoid Offset", FormatGeoidOffset(package.GeoidHeightOffsetMeters)),
            new DetailRow("Precise CRS", package.UsedPreciseCrsProjection ? "Enabled" : "Disabled"),
            new DetailRow("Tileset File", "tileset.json"),
            new DetailRow("Content File", package.ContentFileName),
            new DetailRow("Level Manifest", package.LevelManifestFileName)
        };

        foreach (Tiles3DLevelGroup group in levelGroups)
        {
            rows.Add(new DetailRow($"  {group.LevelName}", $"{group.ElementCount} elements"));
        }

        return rows;
    }

    private static IReadOnlyCollection<string> BuildFeatureNames(Tiles3DExportPackage package)
    {
        List<string> names = package.Meshes
            .Select(mesh => mesh.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(24)
            .ToList();

        if (package.Meshes.Count > names.Count)
        {
            names.Add($"... and {package.Meshes.Count - names.Count} more");
        }

        return names;
    }
}
