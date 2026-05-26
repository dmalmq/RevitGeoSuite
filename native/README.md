# Building draco_dec.dll for PLATEAU online import

The Revit add-in's PLATEAU online import path consumes 3D Tiles whose meshes are
compressed with `KHR_draco_mesh_compression`. Decoding requires Google's Draco
C++ library plus the small C wrapper in [`draco_c_api.cpp`](./draco_c_api.cpp).
The wrapper exports a stable C ABI that matches the P/Invoke contract in
[`NativeDracoMeshDecoder.cs`](../src/RevitGeoSuite.Core.Plateau/Tiles3D/NativeDracoMeshDecoder.cs).

## Build prerequisites

- Windows 10/11 x64
- Visual Studio 2022 (or Build Tools) with the C++ workload
- CMake 3.16+
- Git

## Steps

```powershell
# 1. Get Google's Draco source.
git clone --depth=1 https://github.com/google/draco.git C:\src\draco

# 2. Configure + build the wrapper. Output goes to native\build\Release\draco_dec.dll.
cd C:\Repositories\RevitGeoSuite\native
cmake -S . -B build -A x64 -DDRACO_ROOT=C:\src\draco
cmake --build build --config Release

# 3. Copy the DLL next to the add-in.
copy build\Release\draco_dec.dll ..\bin\Deploy\draco_dec.dll
```

After the DLL is in `bin\Deploy\`, restart Revit. The Import PLATEAU Online
command auto-detects the DLL via `NativeDracoMeshDecoder.IsAvailable()`. If the
DLL is missing the command will show a clear in-dialog warning pointing back at
this README.

## ABI summary

```c
int32_t draco_dec_version();           // returns 1 for v1 of this wrapper

int32_t draco_dec_decode_mesh(
    const void* buffer, int32_t buffer_size,
    int32_t position_attribute_id,
    int32_t batch_id_attribute_id,     // -1 if absent
    int32_t normal_attribute_id,       // -1 to skip; currently ignored
    int32_t* out_vertex_count,
    int32_t* out_index_count,
    float**  out_positions,            // [x,y,z,...]; free with draco_dec_free_mesh_buffers
    uint32_t** out_batch_ids,          // NULL if batch_id_attribute_id < 0
    uint32_t** out_indices);

void draco_dec_free_mesh_buffers(float* positions, uint32_t* batch_ids, uint32_t* indices);
```

Status codes: `0` = success; negative values indicate decode failure. See
`draco_c_api.cpp` for details.
