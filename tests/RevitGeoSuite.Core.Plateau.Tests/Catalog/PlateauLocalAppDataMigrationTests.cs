using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class PlateauLocalAppDataMigrationTests : IDisposable
{
    private readonly string testRoot;

    public PlateauLocalAppDataMigrationTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "kanizu-plateau-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task CityGmlPackageDownloader_ReusesLegacyRgsCacheWithoutRedownloading_AndPrefersNewFolderWhenPresent()
    {
        string newRoot = Path.Combine(testRoot, "KanizuRevit", "CityGmlDownloads");
        string legacyRoot = Path.Combine(testRoot, "RevitGeoSuite", "CityGmlDownloads");
        string url = "https://ex/r1/download/13107_sumida-ku_2025_citygml_building_lod2.zip";

        // Seed an existing legacy download with its .rgs-cache marker.
        byte[] zip = BuildZip(("pkg/udx/bldg/legacy.gml", "<gml/>"));
        StubHttpClient legacyHttp = new();
        legacyHttp.Files[new Uri(url)] = zip;
        CityGmlPackageDownloader legacyDownloader = new(legacyHttp, legacyRoot);
        CityGmlPackageDownloadResult legacySeed = await legacyDownloader.DownloadAsync(
            "13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku");
        Assert.Equal(1, legacyHttp.DownloadCount);
        Assert.True(Directory.Exists(Path.Combine(legacySeed.FolderPath, ".rgs-cache")));

        // Kanizu Revit downloader falls back to legacy folder and sees the .rgs-cache marker (zero re-downloads).
        StubHttpClient migratedHttp = new();
        migratedHttp.Files[new Uri(url)] = zip;
        CityGmlPackageDownloader migratedDownloader = new(migratedHttp, newRoot, legacyRoot);

        Assert.Equal(legacySeed.FolderPath, migratedDownloader.ResolvePackageFolder("13107", "2025", "Sumida-ku"));
        Assert.Equal(new[] { url }, migratedDownloader.GetAlreadyDownloaded("13107", "2025", "Sumida-ku", new[] { url }));

        CityGmlPackageDownloadResult reused = await migratedDownloader.DownloadAsync(
            "13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku");
        Assert.Equal(0, migratedHttp.DownloadCount);
        Assert.Equal(legacySeed.FolderPath, reused.FolderPath);

        // New municipalities not in legacy are written to the new KanizuRevit folder.
        string newUrl = "https://ex/r2/download/13113_shibuya-ku_2025_citygml_building_lod2.zip";
        migratedHttp.Files[new Uri(newUrl)] = BuildZip(("pkg/udx/bldg/new.gml", "<gml/>"));
        CityGmlPackageDownloadResult fresh = await migratedDownloader.DownloadAsync(
            "13113", "2025", new[] { newUrl }, null, CancellationToken.None, "Shibuya-ku");
        Assert.Equal(1, migratedHttp.DownloadCount);
        Assert.StartsWith(newRoot, fresh.FolderPath, StringComparison.Ordinal);

        // When a folder exists in both new and legacy roots, the new KanizuRevit folder wins.
        string newSumidaFolder = Path.Combine(newRoot, Path.GetFileName(legacySeed.FolderPath));
        Directory.CreateDirectory(newSumidaFolder);
        Assert.Equal(newSumidaFolder, migratedDownloader.ResolvePackageFolder("13107", "2025", "Sumida-ku"));
    }

    [Fact]
    public void PlateauTilesetCache_FallsBackToLegacyFolderWhenMissing_AndPrefersNewFolderWhenPresent()
    {
        string newRoot = Path.Combine(testRoot, "KanizuRevit", "PlateauOnline");
        string legacyRoot = Path.Combine(testRoot, "RevitGeoSuite", "PlateauOnline");
        Uri tileUrl = new("https://assets.cms.plateau.re.jp/13101/bldg/tile01.b3dm");

        PlateauTilesetCache legacyCache = new(legacyRoot);
        string legacyDataset = legacyCache.GetDatasetFolder("13101", "bldg", "2", texture: false);
        string legacyTilePath = legacyCache.GetFilePath(legacyDataset, tileUrl);
        legacyCache.Store(legacyTilePath, new byte[] { 1, 2, 3 });

        PlateauTilesetCache migratedCache = new(newRoot, legacyRoot);
        string newDataset = migratedCache.GetDatasetFolder("13101", "bldg", "2", texture: false);

        // Cache hit falls back to legacy RevitGeoSuite\PlateauOnline when missing in KanizuRevit.
        Assert.True(migratedCache.TryGet(newDataset, tileUrl, out string resolvedFromLegacy));
        Assert.Equal(legacyTilePath, resolvedFromLegacy);

        // Cache miss returns a path inside new KanizuRevit\PlateauOnline so Store writes new.
        Uri missingUrl = new("https://assets.cms.plateau.re.jp/13101/bldg/tile02.b3dm");
        Assert.False(migratedCache.TryGet(newDataset, missingUrl, out string targetNewPath));
        Assert.StartsWith(newRoot, targetNewPath, StringComparison.Ordinal);

        // Once stored in new KanizuRevit\PlateauOnline, reads prefer the new file over legacy.
        string newTilePath = migratedCache.GetFilePath(newDataset, tileUrl);
        migratedCache.Store(newTilePath, new byte[] { 9, 8, 7 });
        Assert.True(migratedCache.TryGet(newDataset, tileUrl, out string resolvedFromNew));
        Assert.Equal(newTilePath, resolvedFromNew);
    }

    [Fact]
    public async Task ResponseCache_FallsBackToLegacyCacheWhenMissing_AndPrefersNewCacheWhenPresent()
    {
        string newRoot = Path.Combine(testRoot, "KanizuRevit", "Cache");
        string legacyRoot = Path.Combine(testRoot, "RevitGeoSuite", "Cache");

        ResponseCache legacyCache = new(legacyRoot);
        await legacyCache.StoreAsync("catalog", "{\"source\":\"legacy\"}");

        ResponseCache migratedCache = new(newRoot, legacyRoot);

        Assert.Equal("{\"source\":\"legacy\"}", await migratedCache.TryGetAsync("catalog", TimeSpan.FromHours(1)));
        Assert.Equal("{\"source\":\"legacy\"}", migratedCache.TryGetStale("catalog"));

        await migratedCache.StoreAsync("catalog", "{\"source\":\"kanizu\"}");
        Assert.Equal("{\"source\":\"kanizu\"}", await migratedCache.TryGetAsync("catalog", TimeSpan.FromHours(1)));
        Assert.Equal("{\"source\":\"kanizu\"}", migratedCache.TryGetStale("catalog"));
    }

    private static byte[] BuildZip(params (string EntryPath, string Content)[] entries)
    {
        using MemoryStream ms = new();
        using (ZipArchive archive = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string entryPath, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryPath);
                using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    private sealed class StubHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, byte[]> Files { get; } = new();
        public int DownloadCount { get; private set; }

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            byte[] bytes = Files[url];
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, bytes);
            progress?.Report(1.0);
            return Task.CompletedTask;
        }
    }
}
