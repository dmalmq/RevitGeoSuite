using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportCoordinator
{
    private readonly Tiles3DGeometryExtractor geometryExtractor;
    private readonly Tiles3DGeometrySimplifier geometrySimplifier;
    private readonly Tiles3DPackageWriter packageWriter;
    private readonly Tiles3DExportStateService stateService;

    public Tiles3DExportCoordinator(
        Tiles3DGeometryExtractor? geometryExtractor = null,
        Tiles3DGeometrySimplifier? geometrySimplifier = null,
        Tiles3DPackageWriter? packageWriter = null,
        Tiles3DExportStateService? stateService = null)
    {
        this.geometryExtractor = geometryExtractor ?? new Tiles3DGeometryExtractor();
        this.geometrySimplifier = geometrySimplifier ?? new Tiles3DGeometrySimplifier();
        this.packageWriter = packageWriter ?? new Tiles3DPackageWriter();
        this.stateService = stateService ?? new Tiles3DExportStateService();
    }

    public Tiles3DExportPreparationResult Prepare(
        IDocumentHandle document,
        Tiles3DExportReferenceContext referenceContext,
        Tiles3DLevelOfDetail levelOfDetail)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("3D Tiles export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("3D Tiles export is not supported in family documents.");
        }

        IReadOnlyCollection<Tiles3DMeshPrimitive> extracted = geometryExtractor.Extract(revitDocument, referenceContext);
        IReadOnlyCollection<Tiles3DMeshPrimitive> simplified = geometrySimplifier.Simplify(extracted, levelOfDetail);
        if (simplified.Count == 0)
        {
            throw new InvalidOperationException("No exportable model geometry was found for the selected 3D Tiles reference context.");
        }

        Tiles3DExportPackage package = new Tiles3DExportPackage
        {
            ReferenceContext = referenceContext,
            LevelOfDetail = levelOfDetail,
            Meshes = simplified.ToList(),
            ElementCount = simplified.Count,
            TriangleCount = simplified.Sum(mesh => mesh.Triangles.Count),
            GeometricError = CalculateGeometricError(simplified, levelOfDetail),
            BoundingBox = BuildBoundingBox(simplified)
        };

        return new Tiles3DExportPreparationResult
        {
            Package = package,
            PreparedRows = BuildPreparedRows(package),
            FeatureNames = BuildFeatureNames(package),
            StatusMessage = $"Prepared {package.ElementCount} exportable elements and {package.TriangleCount} triangles for 3D Tiles export using {referenceContext.Title}."
        };
    }

    public Tiles3DExportResult Export(
        IDocumentHandle document,
        Tiles3DExportPackage package,
        string outputDirectory,
        Tiles3DExportReferenceSource referenceSource,
        Tiles3DExportState? existingState)
    {
        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("3D Tiles export requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("3D Tiles export is not supported in family documents.");
        }

        (string tilesetPath, string contentPath) = packageWriter.Write(outputDirectory, package);
        Tiles3DExportState updatedState = BuildUpdatedState(existingState, outputDirectory, package, referenceSource);
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
        Tiles3DExportReferenceSource referenceSource)
    {
        return new Tiles3DExportState
        {
            LastExportPath = outputDirectory,
            LastLodSetting = package.LevelOfDetail.ToString(),
            LastExportDateUtc = DateTime.UtcNow,
            LastReferenceSource = referenceSource,
            LastExportedElementCount = package.ElementCount,
            LastExportedTriangleCount = package.TriangleCount
        };
    }

    private static string BuildSummaryMessage(Tiles3DExportState state, Tiles3DExportPackage package, bool statePersisted)
    {
        string persistenceText = statePersisted
            ? "The export state was saved in module storage separately from GeoProjectInfo."
            : "The export state was not saved because the Revit document is read-only.";
        return $"Exported {package.ElementCount} elements and {package.TriangleCount} triangles to '{state.LastExportPath}' using {state.LastLodSetting} LOD and {FormatReferenceSource(state.LastReferenceSource)}. {persistenceText}";
    }

    private static string FormatReferenceSource(Tiles3DExportReferenceSource referenceSource)
    {
        return referenceSource == Tiles3DExportReferenceSource.WorkingProjectBasePoint
            ? "Working Project Base Point"
            : "Canonical Origin";
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

    private static IReadOnlyCollection<DetailRow> BuildPreparedRows(Tiles3DExportPackage package)
    {
        return new[]
        {
            new DetailRow("Export Reference", package.ReferenceContext.Title),
            new DetailRow("Reference CRS", $"EPSG:{package.ReferenceContext.ProjectCrs.EpsgCode}  {package.ReferenceContext.ProjectCrs.NameSnapshot}"),
            new DetailRow("Anchor Location", $"{package.ReferenceContext.AnchorLatitude:F6}, {package.ReferenceContext.AnchorLongitude:F6}, elev {package.ReferenceContext.AnchorElevationMeters:F3} m"),
            new DetailRow("LOD", package.LevelOfDetail.ToString()),
            new DetailRow("Exportable Elements", package.ElementCount.ToString()),
            new DetailRow("Exportable Triangles", package.TriangleCount.ToString()),
            new DetailRow("Geometric Error", package.GeometricError.ToString("F3")),
            new DetailRow("Tileset File", "tileset.json"),
            new DetailRow("Content File", package.ContentFileName)
        };
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

