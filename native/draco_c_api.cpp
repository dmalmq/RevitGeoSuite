// draco_c_api.cpp
// Minimal C wrapper around Google's Draco library (https://github.com/google/draco) so that
// .NET P/Invoke (see NativeDracoMeshDecoder.cs) can decode KHR_draco_mesh_compression buffers
// from PLATEAU 3D Tiles. Builds into draco_dec.dll on Windows x64.

#include <cstdint>
#include <cstring>
#include <cstdlib>

#include "draco/compression/decode.h"
#include "draco/mesh/mesh.h"
#include "draco/attributes/point_attribute.h"

#if defined(_WIN32)
#  define DRACO_DEC_API extern "C" __declspec(dllexport)
#else
#  define DRACO_DEC_API extern "C" __attribute__((visibility("default")))
#endif

namespace {

// Allocates a new float[count] and copies a Draco attribute into it.
float* CopyFloatAttribute(const draco::Mesh& mesh, const draco::PointAttribute* attr, int components) {
    if (!attr) return nullptr;
    const draco::PointIndex::ValueType vertex_count = static_cast<draco::PointIndex::ValueType>(mesh.num_points());
    float* out = static_cast<float*>(std::malloc(sizeof(float) * components * vertex_count));
    if (!out) return nullptr;
    for (draco::PointIndex i(0); i < vertex_count; ++i) {
        attr->ConvertValue<float>(attr->mapped_index(i), out + components * i.value());
    }
    return out;
}

uint32_t* CopyUint32Attribute(const draco::Mesh& mesh, const draco::PointAttribute* attr) {
    if (!attr) return nullptr;
    const draco::PointIndex::ValueType vertex_count = static_cast<draco::PointIndex::ValueType>(mesh.num_points());
    uint32_t* out = static_cast<uint32_t*>(std::malloc(sizeof(uint32_t) * vertex_count));
    if (!out) return nullptr;
    for (draco::PointIndex i(0); i < vertex_count; ++i) {
        attr->ConvertValue<uint32_t>(attr->mapped_index(i), out + i.value());
    }
    return out;
}

}  // namespace

// API_VERSION 1: initial release.
DRACO_DEC_API int32_t draco_dec_version() {
    return 1;
}

DRACO_DEC_API int32_t draco_dec_decode_mesh(
        const void* buffer,
        int32_t buffer_size,
        int32_t position_attribute_id,
        int32_t batch_id_attribute_id,
        int32_t normal_attribute_id,
        int32_t* out_vertex_count,
        int32_t* out_index_count,
        float** out_positions,
        uint32_t** out_batch_ids,
        uint32_t** out_indices) {
    if (!buffer || buffer_size <= 0 || !out_vertex_count || !out_index_count ||
        !out_positions || !out_batch_ids || !out_indices) {
        return -1;
    }

    *out_vertex_count = 0;
    *out_index_count = 0;
    *out_positions = nullptr;
    *out_batch_ids = nullptr;
    *out_indices = nullptr;

    draco::DecoderBuffer dec_buffer;
    dec_buffer.Init(static_cast<const char*>(buffer), static_cast<size_t>(buffer_size));

    draco::Decoder decoder;
    auto type_status = draco::Decoder::GetEncodedGeometryType(&dec_buffer);
    if (!type_status.ok() || type_status.value() != draco::TRIANGULAR_MESH) {
        return -2;
    }

    auto mesh_status = decoder.DecodeMeshFromBuffer(&dec_buffer);
    if (!mesh_status.ok()) {
        return -3;
    }
    std::unique_ptr<draco::Mesh> mesh = std::move(mesh_status).value();

    const draco::PointAttribute* position_attr =
            mesh->GetAttributeByUniqueId(static_cast<uint32_t>(position_attribute_id));
    if (!position_attr) return -4;

    float* positions = CopyFloatAttribute(*mesh, position_attr, 3);
    if (!positions) return -5;
    *out_positions = positions;
    *out_vertex_count = static_cast<int32_t>(mesh->num_points());

    if (batch_id_attribute_id >= 0) {
        const draco::PointAttribute* batch_attr =
                mesh->GetAttributeByUniqueId(static_cast<uint32_t>(batch_id_attribute_id));
        if (batch_attr) {
            *out_batch_ids = CopyUint32Attribute(*mesh, batch_attr);
        }
    }

    // Normals are intentionally ignored; the importer does not need them for DirectShape.
    (void)normal_attribute_id;

    const int32_t face_count = static_cast<int32_t>(mesh->num_faces());
    const int32_t index_count = face_count * 3;
    uint32_t* indices = static_cast<uint32_t*>(std::malloc(sizeof(uint32_t) * index_count));
    if (!indices) {
        std::free(positions);
        return -6;
    }
    for (draco::FaceIndex i(0); i < face_count; ++i) {
        const draco::Mesh::Face& face = mesh->face(i);
        indices[3 * i.value() + 0] = face[0].value();
        indices[3 * i.value() + 1] = face[1].value();
        indices[3 * i.value() + 2] = face[2].value();
    }
    *out_indices = indices;
    *out_index_count = index_count;

    return 0;
}

DRACO_DEC_API void draco_dec_free_mesh_buffers(float* positions, uint32_t* batch_ids, uint32_t* indices) {
    if (positions) std::free(positions);
    if (batch_ids) std::free(batch_ids);
    if (indices) std::free(indices);
}
