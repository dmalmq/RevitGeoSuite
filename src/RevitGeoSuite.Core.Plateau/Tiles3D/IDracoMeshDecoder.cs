using System;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class DracoMeshAttributes
{
    public DracoMeshAttributes(int positionAttributeId, int batchIdAttributeId, int normalAttributeId)
    {
        PositionAttributeId = positionAttributeId;
        BatchIdAttributeId = batchIdAttributeId;
        NormalAttributeId = normalAttributeId;
    }

    public int PositionAttributeId { get; }

    public int BatchIdAttributeId { get; }

    public int NormalAttributeId { get; }

    public bool HasBatchIds => BatchIdAttributeId >= 0;
}

public sealed class DracoDecodedMesh
{
    public DracoDecodedMesh(float[] positions, uint[]? batchIds, uint[] indices)
    {
        Positions = positions;
        BatchIds = batchIds;
        Indices = indices;
    }

    /// <summary>Flat array of [x,y,z, x,y,z, ...] in Draco-local coordinates (caller applies tile transform + RTC center).</summary>
    public float[] Positions { get; }

    /// <summary>Per-vertex batch ID, length = VertexCount. Null when the source primitive has no _BATCHID.</summary>
    public uint[]? BatchIds { get; }

    /// <summary>Flat array of triangle vertex indices, length is multiple of 3.</summary>
    public uint[] Indices { get; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

public interface IDracoMeshDecoder
{
    DracoDecodedMesh Decode(ReadOnlySpan<byte> dracoBuffer, DracoMeshAttributes attributes);
}
