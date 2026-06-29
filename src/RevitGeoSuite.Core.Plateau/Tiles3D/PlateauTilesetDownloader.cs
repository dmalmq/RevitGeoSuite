using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class PlateauTilesetDownloadProgress
{
    public PlateauTilesetDownloadProgress(int completed, int total, string currentItem)
    {
        Completed = completed;
        Total = total;
        CurrentItem = currentItem;
    }

    public int Completed { get; }
    public int Total { get; }
    public string CurrentItem { get; }
    public double Fraction => Total > 0 ? (double)Completed / Total : 0;
}

public sealed class PlateauTilesetDownloader
{
    private readonly IPlateauHttpClient http;
    private readonly TilesetWalker walker;
    private readonly GltfMeshDecoder meshDecoder;
    private readonly EcefToProjectTransformer ecefTransformer;
    private readonly PlateauTilesetCache cache;
    private readonly List<string> warnings = new();

    public PlateauTilesetDownloader(
        IPlateauHttpClient http,
        GltfMeshDecoder meshDecoder,
        EcefToProjectTransformer ecefTransformer,
        PlateauTilesetCache? cache = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        walker = new TilesetWalker(http);
        this.meshDecoder = meshDecoder ?? throw new ArgumentNullException(nameof(meshDecoder));
        this.ecefTransformer = ecefTransformer ?? throw new ArgumentNullException(nameof(ecefTransformer));
        this.cache = cache ?? new PlateauTilesetCache();
    }

    public IReadOnlyList<string> Warnings => warnings;

