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

    /// <summary>
    /// Normalized RGBA channels (<c>channel / 255</c>). Preserved verbatim for the default
    /// <see cref="Tiles3DMaterialPalette.Source"/> export so existing GLB conformance fixtures stay
    /// byte-identical.
    /// </summary>
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

    /// <summary>
    /// glTF 2.0 linear-light <c>pbrMetallicRoughness.baseColorFactor</c>: the sRGB
    /// <see cref="Red"/>/<see cref="Green"/>/<see cref="Blue"/> bytes converted to linear light and
    /// quantized back to a byte exactly as <c>kanizu-3d</c>'s massing canvas does, so the opt-in Kanizu
    /// palettes render the same tints. <see cref="Alpha"/> is already linear. Quantizing to a byte keeps
    /// the factors (and so the GLB bytes) identical on .NET Framework and .NET, whose <c>Math.Pow</c>
    /// may differ in the last bit.
    /// </summary>
    public double[] ToLinearBaseColorFactor()
    {
        return new[]
        {
            SrgbByteToLinearByte(Red) / 255d,
            SrgbByteToLinearByte(Green) / 255d,
            SrgbByteToLinearByte(Blue) / 255d,
            Alpha / 255d
        };
    }

    /// <summary>The sRGB byte <paramref name="channel"/> in linear light, rounded half away from zero to a byte.</summary>
    public static byte SrgbByteToLinearByte(byte channel)
    {
        double normalized = channel / 255d;
        double linear = normalized <= 0.04045d
            ? normalized / 12.92d
            : Math.Pow((normalized + 0.055d) / 1.055d, 2.4d);
        return (byte)Math.Round(linear * 255d, MidpointRounding.AwayFromZero);
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
