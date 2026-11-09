using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Writes <c>elements.json</c>: one entry per exported element with a geometry hash and a cheap
/// fingerprint, so a consumer can list what changed between two exports without reading the GLB.
/// </summary>
/// <remarks>
/// Everything is measured in a frame that does not depend on what else was exported: plane-grid
/// easting and northing in the project CRS and orthometric height. Vertices are rounded to the
/// millimetre and triangles are put in a canonical order before hashing, because Revit does not
/// tessellate the same element bit-for-bit between sessions. That makes most re-exports of an
/// unchanged element hash the same, not all: a coordinate near a 0.5 mm boundary can still round
/// the other way. So a changed hash alone is not an edit. The fingerprint (bounding box,
/// triangle count, volume) decides: a changed hash with an unchanged fingerprint is
/// "re-tessellated", a changed fingerprint is "moved" or "edited".
/// Elements are keyed by source document, link instance and UniqueId, because one linked file
/// placed twice repeats its UniqueIds.
/// </remarks>
public sealed class Tiles3DElementIndexWriter
{
    public const int FormatVersion = 1;

    public string BuildJson(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        return Serialize(package.ElementIndex ?? Build(package));
    }

    /// <summary>Builds the index from the package's vertices, which must still be plane-grid metres.</summary>
    public Tiles3DElementIndex Build(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        if (package.UsedExactPlacement)
        {
            throw new InvalidOperationException("The element index must be built before exact placement moves the vertices.");
        }

        double originEasting = package.ReferenceContext.AnchorProjectedCoordinate.Easting;
        double originNorthing = package.ReferenceContext.AnchorProjectedCoordinate.Northing;
        double originHeight = package.ReferenceContext.AnchorElevationMeters - package.GeoidHeightOffsetMeters;

        List<Tiles3DElementIndexEntry> elements = package.Meshes
            .GroupBy(mesh => (mesh.Metadata.SourceDocument, mesh.Metadata.SourceLinkInstanceUniqueId, mesh.Metadata.RevitUniqueId))
            .Select(group => BuildEntry(group.ToList(), originEasting, originNorthing, originHeight))
            .OrderBy(entry => entry.SourceDocument, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceLinkInstanceUniqueId, StringComparer.Ordinal)
            .ThenBy(entry => entry.UniqueId, StringComparer.Ordinal)
            .ToList();

        return new Tiles3DElementIndex
        {
            Version = FormatVersion,
            Frame = "projected-grid-orthometric",
            HashMethod = "sha256-mm-sorted-triangles",
            LevelOfDetail = package.LevelOfDetail.ToString().ToLowerInvariant(),
            Elements = elements,
        };
    }

