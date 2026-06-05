using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace RevitGeoSuite.SharedUI.Shell;

public static class WebShellEnvironment
{
    public const string HostName = "ui.revitgeosuite.local";
    private const string WebDistResourcePrefix = "RevitGeoSuite.SharedUI.Resources.Web.dist.";
    private static readonly Lazy<string> AssetVersion = new Lazy<string>(ComputeCurrentAssetVersion);

    public static string GetUserDataFolder(string? baseFolder = null)
    {
        string rootFolder = ResolveRootFolder(baseFolder);
        return Path.Combine(rootFolder, "RevitGeoSuite", "WebView2");
    }

    public static string GetWebAssetFolder(string? baseFolder = null)
    {
        string rootFolder = ResolveRootFolder(baseFolder);
        return Path.Combine(rootFolder, "RevitGeoSuite", "WebShell");
    }

    public static Uri GetIndexPageUri(string route = "")
    {
        string version = Uri.EscapeDataString(AssetVersion.Value);
        string query = string.IsNullOrEmpty(version) ? "" : $"?v={version}";
        string fragment = string.IsNullOrEmpty(route) ? "" : $"#{route}";
        return new Uri($"https://{HostName}/index.html{query}{fragment}", UriKind.Absolute);
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

    public static string EnsureWebAssets(string? baseFolder = null)
    {
        string assetFolder = GetWebAssetFolder(baseFolder);
        Directory.CreateDirectory(assetFolder);

        var assembly = typeof(WebShellEnvironment).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames();

        string currentHash = ComputeManifestHash(resourceNames, assembly);
        string hashFile = Path.Combine(assetFolder, ".hash");
        string existingHash = File.Exists(hashFile) ? File.ReadAllText(hashFile) : "";

        if (existingHash == currentHash)
        {
            return assetFolder;
        }

        ClearWebAssetFolder(assetFolder);

        foreach (string resourceName in resourceNames)
        {
            if (!resourceName.StartsWith(WebDistResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string relativePath = resourceName.Substring(WebDistResourcePrefix.Length);
            
            int lastDot = relativePath.LastIndexOf('.');
            if (lastDot < 0)
            {
                continue;
            }

            string extension = relativePath.Substring(lastDot);
            string pathWithoutExtension = relativePath.Substring(0, lastDot);
            
            string convertedPath = pathWithoutExtension.Replace('.', Path.DirectorySeparatorChar);
            relativePath = convertedPath + extension;

            string targetPath = Path.Combine(assetFolder, relativePath);
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir != null)
            {
                Directory.CreateDirectory(targetDir);
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
        }

        File.WriteAllText(hashFile, currentHash);
        return assetFolder;
    }

    private static void ClearWebAssetFolder(string assetFolder)
    {
        foreach (string file in Directory.EnumerateFiles(assetFolder))
        {
            File.Delete(file);
        }

        foreach (string directory in Directory.EnumerateDirectories(assetFolder))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ComputeCurrentAssetVersion()
    {
        var assembly = typeof(WebShellEnvironment).Assembly;
        return ComputeManifestHash(assembly.GetManifestResourceNames(), assembly);
    }

    private static string ComputeManifestHash(string[] resourceNames, Assembly assembly)
    {
        var sb = new StringBuilder();
        Array.Sort(resourceNames, StringComparer.Ordinal);
        foreach (string name in resourceNames)
        {
            if (name.StartsWith(WebDistResourcePrefix, StringComparison.Ordinal))
            {
                sb.AppendLine(name);
                using Stream? stream = assembly.GetManifestResourceStream(name);
                if (stream != null)
                {
                    using var resourceSha256 = SHA256.Create();
                    sb.AppendLine(Convert.ToBase64String(resourceSha256.ComputeHash(stream)));
                }
            }
        }

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash);
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
