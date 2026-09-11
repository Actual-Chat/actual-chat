# Client-side image pipeline (jpegli WASM) for attachments

Status: approved design, 2026-09-11. Builds on PR #4472 (`feat/media-quality-selector`).

## Summary

Photos attached to a chat message are resized and re-encoded on the client by one
shared JS pipeline: decode with the platform decoder, resize on an `OffscreenCanvas`,
encode with jpegli compiled to WebAssembly. The same pipeline serves the web app,
Blazor WASM and all MAUI apps (iOS, Android, macOS, Windows): MAUI apps keep native
file picking and native uploads, and hand the picked file to the pipeline through the
local content URLs the WebView already uses. The server stops resizing and
re-encoding chat images; it only reads dimensions and strips metadata losslessly.

## Why

Measured on 11 photos at 1920 px, equal SSIMULACRA2 quality (score 70):

| Encoder | KB | vs libjpeg-turbo | Time per image |
|---|---|---|---|
| Chromium canvas `toBlob` / Android `Bitmap.compress` | 239 | 0% | 12 ms |
| Safari canvas = iOS ImageIO | 225 | -2.5% | 14-20 ms |
| jSquash mozjpeg WASM | 203 | -13% | ~260 ms |
| jpegli WASM (SIMD) | 193 | -17% | 27 ms (Ryzen 9950X3D), 40 ms (M1) |

- `toBlob('image/jpeg', 0.85)` differs by engine: 398 KB in Chromium, 726 KB in WebKit.
- Default canvas downscaling (no `imageSmoothingQuality`) loses SSIM2 on detailed
  photos in Chromium (71 vs 80 for night shots).
- WebP and AVIF were rejected as stored formats: WebP is no smaller than jpegli and
  fails in iOS Photos, classic Outlook, clipboard; AVIF is rejected by LLM vision APIs,
  iCloud Photos and Apple Messages, and has no browser encoder.

## Decisions

| Topic | Decision |
|---|---|
| Presets | `4K` (max 3840 px, default), `1080p` (max 1920 px), `Original`, `Original w/ EXIF` |
| 4K / 1080p | Re-encode with jpegli; metadata stripped |
| Original | No re-encode; metadata stripped losslessly on the client |
| Original w/ EXIF | Uploaded byte-exact |
| PNG | Opaque PNG is re-encoded like a photo; transparent images stay PNG (resize only) |
| Android HEIC/HEIF | Decoded natively first, then the same pipeline (4K / 1080p only) |
| Server | No size cap for chat attachments; no decode/re-encode; lossless metadata strip unless `KeepMetadata=true` |
| MAUI file access | JS fetches the native file through local content URLs (option 1) |
| Link previews, avatars, chat icons | Unchanged |

## Architecture

### 1. Shared TS module: `src/nodejs/src/image-processing/`

- `image-processor.ts` - main-thread entry point:
  `ImageProcessor.process(source: Blob | string, request: ImageProcessRequest): Promise<ImageProcessResult>`.
  - When `source` is a URL, it is fetched on the main thread and the `Blob` is passed
    to the worker (custom-scheme fetches from workers are unreliable in WebViews).
  - Creates the worker lazily: `Versioning.mapPath('/dist/imageProcessorWorker.js')`,
    module worker, `rpcClient`.
- `image-processor-worker.ts` + `image-processor-worker-bootstrap.ts`
  (`bootstrapWorker`, `rpcServer`). Per request:
  1. Sniff the real format from the bytes (JPEG, PNG, WebP, GIF, HEIC/HEIF, AVIF, BMP).
  2. Passthrough for GIF, animated WebP/APNG, SVG.
  3. Decode with `createImageBitmap` (applies EXIF orientation; uses OS decoders, so
     HEIC decodes in WebKit).
  4. Detect alpha.
  5. Resize on `OffscreenCanvas` with `imageSmoothingQuality = 'high'`.
  6. Encode each requested output:
     - opaque: jpegli, distance 1.9, 4:2:0, progressive level 2;
     - transparent: PNG via `convertToBlob({ type: 'image/png' })`;
     - if the source is a JPEG already within the preset and the re-encoded result is
       larger, return the metadata-stripped source instead.
  7. Return `{ outputs: [{ kind, blob, mimeType, width, height }], sourceWidth, sourceHeight, hasAlpha }`.
