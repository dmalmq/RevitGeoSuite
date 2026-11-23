// draco_c_api.cpp
// Minimal C wrapper around Google's Draco library (https://github.com/google/draco) so that
// .NET P/Invoke (see NativeDracoMeshDecoder.cs) can decode KHR_draco_mesh_compression buffers
// from PLATEAU 3D Tiles. Builds into draco_dec.dll on Windows x64.

#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <memory>

#include "draco/compression/decode.h"
#include "draco/mesh/mesh.h"
#include "draco/attributes/point_attribute.h"

#if defined(_WIN32)
#  define DRACO_DEC_API extern "C" __declspec(dllexport)
#else
#  define DRACO_DEC_API extern "C" __attribute__((visibility("default")))
#endif

namespace {

constexpr int32_t kPositionComponents = 3;
constexpr int32_t kBatchIdComponents = 1;

// Conservative upper bounds that prevent signed int32_t overflow in both C++ and C# callers
// (e.g., vertexCount * 3, vertexCount * sizeof(uint32_t), faceCount * 3, indexCount * sizeof(uint32_t)).
constexpr uint64_t kMaxVertexCount =
    static_cast<uint64_t>(std::numeric_limits<int32_t>::max()) / 4ULL;
constexpr uint64_t kMaxFaceCount =
    static_cast<uint64_t>(std::numeric_limits<int32_t>::max()) / 12ULL;

struct MallocDeleter {
    void operator()(void* ptr) const noexcept {
        std::free(ptr);
    }
};

using FloatBuffer = std::unique_ptr<float, MallocDeleter>;
using Uint32Buffer = std::unique_ptr<uint32_t, MallocDeleter>;

bool TryMultiplySize(size_t a, size_t b, size_t* out_product) noexcept {
    if (a != 0 && b > (std::numeric_limits<size_t>::max() / a)) {
        return false;
    }
    *out_product = a * b;
    return true;
}

// Allocates a float[vertex_count * expected_components], verifies component count and
// mapped index bounds, and rejects any NaN/Infinity coordinates.
int32_t CopyFloatAttribute(
        const draco::PointAttribute* attr,
        int32_t expected_components,
        uint32_t vertex_count,
        FloatBuffer* out_buffer) {
    if (!attr || expected_components <= 0 || vertex_count == 0 || !out_buffer) {
        return -4;
    }
    if (static_cast<int32_t>(attr->num_components()) != expected_components) {
        return -4;
    }
    const size_t attr_size = attr->size();
    if (attr_size == 0) {
        return -4;
    }

    size_t total_floats = 0;
    size_t total_bytes = 0;
    if (!TryMultiplySize(static_cast<size_t>(vertex_count), static_cast<size_t>(expected_components), &total_floats) ||
        !TryMultiplySize(total_floats, sizeof(float), &total_bytes)) {
        return -5;
    }

    FloatBuffer buffer(static_cast<float*>(std::malloc(total_bytes)));
    if (!buffer) {
        return -6;
    }

    float* const raw = buffer.get();
    const size_t stride = static_cast<size_t>(expected_components);
    for (uint32_t i = 0; i < vertex_count; ++i) {
        const draco::AttributeValueIndex mapped = attr->mapped_index(draco::PointIndex(i));
        if (static_cast<size_t>(mapped.value()) >= attr_size) {
            return -7;
        }
        float* const dst = raw + (static_cast<size_t>(i) * stride);
        for (int32_t c = 0; c < expected_components; ++c) {
            dst[c] = 0.0f;
        }
        if (!attr->ConvertValue<float>(mapped, dst)) {
            return -7;
        }
        for (int32_t c = 0; c < expected_components; ++c) {
            if (!std::isfinite(dst[c])) {
                return -7;
            }
        }
    }

    *out_buffer = std::move(buffer);
    return 0;
}

// Allocates a uint32_t[vertex_count] for a single-component attribute, validating
// component count and mapped index bounds to prevent heap buffer overflows.
int32_t CopyUint32Attribute(
        const draco::PointAttribute* attr,
        uint32_t vertex_count,
        Uint32Buffer* out_buffer) {
    if (!attr || vertex_count == 0 || !out_buffer) {
        return -8;
    }
    if (static_cast<int32_t>(attr->num_components()) != kBatchIdComponents) {
        return -8;
    }
    const size_t attr_size = attr->size();
    if (attr_size == 0) {
        return -8;
    }

    size_t total_bytes = 0;
    if (!TryMultiplySize(static_cast<size_t>(vertex_count), sizeof(uint32_t), &total_bytes)) {
        return -5;
    }

    Uint32Buffer buffer(static_cast<uint32_t*>(std::malloc(total_bytes)));
    if (!buffer) {
        return -6;
    }

    uint32_t* const raw = buffer.get();
    for (uint32_t i = 0; i < vertex_count; ++i) {
        const draco::AttributeValueIndex mapped = attr->mapped_index(draco::PointIndex(i));
        if (static_cast<size_t>(mapped.value()) >= attr_size) {
            return -8;
        }
        uint32_t value = 0;
        if (!attr->ConvertValue<uint32_t>(mapped, &value)) {
            return -8;
        }
        raw[static_cast<size_t>(i)] = value;
    }

    *out_buffer = std::move(buffer);
    return 0;
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
    if (out_vertex_count) *out_vertex_count = 0;
    if (out_index_count) *out_index_count = 0;
    if (out_positions) *out_positions = nullptr;
    if (out_batch_ids) *out_batch_ids = nullptr;
    if (out_indices) *out_indices = nullptr;

    if (!buffer || buffer_size <= 0 || position_attribute_id < 0 ||
        !out_vertex_count || !out_index_count ||
        !out_positions || !out_batch_ids || !out_indices) {
        return -1;
    }

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
    if (!mesh) {
        return -3;
    }

    const uint64_t raw_vertex_count = static_cast<uint64_t>(mesh->num_points());
    const uint64_t raw_face_count = static_cast<uint64_t>(mesh->num_faces());
    if (raw_vertex_count == 0 || raw_vertex_count > kMaxVertexCount ||
        raw_face_count == 0 || raw_face_count > kMaxFaceCount) {
        return -5;
    }

    const uint32_t vertex_count = static_cast<uint32_t>(raw_vertex_count);
    const uint32_t face_count = static_cast<uint32_t>(raw_face_count);
    const uint32_t index_count = face_count * 3U;

    const draco::PointAttribute* position_attr =
        mesh->GetAttributeByUniqueId(static_cast<uint32_t>(position_attribute_id));
    if (!position_attr) {
        return -4;
    }

    FloatBuffer positions;
    const int32_t pos_status = CopyFloatAttribute(
        position_attr,
        kPositionComponents,
        vertex_count,
        &positions);
    if (pos_status != 0) {
        return pos_status;
    }

    Uint32Buffer batch_ids;
    if (batch_id_attribute_id >= 0) {
        const draco::PointAttribute* batch_attr =
            mesh->GetAttributeByUniqueId(static_cast<uint32_t>(batch_id_attribute_id));
        if (batch_attr) {
            const int32_t batch_status = CopyUint32Attribute(
                batch_attr,
                vertex_count,
                &batch_ids);
            if (batch_status != 0) {
                return batch_status;
            }
        }
    }

    // Normals are intentionally ignored; the importer does not need them for DirectShape.
    (void)normal_attribute_id;

    size_t index_bytes = 0;
    if (!TryMultiplySize(static_cast<size_t>(index_count), sizeof(uint32_t), &index_bytes)) {
        return -5;
    }

    Uint32Buffer indices(static_cast<uint32_t*>(std::malloc(index_bytes)));
    if (!indices) {
        return -6;
    }

    uint32_t* const raw_indices = indices.get();
    for (uint32_t i = 0; i < face_count; ++i) {
        const draco::Mesh::Face& face = mesh->face(draco::FaceIndex(i));
        const uint32_t v0 = face[0].value();
        const uint32_t v1 = face[1].value();
        const uint32_t v2 = face[2].value();
        if (v0 >= vertex_count || v1 >= vertex_count || v2 >= vertex_count) {
            return -9;
        }
        const size_t base = static_cast<size_t>(i) * 3U;
        raw_indices[base + 0] = v0;
        raw_indices[base + 1] = v1;
        raw_indices[base + 2] = v2;
    }

    // Commit all out-parameters atomically only after every allocation and check has succeeded.
    *out_vertex_count = static_cast<int32_t>(vertex_count);
    *out_index_count = static_cast<int32_t>(index_count);
    *out_positions = positions.release();
    *out_batch_ids = batch_ids.release();
    *out_indices = indices.release();

    return 0;
}

DRACO_DEC_API void draco_dec_free_mesh_buffers(float* positions, uint32_t* batch_ids, uint32_t* indices) {
    if (positions) std::free(positions);
    if (batch_ids) std::free(batch_ids);
    if (indices) std::free(indices);
}
