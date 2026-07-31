using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Fingerprints a package's payload so the viewer can skip re-ingesting an unchanged
/// re-push. SHA-256 over each artifact's relative path and bytes, in path order, so the
/// hash is independent of file timestamps and enumeration order. Missing files are
/// skipped (a tiles-only push and a GIS-only push naturally hash different sets).
/// </summary>
public static class CesiumPackageContentHasher
{
    public static string Compute(string packageRoot, IEnumerable<string> relativePaths)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new ArgumentException("A package root is required.", nameof(packageRoot));
        }

        using var sha = SHA256.Create();
        var ordered = (relativePaths ?? Enumerable.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (string relativePath in ordered)
        {
            string fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath + "\n");
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

            using FileStream stream = File.OpenRead(fullPath);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var builder = new StringBuilder(64);
        foreach (byte value in sha.Hash!)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}
