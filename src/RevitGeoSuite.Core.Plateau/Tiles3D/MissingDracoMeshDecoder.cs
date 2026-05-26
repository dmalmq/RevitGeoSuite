using System;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Fallback decoder used when the native Draco DLL is not available. Throws on every call
/// with a clear message pointing at the deployment instructions.
/// </summary>
public sealed class MissingDracoMeshDecoder : IDracoMeshDecoder
{
    public const string MissingMessage =
        "PLATEAU 3D Tiles use KHR_draco_mesh_compression which requires the native draco_dec.dll. " +
        "Drop draco_dec.dll into the RevitGeoSuite deploy folder " +
        "(see native/README.md for build instructions) and restart Revit.";

    public DracoDecodedMesh Decode(ReadOnlySpan<byte> dracoBuffer, DracoMeshAttributes attributes) =>
        throw new DracoDecoderUnavailableException(MissingMessage);
}

public sealed class DracoDecoderUnavailableException : Exception
{
    public DracoDecoderUnavailableException(string message) : base(message) { }
}
