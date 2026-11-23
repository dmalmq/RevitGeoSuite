using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// Creates Revit DirectShape elements from a PLATEAU 3D Tiles or OSM Buildings dataset.
/// Coordinates in the input model are project-CRS metres; this importer converts them to
/// internal feet, scopes re-import replacement strictly per (area, dataset, LOD, grid),
/// and batches DirectShape creation into per-tile/grid transactions inside a
/// <see cref="TransactionGroup"/> when invoked outside an active transaction.
/// </summary>
public sealed class PlateauTilesImporter
{
    internal const string ApplicationId = "RevitGeoSuite.PlateauImport.Online";
    internal const int DefaultMaxFeaturesPerBatch = 250;

    public PlateauTilesImporterResult Import(
        Document document,
        PlateauTilesetModel buildings,
        PlateauOnlineGeometryMode mode)
    {
        return Import(document, buildings, mode, CancellationToken.None);
    }

    public PlateauTilesImporterResult Import(
        Document document,
        PlateauTilesetModel buildings,
        PlateauOnlineGeometryMode mode,
        CancellationToken cancellationToken)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (buildings is null) throw new ArgumentNullException(nameof(buildings));

        IReadOnlyList<OnlineImportBatch> batches = PartitionIntoBatches(buildings, DefaultMaxFeaturesPerBatch);
        List<string> warnings = new List<string>();
        int importedCount = 0;
        int groupCount = 0;

        if (!document.IsModifiable)
        {
            if (document.IsFamilyDocument)
            {
                throw new InvalidOperationException("PLATEAU online import is not supported in family documents.");
            }

            if (document.IsReadOnly)
            {
                throw new InvalidOperationException("This Revit document is read-only. PLATEAU online import requires an editable project.");
            }

            using TransactionGroup transactionGroup = new TransactionGroup(document, "Import PLATEAU Online");
            transactionGroup.Start();
            try
            {
                using (Transaction deleteTx = new Transaction(document, "Replace PLATEAU Online Imports"))
                {
                    deleteTx.Start();
                    DeleteMatchingExistingImports(document, buildings);
                    deleteTx.Commit();
                }

                HashSet<string> existingGroupNames = GetExistingGroupNames(document);
                ElementId categoryId = ResolveCategoryId(document, warnings);
                PlateauImportMaterialFactory materialFactory = new PlateauImportMaterialFactory();

                for (int i = 0; i < batches.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    OnlineImportBatch batch = batches[i];

                    using Transaction batchTx = new Transaction(document, BuildBatchTransactionName(buildings, batch));
                    batchTx.Start();
                    ElementId buildingMaterialId = materialFactory.GetMaterialId(document, PlateauFeatureType.Building);
                    (int batchImported, int batchGroups) = ImportBatchInActiveTransaction(
                        document,
                        buildings,
                        batch,
                        mode,
                        categoryId,
                        buildingMaterialId,
                        existingGroupNames,
                        warnings);
                    batchTx.Commit();

                    importedCount += batchImported;
                    groupCount += batchGroups;
                }

                transactionGroup.Assimilate();
                return new PlateauTilesImporterResult(importedCount, groupCount, warnings);
            }
            catch
            {
                if (transactionGroup.GetStatus() == TransactionStatus.Started)
                {
                    transactionGroup.RollBack();
                }

                throw;
            }
        }

        // Caller already owns an active transaction (e.g. legacy handler wrapper): perform the same
        // strictly scoped delete and per-grid/tile batch creation + grouping within it.
        DeleteMatchingExistingImports(document, buildings);
        HashSet<string> activeGroupNames = GetExistingGroupNames(document);
        ElementId activeCategoryId = ResolveCategoryId(document, warnings);
        PlateauImportMaterialFactory activeMaterialFactory = new PlateauImportMaterialFactory();
        ElementId activeBuildingMaterialId = activeMaterialFactory.GetMaterialId(document, PlateauFeatureType.Building);

