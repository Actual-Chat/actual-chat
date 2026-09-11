# Vendored jpegli (WebAssembly)

JPEG encoder used by `src/nodejs/src/image-processing/jpegli-encoder.ts` to
re-encode photo attachments on the client. Loaded at runtime from
`dist/jpegli/`, never bundled. jpegli makes ~17% smaller files than
libjpeg-turbo (and canvas `toBlob`) at equal SSIMULACRA2, and its output is a
standard JPEG.

| file | what |
|---|---|
| `simd/jpegli.{js,wasm}` | `-msimd128` build (Highway target WASM); used when WASM SIMD validates |
| `scalar/jpegli.{js,wasm}` | baseline build (Highway target EMU128) |
| `build/` | reproducible build: `Dockerfile`, `build.sh`, `jpegli_wasm.cc` (C ABI wrapper) |

Single-threaded on purpose: threaded builds need cross-origin isolation
(COOP/COEP), which this app and its WebViews don't have.

Source: https://github.com/google/jpegli at
`031a0077f5799a6041004267fc12b956c1f52a20` (BSD-3-Clause), built with
Emscripten 6.0.9. Rebuild: `cd build && docker build -o ../dist-tmp .`, then
copy `dist-tmp/{simd,scalar}` here.
