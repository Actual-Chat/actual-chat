// Minimal C ABI over jpegli for WebAssembly.
// Compiled as C++ only because lib/jpegli/encode.h is a C++ header; the
// exported surface is plain C (extern "C", no exceptions, no RTTI).
//
// Settings mirror libjxl/jpegli's extras/enc/jpegli.cc (what cjpegli does):
// jpegli_set_defaults -> sampling factors -> adaptive quantization on ->
// jpegli_set_distance(quality_to_distance(q)) -> progressive level ->
// optimize_coding. Input is canvas getImageData RGBA; alpha is ignored via
// JCS_EXT_RGBA, so no RGB copy is made.

#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include <cmath>
#include <cstdio>

#include "hwy/targets.h"
#include "lib/jpegli/encode.h"

namespace {

struct ErrorCtx {
  jpeg_error_mgr pub;
  jmp_buf env;
};

void OnErrorExit(j_common_ptr cinfo) {
  auto* ctx = reinterpret_cast<ErrorCtx*>(cinfo->err);
  longjmp(ctx->env, 1);
}

void OnOutputMessage(j_common_ptr) {}

struct Output {
  unsigned char* buf = nullptr;
  unsigned long size = 0;  // NOLINT: libjpeg API type
};

// Everything that may longjmp lives here, so Emscripten's setjmp emulation
// only wraps one call in jpegli_encode_rgba instead of one call per row.
void Encode(j_compress_ptr cinfo, uint8_t* rgba, int width, int height,
            float quality_or_distance, int use_distance, int subsampling,
            int progressive, Output* out) {
  jpegli_mem_dest(cinfo, &out->buf, &out->size);
  cinfo->image_width = static_cast<JDIMENSION>(width);
  cinfo->image_height = static_cast<JDIMENSION>(height);
  cinfo->input_components = 4;
  cinfo->in_color_space = JCS_EXT_RGBA;
  jpegli_set_defaults(cinfo);

  int luma_factor = subsampling == 420 ? 2 : 1;
  cinfo->comp_info[0].h_samp_factor = luma_factor;
  cinfo->comp_info[0].v_samp_factor = luma_factor;
  for (int i = 1; i < cinfo->num_components; ++i) {
    cinfo->comp_info[i].h_samp_factor = 1;
    cinfo->comp_info[i].v_samp_factor = 1;
  }

  jpegli_enable_adaptive_quantization(cinfo, TRUE);
  float distance =
      use_distance
          ? quality_or_distance
          : jpegli_quality_to_distance(
                static_cast<int>(std::lround(quality_or_distance)));
  jpegli_set_distance(cinfo, distance, TRUE);
  jpegli_set_progressive_level(cinfo, progressive < 0   ? 0
                                      : progressive > 2 ? 2
                                                        : progressive);
  cinfo->optimize_coding = TRUE;
  jpegli_set_input_format(cinfo, JPEGLI_TYPE_UINT8, JPEGLI_NATIVE_ENDIAN);

  jpegli_start_compress(cinfo, TRUE);
  const size_t stride = static_cast<size_t>(width) * 4;
  JSAMPROW row[1];
  while (cinfo->next_scanline < cinfo->image_height) {
    row[0] = rgba + cinfo->next_scanline * stride;
    jpegli_write_scanlines(cinfo, row, 1);
  }
  jpegli_finish_compress(cinfo);
}

}  // namespace

extern "C" {

// progressive: 0 = sequential, 1..2 = jpegli progressive level (2 = cjpegli
// default). subsampling: 420 or 444. Returns a malloc'ed JPEG (free with
// free_buf) or NULL on error. The input buffer may be used as scratch.
uint8_t* jpegli_encode_rgba(uint8_t* rgba, int width, int height,
                            float quality_or_distance, int use_distance,
                            int subsampling, int progressive,
                            size_t* out_size) {
  if (out_size) *out_size = 0;
  if (!rgba || width <= 0 || height <= 0 || !out_size) return nullptr;

  jpeg_compress_struct cinfo;
  ErrorCtx err;
  Output out;
  cinfo.err = jpegli_std_error(&err.pub);
  err.pub.error_exit = OnErrorExit;
  err.pub.output_message = OnOutputMessage;
  jpegli_create_compress(&cinfo);
  if (setjmp(err.env)) {
    jpegli_destroy_compress(&cinfo);
    free(out.buf);
    return nullptr;
  }
  Encode(&cinfo, rgba, width, height, quality_or_distance, use_distance,
         subsampling, progressive, &out);
  jpegli_destroy_compress(&cinfo);
  *out_size = out.size;
  return out.buf;
}

void free_buf(uint8_t* buf) { free(buf); }

// Highway's compile-time target, e.g. "WASM" for -msimd128, "EMU128" without.
const char* jpegli_hwy_target(void) { return hwy::TargetName(HWY_STATIC_TARGET); }

}  // extern "C"