        for (int i = 0; i < batches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int batchImported, int batchGroups) = ImportBatchInActiveTransaction(
                document,
                buildings,
                batches[i],
                mode,
                activeCategoryId,
                activeBuildingMaterialId,
                activeGroupNames,
                warnings);
            importedCount += batchImported;
            groupCount += batchGroups;
        }

        return new PlateauTilesImporterResult(importedCount, groupCount, warnings);
    }

    private static (int ImportedCount, int GroupCount) ImportBatchInActiveTransaction(
        Document document,
        PlateauTilesetModel buildings,
        OnlineImportBatch batch,
        PlateauOnlineGeometryMode mode,
        ElementId categoryId,
        ElementId buildingMaterialId,
        ISet<string> existingGroupNames,
        ICollection<string> warnings)
    {
        List<ElementId> createdElements = new List<ElementId>(batch.Features.Count);
        foreach (PlateauTilesetFeature feature in batch.Features)
        {
            try
            {
                IList<GeometryObject>? geometry = BuildTessellatedGeometry(feature, warnings, buildingMaterialId);
                if (geometry is null || geometry.Count == 0) continue;

                DirectShape directShape = DirectShape.CreateElement(document, categoryId);
                directShape.ApplicationId = ApplicationId;
                directShape.ApplicationDataId = SerializeMetadata(buildings, feature, mode);
                directShape.Name = BuildElementName(feature, buildings);
                directShape.SetShape(geometry);
                createdElements.Add(directShape.Id);
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped {feature.Id}: {ex.Message}");
            }
        }

        int groupCount = 0;
        if (createdElements.Count > 0)
        {
            try
            {
                Group group = document.Create.NewGroup(createdElements);
                string preferred = BuildPreferredGroupName(buildings, batch, mode);
                string uniqueName = MakeUniqueGroupName(existingGroupNames, preferred);
                group.GroupType.Name = uniqueName;
                existingGroupNames.Add(uniqueName);
                groupCount = 1;
            }
            catch (Exception ex)
            {
                warnings.Add($"Imported {createdElements.Count} elements but grouping failed: {ex.Message}");
            }
        }

        return (createdElements.Count, groupCount);
    }

    internal static string SerializeMetadata(
        PlateauTilesetModel buildings,
        PlateauTilesetFeature feature,
        PlateauOnlineGeometryMode mode)
    {
        PlateauOnlineImportMetadata metadata = new PlateauOnlineImportMetadata
        {
            GmlId = feature.Id,
            FeatureType = feature.GetStringAttribute("feature_type") ?? "bldg:Building",
            SourceUrl = buildings.SourceUrl,
            AreaCode = buildings.AreaCode,
            Dataset = buildings.TypeEn,
            Lod = buildings.Lod,
            GridId = feature.GridId,
            Mode = mode.ToString()
        };
        return JsonConvert.SerializeObject(metadata);
    }

    internal static bool TryReadMetadata(string? json, out PlateauOnlineImportMetadata? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            metadata = JsonConvert.DeserializeObject<PlateauOnlineImportMetadata>(json!);
            return metadata is not null;
        }
        catch
        {
            return false;
        }
    }

    internal static HashSet<string> CollectIncomingGridIds(PlateauTilesetModel buildings)
    {
        HashSet<string> gridIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlateauTilesetFeature feature in buildings.Features)
        {
            string normalized = NormalizeScopeToken(feature.GridId);
            if (normalized.Length > 0)
            {
                gridIds.Add(normalized);
            }
        }

        return gridIds;
    }

    /// <summary>
    /// Determines whether an existing online-imported DirectShape falls within the replacement scope
    /// of <paramref name="incoming"/>: strictly matching <c>(area, dataset, LOD, grid)</c>.
    /// Null/empty <c>AreaCode</c> (e.g. OSM Buildings) matches null/empty <c>AreaCode</c> instead of
    /// failing string-concatenation comparisons.
    /// </summary>
    internal static bool MatchesReplacementScope(
        PlateauOnlineImportMetadata existing,
        PlateauTilesetModel incoming,
        ISet<string> incomingGridIds)
    {
        if (existing is null) throw new ArgumentNullException(nameof(existing));
        if (incoming is null) throw new ArgumentNullException(nameof(incoming));
        if (incomingGridIds is null) throw new ArgumentNullException(nameof(incomingGridIds));

        // 1) Area code (handles null == null for OSM Buildings).
        string existingArea = NormalizeScopeToken(existing.AreaCode);
        string incomingArea = NormalizeScopeToken(incoming.AreaCode);
        if (!string.Equals(existingArea, incomingArea, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2) Dataset (TypeEn). Legacy shapes written before "dataset" existed fall back to SourceUrl.
        string existingDataset = NormalizeScopeToken(existing.Dataset);
        string incomingDataset = NormalizeScopeToken(incoming.TypeEn);
        if (existingDataset.Length > 0)
        {
            if (!string.Equals(existingDataset, incomingDataset, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else
        {
            string existingSourceUrl = NormalizeScopeToken(existing.SourceUrl);
            string incomingSourceUrl = NormalizeScopeToken(incoming.SourceUrl);
            if (existingSourceUrl.Length > 0
                && incomingSourceUrl.Length > 0
                && !string.Equals(existingSourceUrl, incomingSourceUrl, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3) LOD.
        string existingLod = NormalizeScopeToken(existing.Lod);
        string incomingLod = NormalizeScopeToken(incoming.Lod);
        if (!string.Equals(existingLod, incomingLod, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 4) Grid / tile scope: importing grid B must NEVER delete grid A.
        string existingGrid = NormalizeScopeToken(existing.GridId);
        if (incomingGridIds.Count > 0)
        {
            return existingGrid.Length > 0 && incomingGridIds.Contains(existingGrid);
        }

        // Incoming model has no grid IDs at all (e.g. synthetic or ungridded OSM model): only replace
        // prior ungridded elements of the exact same (area, dataset, LOD) scope.
        return existingGrid.Length == 0;
    }

    internal static IReadOnlyList<OnlineImportBatch> PartitionIntoBatches(
        PlateauTilesetModel buildings,
        int maxFeaturesPerBatch = DefaultMaxFeaturesPerBatch)
    {
        if (buildings is null) throw new ArgumentNullException(nameof(buildings));
        if (maxFeaturesPerBatch < 1) throw new ArgumentOutOfRangeException(nameof(maxFeaturesPerBatch));

        if (buildings.Features.Count == 0)
        {
            return Array.Empty<OnlineImportBatch>();
        }

        List<OnlineImportBatch> batches = new List<OnlineImportBatch>();
        IEnumerable<IGrouping<string, PlateauTilesetFeature>> byGrid = buildings.Features
            .GroupBy(feature => NormalizeScopeToken(feature.GridId), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, PlateauTilesetFeature> gridGroup in byGrid)
        {
            List<PlateauTilesetFeature> gridFeatures = gridGroup.ToList();
            int totalChunks = (gridFeatures.Count + maxFeaturesPerBatch - 1) / maxFeaturesPerBatch;
            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                int start = chunkIndex * maxFeaturesPerBatch;
                int count = Math.Min(maxFeaturesPerBatch, gridFeatures.Count - start);
                List<PlateauTilesetFeature> slice = gridFeatures.GetRange(start, count);
                batches.Add(new OnlineImportBatch(gridGroup.Key, chunkIndex + 1, totalChunks, slice));
            }
        }

        return batches;
    }

    private static string NormalizeScopeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static IList<GeometryObject>? BuildTessellatedGeometry(PlateauTilesetFeature feature, ICollection<string> warnings, ElementId materialId)
    {
        if (feature.Triangles.Count == 0)
        {
            warnings.Add($"Skipped {feature.Id}: feature contained no triangles.");
            return null;
        }

        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
        builder.OpenConnectedFaceSet(false);
        int facesAdded = 0;
        foreach (PlateauTilesetTriangle tri in feature.Triangles)
        {
            XYZ a = ToFeet(tri.A);
            XYZ b = ToFeet(tri.B);
            XYZ c = ToFeet(tri.C);
            if (IsDegenerate(a, b, c)) continue;
            builder.AddFace(new TessellatedFace(new List<XYZ> { a, b, c }, materialId));
            facesAdded++;
        }
        builder.CloseConnectedFaceSet();

        if (facesAdded == 0)
        {
            warnings.Add($"Skipped {feature.Id}: all triangles were degenerate after unit conversion.");
            return null;
        }

        builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
        builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
        builder.Build();
        return builder.GetBuildResult().GetGeometricalObjects();
    }

    private static ElementId ResolveCategoryId(Document document, ICollection<string> warnings)
    {
        ElementId preferred = new ElementId(BuiltInCategory.OST_GenericModel);
        if (DirectShape.IsValidCategoryId(preferred, document)) return preferred;
        warnings.Add("OST_GenericModel was not valid for DirectShape; falling back to default.");
        return preferred;
    }

    private static void DeleteMatchingExistingImports(Document document, PlateauTilesetModel buildings)
    {
        HashSet<string> incomingGridIds = CollectIncomingGridIds(buildings);
        Dictionary<long, HashSet<long>> matchedMembersByGroupId = new Dictionary<long, HashSet<long>>();
        HashSet<long> ungroupedIds = new HashSet<long>();

        foreach (DirectShape directShape in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<DirectShape>())
        {
            if (!string.Equals(directShape.ApplicationId, ApplicationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadMetadata(directShape.ApplicationDataId, out PlateauOnlineImportMetadata? metadata) || metadata is null)
            {
                continue;
            }

            if (!MatchesReplacementScope(metadata, buildings, incomingGridIds))
            {
                continue;
            }

            if (directShape.GroupId != ElementId.InvalidElementId)
            {
                long groupIdValue = directShape.GroupId.Value;
                if (!matchedMembersByGroupId.TryGetValue(groupIdValue, out HashSet<long>? memberIds))
                {
                    memberIds = new HashSet<long>();
                    matchedMembersByGroupId[groupIdValue] = memberIds;
                }

                memberIds.Add(directShape.Id.Value);
            }
            else
            {
                ungroupedIds.Add(directShape.Id.Value);
            }
        }

        List<ElementId> groupIdsToDelete = new List<ElementId>();
        List<ElementId> directShapeIdsToDelete = ungroupedIds.Select(id => new ElementId(id)).ToList();

        foreach (KeyValuePair<long, HashSet<long>> entry in matchedMembersByGroupId)
        {
            ElementId groupId = new ElementId(entry.Key);
            Group? group = document.GetElement(groupId) as Group;
            ICollection<ElementId>? allMemberIds = group?.GetMemberIds();
            if (allMemberIds is not null
                && allMemberIds.Count > 0
                && allMemberIds.All(memberId => entry.Value.Contains(memberId.Value)))
            {
                // Every member in this group belongs to the replaced scope — delete the group cleanly.
                groupIdsToDelete.Add(groupId);
            }
            else
            {
                // Partial match inside a legacy multi-grid group: only delete the matching members.
                foreach (long memberId in entry.Value)
                {
                    directShapeIdsToDelete.Add(new ElementId(memberId));
                }
            }
        }

        if (groupIdsToDelete.Count > 0)
        {
            document.Delete(groupIdsToDelete);
        }

        if (directShapeIdsToDelete.Count > 0)
        {
            document.Delete(directShapeIdsToDelete);
        }
    }

    private static string BuildBatchTransactionName(PlateauTilesetModel buildings, OnlineImportBatch batch)
    {
        string scope = string.IsNullOrWhiteSpace(batch.GridId)
            ? (string.IsNullOrWhiteSpace(buildings.AreaCode) ? buildings.TypeEn : buildings.AreaCode!)
            : batch.GridId;
        return batch.TotalChunks > 1
            ? $"Import PLATEAU Online {scope} ({batch.ChunkNumber}/{batch.TotalChunks})"
            : $"Import PLATEAU Online {scope}";
    }

    private static string BuildPreferredGroupName(PlateauTilesetModel buildings, OnlineImportBatch batch, PlateauOnlineGeometryMode mode)
    {
        List<string> parts = new List<string> { "PLATEAU Online" };
        if (!string.IsNullOrWhiteSpace(buildings.AreaCode))
        {
            parts.Add(buildings.AreaCode!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(buildings.TypeEn))
        {
            parts.Add(buildings.TypeEn.Trim());
        }

        if (!string.IsNullOrWhiteSpace(buildings.Lod))
        {
            parts.Add("LOD" + buildings.Lod!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(batch.GridId))
        {
            parts.Add(batch.GridId);
        }

        string baseName = string.Join(" ", parts);
        if (batch.TotalChunks > 1)
        {
            baseName = string.Format(CultureInfo.InvariantCulture, "{0} Part {1}", baseName, batch.ChunkNumber);
        }

        return SanitizeRevitName($"{baseName} ({mode})");
    }

    private static string BuildElementName(PlateauTilesetFeature feature, PlateauTilesetModel buildings)
    {
        string baseName = feature.GetStringAttribute("feature_type") ?? buildings.TypeEn;
        return SanitizeRevitName($"{baseName} {feature.Id}");
    }

    private static readonly char[] RevitProhibitedNameChars = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

    private static string SanitizeRevitName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        char[] buffer = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            buffer[i] = Array.IndexOf(RevitProhibitedNameChars, c) >= 0 ? '_' : c;
        }
        return new string(buffer);
    }

    private static HashSet<string> GetExistingGroupNames(Document document)
    {
        return new HashSet<string>(
            new FilteredElementCollector(document).OfClass(typeof(GroupType)).Cast<GroupType>().Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string MakeUniqueGroupName(ISet<string> existing, string preferred)
    {
        if (!existing.Contains(preferred)) return preferred;
        for (int i = 2; i < 1000; i++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", preferred, i);
            if (!existing.Contains(candidate)) return candidate;
        }
        return preferred + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private static bool IsDegenerate(XYZ a, XYZ b, XYZ c) => (b - a).CrossProduct(c - a).GetLength() <= 1e-8;

    private static double MetersToFeet(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

    private static XYZ ToFeet(Core.Plateau.Tiles3D.Vector3d v) => new XYZ(MetersToFeet(v.X), MetersToFeet(v.Y), MetersToFeet(v.Z));

    internal sealed class PlateauOnlineImportMetadata
    {
        [JsonProperty("gml_id")]
        public string? GmlId { get; set; }

        [JsonProperty("feature_type")]
        public string? FeatureType { get; set; }

        [JsonProperty("source_url")]
        public string? SourceUrl { get; set; }

        [JsonProperty("area_code")]
        public string? AreaCode { get; set; }

        [JsonProperty("dataset")]
        public string? Dataset { get; set; }

        [JsonProperty("lod")]
        public string? Lod { get; set; }

        [JsonProperty("grid_id")]
        public string? GridId { get; set; }

        [JsonProperty("mode")]
        public string? Mode { get; set; }
    }

    internal sealed class OnlineImportBatch
    {
        public OnlineImportBatch(
            string gridId,
            int chunkNumber,
            int totalChunks,
            IReadOnlyList<PlateauTilesetFeature> features)
        {
            GridId = gridId;
            ChunkNumber = chunkNumber;
            TotalChunks = totalChunks;
            Features = features;
        }

        public string GridId { get; }

        public int ChunkNumber { get; }

        public int TotalChunks { get; }

        public IReadOnlyList<PlateauTilesetFeature> Features { get; }
    }
}
