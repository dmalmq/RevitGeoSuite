using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// P/Invoke shim against draco_dec.dll (a small C wrapper around Google's Draco library).
/// See native/draco_c_api.cpp + native/CMakeLists.txt for the source of the matching DLL.
/// </summary>
public sealed class NativeDracoMeshDecoder : IDracoMeshDecoder
{
    private const string DllName = "draco_dec";

    public static bool IsAvailable()
    {
        try
        {
            int version = draco_dec_version();
            return version > 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public DracoDecodedMesh Decode(ReadOnlySpan<byte> dracoBuffer, DracoMeshAttributes attributes)
    {
        if (dracoBuffer.IsEmpty) throw new InvalidDataException("Draco buffer is empty.");

        IntPtr positionsPtr = IntPtr.Zero;
        IntPtr batchIdsPtr = IntPtr.Zero;
        IntPtr indicesPtr = IntPtr.Zero;
        int vertexCount = 0;
        int indexCount = 0;

        unsafe
        {
            fixed (byte* bufferPtr = dracoBuffer)
            {
                int status = draco_dec_decode_mesh(
                    (IntPtr)bufferPtr,
                    dracoBuffer.Length,
                    attributes.PositionAttributeId,
                    attributes.BatchIdAttributeId,
                    attributes.NormalAttributeId,
                    out vertexCount,
                    out indexCount,
                    out positionsPtr,
                    out batchIdsPtr,
                    out indicesPtr);

                if (status != 0)
                {
                    throw new InvalidDataException($"Draco decode failed (status={status}). Mesh may be malformed.");
                }
            }
        }

        try
        {
            float[] positions = new float[vertexCount * 3];
            Marshal.Copy(positionsPtr, positions, 0, positions.Length);

            uint[]? batchIds = null;
            if (attributes.HasBatchIds && batchIdsPtr != IntPtr.Zero)
            {
                int[] temp = new int[vertexCount];
                Marshal.Copy(batchIdsPtr, temp, 0, temp.Length);
                batchIds = new uint[vertexCount];
                Buffer.BlockCopy(temp, 0, batchIds, 0, vertexCount * sizeof(uint));
            }

            int[] tempIndices = new int[indexCount];
            Marshal.Copy(indicesPtr, tempIndices, 0, indexCount);
            uint[] indices = new uint[indexCount];
            Buffer.BlockCopy(tempIndices, 0, indices, 0, indexCount * sizeof(uint));

            return new DracoDecodedMesh(positions, batchIds, indices);
        }
        finally
        {
            draco_dec_free_mesh_buffers(positionsPtr, batchIdsPtr, indicesPtr);
        }
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int draco_dec_version();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int draco_dec_decode_mesh(
        IntPtr buffer,
        int bufferSize,
        int positionAttributeId,
        int batchIdAttributeId,
        int normalAttributeId,
        out int vertexCount,
        out int indexCount,
        out IntPtr positions,
        out IntPtr batchIds,
        out IntPtr indices);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void draco_dec_free_mesh_buffers(
        IntPtr positions,
        IntPtr batchIds,
        IntPtr indices);
}