- Request: `{ outputs: ImageOutputSpec[] }`, where
  `ImageOutputSpec = { kind: 'main', maxSize: number | null, codec: 'jpegli' | 'png' | 'passthrough', stripMetadata: boolean }`.
  Only `kind: 'main'` exists now; client thumbnails are a later extension that adds
  outputs to the same request, reusing one decode.
- `jpegli-encoder.ts` - loads `dist/jpegli/{simd|scalar}/jpegli.js` via dynamic import
  (`webpackIgnore`, same as libav); picks SIMD by `WebAssembly.validate` of a SIMD
  probe module. Fallback when WASM fails: `convertToBlob('image/jpeg', q)` with an
  engine-specific quality (Chromium ~0.75, WebKit ~0.50, targeting SSIM2 ~73-75).
- `metadata-stripper.ts` - lossless strip:
  - JPEG: drop APP1 EXIF (re-add a minimal EXIF with only Orientation when the tag
    is not 1), APP1 XMP except the Ultra HDR `hdrgm` XMP, APP13 IPTC, COM; keep APP2
    ICC and MPF, keep everything after EOI (gain map).
  - PNG: drop `eXIf`, `tEXt`, `iTXt`, `zTXt`, `tIME`; keep `iCCP`, `sRGB`, `gAMA`, `cHRM`.
  - WebP: drop `EXIF` and `XMP ` chunks, clear the VP8X EXIF/XMP flags; keep `ICCP`.
  - HEIC/AVIF: returned unchanged (not strippable without a container rewrite).
- Colour: canvas stays sRGB (Display P3 is converted to sRGB).

### 2. jpegli vendoring: `src/nodejs/jpegli/`

- `simd/jpegli.{js,wasm}`, `scalar/jpegli.{js,wasm}` - built artifacts.
- `build/` - `Dockerfile`, `build.sh`, `jpegli_wasm.cc`, correctness check against
  native `cjpegli`.
- `README.md` - source (google/jpegli `031a0077f5799a6041004267fc12b956c1f52a20`,
  BSD-3), Emscripten 6.0.9, no threads (no cross-origin isolation), how to rebuild.
- `build.mjs`: copy `src/nodejs/jpegli` to `dist/jpegli` (like `libav`), add entry
  `imageProcessorWorker`.
- Artifact sizes: `jpegli.wasm` 54 KB brotli, glue 4 KB.

### 3. C# attachment flow (`UI.Blazor.App`)

- `ImageQualityPreset { Uhd4K = 3840, FullHd = 1920, Original, OriginalWithExif }`,
  default `Uhd4K`. `ImageQualityPresetExt.ToRequest()` maps a preset to the JS request.
