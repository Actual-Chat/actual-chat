# Vendored libav.js + WebCodecs polyfill

Loaded at runtime by `src/nodejs/src/webcodecs-polyfill.ts` when the
WebCodecs polyfill level resolves to anything but `none`. Copied here rather
than pulled from npm because `vp9-opus-avf-simd` is a custom libav.js build:
libav.js ships libvpx with SIMD disabled, and this variant lies to libvpx about
its target so Emscripten's SIMDe lowers libvpx's NEON intrinsics onto wasm SIMD.
That is worth ~10x on VP9 encode, which is the whole reason this exists.

| file | what |
|---|---|
| `libav-6.10.9.0-vp9-opus-avf-simd.mjs` | loader; picks the wasm build below |
| `libav-6.10.9.0-vp9-opus-avf-simd.wasm.mjs` | non-threaded wasm glue |
| `libav-6.10.9.0-vp9-opus-avf-simd.wasm.wasm` | the module itself |
| `libavjs-webcodecs-polyfill.js` | libavjs-webcodecs-polyfill 0.5.5 (0BSD) |

ES modules, not the classic scripts: this app's workers are module workers, and
the classic loader calls `importScripts` internally, which they do not have.

The threaded (`.thr.*`) builds are deliberately absent: they need cross-origin
isolation (COOP/COEP), which this app does not set.

`-avf-` rather than the leaner `vp9-opus-simd` because the polyfill's
`AudioEncoder` resamples through an `aresample` filter graph, so Opus needs
avfilter linked in. Video does not — it uses swscale directly. One variant
serving both levels costs the `vp9` level ~0.5 MB and no measurable speed.

Sources: https://github.com/Yahweasel/libav.js (variant built from the
`vp9-opus-simd` recipe) and
https://github.com/ennuicastr/libavjs-webcodecs-polyfill.
