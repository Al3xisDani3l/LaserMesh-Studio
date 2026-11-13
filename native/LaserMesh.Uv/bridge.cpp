#include "xatlas.h"
#include <cstdlib>
#include <cstdint>
#include <memory>

struct Result { float* uv; int32_t* charts; uint32_t count; };
struct Progress { int (*callback)(int); };
extern "C" __declspec(dllexport) void lm_uv_free(Result* result) noexcept {
    if (result) { std::free(result->uv); std::free(result->charts); std::free(result); }
}
extern "C" __declspec(dllexport) Result* lm_uv_generate(const float* positions, uint32_t count, int (*callback)(int)) noexcept {
    try {
        if (!positions || count == 0 || count % 3 != 0 || count > 3000000) return nullptr;
        std::unique_ptr<xatlas::Atlas, decltype(&xatlas::Destroy)> atlas(xatlas::Create(), xatlas::Destroy);
        Progress progress{callback};
        xatlas::SetProgressCallback(atlas.get(), [](xatlas::ProgressCategory, int percent, void* data) {
            auto p = static_cast<Progress*>(data); return !p->callback || p->callback(percent) != 0;
        }, &progress);
        xatlas::MeshDecl mesh{};
        mesh.vertexPositionData = positions; mesh.vertexPositionStride = sizeof(float) * 3; mesh.vertexCount = count;
        if (xatlas::AddMesh(atlas.get(), mesh) != xatlas::AddMeshError::Success) return nullptr;
        xatlas::ChartOptions charts{};
        xatlas::PackOptions pack{}; pack.resolution = 1024; pack.padding = 2;
        xatlas::Generate(atlas.get(), charts, pack);
        if (atlas->meshCount != 1 || atlas->width == 0 || atlas->height == 0 || (callback && !callback(100))) return nullptr;
        const auto& output = atlas->meshes[0];
        if (output.indexCount != count) return nullptr;
        auto result = static_cast<Result*>(std::calloc(1, sizeof(Result)));
        if (!result) return nullptr;
        result->count = count;
        result->uv = static_cast<float*>(std::malloc(sizeof(float) * 2 * count));
        result->charts = static_cast<int32_t*>(std::malloc(sizeof(int32_t) * count));
        if (!result->uv || !result->charts) { lm_uv_free(result); return nullptr; }
        for (uint32_t i = 0; i < count; ++i) {
            const auto& vertex = output.vertexArray[output.indexArray[i]];
            if (vertex.xref >= count || positions[vertex.xref*3] != positions[i*3]
                || positions[vertex.xref*3+1] != positions[i*3+1] || positions[vertex.xref*3+2] != positions[i*3+2]) {
                lm_uv_free(result); return nullptr;
            }
            result->uv[i*2] = vertex.uv[0] / atlas->width;
            result->uv[i*2+1] = vertex.uv[1] / atlas->height;
            result->charts[i] = vertex.chartIndex;
        }
        return result;
    } catch (...) { return nullptr; }
}
