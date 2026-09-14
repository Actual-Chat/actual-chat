# Vendored libheif (WebAssembly)

HEIC/HEIF decoder used by `src/nodejs/src/image-processing/heif-decoder.ts` to
decode photo attachments on browsers that cannot. Loaded at runtime from
`dist/libheif/`, never bundled, and fetched only when someone actually attaches
a HEIC on such a browser.

| file | what |
|---|---|
| `libheif.js` | Emscripten glue, 91 KB (29 KB gzipped) |
| `libheif.wasm` | the decoder, 1.42 MB (469 KB gzipped) |
| `LICENSE` | upstream licence text, see the note below |

## Why it is here

Chromium cannot decode HEIC at all — measured 2026-09-13 on Chrome 152, Windows
and Android alike: `createImageBitmap` throws `InvalidStateError`, `<img>`
fails, and `ImageDecoder.isTypeSupported('image/heic')` is false. WebKit reads
it natively, so Safari and the iOS app never load this. Firefox has no HEVC on
most platforms either, which is why reusing the browser's own decoder through
WebCodecs was not an option.

**EXIF orientation must be applied by the caller — but only sometimes.** Both
libheif and WebKit apply the `irot`/`imir` container properties and stop there;
only WebKit also reads EXIF `Orientation`, and only when the file has no
`irot`/`imir` (it applies it even under `imageOrientation: 'none'`). So
`HeifDecoder.decode` applies the EXIF tag exactly when the container carries no
transform property. Measured, full-size:

| file | `irot` | EXIF | WebKit | libheif |
|---|---|---|---|---|
| `voxt-test-portrait.heic` | yes | 6 | 4000x3000 | 4000x3000 |
| `rot6.heic` | no | 6 | 4096x3072 | 3072x4096 |

Every camera HEIC carries `irot` — angle 0 included — so applying EXIF
unconditionally rotates a real photo twice. Left alone in either direction, the
same photo is upright from Safari and sideways from Chrome.
`tests/ts/unit/fixtures/orientation{1,6,6-irot}.heic` pin all three cases.

Source: https://github.com/catdad-experiments/libheif-js at `1.23.2`, the
`libheif-wasm` build (separate `.wasm`, not the base64-inlined `wasm-bundle`).

## The one local change to `libheif.js`

Upstream ships UMD. Imported as an ES module — the only way a `type: 'module'`
worker can load it — a UMD file exports nothing, and the app's CSP has no
`blob:` in `script-src`, so it can't be rewrapped at runtime either. So the
trailing UMD line

```js
typeof exports=="object"&&typeof module=="object"?module.exports=libheif:typeof define=="function"&&define.amd&&define([],()=>libheif);
```

is replaced with `export default libheif;`. Nothing else is touched. Redo it
when upgrading.

The glue instantiates the wasm **synchronously**, and its own loaders (XHR,
`importScripts`) don't exist in a module worker — so `HeifDecoder.load` fetches
`libheif.wasm` itself and passes it as `Module.wasmBinary`.

## Test fixtures

`tests/ts/unit/fixtures/orientation1.heic` is a 240x160 gradient (3.5 KB)
written by `pillow-heif` with a minimal EXIF IFD0 carrying `Orientation`;
`orientation6.heic` is a byte-for-byte copy with that one value changed to 6.
`orientation6-irot.heic` is the same gradient written with
`image_orientation=6`, so libheif's encoder emits `irot` (angle 3) alongside
EXIF 6 — the shape every camera HEIC has. Full-size phone HEICs are ~3.4 MB,
which is too much to keep in the repo.

The leak test pads a fixture with a `free` box rather than shipping a large
one: the padding is copied into the wasm heap like any other source byte, so a
retained `heif_context` is measurable from a 3.5 KB file.

## Licensing

libheif and libde265 are **LGPL-3.0**; the AV1 support is BSD-2. No GPL
component is compiled in — checked for `x265` and `kvazaar` symbols, neither is
present, and the encode entry points have no encoder behind them.

Serving this `.wasm` to browsers **is distribution**, so the LGPL applies to it
(unlike libheif running server-side inside imagor, which is not distribution).
It is kept as a separate file fetched at runtime, never inlined into a bundle,
so it can be replaced with a user's own build — which is the substitution route
LGPL-3.0 §4 contemplates. Keep it that way, and keep this notice with it.
