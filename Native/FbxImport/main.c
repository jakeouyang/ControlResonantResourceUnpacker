#include <stdio.h>
#include <stdint.h>
#include <math.h>
#include <windows.h>
#include "ufbx.h"

static FILE *out;
static int failed;
static void bytes(const void *p, size_t n) { if (fwrite(p, 1, n, out) != n) failed = 1; }
static void u32(uint32_t v) { bytes(&v, 4); }
static void f64(double v) { if (!isfinite(v)) failed = 1; bytes(&v, 8); }
static void string(ufbx_string s) { u32((uint32_t)s.length); bytes(s.data, s.length); }

// Offline parser only: no external textures/cache files, scripts, or network requests.
int wmain(int argc, wchar_t **argv) {
    if (argc != 3) return 2;
    int length = WideCharToMultiByte(CP_UTF8, 0, argv[1], -1, NULL, 0, NULL, NULL);
    char *path = malloc(length); if (!path) return 13;
    WideCharToMultiByte(CP_UTF8, 0, argv[1], -1, path, length, NULL, NULL);
    ufbx_load_opts opts = {0};
    opts.strict = true; opts.file_format = UFBX_FILE_FORMAT_FBX;
    opts.load_external_files = false;
    // Match the right-handed axes declared by FbxWriter.
    opts.target_axes = ufbx_axes_right_handed_y_up;
    opts.target_unit_meters = 1.0;
    opts.temp_allocator.memory_limit = 512 * 1024 * 1024;
    opts.result_allocator.memory_limit = 512 * 1024 * 1024;
    ufbx_error error;
    ufbx_scene *scene = ufbx_load_file(path, &opts, &error); free(path);
    if (!scene) { char text[2048]; ufbx_format_error(text, sizeof(text), &error); fputs(text, stderr); return 10; }
    if (scene->skin_deformers.count || scene->blend_deformers.count || scene->bones.count || scene->anim_curves.count) { ufbx_free_scene(scene); return 11; }
    size_t count = 0, vertices = 0, indices = 0;
    for (size_t i=0; i<scene->nodes.count; i++) {
        ufbx_node *node=scene->nodes.data[i]; ufbx_mesh *m=node->mesh; if (!m) continue;
        count++; vertices+=m->num_vertices; indices+=m->num_indices;
        if (node->materials.count != 1 || m->uv_sets.count > 8) { ufbx_free_scene(scene); return 14; }
        for(size_t f=0;f<m->faces.count;f++) if(m->faces.data[f].num_indices!=3) { ufbx_free_scene(scene); return 12; }
    }
    if (!count || count>65536 || vertices>5000000 || indices>15000000) { ufbx_free_scene(scene); return 13; }
    out=_wfopen(argv[2], L"wb"); if(!out) { ufbx_free_scene(scene); return 15; }
    bytes("RFBXIMP1",8); u32((uint32_t)count);
    for(size_t i=0;i<scene->nodes.count;i++) {
        ufbx_node *node=scene->nodes.data[i]; ufbx_mesh *m=node->mesh;if(!m)continue;
        string(node->name); string(node->materials.data[0]->name);
        u32((uint32_t)m->num_vertices); u32((uint32_t)m->num_indices); u32((uint32_t)m->uv_sets.count);
        for(size_t v=0;v<m->vertices.count;v++) {
            ufbx_vec3 p=ufbx_transform_position(&node->geometry_to_world,m->vertices.data[v]); f64(p.x);f64(p.y);f64(p.z);
        }
        for(size_t v=0;v<m->vertex_indices.count;v++)u32(m->vertex_indices.data[v]);
        for(size_t j=0;j<m->uv_sets.count;j++)for(size_t v=0;v<m->num_indices;v++) {
            ufbx_vec2 uv=ufbx_get_vertex_vec2(&m->uv_sets.data[j].vertex_uv,v);f64(uv.x);f64(uv.y);
        }
    }
    if(fclose(out)!=0)failed=1;ufbx_free_scene(scene);return failed?15:0;
}
