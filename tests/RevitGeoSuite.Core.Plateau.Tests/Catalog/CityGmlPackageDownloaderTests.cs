using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class CityGmlPackageDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_strips_wrapper_so_udx_lands_at_package_root()
    {
        // A typical PLATEAU resource ZIP wraps everything in one package folder.
        byte[] zip = BuildZip(
            ("13113_shibuya-ku_2023_citygml_3_op/udx/bldg/foo.gml", "<gml/>"),
            ("13113_shibuya-ku_2023_citygml_3_op/codelists/bar.xml", "<x/>"));

        string url = "https://ex/dataset/p/resource/r1/download/13113_shibuya-ku_2023_citygml_building_lod2_op.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(url)] = zip;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);
            CityGmlPackageDownloadResult result = await downloader.DownloadAsync(
                "13113", "2023", new[] { url }, null, CancellationToken.None);

            // udx must sit directly under the returned package root (what PlateauFolderScanService expects).
            Assert.True(File.Exists(Path.Combine(result.FolderPath, "udx", "bldg", "foo.gml")));
            Assert.True(File.Exists(Path.Combine(result.FolderPath, "codelists", "bar.xml")));
            Assert.Equal(1, result.FilesExtracted);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    [Fact]
    public async Task DownloadAsync_merges_multiple_resources_into_one_root()
    {
        byte[] buildings = BuildZip(
            ("pkg/udx/bldg/a.gml", "<a/>"),
            ("pkg/codelists/shared.xml", "<c/>"));
        byte[] roads = BuildZip(
            ("pkg/udx/tran/b.gml", "<b/>"),
            ("pkg/codelists/shared.xml", "<c2/>")); // overlaps — must overwrite, not throw

        string bldgUrl = "https://ex/r1/download/13113_citygml_building_lod2.zip";
        string tranUrl = "https://ex/r2/download/13113_citygml_tran_lod1.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(bldgUrl)] = buildings;
        fake.Files[new Uri(tranUrl)] = roads;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);
            CityGmlPackageDownloadResult result = await downloader.DownloadAsync(
                "13113", "2023", new[] { bldgUrl, tranUrl }, null, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(result.FolderPath, "udx", "bldg", "a.gml")));
            Assert.True(File.Exists(Path.Combine(result.FolderPath, "udx", "tran", "b.gml")));
            Assert.True(File.Exists(Path.Combine(result.FolderPath, "codelists", "shared.xml")));
            Assert.Equal(2, result.FilesExtracted);
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    [Fact]
    public async Task DownloadAsync_skips_already_extracted_resource_on_second_run()
    {
        byte[] zip = BuildZip(("pkg/udx/bldg/foo.gml", "<gml/>"));
        string url = "https://ex/r1/download/13113_citygml_building_lod2.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(url)] = zip;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);
            await downloader.DownloadAsync("13113", "2023", new[] { url }, null, CancellationToken.None);
            Assert.Equal(1, fake.DownloadCount);

            await downloader.DownloadAsync("13113", "2023", new[] { url }, null, CancellationToken.None);
            Assert.Equal(1, fake.DownloadCount); // cached marker — no re-download
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    [Fact]
    public async Task DownloadAsync_includes_area_name_in_folder_when_provided()
    {
        byte[] zip = BuildZip(("pkg/udx/bldg/foo.gml", "<gml/>"));
        string url = "https://ex/r1/download/13107_sumida-ku_2025_citygml_building_lod2.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(url)] = zip;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);
            CityGmlPackageDownloadResult result = await downloader.DownloadAsync(
                "13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku");

            Assert.Equal("13107-2025 (Sumida-ku)", Path.GetFileName(result.FolderPath));
            Assert.True(File.Exists(Path.Combine(result.FolderPath, "udx", "bldg", "foo.gml")));
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    [Fact]
    public async Task GetAlreadyDownloaded_reports_only_extracted_resources()
    {
        byte[] zip = BuildZip(("pkg/udx/bldg/foo.gml", "<gml/>"));
        string url = "https://ex/r1/download/13107_sumida-ku_2025_citygml_building_lod2.zip";
        string otherUrl = "https://ex/r2/download/13107_sumida-ku_2025_citygml_tran_lod1.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(url)] = zip;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);

            // Nothing downloaded yet.
            Assert.Empty(downloader.GetAlreadyDownloaded("13107", "2025", "Sumida-ku", new[] { url, otherUrl }));

            await downloader.DownloadAsync("13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku");

            IReadOnlyList<string> existing =
                downloader.GetAlreadyDownloaded("13107", "2025", "Sumida-ku", new[] { url, otherUrl });
            Assert.Equal(new[] { url }, existing.ToArray());
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    [Fact]
    public async Task DownloadAsync_force_redownloads_existing_resource()
    {
        byte[] zip = BuildZip(("pkg/udx/bldg/foo.gml", "<gml/>"));
        string url = "https://ex/r1/download/13107_citygml_building_lod2.zip";
        FakeDownloadHttpClient fake = new();
        fake.Files[new Uri(url)] = zip;

        string downloadsRoot = NewTempDir();
        try
        {
            CityGmlPackageDownloader downloader = new(fake, downloadsRoot);
            await downloader.DownloadAsync("13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku");
            Assert.Equal(1, fake.DownloadCount);

            await downloader.DownloadAsync("13107", "2025", new[] { url }, null, CancellationToken.None, "Sumida-ku", force: true);
            Assert.Equal(2, fake.DownloadCount); // force ignores the cached marker
        }
        finally
        {
            DeleteDir(downloadsRoot);
        }
    }

    private static byte[] BuildZip(params (string path, string content)[] entries)
    {
        using MemoryStream memory = new();
        using (ZipArchive archive = new(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream stream = entry.Open();
                using StreamWriter writer = new(stream);
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    private static string NewTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "rgs-ckan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort in tests.
        }
    }

    private sealed class FakeDownloadHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, byte[]> Files { get; } = new();
        public int DownloadCount { get; private set; }

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken) =>
            Task.FromResult(Files[url]);

        public async Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            byte[] data = Files[url];
            await destination.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            progress?.Report(1.0);
        }

        public Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
