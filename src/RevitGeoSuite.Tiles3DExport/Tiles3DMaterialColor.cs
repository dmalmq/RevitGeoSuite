using System;

namespace RevitGeoSuite.Tiles3DExport;

public readonly struct Tiles3DMaterialColor : IEquatable<Tiles3DMaterialColor>
{
    public static readonly Tiles3DMaterialColor Default = new Tiles3DMaterialColor(184, 191, 199);

    public Tiles3DMaterialColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public byte R { get; }

    public byte G { get; }

    public byte B { get; }

    public double[] ToNormalizedArray()
    {
        return new[] { R / 255d, G / 255d, B / 255d, 1d };
    }

    public bool Equals(Tiles3DMaterialColor other)
    {
        return R == other.R && G == other.G && B == other.B;
    }

    public override bool Equals(object? obj)
    {
        return obj is Tiles3DMaterialColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (R << 16) | (G << 8) | B;
    }

    public static bool operator ==(Tiles3DMaterialColor left, Tiles3DMaterialColor right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Tiles3DMaterialColor left, Tiles3DMaterialColor right)
    {
        return !left.Equals(right);
    }
}
