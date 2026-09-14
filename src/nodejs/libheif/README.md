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

**EXIF orientation must be applied by the caller.** libheif applies `irot`/`imir`
container properties but ignores EXIF `Orientation`, whereas WebKit applies it —
even under `imageOrientation: 'none'`. Left alone, the same photo is upright from
Safari and sideways from Chrome. `HeifDecoder.decode` applies it;
`tests/ts/unit/fixtures/orientation{1,6}.heic` pin it.

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
Full-size phone HEICs are ~4.6 MB, which is too much to keep in the repo.

## Licensing

libheif and libde265 are **LGPL-3.0**; the AV1 support is BSD-2. No GPL
component is compiled in — checked for `x265` and `kvazaar` symbols, neither is
present, and the encode entry points have no encoder behind them.

Serving this `.wasm` to browsers **is distribution**, so the LGPL applies to it
(unlike libheif running server-side inside imagor, which is not distribution).
It is kept as a separate file fetched at runtime, never inlined into a bundle,
so it can be replaced with a user's own build — which is the substitution route
LGPL-3.0 §4 contemplates. Keep it that way, and keep this notice with it.