- The chip (PR #4472 UI) shows when at least one attachment is a processable image.
  Menu sizes: Original and Original w/ EXIF use the source length; 4K and 1080p sizes
  are computed in the background when the menu opens.
- Lifecycle:
  1. On add, an image attachment enters `Processing` and is processed with the
     current preset; when done its file provider is replaced by the processed one and
     the upload starts (uploads start while the user types, as before PR #4472).
  2. On preset change, in-flight processing and uploads are cancelled via the
     existing restart/cleanup paths; cached results for the preset are reused,
     otherwise the source is processed again.
  3. On Send, pending processing is awaited. On failure the attachment falls back to
     the source (metadata-stripped when possible) and the failure is logged.
- `ImageAttachmentProcessor` (`Services/FileUploads`):
  `Task<IFileProvider> Process(IFileProvider source, ImageQualityPreset preset, CancellationToken)`.
  - `WebFileProvider`: passes its Blob to `ImageProcessor.process`; the result is a
    new in-memory `WebFileProvider` with correct name/type/length and no file handle,
    so a reload never resumes from the original file (fixes the length mismatch).
  - `MauiFileProvider`: `IMauiFileProviderImpl.GetContentUrl()` gives the main file's
    URL; JS returns the output as `IJSStreamReference`; `IProcessedImageStore`
    (implemented in `App.Maui`) writes it to `CacheDirectory/processed-images/` and
    returns a `MauiFileProvider` for it. The temp file is deleted on removal or after
    the upload completes.
  - Android HEIC/HEIF (4K / 1080p): `INativeImageDecoder` (Android) decodes with
    `ImageDecoder` at the preset's target size, writes a q100 JPEG to the cache, and
    its URL is passed to JS.
- Upload metadata: `KeepMetadata=true` only for `Original w/ EXIF`.
- Photos above 50 MP: both Original presets are hidden (server pixel limit).
- Removed from PR #4472: `ConfirmImageQuality`, `ApplyQualityAndStartUploads`,
  `EstimateAndUpdateLength`, `WebFileProvider.ResizeImage` / `EstimateResizedSizes`,
  TS `replaceBlob` / `estimateResizedSizes`, the 720p/480p presets.
- Restored: concurrent add in `TryAddFileAttachments` (commit 8d443a3224).

### 4. MAUI content URLs

| Platform | `GetContentUrl()` | Change |
|---|---|---|
| Android | `/in/content/<key>` (same-origin) | none |
| iOS / macOS | `content://files/<key>` | `ContentSchemeHandler` adds `Access-Control-Allow-Origin` for the app origin |
| Windows | `https://0.0.0.1/in/content/<key>` (same-origin) | existing `WebResourceRequested` handler also serves this path |

Risk-first step: verify `fetch(GetContentUrl())` returns the file bytes on iOS,
Android and Windows before building the rest. If a platform fails, that platform
switches to streaming bytes from .NET (`DotNetStreamReference`); nothing else changes.

### 5. Server (`Core.Server/Uploads`, chat attachments)

- `ImageUploadProcessor` for `MediaKind.ChatEntryAttachment`: no `Normalize`, no
  resize, no re-encode. Reads dimensions via `Image.IdentifyAsync` (header only),
  applies `ImageLimits` pixel checks, swaps width/height for EXIF Orientation 5-8.
- Lossless metadata strip in C# (same rules as the TS stripper) unless the upload
  metadata has `KeepMetadata=true`. Client-processed files have no metadata, so this
  is a no-op for them; it protects older clients, the iOS share extension and MCP.
- Link previews (`LinkPreviewPicture`), avatars and chat icons keep current processing.

## Error handling

- Unsupported format or decode failure: source file, metadata-stripped when possible.
- WASM load failure: canvas JPEG fallback.
- One worker, one image at a time (4K RGBA is about 44 MB); per-job cancellation;
  30 s timeout; the worker is recreated after a crash.
- `IJSStreamReference` or cache write failure on MAUI: source provider.
- Fallbacks are logged with a reason and not shown to the user.

## Performance (estimate, M1, 4K)

Decode 100-300 ms, resize ~30 ms, jpegli ~120-160 ms: about 0.3-0.5 s per photo in
the background from add time. 1080p is about a third of that.

## Testing

- TS unit (vitest, `tests/ts`): metadata stripper fixtures for JPEG/PNG/WebP (GPS,
  EXIF, XMP, text removed; ICC, Orientation, gain map kept; image data unchanged);
  format sniffing; resize dimension math; preset-to-request mapping.
- C# unit (`tests/Core.Server.UnitTests`): C# stripper on the same fixtures;
  `KeepMetadata`; Orientation 5-8 dimension swap; stored bytes equal uploaded bytes
  minus metadata.
- jpegli build: correctness check vs native `cjpegli` kept in `src/nodejs/jpegli/build/`.
- Manual / device (`/debug-ui`, `/server-loop`, `macmini`): web Chrome and Safari,
  Android device (incl. Samsung HEIC), iPhone (incl. HEIC), Windows. For each: add
  photos, check processed size and dimensions, change preset (upload restarts),
  Original w/ EXIF keeps GPS, MAUI mid-upload restart resumes.
- `npm run build:Verify`, `dotnet build ActualChat.CI.slnf`.

## Compatibility

- Upload metadata gains `KeepMetadata` (default false).
- Older clients' image uploads are stored at full resolution (no server cap), with
  metadata stripped by the server.

## Out of scope

iOS share extension; client thumbnails (the multi-output request is the extension
point; candidates are a ThumbHash placeholder and a header-stripped WebP); Display P3
preservation; server HEIC decode; JPEG XL; link preview / avatar / chat icon pipelines.

## Reuse

- TS: `worker-bootstrap` (`bootstrapWorker`), `rpc.ts` (`rpcClient`, `rpcServer`),
  `versioning.ts` (`Versioning.mapPath`), `logging` (`getLogs`), the libav vendoring
  and `build.mjs` asset-copy pattern.
- C#: `AttachmentList`, `AttachmentsController` (`InitUploadSession`, `ResumeUpload`,
  restart), `AttachmentsState`, `AttachmentCleanupFactory`, `FileAttachments`,
  `MauiFileProvider`, `IMauiFileProviderImpl`, `WebFileProvider`, `ContentResolver`,
  `LocalContentRegistry`, `AndroidContentDownloader`, `MediaTypeExt`, `ImageLimits`,
  `MediaSaver` (thumbnail support for the later extension).
- New shared components and placement:
  - `image-processing/` and `jpegli/` go to `src/nodejs` (shared), not the chat editor.
  - The C# metadata stripper goes to `ActualChat.Core.Server` (`Uploads/`), next to
    `ImageLimits`; it has no UI dependencies.
  - `ImageAttachmentProcessor` stays in `UI.Blazor.App` (depends on attachments).

## Implementation notes

Deviations from the design, decided during implementation:

- No per-preset result cache: switching the preset always reprocesses the source
  (about 0.3-0.5 s per photo) instead of keeping each preset's output alive, to
  keep cleanup simple.
- The menu's 4K and 1080p size estimates come from the same worker call that
  processes the selected preset (each call also encodes an estimate for the other
  size), rather than a separate pass.
- Original and Original w/ EXIF re-encode (instead of hiding) a source whose long
  side exceeds 7680 px, downscaling to that size; the worker reads dimensions from
  the file header without a full decode. The server's chat-attachment pixel cap
  was raised to 7680² (~59 MP) to match; other image paths keep the 50 MP cap.
- Windows local content URLs use a WebView2 custom-scheme registration (`content`
  scheme, allowed origin `https://0.0.0.1`) instead of a same-origin
  `/in/content/` path, because MAUI's own resource handler would otherwise race
  with an extensionless same-origin path.
- `ImageQualityPreset` values aren't pixel sizes; the pixel size comes from an
  extension method, `GetMaxSize()`.
- The worker has no per-job cancellation: a superseded job still runs to
  completion and its result is discarded. Because jobs are serialized, each
  call's RPC deadline is `30_000 ms × (jobs already pending + 1)`.

Known gaps and follow-ups:

- A HEIC/HEIF file picked with the Original preset stays a plain file attachment,
  because the server doesn't decode HEVC.
- `IncomingShareUI.SendFiles`'s multi-chat / more-than-10-files share path
  bypasses the pipeline, so those photos upload at full resolution and the
  server no longer resizes them.
- Stored chat image attachments keep their EXIF Orientation tag, since the server
  no longer re-encodes them; any future server-side decoder of attachments
  (thumbnails, vision, previews) must apply auto-orientation itself.
- On Android, HEIC/HEIF is decoded natively into an sRGB bitmap with no ICC
  profile, so wide-gamut (Display P3) photos lose some saturation on that path.
  Display P3 preservation was already out of scope.
- `AttachmentCleanupCollection.Items` returns the live list while
  `AttachmentsController.CleanupAttachmentResources` iterates it on a background
  task and `RemoveByKind` mutates it on the UI thread; the fix is
  `Items => _items.ToArray()`. Pre-existing shape, left as is.
- Runtime verification on Chrome, Safari, Android, iOS and the Windows app is
  still outstanding (plan Task 13 Steps 2-4).
- The Apple targets were compile-checked only (iOS simulator and Mac Catalyst,
  0 C# errors); no Apple device build or run.
