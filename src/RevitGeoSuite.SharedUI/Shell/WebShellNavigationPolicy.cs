using System;

namespace RevitGeoSuite.SharedUI.Shell;

/// <summary>
/// Containment rules for the embedded WebView2 shell: only pages served from the add-in's
/// virtual host may stay inside the WebView2 frame or call the host RPC bridge, and external
/// links may only be handed off to the OS browser when they use <c>http</c> or <c>https</c>.
/// </summary>
public static class WebShellNavigationPolicy
{
    public static bool IsTrustedRpcSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, WebShellEnvironment.HostName, StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort;
    }

    public static bool IsAllowedAppNavigation(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return false;
        }

        if (string.Equals(uriString!.Trim(), "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsTrustedRpcSource(uriString);
    }

    public static bool IsSafeExternalUri(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return false;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }
}
