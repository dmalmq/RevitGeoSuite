using System;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Derives the stable building id the Cesium viewer uses to recognize re-pushes of the same
/// model: a URL-safe slug of the building name plus a short hash of the document key, so two
/// models with the same title still get distinct ids.
/// </summary>
public static class CesiumBuildingIdentity
{
    public static string CreateId(string documentKey, string buildingName)
    {
        string slug = Slugify(buildingName);
        string hash = ShortHash(documentKey ?? string.Empty);
        return slug.Length > 0 ? $"{slug}-{hash}" : hash;
    }

    private static string Slugify(string? value)
    {
        string trimmed = value?.Trim().ToLowerInvariant() ?? string.Empty;
        var builder = new StringBuilder(trimmed.Length);
        bool lastWasSeparator = true;
        foreach (char c in trimmed)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ShortHash(string value)
    {
        using var sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
        {
            builder.Append(digest[i].ToString("x2"));
        }

        return builder.ToString();
    }
}