    public async Task<PlateauTilesetModel> DownloadAsync(
        PlateauDatasetEntry entry,
        string areaCode,
        IProgress<PlateauTilesetDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await DownloadAsync(entry, areaCode, selectedMeshIds: null, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlateauTilesetModel> DownloadAsync(
        Uri tilesetUrl,
        string sourceLabel,
        BoundingBoxDegrees? bbox,
        IProgress<PlateauTilesetDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (tilesetUrl is null) throw new ArgumentNullException(nameof(tilesetUrl));

        string datasetFolder = Path.Combine(
            cache.GetDatasetFolder("ion", sourceLabel.Replace(' ', '-').ToLowerInvariant(), null, null));

        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(tilesetUrl, cancellationToken).ConfigureAwait(false);
        if (bbox.HasValue)
        {
            BoundingBoxDegrees b = bbox.Value;
            leaves = leaves
                .Where(leaf => ShouldDownloadLeafByBbox(leaf, b))
                .ToArray();
        }

        Dictionary<string, PlateauTilesetFeatureBuilder> byId = new(StringComparer.Ordinal);

        for (int i = 0; i < leaves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TilesetLeaf leaf = leaves[i];
            progress?.Report(new PlateauTilesetDownloadProgress(i, leaves.Count, leaf.B3dmUrl.AbsoluteUri));

            byte[] b3dmBytes;
            string cachePath = cache.GetFilePath(datasetFolder, leaf.B3dmUrl);
            if (File.Exists(cachePath))
            {
                b3dmBytes = File.ReadAllBytes(cachePath);
            }
            else
            {
                b3dmBytes = await http.GetBytesAsync(leaf.B3dmUrl, cancellationToken).ConfigureAwait(false);
                cache.Store(cachePath, b3dmBytes);
            }

            B3dmContents b3dm;
            IReadOnlyList<PlateauTilesetFeatureMesh> meshes;
            try
            {
                b3dm = B3dmParser.Parse(b3dmBytes);
                meshes = meshDecoder.Decode(b3dm);
            }
            catch (DracoDecoderUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped {leaf.B3dmUrl.AbsoluteUri}: {ex.Message}");
                continue;
            }

            ProjectFeatures(leaf.Transform, meshes, byId);
        }

        progress?.Report(new PlateauTilesetDownloadProgress(leaves.Count, leaves.Count, "completed"));

        List<PlateauTilesetFeature> features = new(byId.Count);
        foreach (var pair in byId)
        {
            features.Add(new PlateauTilesetFeature(pair.Key, pair.Value.Attributes, pair.Value.Triangles));
        }
        return new PlateauTilesetModel(tilesetUrl.AbsoluteUri, sourceLabel, null, null, null, features);
    }

    public async Task<PlateauTilesetModel> DownloadAsync(
        PlateauDatasetEntry entry,
        string areaCode,
        IReadOnlyCollection<string>? selectedMeshIds,
        IProgress<PlateauTilesetDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        string? sourceUrl = entry.PreferredUrl ?? throw new InvalidOperationException("Dataset entry has no URL.");
        Uri tilesetUri = new Uri(sourceUrl);
        string datasetFolder = cache.GetDatasetFolder(areaCode, entry.TypeEn ?? "unknown", entry.Lod, entry.Texture);

        IReadOnlyList<TilesetLeaf> leaves = await walker.WalkAsync(tilesetUri, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SelectedMeshBounds> selectedBounds = BuildSelectedMeshBounds(selectedMeshIds);
        if (selectedBounds.Count > 0)
        {
            leaves = leaves
                .Where(leaf => ShouldDownloadLeaf(leaf, selectedBounds))
                .ToArray();
        }

        Dictionary<string, PlateauTilesetFeatureBuilder> byId = new(StringComparer.Ordinal);

        for (int i = 0; i < leaves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TilesetLeaf leaf = leaves[i];
            progress?.Report(new PlateauTilesetDownloadProgress(i, leaves.Count, leaf.B3dmUrl.AbsoluteUri));

            byte[] b3dmBytes;
            string cachePath = cache.GetFilePath(datasetFolder, leaf.B3dmUrl);
            if (File.Exists(cachePath))
            {
                b3dmBytes = File.ReadAllBytes(cachePath);
            }
            else
            {
                b3dmBytes = await http.GetBytesAsync(leaf.B3dmUrl, cancellationToken).ConfigureAwait(false);
                cache.Store(cachePath, b3dmBytes);
            }

            B3dmContents b3dm;
            IReadOnlyList<PlateauTilesetFeatureMesh> meshes;
            try
            {
                b3dm = B3dmParser.Parse(b3dmBytes);
                meshes = meshDecoder.Decode(b3dm);
            }
            catch (DracoDecoderUnavailableException)
            {
                throw; // Propagate so the UI can show the install instructions clearly.
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped {leaf.B3dmUrl.AbsoluteUri}: {ex.Message}");
                continue;
            }

            ProjectFeatures(leaf.Transform, meshes, byId);
        }

        progress?.Report(new PlateauTilesetDownloadProgress(leaves.Count, leaves.Count, "completed"));

        List<PlateauTilesetFeature> features = new(byId.Count);
        foreach (var pair in byId)
        {
            features.Add(new PlateauTilesetFeature(pair.Key, pair.Value.Attributes, pair.Value.Triangles));
        }
        return new PlateauTilesetModel(sourceUrl, entry.TypeEn ?? string.Empty, entry.Lod, entry.Texture, areaCode, features);
    }

    private static IReadOnlyList<SelectedMeshBounds> BuildSelectedMeshBounds(IReadOnlyCollection<string>? selectedMeshIds)
    {
        if (selectedMeshIds is null || selectedMeshIds.Count == 0)
        {
            return Array.Empty<SelectedMeshBounds>();
        }

        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        List<SelectedMeshBounds> result = new List<SelectedMeshBounds>();
        foreach (string raw in selectedMeshIds)
        {
            string meshId = raw?.Trim() ?? string.Empty;
            if (meshId.Length == 0)
            {
                continue;
            }

            try
            {
                MeshBounds bounds = meshCalculator.GetBounds(new MeshCode { Value = meshId });
                result.Add(new SelectedMeshBounds(
                    meshId,
                    bounds.WestLongitude,
                    bounds.SouthLatitude,
                    bounds.EastLongitude,
                    bounds.NorthLatitude));
            }
            catch
            {
                // Ignore malformed mesh IDs here; the handler validates user-facing payloads.
            }
        }

        return result;
    }

    private static bool ShouldDownloadLeafByBbox(TilesetLeaf leaf, BoundingBoxDegrees bbox)
    {
        if (leaf.BoundingRegion is not TilesetRegion region)
        {
            return true;
        }

        return region.IntersectsDegrees(
            bbox.WestLongitude,
            bbox.SouthLatitude,
            bbox.EastLongitude,
            bbox.NorthLatitude);
    }

    private static bool ShouldDownloadLeaf(TilesetLeaf leaf, IReadOnlyList<SelectedMeshBounds> selectedBounds)
    {
        if (leaf.BoundingRegion is not TilesetRegion region)
        {
            // Some tilesets only expose box/sphere bounds. Keep those leaves and let the post-download
            // feature filter enforce the selected mesh set instead of accidentally dropping data.
            return true;
        }

        return selectedBounds.Any(bounds => region.IntersectsDegrees(
            bounds.WestLongitude,
            bounds.SouthLatitude,
            bounds.EastLongitude,
            bounds.NorthLatitude));
    }

    private void ProjectFeatures(Matrix4x4d tileTransform, IReadOnlyList<PlateauTilesetFeatureMesh> meshes, Dictionary<string, PlateauTilesetFeatureBuilder> byId)
    {
        foreach (PlateauTilesetFeatureMesh feature in meshes)
        {
            string id = feature.GmlId ?? $"batch-{feature.BatchId}";
            if (!byId.TryGetValue(id, out PlateauTilesetFeatureBuilder? builder))
            {
                builder = new PlateauTilesetFeatureBuilder(feature.Attributes);
                byId[id] = builder;
            }
            foreach (PlateauTilesetTriangle tri in feature.Triangles)
            {
                Vector3d a = ProjectVertex(tri.A, tileTransform);
                Vector3d b = ProjectVertex(tri.B, tileTransform);
                Vector3d c = ProjectVertex(tri.C, tileTransform);
                builder.Triangles.Add(new PlateauTilesetTriangle(a, b, c));
            }
        }
    }

    private Vector3d ProjectVertex(Vector3d local, Matrix4x4d tileTransform)
    {
        Vector3d ecef = tileTransform.TransformPoint(local);
        return ecefTransformer.TransformEcefToProject(ecef);
    }

    private sealed class PlateauTilesetFeatureBuilder
    {
        public PlateauTilesetFeatureBuilder(IReadOnlyDictionary<string, object?> attributes)
        {
            Attributes = attributes;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; }
        public List<PlateauTilesetTriangle> Triangles { get; } = new();
    }

    private sealed class SelectedMeshBounds
    {
        public SelectedMeshBounds(
            string meshId,
            double westLongitude,
            double southLatitude,
            double eastLongitude,
            double northLatitude)
        {
            MeshId = meshId;
            WestLongitude = westLongitude;
            SouthLatitude = southLatitude;
            EastLongitude = eastLongitude;
            NorthLatitude = northLatitude;
        }

        public string MeshId { get; }

        public double WestLongitude { get; }

        public double SouthLatitude { get; }

        public double EastLongitude { get; }

        public double NorthLatitude { get; }
    }
}
