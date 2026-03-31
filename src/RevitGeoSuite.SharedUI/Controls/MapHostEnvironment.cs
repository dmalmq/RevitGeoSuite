using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace RevitGeoSuite.SharedUI.Controls;

public static class MapHostEnvironment
{
    public const string HostName = "map.revitgeosuite.local";
    private const string MapHostFileName = "MapHost.html";

    public static string GetUserDataFolder(string? baseFolder = null)
    {
        string rootFolder = ResolveRootFolder(baseFolder);
        return Path.Combine(rootFolder, "RevitGeoSuite", "WebView2");
    }

    public static string GetHostAssetFolder(string? baseFolder = null)
    {
        string rootFolder = ResolveRootFolder(baseFolder);
        return Path.Combine(rootFolder, "RevitGeoSuite", "MapHost");
    }

    public static string GetHostPagePath(string? baseFolder = null)
    {
        return Path.Combine(GetHostAssetFolder(baseFolder), MapHostFileName);
    }

    public static Uri GetHostPageUri()
    {
        return new Uri($"https://{HostName}/{MapHostFileName}", UriKind.Absolute);
    }

    public static string EnsureHostPage(string html, string? baseFolder = null)
    {
        if (html is null)
        {
            throw new ArgumentNullException(nameof(html));
        }

        string assetFolder = GetHostAssetFolder(baseFolder);
        Directory.CreateDirectory(assetFolder);

        string pagePath = GetHostPagePath(baseFolder);
        File.WriteAllText(pagePath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return pagePath;
    }

    public static async Task<CoreWebView2Environment> CreateAsync(string? baseFolder = null)
    {
        string userDataFolder = GetUserDataFolder(baseFolder);
        Directory.CreateDirectory(userDataFolder);

        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: null);
    }

    private static string ResolveRootFolder(string? baseFolder)
    {
        string rootFolder = string.IsNullOrWhiteSpace(baseFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : baseFolder!;

        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            rootFolder = Path.GetTempPath();
        }

        return rootFolder;
    }
}
