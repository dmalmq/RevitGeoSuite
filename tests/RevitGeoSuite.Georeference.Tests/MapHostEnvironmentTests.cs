using System;
using System.IO;
using RevitGeoSuite.SharedUI.Controls;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class MapHostEnvironmentTests
{
    [Fact]
    public void GetUserDataFolder_appends_suite_path_to_supplied_base_folder()
    {
        string baseFolder = Path.Combine("C:\\", "Temp", "GeoTests");

        string folder = MapHostEnvironment.GetUserDataFolder(baseFolder);

        Assert.Equal(Path.Combine(baseFolder, "RevitGeoSuite", "WebView2"), folder);
    }

    [Fact]
    public void GetHostPagePath_appends_map_host_file_to_supplied_base_folder()
    {
        string baseFolder = Path.Combine("C:\\", "Temp", "GeoTests");

        string pagePath = MapHostEnvironment.GetHostPagePath(baseFolder);

        Assert.Equal(Path.Combine(baseFolder, "RevitGeoSuite", "MapHost", "MapHost.html"), pagePath);
    }

    [Fact]
    public void GetHostPageUri_returns_local_https_origin()
    {
        Uri uri = MapHostEnvironment.GetHostPageUri();

        Assert.Equal("https", uri.Scheme);
        Assert.Equal(MapHostEnvironment.HostName, uri.Host);
        Assert.Equal("/MapHost.html", uri.AbsolutePath);
    }

    [Fact]
    public void EnsureHostPage_writes_utf8_html_file_to_host_folder()
    {
        string baseFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            string pagePath = MapHostEnvironment.EnsureHostPage("<html></html>", baseFolder);

            Assert.True(File.Exists(pagePath));
            Assert.Equal("<html></html>", File.ReadAllText(pagePath));
        }
        finally
        {
            if (Directory.Exists(baseFolder))
            {
                Directory.Delete(baseFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void GetUserDataFolder_returns_rooted_suite_path_when_base_folder_is_empty()
    {
        string folder = MapHostEnvironment.GetUserDataFolder(string.Empty);

        Assert.True(Path.IsPathRooted(folder));
        Assert.EndsWith(Path.Combine("RevitGeoSuite", "WebView2"), folder, StringComparison.OrdinalIgnoreCase);
    }
}
