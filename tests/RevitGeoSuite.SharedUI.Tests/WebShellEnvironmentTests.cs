using System;
using System.IO;
using System.Linq;
using RevitGeoSuite.SharedUI.Shell;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

public class WebShellEnvironmentTests
{
    [Fact]
    public void GetIndexPageUri_IncludesAssetVersionBeforeRouteFragment()
    {
        var uri = WebShellEnvironment.GetIndexPageUri("/georeference");

        Assert.Equal(WebShellEnvironment.HostName, uri.Host);
        Assert.Equal("/index.html", uri.AbsolutePath);
        Assert.Contains("v=", uri.Query);
        Assert.Equal("#/georeference", uri.Fragment);
    }

    [Fact]
    public void EnsureWebAssets_RemovesStaleExtractedFilesWhenHashChanges()
    {
        string baseFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            "WebShellEnvironmentTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            string assetFolder = WebShellEnvironment.GetWebAssetFolder(baseFolder);
            string staleAssetFolder = Path.Combine(assetFolder, "assets");
            Directory.CreateDirectory(staleAssetFolder);
            string staleBundle = Path.Combine(staleAssetFolder, "stale-old-bundle.js");
            File.WriteAllText(staleBundle, "old");
            File.WriteAllText(Path.Combine(assetFolder, ".hash"), "old");

            string result = WebShellEnvironment.EnsureWebAssets(baseFolder);

            Assert.Equal(assetFolder, result);
            Assert.False(File.Exists(staleBundle));
            Assert.True(File.Exists(Path.Combine(assetFolder, "index.html")));
            Assert.True(Directory.EnumerateFiles(Path.Combine(assetFolder, "assets")).Any());
        }
        finally
        {
            if (Directory.Exists(baseFolder))
            {
                Directory.Delete(baseFolder, recursive: true);
            }
        }
    }
}
