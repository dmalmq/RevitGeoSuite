using System;

namespace RevitGeoSuite.Tiles3DExport;

public readonly struct Tiles3DMaterialColor : IEquatable<Tiles3DMaterialColor>
{
    public static readonly Tiles3DMaterialColor Default = new Tiles3DMaterialColor(184, 191, 199, 255);

    public Tiles3DMaterialColor(byte red, byte green, byte blue, byte alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public byte Alpha { get; }

    public double[] ToBaseColorFactor()
    {
        return new[]
        {
            Red / 255d,
            Green / 255d,
            Blue / 255d,
            Alpha / 255d
        };
    }

    public bool Equals(Tiles3DMaterialColor other)
    {
        return Red == other.Red
            && Green == other.Green
            && Blue == other.Blue
            && Alpha == other.Alpha;
    }

    public override bool Equals(object? obj)
    {
        return obj is Tiles3DMaterialColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Red << 24) ^ (Green << 16) ^ (Blue << 8) ^ Alpha;
    }
}
