#!/bin/bash
# Builds jpegli (encoder-only static lib + Highway) with Emscripten, twice:
#   dist/scalar : baseline wasm (Highway static target EMU128)
#   dist/simd   : -msimd128   (Highway static target WASM)
# Run inside emscripten/emsdk (see Dockerfile). Env overrides: SRC, BUILD_ROOT, OUT, JOBS.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="${SRC:-/src/jpegli}"
BUILD_ROOT="${BUILD_ROOT:-/src/build}"
OUT="${OUT:-$HERE/dist}"
JOBS="${JOBS:-$(nproc)}"

emcc --version | head -1
mkdir -p "$BUILD_ROOT" "$OUT"

LINK_FLAGS=(
  -O3 -flto -fno-exceptions -fno-rtti
  -sMODULARIZE=1 -sEXPORT_ES6=1 -sEXPORT_NAME=createJpegli
  -sENVIRONMENT=web,worker,node -sFILESYSTEM=0
  -sALLOW_MEMORY_GROWTH=1 -sINITIAL_MEMORY=64MB -sSTACK_SIZE=1MB
  -sEXPORTED_FUNCTIONS=_jpegli_encode_rgba,_free_buf,_jpegli_hwy_target,_malloc,_free
  -sEXPORTED_RUNTIME_METHODS=HEAPU8,HEAPU32,UTF8ToString
)

build_variant() {
  local name="$1" simd="$2"
  local b="$BUILD_ROOT/$name"
  local flags="-O3 -flto $simd"

  emcmake cmake -S "$SRC" -B "$b" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_FLAGS="$flags" -DCMAKE_CXX_FLAGS="$flags" \
    -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF \
    -DJPEGLI_ENABLE_WASM_THREADS=OFF \
    -DJPEGLI_ENABLE_TOOLS=OFF -DJPEGLI_ENABLE_DEVTOOLS=OFF -DJPEGLI_ENABLE_BENCHMARK=OFF \
    -DJPEGLI_ENABLE_FUZZERS=OFF -DJPEGLI_ENABLE_SJPEG=OFF -DJPEGLI_ENABLE_JNI=OFF \
    -DJPEGLI_ENABLE_DOXYGEN=OFF -DJPEGLI_ENABLE_MANPAGES=OFF -DJPEGLI_ENABLE_OPENEXR=OFF \
    -DJPEGLI_ENABLE_JPEGLI_LIBJPEG=OFF -DJPEGLI_ENABLE_TCMALLOC=OFF \
    -DJPEGLI_ENABLE_SKCMS=ON -DJPEGLI_BUNDLE_LIBPNG=OFF \
    > "$b.configure.log" 2>&1 || { tail -40 "$b.configure.log"; exit 1; }

  cmake --build "$b" -j"$JOBS" --target jpegli-static hwy > "$b.build.log" 2>&1 \
    || { grep -m20 -B2 -A8 "error" "$b.build.log"; exit 1; }

  local jpeglib_inc hwy_a jpegli_a
  jpeglib_inc="$(dirname "$(find "$b" -name jpeglib.h | head -1)")"
  hwy_a="$(find "$b" -name libhwy.a | head -1)"
  jpegli_a="$(find "$b" -name 'libjpegli-static.a' | head -1)"

  mkdir -p "$OUT/$name"
  em++ $simd "${LINK_FLAGS[@]}" -std=c++17 \
    -I"$SRC" -I"$SRC/third_party/highway" -I"$jpeglib_inc" -I"$(dirname "$jpeglib_inc")" \
    "$HERE/jpegli_wasm.cc" "$jpegli_a" "$hwy_a" \
    -o "$OUT/$name/jpegli.js"

  ls -l "$OUT/$name"
}

build_variant scalar ""
build_variant simd "-msimd128"

{
  echo "emscripten: $(emcc --version | head -1)"
  echo "jpegli: $(git -C "$SRC" rev-parse HEAD)"
  git -C "$SRC" submodule status third_party/highway third_party/libjpeg-turbo third_party/skcms
} > "$OUT/BUILD_INFO.txt"
cat "$OUT/BUILD_INFO.txt"
