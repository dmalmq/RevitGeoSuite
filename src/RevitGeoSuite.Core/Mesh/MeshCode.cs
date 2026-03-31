using System;

namespace RevitGeoSuite.Core.Mesh;

public sealed class MeshCode : IEquatable<MeshCode>
{
    public string Value { get; set; } = string.Empty;

    public bool Equals(MeshCode? other)
    {
        return other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MeshCode);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }
}