    private static string Serialize(Tiles3DElementIndex document)
    {
        return JsonConvert.SerializeObject(document, Formatting.None, new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Culture = CultureInfo.InvariantCulture,
        });
    }

    public string Write(string outputDirectory, Tiles3DExportPackage package)
    {
        string path = Path.Combine(outputDirectory, package.ElementIndexFileName);
        File.WriteAllText(path, BuildJson(package));
        return path;
    }

    private static Tiles3DElementIndexEntry BuildEntry(
        IReadOnlyList<Tiles3DMeshPrimitive> meshes,
        double originEasting,
        double originNorthing,
        double originHeight)
    {
        Tiles3DObjectMetadata metadata = meshes[0].Metadata;
        var triangles = new List<QuantizedTriangle>();
        double volume = 0d;
        Tiles3DPoint? reference = null;
        foreach (Tiles3DTriangle triangle in meshes.SelectMany(mesh => mesh.Triangles))
        {
            reference ??= triangle.A;
            volume += SignedVolume(triangle, reference.Value);
            triangles.Add(QuantizedTriangle.Create(
                Quantize(triangle.A, originEasting, originNorthing, originHeight),
                Quantize(triangle.B, originEasting, originNorthing, originHeight),
                Quantize(triangle.C, originEasting, originNorthing, originHeight)));
        }

        triangles.Sort();
        long[] min = { long.MaxValue, long.MaxValue, long.MaxValue };
        long[] max = { long.MinValue, long.MinValue, long.MinValue };
        foreach (QuantizedTriangle triangle in triangles)
        {
            foreach (QuantizedPoint point in new[] { triangle.A, triangle.B, triangle.C })
            {
                min[0] = Math.Min(min[0], point.X);
                min[1] = Math.Min(min[1], point.Y);
                min[2] = Math.Min(min[2], point.Z);
                max[0] = Math.Max(max[0], point.X);
                max[1] = Math.Max(max[1], point.Y);
                max[2] = Math.Max(max[2], point.Z);
            }
        }

        bool empty = triangles.Count == 0;
        return new Tiles3DElementIndexEntry
        {
            UniqueId = metadata.RevitUniqueId,
            ElementId = metadata.RevitElementId,
            SourceDocument = metadata.SourceDocument,
            SourceLinkName = metadata.SourceLinkName,
            SourceLinkInstanceUniqueId = metadata.SourceLinkInstanceUniqueId,
            LevelUniqueId = metadata.LevelUniqueId,
            LevelKey = metadata.LevelKey,
            Category = metadata.Category,
            Hash = Hash(triangles),
            TriangleCount = triangles.Count,
            Min = empty ? new double[3] : min.Select(Millimetres).ToArray(),
            Max = empty ? new double[3] : max.Select(Millimetres).ToArray(),
            VolumeCubicMeters = Math.Round(Math.Abs(volume), 3),
        };
    }

    private static QuantizedPoint Quantize(Tiles3DPoint point, double originEasting, double originNorthing, double originHeight)
    {
        return new QuantizedPoint(
            ToMillimetres(originEasting + point.X),
            ToMillimetres(originNorthing + point.Y),
            ToMillimetres(originHeight + point.Z));
    }

    private static long ToMillimetres(double meters)
    {
        return (long)Math.Round(meters * 1000d, MidpointRounding.AwayFromZero);
    }

    private static double Millimetres(long value)
    {
        return value / 1000d;
    }

    /// <summary>
    /// Signed volume of the tetrahedron from <paramref name="reference"/> to the triangle. Summed over
    /// a closed mesh it is the enclosed volume; for open meshes it is only a fingerprint.
    /// </summary>
    private static double SignedVolume(Tiles3DTriangle triangle, Tiles3DPoint reference)
    {
        double ax = triangle.A.X - reference.X, ay = triangle.A.Y - reference.Y, az = triangle.A.Z - reference.Z;
        double bx = triangle.B.X - reference.X, by = triangle.B.Y - reference.Y, bz = triangle.B.Z - reference.Z;
        double cx = triangle.C.X - reference.X, cy = triangle.C.Y - reference.Y, cz = triangle.C.Z - reference.Z;
        return (ax * ((by * cz) - (bz * cy)) - ay * ((bx * cz) - (bz * cx)) + az * ((bx * cy) - (by * cx))) / 6d;
    }

    private static string Hash(IReadOnlyList<QuantizedTriangle> triangles)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[9 * sizeof(long)];
        foreach (QuantizedTriangle triangle in triangles)
        {
            int offset = 0;
            foreach (QuantizedPoint point in new[] { triangle.A, triangle.B, triangle.C })
            {
                WriteInt64(buffer, ref offset, point.X);
                WriteInt64(buffer, ref offset, point.Y);
                WriteInt64(buffer, ref offset, point.Z);
            }

            sha.TransformBlock(buffer, 0, buffer.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var builder = new StringBuilder(64);
        foreach (byte value in sha.Hash!)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void WriteInt64(byte[] buffer, ref int offset, long value)
    {
        for (int i = 0; i < sizeof(long); i++)
        {
            buffer[offset++] = (byte)(value >> (8 * i));
        }
    }

    private readonly struct QuantizedPoint : IComparable<QuantizedPoint>
    {
        public QuantizedPoint(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }

        public long Y { get; }

        public long Z { get; }

        public int CompareTo(QuantizedPoint other)
        {
            int compare = X.CompareTo(other.X);
            if (compare != 0)
            {
                return compare;
            }

            compare = Y.CompareTo(other.Y);
            return compare != 0 ? compare : Z.CompareTo(other.Z);
        }
    }

    /// <summary>A triangle rotated so its smallest vertex comes first; winding is kept.</summary>
    private readonly struct QuantizedTriangle : IComparable<QuantizedTriangle>
    {
        private QuantizedTriangle(QuantizedPoint a, QuantizedPoint b, QuantizedPoint c)
        {
            A = a;
            B = b;
            C = c;
        }

        public QuantizedPoint A { get; }

        public QuantizedPoint B { get; }

        public QuantizedPoint C { get; }

        public static QuantizedTriangle Create(QuantizedPoint a, QuantizedPoint b, QuantizedPoint c)
        {
            if (b.CompareTo(a) < 0 && b.CompareTo(c) <= 0)
            {
                return new QuantizedTriangle(b, c, a);
            }

            if (c.CompareTo(a) < 0 && c.CompareTo(b) < 0)
            {
                return new QuantizedTriangle(c, a, b);
            }

            return new QuantizedTriangle(a, b, c);
        }

        public int CompareTo(QuantizedTriangle other)
        {
            int compare = A.CompareTo(other.A);
            if (compare != 0)
            {
                return compare;
            }

            compare = B.CompareTo(other.B);
            return compare != 0 ? compare : C.CompareTo(other.C);
        }
    }
}

public sealed class Tiles3DElementIndex
{
    public int Version { get; set; }

    /// <summary>"projected-grid-orthometric": x/y are project-CRS easting/northing, z is the anchor's height datum.</summary>
    public string Frame { get; set; } = string.Empty;

    public string HashMethod { get; set; } = string.Empty;

    public string LevelOfDetail { get; set; } = string.Empty;

    public List<Tiles3DElementIndexEntry> Elements { get; set; } = new List<Tiles3DElementIndexEntry>();
}

public sealed class Tiles3DElementIndexEntry
{
    public string UniqueId { get; set; } = string.Empty;

    public string ElementId { get; set; } = string.Empty;

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLinkName { get; set; } = string.Empty;

    /// <summary>The link instance's UniqueId; empty for host elements.</summary>
    public string SourceLinkInstanceUniqueId { get; set; } = string.Empty;

    public string LevelUniqueId { get; set; } = string.Empty;

    public string LevelKey { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public int TriangleCount { get; set; }

    public double[] Min { get; set; } = new double[3];

    public double[] Max { get; set; } = new double[3];

    public double VolumeCubicMeters { get; set; }
}
