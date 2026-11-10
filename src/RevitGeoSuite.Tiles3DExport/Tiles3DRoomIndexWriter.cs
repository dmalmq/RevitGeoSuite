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
/// Writes <c>rooms.json</c>: every placed room's footprint, so Kanizu can pair the GDB's units
/// with Revit rooms by overlap and notice when either side moves.
/// </summary>
/// <remarks>
/// The frame is <c>elements.json</c>'s: plane-grid easting and northing in the project CRS and the
/// anchor's height datum, so neither file depends on the tiles' placement. Coordinates are
/// rounded to the millimetre. Rooms are keyed by source document, link instance and UniqueId,
/// like elements. Each room's hash covers its rounded footprint only, with every ring started at
/// its smallest point, so renaming a room does not change it.
/// </remarks>
public sealed class Tiles3DRoomIndexWriter
{
    public const int FormatVersion = 1;

    public string BuildJson(IReadOnlyCollection<Tiles3DRoomFootprint> rooms)
    {
        if (rooms is null)
        {
            throw new ArgumentNullException(nameof(rooms));
        }

        return JsonConvert.SerializeObject(Build(rooms), Formatting.None, new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Culture = CultureInfo.InvariantCulture,
        });
    }

    /// <summary>Writes <c>rooms.json</c> when the package has rooms; returns its path, or null.</summary>
    public string? Write(string outputDirectory, Tiles3DExportPackage package)
    {
        if (package.Rooms is null)
        {
            return null;
        }

        string path = Path.Combine(outputDirectory, package.RoomIndexFileName);
        File.WriteAllText(path, BuildJson(package.Rooms));
        return path;
    }

    public Tiles3DRoomIndex Build(IReadOnlyCollection<Tiles3DRoomFootprint> rooms)
    {
        List<Tiles3DRoomIndexEntry> entries = rooms
            .Where(room => room.Rings.Count > 0 && room.Rings[0].Count >= 3)
            .Select(BuildEntry)
            .OrderBy(entry => entry.SourceDocument, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceLinkInstanceUniqueId, StringComparer.Ordinal)
            .ThenBy(entry => entry.UniqueId, StringComparer.Ordinal)
            .ToList();

        return new Tiles3DRoomIndex
        {
            Version = FormatVersion,
            Frame = "projected-grid-orthometric",
            HashMethod = "sha256-mm-rings",
            Rooms = entries,
        };
    }

    private static Tiles3DRoomIndexEntry BuildEntry(Tiles3DRoomFootprint room)
    {
        List<List<long[]>> rings = room.Rings
            .Select(ring => Close(ring.Select(point => new[] { ToMillimetres(point[0]), ToMillimetres(point[1]) }).ToList()))
            .Where(ring => ring.Count >= 4)
            .ToList();

        return new Tiles3DRoomIndexEntry
        {
            UniqueId = room.UniqueId,
            ElementId = room.ElementId,
            SourceDocument = room.SourceDocument,
            SourceLinkName = room.SourceLinkName,
            SourceLinkInstanceUniqueId = room.SourceLinkInstanceUniqueId,
            LevelUniqueId = room.LevelUniqueId,
            LevelName = room.LevelName,
            Name = room.Name,
            Number = room.Number,
            Elevation = Math.Round(room.ElevationMeters, 3),
            AreaSquareMeters = Math.Round(room.AreaSquareMeters, 3),
            Hash = Hash(rings),
            Footprint = rings
                .Select(ring => ring.Select(point => new[] { point[0] / 1000d, point[1] / 1000d }).ToList())
                .ToList(),
        };
    }

    /// <summary>Drops repeated points and closes the ring.</summary>
    private static List<long[]> Close(List<long[]> ring)
    {
        var points = new List<long[]>(ring.Count + 1);
        foreach (long[] point in ring)
        {
            if (points.Count == 0 || !SamePoint(points[points.Count - 1], point))
            {
                points.Add(point);
            }
        }

        if (points.Count > 1 && SamePoint(points[0], points[points.Count - 1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            return new List<long[]>();
        }

        points.Add(points[0]);
        return points;
    }

    private static bool SamePoint(long[] a, long[] b)
    {
        return a[0] == b[0] && a[1] == b[1];
    }

    private static long ToMillimetres(double meters)
    {
        return (long)Math.Round(meters * 1000d, MidpointRounding.AwayFromZero);
    }

    private static string Hash(IReadOnlyList<List<long[]>> rings)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[2 * sizeof(long)];
        foreach (List<long[]> ring in rings)
        {
            // The ring without its closing point, started at its smallest point.
            int count = ring.Count - 1;
            int start = 0;
            for (int i = 1; i < count; i++)
            {
                if (Compare(ring[i], ring[start]) < 0)
                {
                    start = i;
                }
            }

            for (int i = 0; i < count; i++)
            {
                long[] point = ring[(start + i) % count];
                int offset = 0;
                WriteInt64(buffer, ref offset, point[0]);
                WriteInt64(buffer, ref offset, point[1]);
                sha.TransformBlock(buffer, 0, buffer.Length, null, 0);
            }

            // A ring separator, so two rings never hash as one.
            sha.TransformBlock(new byte[] { 0xff }, 0, 1, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var builder = new StringBuilder(64);
        foreach (byte value in sha.Hash!)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static int Compare(long[] a, long[] b)
    {
        int compare = a[0].CompareTo(b[0]);
        return compare != 0 ? compare : a[1].CompareTo(b[1]);
    }

    private static void WriteInt64(byte[] buffer, ref int offset, long value)
    {
        for (int i = 0; i < sizeof(long); i++)
        {
            buffer[offset++] = (byte)(value >> (8 * i));
        }
    }
}

public sealed class Tiles3DRoomIndex
{
    public int Version { get; set; }

    /// <summary>"projected-grid-orthometric": x/y are project-CRS easting/northing, elevation is in the anchor's height datum.</summary>
    public string Frame { get; set; } = string.Empty;

    public string HashMethod { get; set; } = string.Empty;

    public List<Tiles3DRoomIndexEntry> Rooms { get; set; } = new List<Tiles3DRoomIndexEntry>();
}

public sealed class Tiles3DRoomIndexEntry
{
    public string UniqueId { get; set; } = string.Empty;

    public string ElementId { get; set; } = string.Empty;

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLinkName { get; set; } = string.Empty;

    /// <summary>The link instance's UniqueId; empty for host rooms.</summary>
    public string SourceLinkInstanceUniqueId { get; set; } = string.Empty;

    public string LevelUniqueId { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    /// <summary>The room's level, in metres in the anchor's height datum.</summary>
    public double Elevation { get; set; }

    public double AreaSquareMeters { get; set; }

    public string Hash { get; set; } = string.Empty;

    /// <summary>Closed rings, outer first, each point <c>[easting, northing]</c> in metres.</summary>
    public List<List<double[]>> Footprint { get; set; } = new List<List<double[]>>();
}
