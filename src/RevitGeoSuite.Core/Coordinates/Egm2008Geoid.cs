using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace RevitGeoSuite.Core.Coordinates;

public static class Egm2008Geoid
{
    private const string ResourceFileName = "egm2008-5.pgm";
    private static readonly Lazy<Grid> LazyGrid = new Lazy<Grid>(LoadGrid);

    public static double GetUndulationMeters(double latitudeDegrees, double longitudeDegrees)
    {
        if (!IsFinite(latitudeDegrees) || !IsFinite(longitudeDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), "Latitude and longitude must be finite numbers.");
        }

        Grid grid = LazyGrid.Value;
        double latitude = Math.Max(-90d, Math.Min(90d, latitudeDegrees));
        double longitude = NormalizeLongitude(longitudeDegrees);
        double rowPosition = (90d - latitude) / grid.LatitudeSpacingDegrees;
        double columnPosition = longitude / grid.LongitudeSpacingDegrees;

        int row0 = Clamp((int)Math.Floor(rowPosition), 0, grid.Height - 1);
        int row1 = Math.Min(row0 + 1, grid.Height - 1);
        int column0 = (int)Math.Floor(columnPosition);
        double x = columnPosition - column0;
        if (column0 >= grid.Width)
        {
            column0 = 0;
            x = 0d;
        }

        int column1 = (column0 + 1) % grid.Width;
        double y = row0 == row1 ? 0d : rowPosition - row0;

        double northWest = grid.GetUndulation(row0, column0);
        double northEast = grid.GetUndulation(row0, column1);
        double southWest = grid.GetUndulation(row1, column0);
        double southEast = grid.GetUndulation(row1, column1);

        double north = Lerp(northWest, northEast, x);
        double south = Lerp(southWest, southEast, x);
        return Lerp(north, south, y);
    }

    private static Grid LoadGrid()
    {
        Assembly assembly = typeof(Egm2008Geoid).Assembly;
        string? resourceName = null;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("." + ResourceFileName, StringComparison.Ordinal))
            {
                resourceName = name;
                break;
            }
        }

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded geoid resource '{ResourceFileName}' was not found.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded geoid resource '{resourceName}' could not be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();

        return ParsePgm(bytes);
    }

    private static Grid ParsePgm(byte[] bytes)
    {
        int position = 0;
        Dictionary<string, double> metadata = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        string magic = ReadToken(bytes, ref position, metadata);
        if (!string.Equals(magic, "P5", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid is not a binary PGM file.");
        }

        int width = ParsePositiveInt(ReadToken(bytes, ref position, metadata), "width");
        int height = ParsePositiveInt(ReadToken(bytes, ref position, metadata), "height");
        int maxValue = ParsePositiveInt(ReadToken(bytes, ref position, metadata), "max value");
        if (maxValue > 65535)
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid uses an unsupported PGM sample depth.");
        }

        if (!metadata.TryGetValue("Offset", out double offset) || !metadata.TryGetValue("Scale", out double scale))
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid is missing Offset/Scale metadata.");
        }

        if (position >= bytes.Length || !IsWhitespace(bytes[position]))
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid has an invalid PGM header.");
        }

        position++;
        int sampleBytes = maxValue < 256 ? 1 : 2;
        long expectedRasterBytes = (long)width * height * sampleBytes;
        if (bytes.Length - position < expectedRasterBytes)
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid raster is incomplete.");
        }

        if (sampleBytes != 2)
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid must use 16-bit samples.");
        }

        return new Grid(bytes, position, width, height, offset, scale);
    }

    private static string ReadToken(byte[] bytes, ref int position, Dictionary<string, double> metadata)
    {
        while (position < bytes.Length)
        {
            byte value = bytes[position];
            if (IsWhitespace(value))
            {
                position++;
                continue;
            }

            if (value == (byte)'#')
            {
                ReadComment(bytes, ref position, metadata);
                continue;
            }

            break;
        }

        if (position >= bytes.Length)
        {
            throw new InvalidOperationException("The embedded EGM2008 geoid grid header ended unexpectedly.");
        }

        int start = position;
        while (position < bytes.Length && !IsWhitespace(bytes[position]))
        {
            position++;
        }

        return Encoding.ASCII.GetString(bytes, start, position - start);
    }

    private static void ReadComment(byte[] bytes, ref int position, Dictionary<string, double> metadata)
    {
        position++;
        int start = position;
        while (position < bytes.Length && bytes[position] != (byte)'\n')
        {
            position++;
        }

        string comment = Encoding.ASCII.GetString(bytes, start, position - start).Trim();
        if (position < bytes.Length && bytes[position] == (byte)'\n')
        {
            position++;
        }

        int separator = comment.IndexOf(' ');
        if (separator <= 0)
        {
            return;
        }

        string key = comment.Substring(0, separator).Trim();
        string valueText = comment.Substring(separator + 1).Trim();
        if ((string.Equals(key, "Offset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Scale", StringComparison.OrdinalIgnoreCase))
            && double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            metadata[key] = value;
        }
    }

    private static int ParsePositiveInt(string text, string label)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new InvalidOperationException($"The embedded EGM2008 geoid grid has an invalid {label}.");
        }

        return value;
    }

    private static double NormalizeLongitude(double longitudeDegrees)
    {
        double normalized = longitudeDegrees % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }

    private static double Lerp(double left, double right, double amount)
    {
        return left + ((right - left) * amount);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsWhitespace(byte value)
    {
        return value == (byte)' '
            || value == (byte)'\t'
            || value == (byte)'\r'
            || value == (byte)'\n'
            || value == (byte)'\f';
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    private sealed class Grid
    {
        private readonly byte[] bytes;
        private readonly int rasterOffset;
        private readonly double offset;
        private readonly double scale;

        public Grid(byte[] bytes, int rasterOffset, int width, int height, double offset, double scale)
        {
            this.bytes = bytes;
            this.rasterOffset = rasterOffset;
            Width = width;
            Height = height;
            this.offset = offset;
            this.scale = scale;
            LatitudeSpacingDegrees = 180d / (height - 1);
            LongitudeSpacingDegrees = 360d / width;
        }

        public int Width { get; }

        public int Height { get; }

        public double LatitudeSpacingDegrees { get; }

        public double LongitudeSpacingDegrees { get; }

        public double GetUndulation(int row, int column)
        {
            int index = rasterOffset + (((row * Width) + column) * 2);
            int sample = (bytes[index] << 8) | bytes[index + 1];
            return offset + (scale * sample);
        }
    }
}
