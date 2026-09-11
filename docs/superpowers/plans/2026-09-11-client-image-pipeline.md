# Client-side Image Pipeline (jpegli WASM) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resize and re-encode photo attachments on the client with a shared jpegli-WASM pipeline that serves the web app and all MAUI apps, and make the server store chat images without re-encoding.

**Architecture:** A module worker in `src/nodejs/src/image-processing/` decodes (`createImageBitmap`), resizes (`OffscreenCanvas`) and encodes (jpegli WASM, canvas fallback), or strips metadata losslessly. C# `ImageAttachmentProcessor` calls it through JS interop: web files pass their Blob, MAUI files pass a local content URL and get the result back as an `IJSStreamReference` written to a cache file. The server's new `AttachmentImageUploadProcessor` only reads dimensions and strips metadata unless `KeepMetadata` is set.

**Tech Stack:** TypeScript (esbuild, vitest), Emscripten-built jpegli, C# / Blazor / MAUI (.NET 11), SixLabors.ImageSharp 3.1.12 (identify only), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-11-client-image-pipeline-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing C#/TS. No `Async` suffix; mixed brace style; control-flow statements on their own line followed by a blank line; no new `///` on members; comments only for non-obvious things.
- Presets: `Uhd4K` (max 3840 px, default), `FullHd` (max 1920 px), `Original` (no re-encode, metadata stripped), `OriginalWithExif` (byte-exact, `KeepMetadata=true`). Both Original presets re-encode an image whose long side exceeds `Constants.Attachments.MaxImageSize` = 7680 down to 7680 px.
- jpegli settings: distance `1.9`, subsampling `420`, progressive level `2`. Canvas fallback quality: `0.50` on WebKit, `0.75` elsewhere.
- Worker RPC timeout: `30_000` ms per job, multiplied by (jobs already pending + 1); the worker processes one image at a time.
- jpegli source: google/jpegli `031a0077f5799a6041004267fc12b956c1f52a20`, Emscripten `6.0.9`, no threads.
- Metadata strip rules (TS and C# identical): JPEG drops APP1 EXIF (re-adds a minimal EXIF with only Orientation when it is not 1), APP1 XMP unless it contains `hdrgm`, other APP1, APP13, COM; keeps APP2 (ICC, MPF) and every segment after the MPF segment, and everything from SOS to the end of file. PNG drops `eXIf`, `tEXt`, `iTXt`, `zTXt`, `tIME`. WebP drops `EXIF` and `XMP ` chunks and clears VP8X flags `0x08` and `0x04`. Everything else is returned unchanged.
- Server: chat attachments get no resize and no re-encode; link previews, avatars and chat icons keep current processing.
- TS changes are validated with `npm run build:Verify`; C# with `dotnet build ActualChat.CI.slnf`.
- Localization: new keys go into the 19 hand-written `Strings.<lang>.json` catalogs (all except `cnr`, `hr`, `sr`, `max`), then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd`.

## Deviations from the spec (decided while planning)

1. **No per-preset result cache.** Changing the preset re-processes from the source (0.3-0.5 s per photo). Keeping processed files per preset alive would complicate cleanup for little gain.
2. **Menu sizes come from the same worker call.** Processing with `4K` also encodes a `1080p` "estimate" output (and vice versa) from the same decode, so both sizes are known without a separate pass when the menu opens.
3. **Original presets re-encode images whose long side exceeds 8K (7680 px) down to 7680 px** instead of hiding the Original options. The worker reads dimensions from the file header (JPEG SOF, PNG IHDR, WebP VP8/VP8L/VP8X, HEIF/AVIF `ispe`) without decoding, so `Original with EXIF` also goes through the worker; a file that comes out unchanged isn't copied (`isSource`). The server's chat-attachment pixel limit becomes 7680² (~59 MP) so a square 8K image is accepted; other image paths keep 50 MP.
4. **Windows content URLs use a WebView2 custom-scheme registration** (`content` scheme, allowed origin `https://0.0.0.1`) instead of a same-origin `/in/content/` path: MAUI's own `WebResourceRequested` handler answers app-origin paths without an extension with `index.html`, which would race with ours. Task 1 verifies it.
5. **`ImageQualityPreset` values are not pixel sizes**; `GetMaxSize()` is an extension method.
6. **No per-job cancellation in the worker.** A cancelled or superseded job still runs to completion and its result is discarded; because jobs are serialized, each call's RPC deadline is `30_000 ms × (jobs already pending + 1)`.

## File Map

**Create (TS, shared):**
- `src/nodejs/jpegli/README.md`, `src/nodejs/jpegli/{simd,scalar}/jpegli.{js,wasm}`, `src/nodejs/jpegli/build/{Dockerfile,build.sh,jpegli_wasm.cc}`
- `src/nodejs/src/image-processing/image-processing-contracts.ts` - request/result/worker types
- `src/nodejs/src/image-processing/image-bytes.ts` - byte reading/writing helpers
- `src/nodejs/src/image-processing/image-format.ts` - format sniffing, animation detection, MIME types
- `src/nodejs/src/image-processing/image-geometry.ts` - `fitWithin`
- `src/nodejs/src/image-processing/metadata-stripper.ts` - lossless strip
- `src/nodejs/src/image-processing/jpegli-encoder.ts` - WASM loader + encode
- `src/nodejs/src/image-processing/image-encoding-policy.ts` - passthrough vs re-encode decision
- `src/nodejs/src/image-processing/image-processor-worker.ts`, `image-processor-worker-bootstrap.ts`
- `src/nodejs/src/image-processing/image-processor.ts` - main-thread entry point
- `tests/ts/unit/image-format.test.ts`, `image-geometry.test.ts`, `metadata-stripper.test.ts`, `jpegli-encoder.test.ts`, `image-encoding-policy.test.ts`

**Create (TS, app):** `src/dotnet/UI.Blazor.App/Services/FileProviders/image-processing-interop.ts`

**Create (C#):**
- `src/dotnet/Core.Server/Uploads/ImageMetadataStripper.cs`, `AttachmentImageUploadProcessor.cs`
- `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageQualityPreset.cs`, `ImageProcessRequest.cs`, `ProcessedImage.cs`, `ImageAttachmentProcessor.cs`, `IProcessedImageStore.cs`
- `src/dotnet/App.Maui/Services/MauiProcessedImageStore.cs`, `src/dotnet/App.Maui/Platforms/Android/AndroidHeifDecoder.cs`
- `tests/Core.Server.UnitTests/Uploads/ImageMetadataStripperTest.cs`, `AttachmentImageUploadProcessorTest.cs`, `tests/Media.UnitTests/UploadTest.cs`, `tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs`

**Modify:** `build.mjs`, `vitest.config.ts`, `UI.Blazor.App/exports.ts`, `web-file-providers.ts`, `WebFileProvider.cs`, `MauiFileProvider.cs`, `AppleFileProviderImpl.cs`, `AndroidFileProviderImpl.cs`, `WindowsFileProviderImpl.cs`, `AndroidContentDownloader.cs`, `ContentSchemeHandler.cs`, `MauiWebView.Windows.cs`, `MauiAppModule.cs`, `BlazorUIAppModule.cs`, `Attachment.cs`, `AttachmentCleanup.cs`, `AttachmentList.cs`, `AttachmentListView.razor`, `AttachmentItem.razor`, `ImageQualitySelector.razor`, `ImageQualityMenu.razor`, `ImageQualityPresetSelectedEvent.cs`, `FileAttachments.cs`, `ChatMessageEditor.razor`, `UploadSessions.cs`, `UploadedFile.cs`, `Upload.cs`, `UploadsBackend.cs`, `ImageUploadProcessor.cs`, `CoreServerModule.cs`, `LocalizedStringsLocalizerExt.cs`, `Strings.*.json`, `tests/Testing/TestImages/TestImages.cs`, `ImageUploadProcessorTest.cs`, `docs/api-index-ts.md` (and `Api/Media/MetadataExt.cs` only if Task 9 Step 6 needs it).

---

### Task 0: Link the GitHub issue

- [ ] **Step 1:** Run the `/track-issue` skill (`.claude/skills/track-issue/SKILL.md`) on branch `feat/media-quality-selector`. Link it to PR #4472's issue if one exists; otherwise let the skill file one titled "Client-side image pipeline (jpegli WASM) for attachments". Never push to `feat/media-quality-selector` (it belongs to iqmulator) without the user's go-ahead.

---

### Task 1: Local content URLs readable by `fetch` on MAUI

Risk-first: if `fetch` of a local file URL can't be made to work on a platform, stop and report - that platform needs spec option 2 (`DotNetStreamReference`), which changes Tasks 10-11.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/FileProviders/MauiFileProvider.cs`
- Modify: `src/dotnet/App.Maui/Apple/AppleFileProviderImpl.cs`, `src/dotnet/App.Maui/Apple/ContentSchemeHandler.cs:37-39`
- Modify: `src/dotnet/App.Maui/Platforms/Windows/WindowsFileProviderImpl.cs`, `src/dotnet/App.Maui/WebView/MauiWebView.Windows.cs:36-37,93,150-154`
- Modify: `src/dotnet/App.Maui/Platforms/Android/AndroidFileProviderImpl.cs`

**Interfaces:**
- Produces: `IMauiFileProviderImpl.GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken): Task<string>`; `MauiFileProvider.GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken): Task<string>`. `decodeMaxSize` is only used by Android HEIF decoding (Task 11); in this task every implementation ignores it.

- [ ] **Step 1: Add the member to the interface and provider** (`MauiFileProvider.cs`)

In `IMauiFileProviderImpl` add after `OpenRead`:

```csharp
    Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken);
```

In `MauiFileProvider` add after `GetUploadSource()`:

```csharp
    public Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
        => Impl.GetContentUrl(decodeMaxSize, cancellationToken);
```

- [ ] **Step 2: Apple implementation** (`AppleFileProviderImpl.cs`, after `OpenRead`)

```csharp
    public async Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
    {
        await WhenFileStreamReady().WaitAsync(cancellationToken).ConfigureAwait(false);
        return ContentResolver.GetFileUri(filePath);
    }
```

- [ ] **Step 3: CORS header on the Apple content scheme** (`ContentSchemeHandler.cs`)

Replace the `headers` construction with:

```csharp
            // fetch() from the app://0.0.0.1 page is cross-origin for this scheme
            var headers = new NSDictionary(
                new NSString("Content-Type"), new NSString(contentType),
                new NSString("Content-Length"), new NSString(fileInfo.Length.ToString()),
                new NSString("Access-Control-Allow-Origin"), new NSString("*"));
```

- [ ] **Step 4: Windows implementation** (`WindowsFileProviderImpl.cs`, after `OpenRead`)

```csharp
    public Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
        => Task.FromResult(ContentResolver.GetFileUri(filePath));
```

- [ ] **Step 5: Windows scheme registration, fetch filter and CORS header** (`MauiWebView.Windows.cs`)

After the two existing `AddWebResourceRequestedFilter(contentSchemeUriFilter, ...)` lines add:

```csharp
        coreWebView2.AddWebResourceRequestedFilter(contentSchemeUriFilter, CoreWebView2WebResourceContext.Fetch);
```

Replace `private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs) { }` with:

```csharp
    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs)
    {
        // Without a registration WebView2 refuses fetch() from https://0.0.0.1 to content:// URLs
        var registration = new CoreWebView2CustomSchemeRegistration(ContentResolver.UriContentScheme) {
            TreatAsSecure = true,
            HasAuthorityComponent = true,
        };
        registration.SetAllowedOrigins([$"https://{MauiSettings.LocalHost}"]);
        eventArgs.EnvironmentOptions ??= new CoreWebView2EnvironmentOptions();
        eventArgs.EnvironmentOptions.CustomSchemeRegistrations.Add(registration);
    }
```

In `WebResourceUtils.GetResponseHeaders` add the header:

```csharp
            => new Dictionary<string, string> {
                { "Content-Type", contentType },
                { "Cache-Control", "no-cache, max-age=0, must-revalidate, no-store" },
                { "Access-Control-Allow-Origin", "*" },
            };
```

If `SetAllowedOrigins` doesn't compile, the WinRT projection exposes the origins as a settable collection; use whichever member IntelliSense shows on `CoreWebView2CustomSchemeRegistration` for allowed origins.

- [ ] **Step 6: Android implementation** (`AndroidFileProviderImpl.cs`, after `OpenRead`)

```csharp
    public Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
        => Task.FromResult(AndroidContentDownloader.CreateWebRequestUri(Uri));
```

- [ ] **Step 7: Build**

Run: `dotnet build ActualChat.CI.slnf`
Expected: 0 errors. MAUI-only files are excluded from the CI filter, so also build the app for each platform you can: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-windows10.0.19041.0` on Windows, and `ssh macmini` + the `ios-run` skill for iOS.

- [ ] **Step 8: Verify fetch on each platform**

For each of Windows, Android (device + `chrome://inspect`), iOS (Mac Mini + Safari Web Inspector): run the app, attach a photo from the gallery, find the preview `image-skeleton` element's `src`, and in the devtools console run:

```js
const r = await fetch(document.querySelector('.attachment-list image-skeleton').getAttribute('src')); (await r.blob()).size
```

Expected: a positive byte count, no CORS or network error. On failure, stop and report per the risk note above.

- [ ] **Step 9: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/FileProviders/MauiFileProvider.cs src/dotnet/App.Maui
git commit -m "feat(maui): expose attachment files to fetch() via local content URLs"
```

---

### Task 2: Vendor the jpegli WASM build

**Files:**
- Create: `src/nodejs/jpegli/README.md`, `src/nodejs/jpegli/build/Dockerfile`, `src/nodejs/jpegli/build/build.sh`, `src/nodejs/jpegli/build/jpegli_wasm.cc`
- Create (built): `src/nodejs/jpegli/simd/jpegli.js`, `src/nodejs/jpegli/simd/jpegli.wasm`, `src/nodejs/jpegli/scalar/jpegli.js`, `src/nodejs/jpegli/scalar/jpegli.wasm`
- Modify: `build.mjs:36-39`

**Interfaces:**
- Produces: `dist/jpegli/{simd,scalar}/jpegli.js` - ES module whose default export is `createJpegli(options?): Promise<JpegliModule>` with `_jpegli_encode_rgba(rgbaPtr, width, height, qualityOrDistance, useDistance, subsampling, progressive, outSizePtr): number`, `_free_buf(ptr)`, `_jpegli_hwy_target(): number`, `_malloc(n)`, `_free(ptr)`, `HEAPU8`, `HEAPU32`, `UTF8ToString(ptr)`.

- [ ] **Step 1: Copy the build sources**

```bash
SCRATCH="C:/Users/Alex/AppData/Local/Temp/claude/D--Projects-ActualChat-C2/1f475c4d-4463-4534-aa13-8f596f40248d/scratchpad/jpegli-wasm"
mkdir -p src/nodejs/jpegli/build
cp "$SCRATCH/Dockerfile" "$SCRATCH/build.sh" "$SCRATCH/jpegli_wasm.cc" src/nodejs/jpegli/build/
```

If the scratchpad is gone, recreate the three files from the spec's build description: `Dockerfile` clones google/jpegli at the pinned commit with submodules `third_party/highway third_party/libjpeg-turbo third_party/skcms` into `emscripten/emsdk:6.0.9` and runs `build.sh`; `build.sh` builds targets `jpegli-static hwy` with `-DJPEGLI_ENABLE_WASM_THREADS=OFF -DJPEGLI_ENABLE_SKCMS=ON -DJPEGLI_BUNDLE_LIBPNG=OFF` and tools/tests off, twice (plain and `-msimd128`), and links `jpegli_wasm.cc` with `-O3 -flto -fno-exceptions -fno-rtti -sMODULARIZE=1 -sEXPORT_ES6=1 -sEXPORT_NAME=createJpegli -sENVIRONMENT=web,worker,node -sFILESYSTEM=0 -sALLOW_MEMORY_GROWTH=1 -sINITIAL_MEMORY=64MB -sSTACK_SIZE=1MB -sEXPORTED_FUNCTIONS=_jpegli_encode_rgba,_free_buf,_jpegli_hwy_target,_malloc,_free -sEXPORTED_RUNTIME_METHODS=HEAPU8,HEAPU32,UTF8ToString`. Drop the `SINGLE_FILE` variant; it isn't shipped.

In the copied `build.sh`, delete the `if [[ "$name" == simd ]]; then ... fi` block that builds `jpegli.single.js`.

- [ ] **Step 2: Build and copy artifacts**

```bash
cd src/nodejs/jpegli/build && docker build -o ../dist-tmp . && cd -
cp -r src/nodejs/jpegli/dist-tmp/simd src/nodejs/jpegli/dist-tmp/scalar src/nodejs/jpegli/
cat src/nodejs/jpegli/dist-tmp/BUILD_INFO.txt
rm -rf src/nodejs/jpegli/dist-tmp
```

Expected: `src/nodejs/jpegli/simd/jpegli.wasm` about 166 KB and `scalar/jpegli.wasm` about 154 KB; `BUILD_INFO.txt` shows the pinned jpegli commit.

- [ ] **Step 3: Write the README** (`src/nodejs/jpegli/README.md`)

```markdown
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
```

- [ ] **Step 4: Copy to `dist` in `build.mjs`**

After the `libav` copy block in `copyAssets()` add:

```js
    // jpegli WASM encoder: fetched at runtime by image-processing/jpegli-encoder.ts,
    // never bundled. See src/nodejs/jpegli/README.md.
    await fs.promises.cp('./src/nodejs/jpegli', `${outputPath}/jpegli`, {
        recursive: true,
        filter: (src) => path.extname(src) !== '.md' && path.basename(src) !== 'build',
    });
```

- [ ] **Step 5: Verify the copy**

Run: `node build.mjs` then `ls src/dotnet/App.Wasm/wwwroot/dist/jpegli/simd src/dotnet/App.Maui/wwwroot/dist/jpegli/simd`
Expected: `jpegli.js  jpegli.wasm` in both; no `build` folder under `dist/jpegli`.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/jpegli build.mjs
git commit -m "build(image-processing): vendor jpegli WebAssembly encoder"
```

### Task 3: Image processing contracts, format sniffing, geometry and encoding policy

**Files:**
- Create: `src/nodejs/src/image-processing/image-processing-contracts.ts`, `image-bytes.ts`, `image-format.ts`, `image-geometry.ts`, `image-encoding-policy.ts`
- Modify: `vitest.config.ts`
- Test: `tests/ts/unit/image-format.test.ts`, `tests/ts/unit/image-geometry.test.ts`, `tests/ts/unit/image-encoding-policy.test.ts`

**Interfaces:**
- Produces (contracts): `ImageFormat`, `ImageOutputKind = 'main' | 'estimate'`, `ImageOutputCodec = 'auto' | 'passthrough'`, `ImageOutputSpec { kind; maxSize: number | null; codec; stripMetadata: boolean; maxPassthroughSize: number | null }`, `ImageProcessRequest { outputs: ImageOutputSpec[] }`, `ImageOutput { kind; blob: Blob; mimeType: string; width: number; height: number; isSource: boolean }`, `ImageProcessResult { format: ImageFormat; outputs: ImageOutput[] }`, `ImageProcessorWorker { init(jpegliBaseUrl: string): Promise<void>; process(source: Blob, request: ImageProcessRequest, timeout?: RpcTimeout): Promise<ImageProcessResult> }`.
- Produces (bytes): `readAscii(bytes, offset, length): string`, `readUint16BE/readUint16LE/readUint32BE/readUint32LE(bytes, offset): number`, `writeUint32LE(bytes, offset, value): void`, `startsWith(bytes, offset, prefix: ArrayLike<number>): boolean`, `indexOfAscii(bytes, text): number`, `concatBytes(parts: Uint8Array[]): Uint8Array`.
- Produces: `sniffImageFormat(bytes: Uint8Array): ImageFormat`, `isAnimatedImage(bytes, format): boolean`, `readImageDimensions(bytes, format): ImageSize | null` (header only, no decode; pre-orientation), `getImageMimeType(format): string`, `fitWithin(width, height, maxSize: number | null): ImageSize`, `chooseEncoding(format, isAnimated, codec, isOversized): 'passthrough' | 'reencode'`.

- [ ] **Step 1: Add vitest aliases** (`vitest.config.ts`, after the `async-processor` alias)

```ts
            'image-processing/image-bytes': src('image-processing/image-bytes'),
            'image-processing/image-format': src('image-processing/image-format'),
            'image-processing/image-geometry': src('image-processing/image-geometry'),
            'image-processing/image-encoding-policy': src('image-processing/image-encoding-policy'),
            'image-processing/metadata-stripper': src('image-processing/metadata-stripper'),
            'image-processing/jpegli-encoder': src('image-processing/jpegli-encoder'),
            'device-info': src('device-info'),
```

(`device-info` is already aliased; skip that line if present.)

- [ ] **Step 2: Write the failing tests**

`tests/ts/unit/image-format.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { getImageMimeType, isAnimatedImage, readImageDimensions, sniffImageFormat } from 'image-processing/image-format';

const ascii = (text: string): number[] => Array.from(text, c => c.charCodeAt(0));
const bytesOf = (...parts: number[][]): Uint8Array => new Uint8Array(parts.flat());
const u32be = (value: number): number[] => [value >>> 24, (value >>> 16) & 0xFF, (value >>> 8) & 0xFF, value & 0xFF];
const pngChunk = (type: string, data: number[] = []): number[] => [...u32be(data.length), ...ascii(type), ...data, 0, 0, 0, 0];
const PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

describe('sniffImageFormat', () => {
    it('should detect common raster formats', () => {
        expect(sniffImageFormat(bytesOf([0xFF, 0xD8, 0xFF, 0xE0]))).toBe('jpeg');
        expect(sniffImageFormat(bytesOf(PNG_SIGNATURE))).toBe('png');
        expect(sniffImageFormat(bytesOf(ascii('GIF89a')))).toBe('gif');
        expect(sniffImageFormat(bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBP')))).toBe('webp');
        expect(sniffImageFormat(bytesOf(ascii('BM'), [0, 0, 0, 0]))).toBe('bmp');
    });

    it('should detect HEIF and AVIF by ftyp brands', () => {
        expect(sniffImageFormat(bytesOf(u32be(16), ascii('ftypheic'), [0, 0, 0, 0]))).toBe('heif');
        expect(sniffImageFormat(bytesOf(u32be(16), ascii('ftypavif'), [0, 0, 0, 0]))).toBe('avif');
        expect(sniffImageFormat(bytesOf(u32be(24), ascii('ftypmif1'), [0, 0, 0, 0], ascii('miafavif')))).toBe('avif');
    });

    it('should detect SVG and fall back to unknown', () => {
        expect(sniffImageFormat(bytesOf(ascii('<?xml version="1.0"?>\n<svg xmlns="x"/>')))).toBe('svg');
        expect(sniffImageFormat(bytesOf(ascii('hello world')))).toBe('unknown');
    });
});

describe('isAnimatedImage', () => {
    it('should detect APNG by acTL before IDAT', () => {
        const apng = bytesOf(PNG_SIGNATURE, pngChunk('IHDR', new Array(13).fill(0)), pngChunk('acTL', new Array(8).fill(0)), pngChunk('IDAT'));
        const png = bytesOf(PNG_SIGNATURE, pngChunk('IHDR', new Array(13).fill(0)), pngChunk('IDAT'), pngChunk('acTL'));

        expect(isAnimatedImage(apng, 'png')).toBe(true);
        expect(isAnimatedImage(png, 'png')).toBe(false);
    });

    it('should detect animated WebP by the VP8X animation flag', () => {
        const webp = (flags: number): Uint8Array => bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBPVP8X'), [10, 0, 0, 0], [flags], new Array(9).fill(0));

        expect(isAnimatedImage(webp(0x02), 'webp')).toBe(true);
        expect(isAnimatedImage(webp(0x10), 'webp')).toBe(false);
    });
});

describe('readImageDimensions', () => {
    it('should read JPEG dimensions from the SOF segment', () => {
        const jpeg = bytesOf([0xFF, 0xD8], [0xFF, 0xE0, 0x00, 0x04, 0, 0], [0xFF, 0xC2, 0x00, 0x0B, 8, 0x0B, 0xB8, 0x0F, 0xA0, 3, 0, 0, 0]);

        expect(readImageDimensions(jpeg, 'jpeg')).toEqual({ width: 4000, height: 3000 });
    });

    it('should read PNG dimensions from IHDR', () => {
        const png = bytesOf(PNG_SIGNATURE, pngChunk('IHDR', [...u32be(8192), ...u32be(6144), 8, 6, 0, 0, 0]));

        expect(readImageDimensions(png, 'png')).toEqual({ width: 8192, height: 6144 });
    });

    it('should read extended WebP dimensions from VP8X', () => {
        const width = 9999;
        const height = 4999;
        const webp = bytesOf(ascii('RIFF'), [0, 0, 0, 0], ascii('WEBPVP8X'), [10, 0, 0, 0], [0, 0, 0, 0],
            [width & 0xFF, (width >> 8) & 0xFF, width >> 16], [height & 0xFF, (height >> 8) & 0xFF, height >> 16]);

        expect(readImageDimensions(webp, 'webp')).toEqual({ width: 10000, height: 5000 });
    });

    it('should read the largest HEIF ispe box', () => {
        const ispe = (w: number, h: number): number[] => [...u32be(20), ...ascii('ispe'), 0, 0, 0, 0, ...u32be(w), ...u32be(h)];
        const heif = bytesOf(u32be(16), ascii('ftypheic'), [0, 0, 0, 0], ispe(512, 512), ispe(16320, 12240));

        expect(readImageDimensions(heif, 'heif')).toEqual({ width: 16320, height: 12240 });
    });

    it('should return null for formats it does not read', () => {
        expect(readImageDimensions(bytesOf(ascii('GIF89a')), 'gif')).toBeNull();
    });
});

describe('getImageMimeType', () => {
    it('should map formats to MIME types', () => {
        expect(getImageMimeType('jpeg')).toBe('image/jpeg');
        expect(getImageMimeType('heif')).toBe('image/heif');
        expect(getImageMimeType('unknown')).toBe('application/octet-stream');
    });
});
```

`tests/ts/unit/image-geometry.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { fitWithin } from 'image-processing/image-geometry';

describe('fitWithin', () => {
    it('should scale the long side down to maxSize', () => {
        expect(fitWithin(4000, 3000, 3840)).toEqual({ width: 3840, height: 2880 });
        expect(fitWithin(3000, 4000, 1920)).toEqual({ width: 1440, height: 1920 });
    });

    it('should keep images that already fit', () => {
        expect(fitWithin(1000, 800, 1920)).toEqual({ width: 1000, height: 800 });
        expect(fitWithin(5000, 4000, null)).toEqual({ width: 5000, height: 4000 });
    });

    it('should never produce a zero dimension', () => {
        expect(fitWithin(10000, 1, 1920)).toEqual({ width: 1920, height: 1 });
    });
});
```

`tests/ts/unit/image-encoding-policy.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { chooseEncoding } from 'image-processing/image-encoding-policy';

describe('chooseEncoding', () => {
    it('should pass through when requested or animated', () => {
        expect(chooseEncoding('jpeg', false, 'passthrough', false)).toBe('passthrough');
        expect(chooseEncoding('webp', true, 'auto', false)).toBe('passthrough');
    });

    it('should re-encode an oversized passthrough image unless it must never be re-encoded', () => {
        expect(chooseEncoding('heif', false, 'passthrough', true)).toBe('reencode');
        expect(chooseEncoding('webp', true, 'passthrough', true)).toBe('passthrough');
        expect(chooseEncoding('gif', false, 'passthrough', true)).toBe('passthrough');
    });

    it('should pass through formats that must not be re-encoded', () => {
        for (const format of ['gif', 'svg', 'unknown'] as const)
            expect(chooseEncoding(format, false, 'auto', false)).toBe('passthrough');
    });

    it('should re-encode decodable still images', () => {
        for (const format of ['jpeg', 'png', 'webp', 'bmp', 'heif', 'avif'] as const)
            expect(chooseEncoding(format, false, 'auto', false)).toBe('reencode');
    });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/image-format.test.ts tests/ts/unit/image-geometry.test.ts tests/ts/unit/image-encoding-policy.test.ts`
Expected: FAIL - modules not found.

- [ ] **Step 4: Write the contracts** (`image-processing-contracts.ts`)

```ts
import type { RpcTimeout } from 'rpc';

export type ImageFormat = 'jpeg' | 'png' | 'webp' | 'gif' | 'bmp' | 'heif' | 'avif' | 'svg' | 'unknown';
export type ImageOutputKind = 'main' | 'estimate';
export type ImageOutputCodec = 'auto' | 'passthrough';

export interface ImageOutputSpec {
    kind: ImageOutputKind;
    maxSize: number | null;
    codec: ImageOutputCodec;
    stripMetadata: boolean;
    /** A passthrough output of an image whose long side exceeds this is re-encoded within maxSize instead. */
    maxPassthroughSize: number | null;
}

export interface ImageProcessRequest {
    outputs: ImageOutputSpec[];
}

export interface ImageOutput {
    kind: ImageOutputKind;
    blob: Blob;
    mimeType: string;
    /** 0 for a passthrough output: the image isn't decoded then. */
    width: number;
    height: number;
    /** True when blob is the source itself, i.e. nothing had to change. */
    isSource: boolean;
}

export interface ImageProcessResult {
    format: ImageFormat;
    outputs: ImageOutput[];
}

export interface ImageProcessorWorker {
    init(jpegliBaseUrl: string): Promise<void>;
    /** `timeout` is consumed by the RPC client, the worker never receives it. */
    process(source: Blob, request: ImageProcessRequest, timeout?: RpcTimeout): Promise<ImageProcessResult>;
}
```

- [ ] **Step 5: Write the byte helpers** (`image-bytes.ts`)

```ts
export function readAscii(bytes: Uint8Array, offset: number, length: number): string {
    if (offset < 0 || offset + length > bytes.length)
        return '';

    let result = '';
    for (let i = offset; i < offset + length; i++)
        result += String.fromCharCode(bytes[i]);
    return result;
}

export function readUint16BE(bytes: Uint8Array, offset: number): number {
    return (bytes[offset] << 8) | bytes[offset + 1];
}

export function readUint16LE(bytes: Uint8Array, offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8);
}

export function readUint32BE(bytes: Uint8Array, offset: number): number {
    return ((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]) >>> 0;
}

export function readUint32LE(bytes: Uint8Array, offset: number): number {
    return (bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)) >>> 0;
}

export function writeUint32LE(bytes: Uint8Array, offset: number, value: number): void {
    bytes[offset] = value & 0xFF;
    bytes[offset + 1] = (value >>> 8) & 0xFF;
    bytes[offset + 2] = (value >>> 16) & 0xFF;
    bytes[offset + 3] = (value >>> 24) & 0xFF;
}

export function startsWith(bytes: Uint8Array, offset: number, prefix: ArrayLike<number>): boolean {
    if (offset + prefix.length > bytes.length)
        return false;

    for (let i = 0; i < prefix.length; i++) {
        if (bytes[offset + i] !== prefix[i])
            return false;
    }
    return true;
}

export function indexOfAscii(bytes: Uint8Array, text: string): number {
    const prefix = Array.from(text, c => c.charCodeAt(0));
    for (let i = 0; i + prefix.length <= bytes.length; i++) {
        if (startsWith(bytes, i, prefix))
            return i;
    }
    return -1;
}

export function concatBytes(parts: Uint8Array[]): Uint8Array {
    const result = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }
    return result;
}
```

- [ ] **Step 6: Write format sniffing** (`image-format.ts`)

```ts
import type { ImageFormat } from './image-processing-contracts';
import type { ImageSize } from './image-geometry';
import { readAscii, readUint16BE, readUint16LE, readUint32BE, readUint32LE, startsWith } from './image-bytes';

const PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
const HEIF_BRANDS = new Set(['heic', 'heix', 'hevc', 'hevx', 'heif', 'heim', 'heis', 'mif1', 'msf1']);
const WEBP_ANIMATION_FLAG = 0x02;
const MIME_TYPES: Record<ImageFormat, string> = {
    jpeg: 'image/jpeg',
    png: 'image/png',
    webp: 'image/webp',
    gif: 'image/gif',
    bmp: 'image/bmp',
    heif: 'image/heif',
    avif: 'image/avif',
    svg: 'image/svg+xml',
    unknown: 'application/octet-stream',
};

export function sniffImageFormat(bytes: Uint8Array): ImageFormat {
    if (startsWith(bytes, 0, [0xFF, 0xD8, 0xFF]))
        return 'jpeg';
    if (startsWith(bytes, 0, PNG_SIGNATURE))
        return 'png';
    if (readAscii(bytes, 0, 4) === 'GIF8')
        return 'gif';
    if (readAscii(bytes, 0, 4) === 'RIFF' && readAscii(bytes, 8, 4) === 'WEBP')
        return 'webp';
    if (readAscii(bytes, 0, 2) === 'BM')
        return 'bmp';
    if (readAscii(bytes, 4, 4) === 'ftyp')
        return getIsoBaseMediaFormat(bytes);
    if (looksLikeSvg(bytes))
        return 'svg';

    return 'unknown';
}

export function isAnimatedImage(bytes: Uint8Array, format: ImageFormat): boolean {
    if (format === 'png')
        return hasPngChunkBeforeImageData(bytes, 'acTL');
    if (format === 'webp')
        return readAscii(bytes, 12, 4) === 'VP8X' && bytes.length > 20 && (bytes[20] & WEBP_ANIMATION_FLAG) !== 0;

    return false;
}

/** Reads the stored (pre-orientation) dimensions from the header without decoding the image. */
export function readImageDimensions(bytes: Uint8Array, format: ImageFormat): ImageSize | null {
    switch (format) {
    case 'jpeg':
        return readJpegDimensions(bytes);
    case 'png':
        return bytes.length >= 24 ? { width: readUint32BE(bytes, 16), height: readUint32BE(bytes, 20) } : null;
    case 'webp':
        return readWebpDimensions(bytes);
    case 'heif':
    case 'avif':
        return readLargestIspeDimensions(bytes);
    default:
        return null;
    }
}

export function getImageMimeType(format: ImageFormat): string {
    return MIME_TYPES[format];
}

// Private methods

function readJpegDimensions(bytes: Uint8Array): ImageSize | null {
    let offset = 2;
    while (offset + 9 <= bytes.length && bytes[offset] === 0xFF) {
        const marker = bytes[offset + 1];
        const isStartOfFrame = marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC;
        if (isStartOfFrame)
            return { width: readUint16BE(bytes, offset + 7), height: readUint16BE(bytes, offset + 5) };
        if (marker === 0xDA)
            return null;

        offset += 2 + readUint16BE(bytes, offset + 2);
    }
    return null;
}

function readWebpDimensions(bytes: Uint8Array): ImageSize | null {
    const chunk = readAscii(bytes, 12, 4);
    if (chunk === 'VP8X' && bytes.length >= 30)
        return { width: 1 + readUint24LE(bytes, 24), height: 1 + readUint24LE(bytes, 27) };
    if (chunk === 'VP8 ' && bytes.length >= 30)
        return { width: readUint16LE(bytes, 26) & 0x3FFF, height: readUint16LE(bytes, 28) & 0x3FFF };
    if (chunk === 'VP8L' && bytes.length >= 25) {
        const bits = readUint32LE(bytes, 21);
        return { width: (bits & 0x3FFF) + 1, height: ((bits >>> 14) & 0x3FFF) + 1 };
    }

    return null;
}

function readLargestIspeDimensions(bytes: Uint8Array): ImageSize | null {
    // Every image item - grid tiles, thumbnails, the primary image - has an 'ispe'; the largest is the full image
    let result: ImageSize | null = null;
    for (let offset = 4; offset + 16 <= bytes.length; offset++) {
        if (bytes[offset] !== 0x69 || readAscii(bytes, offset, 4) !== 'ispe')
            continue;

        const width = readUint32BE(bytes, offset + 8);
        const height = readUint32BE(bytes, offset + 12);
        if (!result || width * height > result.width * result.height)
            result = { width, height };
    }
    return result;
}

function readUint24LE(bytes: Uint8Array, offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
}

function getIsoBaseMediaFormat(bytes: Uint8Array): ImageFormat {
    const boxEnd = Math.min(readUint32BE(bytes, 0), bytes.length);
    const majorBrand = readAscii(bytes, 8, 4);
    if (majorBrand === 'avif' || majorBrand === 'avis')
        return 'avif';

    // Compatible brands start after the major brand and minor version
    for (let offset = 16; offset + 4 <= boxEnd; offset += 4) {
        if (readAscii(bytes, offset, 4) === 'avif')
            return 'avif';
    }
    return HEIF_BRANDS.has(majorBrand) ? 'heif' : 'unknown';
}

function hasPngChunkBeforeImageData(bytes: Uint8Array, type: string): boolean {
    let offset = 8;
    while (offset + 8 <= bytes.length) {
        const chunkType = readAscii(bytes, offset + 4, 4);
        if (chunkType === type)
            return true;
        if (chunkType === 'IDAT')
            return false;

        offset += 12 + readUint32BE(bytes, offset);
    }
    return false;
}

function looksLikeSvg(bytes: Uint8Array): boolean {
    const head = readAscii(bytes, 0, Math.min(bytes.length, 1024)).replace(/^\xEF\xBB\xBF/, '').trimStart();
    return head.startsWith('<svg') || (head.startsWith('<?xml') && head.includes('<svg'));
}
```

- [ ] **Step 7: Write geometry and policy**

`image-geometry.ts`:

```ts
export interface ImageSize {
    width: number;
    height: number;
}

export function fitWithin(width: number, height: number, maxSize: number | null): ImageSize {
    const longSide = Math.max(width, height);
    if (maxSize === null || longSide <= maxSize)
        return { width, height };

    const scale = maxSize / longSide;
    return {
        width: Math.max(1, Math.round(width * scale)),
        height: Math.max(1, Math.round(height * scale)),
    };
}
```

`image-encoding-policy.ts`:

```ts
import type { ImageFormat, ImageOutputCodec } from './image-processing-contracts';

export type ImageEncoding = 'passthrough' | 'reencode';

export function chooseEncoding(
    format: ImageFormat,
    isAnimated: boolean,
    codec: ImageOutputCodec,
    isOversized: boolean,
): ImageEncoding {
    if (isAnimated || format === 'gif' || format === 'svg' || format === 'unknown')
        return 'passthrough';

    return codec === 'auto' || isOversized ? 'reencode' : 'passthrough';
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/image-format.test.ts tests/ts/unit/image-geometry.test.ts tests/ts/unit/image-encoding-policy.test.ts`
Expected: PASS (3 files).

- [ ] **Step 9: Commit**

```bash
git add src/nodejs/src/image-processing vitest.config.ts tests/ts/unit/image-format.test.ts tests/ts/unit/image-geometry.test.ts tests/ts/unit/image-encoding-policy.test.ts
git commit -m "feat(image-processing): format sniffing, geometry and encoding policy"
```

---

### Task 4: Lossless metadata stripper (TS)

**Files:**
- Create: `src/nodejs/src/image-processing/metadata-stripper.ts`
- Test: `tests/ts/unit/metadata-stripper.test.ts`

**Interfaces:**
- Consumes: `image-bytes.ts` helpers, `ImageFormat` (Task 3).
- Produces: `stripImageMetadata(bytes: Uint8Array, format: ImageFormat): Uint8Array` - returns the same instance when nothing changes, the format isn't JPEG/PNG/WebP, or the file is malformed.

- [ ] **Step 1: Write the failing test** (`tests/ts/unit/metadata-stripper.test.ts`)

```ts
import { describe, it, expect } from 'vitest';
import { stripImageMetadata } from 'image-processing/metadata-stripper';

const ascii = (text: string): number[] => Array.from(text, c => c.charCodeAt(0));
const u32be = (value: number): number[] => [value >>> 24, (value >>> 16) & 0xFF, (value >>> 8) & 0xFF, value & 0xFF];
const u32le = (value: number): number[] => [value & 0xFF, (value >>> 8) & 0xFF, (value >>> 16) & 0xFF, value >>> 24];
const contains = (bytes: Uint8Array, text: string): boolean => Buffer.from(bytes).includes(Buffer.from(text, 'latin1'));

const jpegSegment = (marker: number, payload: number[]): number[] =>
    [0xFF, marker, (payload.length + 2) >> 8, (payload.length + 2) & 0xFF, ...payload];
const exifPayload = (orientation: number): number[] => [
    ...ascii('Exif\0\0'),
    0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
    0x02, 0x00,
    0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, orientation, 0x00, 0x00, 0x00,
    0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    ...ascii('GPSSECRET'),
];

const SOI = [0xFF, 0xD8];
const ICC = jpegSegment(0xE2, [...ascii('ICC_PROFILE\0'), 1, 1, 0xAA, 0xBB]);
const MPF = jpegSegment(0xE2, [...ascii('MPF\0'), 0x4D, 0x4D, 0x00, 0x2A]);
const XMP = jpegSegment(0xE1, [...ascii('http://ns.adobe.com/xap/1.0/\0'), ...ascii('<x:xmpmeta>creator</x:xmpmeta>')]);
const XMP_HDR = jpegSegment(0xE1, [...ascii('http://ns.adobe.com/xap/1.0/\0'), ...ascii('<x hdrgm:Version="1.0"/>')]);
const COM = jpegSegment(0xFE, ascii('secret comment'));
const IPTC = jpegSegment(0xED, ascii('Photoshop 3.0\0'));
const DQT = jpegSegment(0xDB, [0x00, ...new Array<number>(64).fill(1)]);
const SCAN = [0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 63, 0, 0x12, 0x34, 0xFF, 0xD9];
const GAIN_MAP = [0xFF, 0xD8, 0x55, 0xFF, 0xD9];

describe('stripImageMetadata: JPEG', () => {
    it('should drop EXIF, XMP, IPTC and comments and keep ICC, image data and trailer', () => {
        const input = new Uint8Array([...SOI, ...jpegSegment(0xE1, exifPayload(1)), ...XMP, ...IPTC, ...COM, ...ICC, ...DQT, ...SCAN, ...GAIN_MAP]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...ICC, ...DQT, ...SCAN, ...GAIN_MAP]);
    });

    it('should keep only the Orientation tag when it is not 1', () => {
        const input = new Uint8Array([...SOI, ...jpegSegment(0xE1, exifPayload(6)), ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(contains(result, 'GPSSECRET')).toBe(false);
        expect(Array.from(result.subarray(0, 4))).toEqual([0xFF, 0xD8, 0xFF, 0xE1]);
        expect(Array.from(result.subarray(4, 6))).toEqual([0x00, 0x22]);
        expect(result[31]).toBe(6);
        expect(Array.from(result.subarray(38))).toEqual([...DQT, ...SCAN]);
    });

    it('should keep Ultra HDR XMP', () => {
        const input = new Uint8Array([...SOI, ...XMP_HDR, ...COM, ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...XMP_HDR, ...DQT, ...SCAN]);
    });

    it('should not move anything that follows the MPF segment', () => {
        const input = new Uint8Array([...SOI, ...COM, ...MPF, ...COM, ...DQT, ...SCAN]);

        const result = stripImageMetadata(input, 'jpeg');

        expect(Array.from(result)).toEqual([...SOI, ...MPF, ...COM, ...DQT, ...SCAN]);
    });

    it('should return the input instance when there is nothing to strip or the file is malformed', () => {
        const clean = new Uint8Array([...SOI, ...DQT, ...SCAN]);
        const truncated = new Uint8Array([...SOI, 0xFF, 0xE1, 0x10, 0x00, 0x01]);

        expect(stripImageMetadata(clean, 'jpeg')).toBe(clean);
        expect(stripImageMetadata(truncated, 'jpeg')).toBe(truncated);
    });
});

describe('stripImageMetadata: PNG', () => {
    const chunk = (type: string, data: number[] = []): number[] => [...u32be(data.length), ...ascii(type), ...data, 1, 2, 3, 4];
    const SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    it('should drop text, time and EXIF chunks', () => {
        const ihdr = chunk('IHDR', new Array<number>(13).fill(0));
        const iccp = chunk('iCCP', [...ascii('icc\0'), 0, 9]);
        const idat = chunk('IDAT', [7, 7]);
        const iend = chunk('IEND');
        const input = new Uint8Array([
            ...SIGNATURE, ...ihdr, ...chunk('tEXt', ascii('Author\0me')), ...chunk('eXIf', [1, 2]),
            ...chunk('iTXt', [0]), ...chunk('zTXt', [0]), ...chunk('tIME', new Array<number>(7).fill(0)),
            ...iccp, ...idat, ...iend,
        ]);

        const result = stripImageMetadata(input, 'png');

        expect(Array.from(result)).toEqual([...SIGNATURE, ...ihdr, ...iccp, ...idat, ...iend]);
    });
});

describe('stripImageMetadata: WebP', () => {
    const chunk = (fourCC: string, data: number[]): number[] =>
        [...ascii(fourCC), ...u32le(data.length), ...data, ...(data.length % 2 === 1 ? [0] : [])];

    it('should drop EXIF and XMP chunks, fix the RIFF size and clear VP8X flags', () => {
        const vp8x = chunk('VP8X', [0x0C | 0x10, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        const iccp = chunk('ICCP', [9, 9]);
        const vp8 = chunk('VP8 ', [5, 5, 5]);
        const body = [...ascii('WEBP'), ...vp8x, ...iccp, ...vp8, ...chunk('EXIF', ascii('GPSSECRET')), ...chunk('XMP ', [1, 2])];
        const input = new Uint8Array([...ascii('RIFF'), ...u32le(body.length), ...body]);

        const result = stripImageMetadata(input, 'webp');

        const expectedBody = [...ascii('WEBP'), ...vp8x, ...iccp, ...vp8];
        expectedBody[12] = 0x10;
        expect(Array.from(result)).toEqual([...ascii('RIFF'), ...u32le(expectedBody.length), ...expectedBody]);
    });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/metadata-stripper.test.ts`
Expected: FAIL - module not found.

- [ ] **Step 3: Write the stripper** (`src/nodejs/src/image-processing/metadata-stripper.ts`)

```ts
import type { ImageFormat } from './image-processing-contracts';
import {
    concatBytes,
    indexOfAscii,
    readAscii,
    readUint16BE,
    readUint16LE,
    readUint32BE,
    readUint32LE,
    startsWith,
    writeUint32LE,
} from './image-bytes';

const EXIF_HEADER = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];
const XMP_HEADER = 'http://ns.adobe.com/xap/1.0/\0';
const EXIF_ORIENTATION_TAG = 0x0112;
const PNG_DROPPED_CHUNKS = new Set(['eXIf', 'tEXt', 'iTXt', 'zTXt', 'tIME']);
const WEBP_DROPPED_CHUNKS = new Set(['EXIF', 'XMP ']);
const WEBP_METADATA_FLAGS = 0x08 | 0x04;

/** Removes EXIF, XMP, IPTC and text metadata without touching image data. Returns the input
 *  instance when nothing changes, the format isn't JPEG/PNG/WebP, or the file is malformed. */
export function stripImageMetadata(bytes: Uint8Array, format: ImageFormat): Uint8Array {
    switch (format) {
    case 'jpeg':
        return stripJpeg(bytes);
    case 'png':
        return stripPng(bytes);
    case 'webp':
        return stripWebp(bytes);
    default:
        return bytes;
    }
}

// Private methods

function stripJpeg(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 2)];
    let isChanged = false;
    let isAfterMpf = false;
    let offset = 2;
    while (offset + 4 <= bytes.length) {
        if (bytes[offset] !== 0xFF)
            return bytes;

        const marker = bytes[offset + 1];
        if (marker === 0xDA || marker === 0xD9) {
            parts.push(bytes.subarray(offset));
            return isChanged ? concatBytes(parts) : bytes;
        }
        if (marker === 0xFF || marker === 0x01 || (marker >= 0xD0 && marker <= 0xD7)) {
            const length = marker === 0xFF ? 1 : 2;
            parts.push(bytes.subarray(offset, offset + length));
            offset += length;
            continue;
        }

        const end = offset + 2 + readUint16BE(bytes, offset + 2);
        if (end > bytes.length)
            return bytes;

        const segment = bytes.subarray(offset, end);
        const payload = segment.subarray(4);
        // MPF stores offsets to the images after it, so no segment behind MPF may move
        const replacement = isAfterMpf ? segment : filterJpegSegment(marker, segment, payload);
        if (marker === 0xE2 && readAscii(payload, 0, 4) === 'MPF\0')
            isAfterMpf = true;
        if (replacement !== segment)
            isChanged = true;
        if (replacement)
            parts.push(replacement);
        offset = end;
    }
    return bytes;
}

function filterJpegSegment(marker: number, segment: Uint8Array, payload: Uint8Array): Uint8Array | null {
    if (marker === 0xED || marker === 0xFE)
        return null;
    if (marker !== 0xE1)
        return segment;
    if (startsWith(payload, 0, EXIF_HEADER)) {
        const orientation = readExifOrientation(payload.subarray(EXIF_HEADER.length));
        return orientation > 1 ? createOrientationExifSegment(orientation) : null;
    }

    const isHdrXmp = readAscii(payload, 0, XMP_HEADER.length) === XMP_HEADER && indexOfAscii(payload, 'hdrgm') >= 0;
    return isHdrXmp ? segment : null;
}

function readExifOrientation(tiff: Uint8Array): number {
    if (tiff.length < 8)
        return 1;

    const isLittleEndian = tiff[0] === 0x49 && tiff[1] === 0x49;
    if (!isLittleEndian && !(tiff[0] === 0x4D && tiff[1] === 0x4D))
        return 1;

    const readUint16 = (offset: number): number => isLittleEndian ? readUint16LE(tiff, offset) : readUint16BE(tiff, offset);
    const ifdOffset = isLittleEndian ? readUint32LE(tiff, 4) : readUint32BE(tiff, 4);
    if (ifdOffset + 2 > tiff.length)
        return 1;

    const entryCount = readUint16(ifdOffset);
    for (let i = 0; i < entryCount; i++) {
        const entry = ifdOffset + 2 + i * 12;
        if (entry + 12 > tiff.length)
            break;
        if (readUint16(entry) !== EXIF_ORIENTATION_TAG)
            continue;

        const value = readUint16(entry + 8);
        return value >= 1 && value <= 8 ? value : 1;
    }
    return 1;
}

function createOrientationExifSegment(orientation: number): Uint8Array {
    // Big-endian TIFF, IFD0 with one entry: Orientation, SHORT, count 1
    return new Uint8Array([
        0xFF, 0xE1, 0x00, 0x22,
        ...EXIF_HEADER,
        0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
        0x00, 0x01,
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, orientation, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ]);
}

function stripPng(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 8)];
    let isChanged = false;
    let offset = 8;
    while (offset + 12 <= bytes.length) {
        const end = offset + 12 + readUint32BE(bytes, offset);
        if (end > bytes.length)
            return bytes;

        const type = readAscii(bytes, offset + 4, 4);
        if (PNG_DROPPED_CHUNKS.has(type))
            isChanged = true;
        else
            parts.push(bytes.subarray(offset, end));
        offset = end;
        if (type === 'IEND')
            break;
    }
    return isChanged ? concatBytes(parts) : bytes;
}

function stripWebp(bytes: Uint8Array): Uint8Array {
    const parts: Uint8Array[] = [bytes.subarray(0, 12)];
    let isChanged = false;
    let offset = 12;
    while (offset + 8 <= bytes.length) {
        const size = readUint32LE(bytes, offset + 4);
        const end = offset + 8 + size + (size & 1);
        if (end > bytes.length)
            return bytes;

        if (WEBP_DROPPED_CHUNKS.has(readAscii(bytes, offset, 4)))
            isChanged = true;
        else
            parts.push(bytes.subarray(offset, end));
        offset = end;
    }
    if (!isChanged)
        return bytes;

    const result = concatBytes(parts);
    writeUint32LE(result, 4, result.length - 8);
    if (readAscii(result, 12, 4) === 'VP8X')
        result[20] &= ~WEBP_METADATA_FLAGS;
    return result;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/metadata-stripper.test.ts`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/nodejs/src/image-processing/metadata-stripper.ts tests/ts/unit/metadata-stripper.test.ts
git commit -m "feat(image-processing): lossless JPEG/PNG/WebP metadata stripper"
```

### Task 5: jpegli encoder wrapper

**Files:**
- Create: `src/nodejs/src/image-processing/jpegli-encoder.ts`
- Modify: `src/nodejs/src/logging.ts:150,270` (log scopes for this task and Task 6)
- Test: `tests/ts/unit/jpegli-encoder.test.ts`

**Interfaces:**
- Consumes: `dist/jpegli/{simd,scalar}/jpegli.js` layout (Task 2).
- Produces: `class JpegliEncoder { static isSimdSupported(): boolean; static load(baseUrl: string, variant?: 'simd' | 'scalar'): Promise<JpegliEncoder>; readonly variant; encode(rgba: Uint8ClampedArray | Uint8Array, width: number, height: number, options: JpegEncodeOptions): Uint8Array }`, `interface JpegEncodeOptions { distance: number; subsampling: 420 | 444; progressive: 0 | 1 | 2 }`.

- [ ] **Step 1: Write the failing test** (`tests/ts/unit/jpegli-encoder.test.ts`)

```ts
import { describe, it, expect } from 'vitest';
import { JpegliEncoder } from 'image-processing/jpegli-encoder';

const BASE_URL = new URL('../../../src/nodejs/jpegli', import.meta.url).href;

function createGradient(width: number, height: number): Uint8ClampedArray {
    const rgba = new Uint8ClampedArray(width * height * 4);
    for (let i = 0; i < rgba.length; i += 4) {
        rgba[i] = (i / 4) % 256;
        rgba[i + 1] = 128;
        rgba[i + 2] = 255 - ((i / 4) % 256);
        rgba[i + 3] = 255;
    }
    return rgba;
}

function readJpegSize(jpeg: Uint8Array): { width: number, height: number } {
    let offset = 2;
    while (offset + 9 < jpeg.length) {
        const marker = jpeg[offset + 1];
        if (marker === 0xC0 || marker === 0xC2)
            return { height: (jpeg[offset + 5] << 8) | jpeg[offset + 6], width: (jpeg[offset + 7] << 8) | jpeg[offset + 8] };

        offset += 2 + ((jpeg[offset + 2] << 8) | jpeg[offset + 3]);
    }
    throw new Error('No SOF marker');
}

describe('JpegliEncoder', () => {
    for (const variant of ['simd', 'scalar'] as const) {
        it(`should encode RGBA pixels into a JPEG (${variant})`, async () => {
            const encoder = await JpegliEncoder.load(BASE_URL, variant);

            const jpeg = encoder.encode(createGradient(64, 48), 64, 48, { distance: 1.9, subsampling: 420, progressive: 2 });

            expect(encoder.variant).toBe(variant);
            expect(Array.from(jpeg.subarray(0, 2))).toEqual([0xFF, 0xD8]);
            expect(Array.from(jpeg.subarray(jpeg.length - 2))).toEqual([0xFF, 0xD9]);
            expect(readJpegSize(jpeg)).toEqual({ width: 64, height: 48 });
        });
    }

    it('should reuse its input buffer across images of different sizes', async () => {
        const encoder = await JpegliEncoder.load(BASE_URL, 'scalar');

        const large = encoder.encode(createGradient(128, 96), 128, 96, { distance: 1.9, subsampling: 420, progressive: 2 });
        const small = encoder.encode(createGradient(16, 16), 16, 16, { distance: 1.9, subsampling: 420, progressive: 0 });

        expect(readJpegSize(large)).toEqual({ width: 128, height: 96 });
        expect(readJpegSize(small)).toEqual({ width: 16, height: 16 });
    });

    it('should reject pixel data shorter than width * height * 4', async () => {
        const encoder = await JpegliEncoder.load(BASE_URL, 'scalar');

        expect(() => encoder.encode(new Uint8ClampedArray(10), 64, 48, { distance: 1.9, subsampling: 420, progressive: 2 }))
            .toThrow(/shorter/);
    });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/jpegli-encoder.test.ts`
Expected: FAIL - module not found.

- [ ] **Step 3a: Register the log scopes** (`src/nodejs/src/logging.ts`)

`getLogs` only accepts members of the closed `LogScope` union, and `defaults` must list every member. Replace the union's last member line `    | 'WebFileProvider';` with:

```ts
    | 'WebFileProvider'
    | 'ImageProcessor'
    | 'ImageProcessorWorker'
    | 'JpegliEncoder';
```

and add after `    WebFileProvider: LogLevel.Warn,` in `defaults`:

```ts
    ImageProcessor: LogLevel.Warn,
    ImageProcessorWorker: LogLevel.Warn,
    JpegliEncoder: LogLevel.Info,
```

- [ ] **Step 3: Write the encoder** (`src/nodejs/src/image-processing/jpegli-encoder.ts`)

```ts
import { getLogs } from 'logging';

const { infoLog } = getLogs('JpegliEncoder');

// The smallest module using a SIMD opcode: WebAssembly.validate rejects it where SIMD is unsupported
const SIMD_PROBE = new Uint8Array([
    0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 96, 0, 1, 123, 3, 2, 1, 0, 10, 10, 1, 8, 0, 65, 0, 253, 15, 253, 98, 11,
]);

export type JpegliVariant = 'simd' | 'scalar';

export interface JpegEncodeOptions {
    distance: number;
    subsampling: 420 | 444;
    progressive: 0 | 1 | 2;
}

interface JpegliModule {
    HEAPU8: Uint8Array;
    HEAPU32: Uint32Array;
    _malloc(size: number): number;
    _free(pointer: number): void;
    _free_buf(pointer: number): void;
    _jpegli_hwy_target(): number;
    _jpegli_encode_rgba(
        rgba: number,
        width: number,
        height: number,
        qualityOrDistance: number,
        useDistance: number,
        subsampling: number,
        progressive: number,
        outSize: number,
    ): number;
    UTF8ToString(pointer: number): string;
}

export class JpegliEncoder {
    private readonly _sizePointer: number;
    private _inputPointer = 0;
    private _inputCapacity = 0;

    private constructor(
        private readonly module: JpegliModule,
        public readonly variant: JpegliVariant,
    ) {
        this._sizePointer = module._malloc(4);
    }

    public static isSimdSupported(): boolean {
        try {
            return WebAssembly.validate(SIMD_PROBE);
        }
        catch {
            return false;
        }
    }

    public static async load(baseUrl: string, variant?: JpegliVariant): Promise<JpegliEncoder> {
        variant ??= JpegliEncoder.isSimdSupported() ? 'simd' : 'scalar';
        const url = `${baseUrl.replace(/\/$/, '')}/${variant}/jpegli.js`;
        const imported = await import(/* webpackIgnore: true */ /* @vite-ignore */ url) as {
            default: () => Promise<JpegliModule>;
        };
        const module = await imported.default();
        infoLog?.log(`load: '${variant}' ready, Highway target ${module.UTF8ToString(module._jpegli_hwy_target())}`);
        return new JpegliEncoder(module, variant);
    }

    public encode(
        rgba: Uint8ClampedArray | Uint8Array,
        width: number,
        height: number,
        options: JpegEncodeOptions,
    ): Uint8Array {
        const length = width * height * 4;
        if (rgba.length < length)
            throw new Error('JpegliEncoder.encode: pixel data is shorter than width * height * 4.');

        const module = this.module;
        if (this._inputCapacity < length) {
            if (this._inputPointer)
                module._free(this._inputPointer);
            this._inputPointer = module._malloc(length);
            this._inputCapacity = length;
        }
        // HEAPU8/HEAPU32 are re-read after every call that may grow memory: growth replaces them
        module.HEAPU8.set(rgba.subarray(0, length), this._inputPointer);
        const outputPointer = module._jpegli_encode_rgba(
            this._inputPointer, width, height, options.distance, 1, options.subsampling, options.progressive,
            this._sizePointer);
        if (!outputPointer)
            throw new Error('JpegliEncoder.encode: jpegli failed.');

        const size = module.HEAPU32[this._sizePointer >> 2];
        const jpeg = module.HEAPU8.slice(outputPointer, outputPointer + size);
        module._free_buf(outputPointer);
        return jpeg;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run --config vitest.config.ts tests/ts/unit/jpegli-encoder.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/nodejs/src/image-processing/jpegli-encoder.ts tests/ts/unit/jpegli-encoder.test.ts
git commit -m "feat(image-processing): jpegli WASM encoder wrapper"
```

---

### Task 6: Image processor worker and main-thread entry point

**Files:**
- Create: `src/nodejs/src/image-processing/image-processor-worker.ts`, `image-processor-worker-bootstrap.ts`, `image-processor.ts`
- Modify: `build.mjs:54-67` (entry points)

**Interfaces:**
- Consumes: Tasks 3-5 (`sniffImageFormat`, `isAnimatedImage`, `getImageMimeType`, `fitWithin`, `chooseEncoding`, `stripImageMetadata`, `JpegliEncoder`), `rpcServer`/`rpcClient` (`rpc`), `bootstrapWorker` (`worker-bootstrap`), `Versioning.mapPath` (`versioning`), `DeviceInfo` (`device-info`).
- Produces: `class ImageProcessor { static process(source: Blob | string, request: ImageProcessRequest): Promise<ImageProcessResult> }`. For a `string` source the URL is fetched on the main thread. Outputs come back in request order; a re-encoded opaque image is `image/jpeg`, a transparent one `image/png`; passthrough outputs have `width = height = 0`. A passthrough spec whose image has a longer side than `maxPassthroughSize` (read from the header) is re-encoded within `maxSize`. `isSource` marks an output that is the unchanged source `Blob`.

- [ ] **Step 1: Write the worker** (`image-processor-worker.ts`)

```ts
import { rpcServer } from 'rpc';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { chooseEncoding } from './image-encoding-policy';
import { getImageMimeType, isAnimatedImage, readImageDimensions, sniffImageFormat } from './image-format';
import { fitWithin } from './image-geometry';
import { JpegliEncoder } from './jpegli-encoder';
import { stripImageMetadata } from './metadata-stripper';
import type {
    ImageFormat,
    ImageOutput,
    ImageOutputSpec,
    ImageProcessorWorker,
    ImageProcessRequest,
    ImageProcessResult,
} from './image-processing-contracts';

const { debugLog, warnLog, errorLog } = getLogs('ImageProcessorWorker');

const JPEG_DISTANCE = 1.9;
// WebKit's canvas encoder maps quality much higher than Chromium's; both land near SSIMULACRA2 74
const FALLBACK_JPEG_QUALITY = DeviceInfo.isWebKit ? 0.5 : 0.75;

let jpegliBaseUrl = '';
let whenEncoderLoaded: Promise<JpegliEncoder | null> | null = null;
let queueTail: Promise<unknown> = Promise.resolve();

const serverImpl: ImageProcessorWorker = {
    init: (baseUrl: string): Promise<void> => {
        jpegliBaseUrl = baseUrl;
        return Promise.resolve();
    },
    // One image at a time: a decoded 4K frame alone is ~44 MB
    process: (source: Blob, request: ImageProcessRequest): Promise<ImageProcessResult> =>
        enqueue(() => processImage(source, request)),
};

rpcServer('ImageProcessorWorker.server', self as unknown as Worker, serverImpl);

function enqueue<T>(run: () => Promise<T>): Promise<T> {
    const result = queueTail.then(run, run);
    queueTail = result.catch(() => undefined);
    return result;
}

async function processImage(source: Blob, request: ImageProcessRequest): Promise<ImageProcessResult> {
    const startedAt = performance.now();
    const bytes = new Uint8Array(await source.arrayBuffer());
    const format = sniffImageFormat(bytes);
    const isAnimated = isAnimatedImage(bytes, format);
    const dimensions = readImageDimensions(bytes, format);
    const longSide = dimensions ? Math.max(dimensions.width, dimensions.height) : 0;
    const outputs: ImageOutput[] = [];
    let bitmap: ImageBitmap | null = null;
    try {
        for (const spec of request.outputs) {
            const isOversized = spec.maxPassthroughSize !== null && longSide > spec.maxPassthroughSize;
            if (chooseEncoding(format, isAnimated, spec.codec, isOversized) === 'passthrough') {
                outputs.push(createPassthroughOutput(source, bytes, format, spec));
                continue;
            }

            bitmap ??= await createImageBitmap(source);
            outputs.push(await reencode(bitmap, source, bytes, format, spec));
        }
        debugLog?.log(`processImage: ${format}, ${outputs.length} output(s) in ${Math.round(performance.now() - startedAt)}ms`);
        return { format, outputs };
    }
    finally {
        bitmap?.close();
    }
}

function createPassthroughOutput(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    spec: ImageOutputSpec,
): ImageOutput {
    const data = spec.stripMetadata ? stripImageMetadata(bytes, format) : bytes;
    const mimeType = getImageMimeType(format);
    const isSource = data === bytes;
    const blob = isSource ? source : new Blob([data], { type: mimeType });
    return { kind: spec.kind, blob, mimeType, width: 0, height: 0, isSource };
}

async function reencode(
    bitmap: ImageBitmap,
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    spec: ImageOutputSpec,
): Promise<ImageOutput> {
    const size = fitWithin(bitmap.width, bitmap.height, spec.maxSize);
    const canvas = new OffscreenCanvas(size.width, size.height);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, size.width, size.height);
    const image = context.getImageData(0, 0, size.width, size.height);
    if (hasTransparentPixels(image.data)) {
        const png = await canvas.convertToBlob({ type: 'image/png' });
        return {
            kind: spec.kind,
            blob: png,
            mimeType: 'image/png',
            width: size.width,
            height: size.height,
            isSource: false,
        };
    }

    const jpeg = await encodeJpeg(canvas, image);
    const isUnscaledJpeg = format === 'jpeg' && size.width === bitmap.width && size.height === bitmap.height;
    if (isUnscaledJpeg) {
        const stripped = stripImageMetadata(bytes, format);
        if (stripped.length <= jpeg.size) {
            const isSource = stripped === bytes;
            const blob = isSource ? source : new Blob([stripped], { type: 'image/jpeg' });
            return { kind: spec.kind, blob, mimeType: 'image/jpeg', width: size.width, height: size.height, isSource };
        }
    }
    return {
        kind: spec.kind,
        blob: jpeg,
        mimeType: 'image/jpeg',
        width: size.width,
        height: size.height,
        isSource: false,
    };
}

async function encodeJpeg(canvas: OffscreenCanvas, image: ImageData): Promise<Blob> {
    const encoder = await getEncoder();
    if (encoder) {
        try {
            const jpeg = encoder.encode(image.data, image.width, image.height, {
                distance: JPEG_DISTANCE,
                subsampling: 420,
                progressive: 2,
            });
            return new Blob([jpeg], { type: 'image/jpeg' });
        }
        catch (e) {
            warnLog?.log('encodeJpeg: jpegli failed, falling back to canvas:', e);
        }
    }
    return await canvas.convertToBlob({ type: 'image/jpeg', quality: FALLBACK_JPEG_QUALITY });
}

function getEncoder(): Promise<JpegliEncoder | null> {
    whenEncoderLoaded ??= JpegliEncoder.load(jpegliBaseUrl).catch((e: unknown) => {
        errorLog?.log('getEncoder: jpegli failed to load, falling back to canvas:', e);
        return null;
    });
    return whenEncoderLoaded;
}

function hasTransparentPixels(rgba: Uint8ClampedArray): boolean {
    for (let i = 3; i < rgba.length; i += 4) {
        if (rgba[i] !== 255)
            return true;
    }
    return false;
}
```

- [ ] **Step 2: Write the bootstrap** (`image-processor-worker-bootstrap.ts`)

```ts
import { bootstrapWorker } from 'worker-bootstrap';

bootstrapWorker(() => import('./image-processor-worker'));
```

- [ ] **Step 3: Write the main-thread entry point** (`image-processor.ts`)

```ts
import { rpcClient, RpcTimeout } from 'rpc';
import { Disposable } from 'disposable';
import { getLogs } from 'logging';
import { Versioning } from 'versioning';
import type { ImageProcessorWorker, ImageProcessRequest, ImageProcessResult } from './image-processing-contracts';

const { errorLog } = getLogs('ImageProcessor');

const PROCESS_TIMEOUT_MS = 30_000;

/** Resizes, re-encodes (jpegli) or strips metadata of images in a module worker, one image at a time. */
export class ImageProcessor {
    private static _worker: Worker | null = null;
    private static _client: (ImageProcessorWorker & Disposable) | null = null;
    private static _pendingCount = 0;

    /** A string source is fetched here rather than in the worker: WebViews handle
     *  custom-scheme requests from workers inconsistently. */
    public static async process(source: Blob | string, request: ImageProcessRequest): Promise<ImageProcessResult> {
        const blob = typeof source === 'string' ? await fetchBlob(source) : source;
        // The worker runs jobs one at a time, so a job's deadline includes the jobs queued ahead of it
        const timeout: RpcTimeout = { type: 'rpc-timeout', timeoutMs: PROCESS_TIMEOUT_MS * (this._pendingCount + 1) };
        this._pendingCount++;
        try {
            return await this.getClient().process(blob, request, timeout);
        }
        finally {
            this._pendingCount--;
        }
    }

    // Private methods

    private static getClient(): ImageProcessorWorker & Disposable {
        if (this._client)
            return this._client;

        const worker = new Worker(Versioning.mapPath('/dist/imageProcessorWorker.js'), { type: 'module' });
        worker.onerror = (e: ErrorEvent) => {
            errorLog?.log('worker error, recreating on next use:', e);
            this.reset();
        };
        const client = rpcClient<ImageProcessorWorker>('ImageProcessor.client', worker, PROCESS_TIMEOUT_MS);
        client.init(new URL('/dist/jpegli', globalThis.location.href).href)
            .catch((e: unknown) => errorLog?.log('init failed:', e));
        this._worker = worker;
        this._client = client;
        return client;
    }

    private static reset(): void {
        this._client?.dispose();
        this._worker?.terminate();
        this._client = null;
        this._worker = null;
    }
}

async function fetchBlob(url: string): Promise<Blob> {
    const response = await fetch(url);
    if (!response.ok)
        throw new Error(`ImageProcessor: HTTP ${response.status} while fetching '${url}'.`);

    return await response.blob();
}
```

- [ ] **Step 4: Register the worker entry point** (`build.mjs`, after the `onDeviceAwakeWorker` entry)

```js
        { out: 'imageProcessorWorker', in: './src/nodejs/src/image-processing/image-processor-worker-bootstrap.ts' },
```

- [ ] **Step 5: Validate**

Run: `npm run build:Verify`
Expected: `tsc`, eslint and the build pass; `src/dotnet/App.Wasm/wwwroot/dist/imageProcessorWorker.js` exists. If `tsc` rejects `new Blob([data])` for `Uint8Array<ArrayBufferLike>`, pass `data as BlobPart`.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/src/image-processing build.mjs
git commit -m "feat(image-processing): image processor worker with jpegli and canvas fallback"
```

### Task 7: Blazor-facing JS interop

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/FileProviders/image-processing-interop.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/FileProviders/web-file-providers.ts:152-211,240-270`
- Modify: `src/dotnet/UI.Blazor.App/exports.ts:92`

**Interfaces:**
- Consumes: `ImageProcessor.process` (Task 6).
- Produces (called from C# in Task 10):
  - `blazorApp.ImageProcessingInterop.processUrl(url: string, request: ImageProcessRequest): Promise<ProcessedStreamImage>` where `ProcessedStreamImage = ProcessedImageInfo & { stream: JSStreamReference }`.
  - JS `WebFileProvider.processImage(request): Promise<ProcessedWebImage>` where `ProcessedWebImage = ProcessedImageInfo & { fileProvider: JSObjectReference; previewUrl: string }`.
  - `ProcessedImageInfo { mimeType: string; width: number; height: number; size: number; estimateSizes: number[]; isSource: boolean }` - `width`/`height`/`size`/`mimeType` describe the `main` output; `estimateSizes` lists the byte sizes of `estimate` outputs in request order. When `isSource` is true the main output is the unchanged source: `stream` / `fileProvider` are `null` and nothing is copied.

- [ ] **Step 1: Write the interop module** (`image-processing-interop.ts`)

```ts
import { ImageProcessor } from 'image-processing/image-processor';
import type { ImageProcessRequest, ImageProcessResult } from 'image-processing/image-processing-contracts';

export interface ProcessedImageInfo {
    mimeType: string;
    width: number;
    height: number;
    size: number;
    estimateSizes: number[];
    isSource: boolean;
}

export interface ProcessedStreamImage extends ProcessedImageInfo {
    stream: unknown;
}

export class ImageProcessingInterop {
    /** Processes a local content URL (MAUI) and hands the main output to .NET as a stream. */
    public static async processUrl(url: string, request: ImageProcessRequest): Promise<ProcessedStreamImage> {
        const result = await ImageProcessor.process(url, request);
        const main = getMainOutput(result);
        const stream = main.isSource ? null : DotNet.createJSStreamReference(main.blob);
        return { ...getProcessedImageInfo(result), stream };
    }
}

export function getMainOutput(result: ImageProcessResult): ImageProcessResult['outputs'][number] {
    const main = result.outputs.find(o => o.kind === 'main');
    if (!main)
        throw new Error('ImageProcessingInterop: the request has no main output.');

    return main;
}

export function getProcessedImageInfo(result: ImageProcessResult): ProcessedImageInfo {
    const main = getMainOutput(result);
    return {
        mimeType: main.mimeType,
        width: main.width,
        height: main.height,
        size: main.blob.size,
        estimateSizes: result.outputs.filter(o => o.kind === 'estimate').map(o => o.blob.size),
        isSource: main.isSource,
    };
}
```

- [ ] **Step 2: Replace the PR's resize code in `web-file-providers.ts`**

Delete `replaceBlob`, `estimateResizedSizes`, the module-level `canvasToBlob` function and the `ImageResizePreset` / `ImageResizeResult` interfaces. Add the imports at the top:

```ts
import { ImageProcessor } from 'image-processing/image-processor';
import type { ImageProcessRequest } from 'image-processing/image-processing-contracts';
import { getMainOutput, getProcessedImageInfo, ProcessedImageInfo } from './image-processing-interop';
```

Add after `getBlob()`:

```ts
    /** Processes this file and wraps the result in a new, in-memory provider with no file
     *  handle: an upload of a processed image can't resume from the original file after reload. */
    public async processImage(request: ImageProcessRequest): Promise<ProcessedImageInfo & CreateWebFileProviderResult> {
        const result = await ImageProcessor.process(this.getBlob(), request);
        const main = getMainOutput(result);
        if (main.isSource)
            return { ...getProcessedImageInfo(result), previewUrl: '', fileProvider: null };

        const provider = new WebFileProvider('', null, main.blob, null);
        return {
            ...getProcessedImageInfo(result),
            previewUrl: provider.createPreviewUrl(),
            fileProvider: DotNet.createJSObjectReference(provider),
        };
    }
```

- [ ] **Step 3: Export the interop** (`exports.ts`, keep the list sorted where it is)

After `export * from './Services/FileProviders/file-handle-permissions';` add:

```ts
export * from './Services/FileProviders/image-processing-interop';
```

- [ ] **Step 4: Validate**

Run: `npm run build:Verify`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/FileProviders/image-processing-interop.ts src/dotnet/UI.Blazor.App/Services/FileProviders/web-file-providers.ts src/dotnet/UI.Blazor.App/exports.ts
git commit -m "feat(attachments): JS interop for the image processor; drop canvas resize"
```

---

### Task 8: Server-side lossless metadata stripper (C#)

**Files:**
- Create: `src/dotnet/Core.Server/Uploads/ImageMetadataStripper.cs`
- Test: `tests/Core.Server.UnitTests/Uploads/ImageMetadataStripperTest.cs`

**Interfaces:**
- Consumes: `BlobContentTypeDetector.Detect(ReadOnlySpan<byte>)`.
- Produces: `static class ImageMetadataStripper { static byte[] Strip(byte[] data) }` - same rules as the TS stripper; returns `data` itself when nothing changes.

- [ ] **Step 1: Write the failing test** (`ImageMetadataStripperTest.cs`)

```csharp
using System.Text;
using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class ImageMetadataStripperTest
{
    private static readonly byte[] Soi = [0xFF, 0xD8];
    private static readonly byte[] Icc = JpegSegment(0xE2, Ascii("ICC_PROFILE\0"), [1, 1, 0xAA, 0xBB]);
    private static readonly byte[] Mpf = JpegSegment(0xE2, Ascii("MPF\0"), [0x4D, 0x4D, 0x00, 0x2A]);
    private static readonly byte[] Xmp = JpegSegment(0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>creator</x:xmpmeta>"));
    private static readonly byte[] XmpHdr = JpegSegment(0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0<x hdrgm:Version=\"1.0\"/>"));
    private static readonly byte[] Comment = JpegSegment(0xFE, Ascii("secret comment"));
    private static readonly byte[] Iptc = JpegSegment(0xED, Ascii("Photoshop 3.0\0"));
    private static readonly byte[] Dqt = JpegSegment(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]);
    private static readonly byte[] Scan = [0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 63, 0, 0x12, 0x34, 0xFF, 0xD9];
    private static readonly byte[] GainMap = [0xFF, 0xD8, 0x55, 0xFF, 0xD9];

    [Fact]
    public void JpegShouldLoseExifXmpIptcAndCommentsButKeepIccImageDataAndTrailer()
    {
        // arrange
        var input = Concat(Soi, JpegSegment(0xE1, ExifPayload(1)), Xmp, Iptc, Comment, Icc, Dqt, Scan, GainMap);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, Icc, Dqt, Scan, GainMap));
    }

    [Fact]
    public void JpegShouldKeepOnlyOrientationWhenItIsNotOne()
    {
        // arrange
        var input = Concat(Soi, JpegSegment(0xE1, ExifPayload(6)), Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        Contains(result, "GPSSECRET").Should().BeFalse();
        result[..6].Should().Equal(0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x22);
        result[31].Should().Be(6);
        result[38..].Should().Equal(Concat(Dqt, Scan));
    }

    [Fact]
    public void JpegShouldKeepUltraHdrXmp()
    {
        // arrange
        var input = Concat(Soi, XmpHdr, Comment, Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, XmpHdr, Dqt, Scan));
    }

    [Fact]
    public void JpegShouldNotMoveSegmentsAfterMpf()
    {
        // arrange
        var input = Concat(Soi, Comment, Mpf, Comment, Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, Mpf, Comment, Dqt, Scan));
    }

    [Fact]
    public void ShouldReturnInputInstanceWhenNothingChangesOrFileIsMalformed()
    {
        // arrange
        var clean = Concat(Soi, Dqt, Scan);
        var truncated = Concat(Soi, [0xFF, 0xE1, 0x10, 0x00, 0x01]);
        var notAnImage = Ascii("hello");

        // act & assert
        ImageMetadataStripper.Strip(clean).Should().BeSameAs(clean);
        ImageMetadataStripper.Strip(truncated).Should().BeSameAs(truncated);
        ImageMetadataStripper.Strip(notAnImage).Should().BeSameAs(notAnImage);
    }

    [Fact]
    public void PngShouldLoseTextTimeAndExifChunks()
    {
        // arrange
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var ihdr = PngChunk("IHDR", new byte[13]);
        var iccp = PngChunk("iCCP", Concat(Ascii("icc\0"), [0, 9]));
        var idat = PngChunk("IDAT", [7, 7]);
        var iend = PngChunk("IEND", []);
        var input = Concat(
            signature, ihdr, PngChunk("tEXt", Ascii("Author\0me")), PngChunk("eXIf", [1, 2]),
            PngChunk("iTXt", [0]), PngChunk("zTXt", [0]), PngChunk("tIME", new byte[7]), iccp, idat, iend);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(signature, ihdr, iccp, idat, iend));
    }

    [Fact]
    public void WebpShouldLoseExifAndXmpChunksAndFixHeader()
    {
        // arrange
        var vp8x = WebpChunk("VP8X", [0x0C | 0x10, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        var iccp = WebpChunk("ICCP", [9, 9]);
        var vp8 = WebpChunk("VP8 ", [5, 5, 5]);
        var body = Concat(Ascii("WEBP"), vp8x, iccp, vp8, WebpChunk("EXIF", Ascii("GPSSECRET")), WebpChunk("XMP ", [1, 2]));
        var input = Concat(Ascii("RIFF"), BitConverter.GetBytes((uint)body.Length), body);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        var expectedBody = Concat(Ascii("WEBP"), vp8x, iccp, vp8);
        expectedBody[12] = 0x10;
        result.Should().Equal(Concat(Ascii("RIFF"), BitConverter.GetBytes((uint)expectedBody.Length), expectedBody));
    }

    // Private methods

    private static byte[] ExifPayload(byte orientation)
        => Concat(
            Ascii("Exif\0\0"),
            [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x02, 0x00],
            [0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, orientation, 0x00, 0x00, 0x00],
            [0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00],
            [0x00, 0x00, 0x00, 0x00],
            Ascii("GPSSECRET"));

    private static byte[] JpegSegment(byte marker, params byte[][] payloadParts)
    {
        var payload = Concat(payloadParts);
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var length = BitConverter.GetBytes((uint)data.Length);
        Array.Reverse(length);
        return Concat(length, Ascii(type), data, [1, 2, 3, 4]);
    }

    private static byte[] WebpChunk(string fourCC, byte[] data)
        => Concat(Ascii(fourCC), BitConverter.GetBytes((uint)data.Length), data, data.Length % 2 == 1 ? new byte[] { 0 } : []);

    private static byte[] Ascii(string text)
        => Encoding.ASCII.GetBytes(text);

    private static byte[] Concat(params byte[][] parts)
        => parts.SelectMany(x => x).ToArray();

    private static bool Contains(byte[] data, string text)
        => data.AsSpan().IndexOf(Ascii(text)) >= 0;
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~ImageMetadataStripperTest"`
Expected: FAIL - `ImageMetadataStripper` does not exist.

- [ ] **Step 3: Write the stripper** (`src/dotnet/Core.Server/Uploads/ImageMetadataStripper.cs`)

```csharp
using System.Buffers.Binary;
using ActualChat.Blobs;

namespace ActualChat.Uploads;

/// <summary>
/// Removes EXIF, XMP, IPTC and text metadata from JPEG, PNG and WebP files without touching image data.
/// Same rules as <c>src/nodejs/src/image-processing/metadata-stripper.ts</c>.
/// </summary>
public static class ImageMetadataStripper
{
    private const ushort ExifOrientationTag = 0x0112;
    private const byte WebpMetadataFlags = 0x08 | 0x04;

    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;
    private static ReadOnlySpan<byte> XmpHeader => "http://ns.adobe.com/xap/1.0/\0"u8;
    private static ReadOnlySpan<byte> HdrGainMapNamespace => "hdrgm"u8;
    private static ReadOnlySpan<byte> MpfHeader => "MPF\0"u8;

    public static byte[] Strip(byte[] data)
        // Returns data itself when nothing changes, the format isn't JPEG/PNG/WebP, or the file is malformed
        => BlobContentTypeDetector.Detect(data) switch {
            "image/jpeg" => StripJpeg(data),
            "image/png" => StripPng(data),
            "image/webp" => StripWebp(data),
            _ => data,
        };

    // Private methods

    private static byte[] StripJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2);
        var isChanged = false;
        var isAfterMpf = false;
        var offset = 2;
        while (offset + 4 <= data.Length) {
            if (data[offset] != 0xFF)
                return data;

            var marker = data[offset + 1];
            if (marker is 0xDA or 0xD9) {
                output.Write(data, offset, data.Length - offset);
                return isChanged ? output.ToArray() : data;
            }
            if (marker is 0xFF or 0x01 or >= 0xD0 and <= 0xD7) {
                var markerLength = marker == 0xFF ? 1 : 2;
                output.Write(data, offset, markerLength);
                offset += markerLength;
                continue;
            }

            var end = offset + 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            if (end > data.Length)
                return data;

            var segment = data.AsSpan(offset, end - offset);
            var payload = segment[4..];
            // MPF stores offsets to the images after it, so no segment behind MPF may move
            var orientation = isAfterMpf ? (ushort)0 : GetReplacementOrientation(marker, payload);
            if (marker == 0xE2 && payload.StartsWith(MpfHeader))
                isAfterMpf = true;
            if (orientation == 0)
                output.Write(segment);
            else {
                isChanged = true;
                if (orientation > 1)
                    output.Write(CreateOrientationExifSegment(orientation));
            }
            offset = end;
        }
        return data;
    }

    // 0 = keep the segment, 1 = drop it, 2..8 = replace it with an Orientation-only EXIF segment
    private static ushort GetReplacementOrientation(byte marker, ReadOnlySpan<byte> payload)
    {
        if (marker is 0xED or 0xFE)
            return 1;
        if (marker != 0xE1)
            return 0;
        if (payload.StartsWith(ExifHeader))
            return ReadExifOrientation(payload[ExifHeader.Length..]);

        var isHdrXmp = payload.StartsWith(XmpHeader) && payload.IndexOf(HdrGainMapNamespace) >= 0;
        return isHdrXmp ? (ushort)0 : (ushort)1;
    }

    private static ushort ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;

        var isLittleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;
        if (!isLittleEndian && !(tiff[0] == 0x4D && tiff[1] == 0x4D))
            return 1;

        var ifdOffset = isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifdOffset + 2L > tiff.Length)
            return 1;

        var entryCount = ReadUInt16(tiff[(int)ifdOffset..], isLittleEndian);
        for (var i = 0; i < entryCount; i++) {
            var entry = (int)ifdOffset + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
                break;
            if (ReadUInt16(tiff[entry..], isLittleEndian) != ExifOrientationTag)
                continue;

            var value = ReadUInt16(tiff[(entry + 8)..], isLittleEndian);
            return value is >= 1 and <= 8 ? value : (ushort)1;
        }
        return 1;
    }

    private static byte[] CreateOrientationExifSegment(ushort orientation)
        // Big-endian TIFF, IFD0 with one entry: Orientation, SHORT, count 1
        => [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

    private static byte[] StripPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var isChanged = false;
        var offset = 8;
        while (offset + 12 <= data.Length) {
            var end = offset + 12L + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            if (end > data.Length)
                return data;

            var type = data.AsSpan(offset + 4, 4);
            if (IsDroppedPngChunk(type))
                isChanged = true;
            else
                output.Write(data, offset, (int)end - offset);
            offset = (int)end;
            if (type.SequenceEqual("IEND"u8))
                break;
        }
        return isChanged ? output.ToArray() : data;
    }

    private static byte[] StripWebp(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 12);
        var isChanged = false;
        var offset = 12;
        while (offset + 8 <= data.Length) {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            var end = offset + 8L + size + (size & 1);
            if (end > data.Length)
                return data;

            var fourCC = data.AsSpan(offset, 4);
            if (fourCC.SequenceEqual("EXIF"u8) || fourCC.SequenceEqual("XMP "u8))
                isChanged = true;
            else
                output.Write(data, offset, (int)end - offset);
            offset = (int)end;
        }
        if (!isChanged)
            return data;

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        if (result.Length > 20 && result.AsSpan(12, 4).SequenceEqual("VP8X"u8))
            result[20] &= unchecked((byte)~WebpMetadataFlags);
        return result;
    }

    private static bool IsDroppedPngChunk(ReadOnlySpan<byte> type)
        => type.SequenceEqual("eXIf"u8)
            || type.SequenceEqual("tEXt"u8)
            || type.SequenceEqual("iTXt"u8)
            || type.SequenceEqual("zTXt"u8)
            || type.SequenceEqual("tIME"u8);

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool isLittleEndian)
        => isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~ImageMetadataStripperTest"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Core.Server/Uploads/ImageMetadataStripper.cs tests/Core.Server.UnitTests/Uploads/ImageMetadataStripperTest.cs
git commit -m "feat(uploads): lossless server-side image metadata stripper"
```

---

### Task 9: Store chat images without re-encoding; honour `KeepMetadata`

**Files:**
- Create: `src/dotnet/Core.Server/Uploads/AttachmentImageUploadProcessor.cs`
- Modify: `src/dotnet/Api/Constants.cs:184-186`, `src/dotnet/Core.Server/Uploads/ImageLimits.cs:27-38`, `src/dotnet/Core.Server/Uploads/UploadedFile.cs:10-13`, `src/dotnet/Api/Media/Upload.cs:29-33`, `src/dotnet/Media.Service/UploadsBackend.cs:321-337`, `src/dotnet/Core.Server/Uploads/ImageUploadProcessor.cs:10-15`, `src/dotnet/Core.Server/Module/CoreServerModule.cs:47-48`
- Modify: `tests/Testing/TestImages/TestImages.cs`, `tests/Core.Server.UnitTests/Uploads/ImageUploadProcessorTest.cs:26-37`
- Test: `tests/Core.Server.UnitTests/Uploads/AttachmentImageUploadProcessorTest.cs`, `tests/Media.UnitTests/UploadTest.cs`

**Interfaces:**
- Consumes: `ImageMetadataStripper.Strip` (Task 8), `ImageLimits` (existing).
- Produces: `Constants.Attachments.MaxImageSize` (7680) and `Constants.Attachments.MaxImagePixelCount` (7680²); `UploadedFile.KeepMetadata { get; init; }`; `Upload.KeepMetadata` (metadata key `"KeepMetadata"`, used by the client in Task 12); `AttachmentImageUploadProcessor` handles `image/*` except GIF/SVG for `MediaKind.ChatEntryAttachment`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Testing/TestImages/TestImages.cs` (after `CreateJpeg`; add `using SixLabors.ImageSharp.Metadata.Profiles.Exif;`):

```csharp
    public static byte[] CreateJpegWithExif(int width, int height, ushort orientation)
    {
        using var image = new Image<Rgba32>(width, height);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        exif.SetValue(ExifTag.Software, "GPSSECRET");
        image.Metadata.ExifProfile = exif;
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }
```

`tests/Core.Server.UnitTests/Uploads/AttachmentImageUploadProcessorTest.cs`:

```csharp
using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class AttachmentImageUploadProcessorTest : IDisposable
{
    private readonly AttachmentImageUploadProcessor _processor
        = new(new ServiceCollection().AddLogging().BuildServiceProvider());
    private readonly List<ProcessedFile> _processedFiles = new();

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.DisposeSilently();
    }

    [Theory]
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/heic", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/gif", MediaKind.ChatEntryAttachment, false)]
    [InlineData("image/svg+xml", MediaKind.ChatEntryAttachment, false)]
    [InlineData("image/jpeg", MediaKind.LinkPreviewPicture, false)]
    [InlineData("image/png", MediaKind.UserPicture, false)]
    [InlineData("video/mp4", MediaKind.ChatEntryAttachment, false)]
    public void ShouldSupportOnlyChatAttachmentImages(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    [Fact]
    public async Task ShouldStripMetadataWithoutReencoding()
    {
        // arrange
        var data = TestImages.CreateJpegWithExif(400, 300, 1);
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", data);

        // act
        var result = await Process(upload);
        var stored = await ReadAll(result.File);

        // assert
        stored.Should().Equal(ImageMetadataStripper.Strip(data));
        stored.Length.Should().BeLessThan(data.Length, "the EXIF segment must be gone");
        result.File.ContentType.Should().Be("image/jpeg");
        result.Size.Should().Be(new Size2D(400, 300));
    }

    [Fact]
    public async Task ShouldKeepFileUntouchedWhenKeepMetadataIsSet()
    {
        // arrange
        var data = TestImages.CreateJpegWithExif(400, 300, 1);
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", data) with { KeepMetadata = true };

        // act
        var result = await Process(upload);

        // assert
        result.File.Should().BeSameAs(upload);
    }

    [Fact]
    public async Task ShouldReportDisplayDimensionsForRotatedPhotos()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpegWithExif(400, 300, 6));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(300, 400));
    }

    [Fact]
    public async Task ShouldNotResizeLargePhotos()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(4000, 3000));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(4000, 3000));
    }

    [Fact]
    public async Task ShouldRejectImageExceedingPixelBudget()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("huge.png", "image/png", TestImages.CreatePngHeader(65535, 65535));

        // act
        var process = () => _processor.Process(upload, null, CancellationToken.None);

        // assert
        await process.Should().ThrowAsync<InvalidOperationException>().WithMessage("*too big*");
    }

    [Fact]
    public async Task ShouldAcceptSquare8KImage()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("square.png", "image/png", TestImages.CreatePngHeader(7680, 7680));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(7680, 7680), "above ImageLimits.MaxPixelCount, but within the 8K bound");
    }

    [Fact]
    public async Task ShouldStoreUndecodableImageAsBinary()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.heic", "image/heic", "not an image"u8.ToArray());

        // act
        var result = await Process(upload);

        // assert
        result.File.ContentType.Should().Be("application/octet-stream");
    }

    // Private methods

    private async Task<ProcessedFile> Process(UploadedFile upload)
    {
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);
        return result;
    }

    private static async Task<byte[]> ReadAll(UploadedFile file)
    {
        var stream = await file.Open();
        await using var _ = stream;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
```

`tests/Media.UnitTests/UploadTest.cs` (add `using System.Text.Json;` only if it isn't a global using in the tests' `Directory.Build.props`):

```csharp
namespace ActualChat.Media.UnitTests;

public sealed class UploadTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void KeepMetadataShouldSurviveTheUploadMetadataFileRoundTrip()
    {
        // arrange
        var metadata = MetadataBag.Empty.Set(nameof(Upload.KeepMetadata), true);
        var upload = new Upload(UploadId.New(), default!, 10, "", metadata);

        // act
        var result = JsonSerializer.Deserialize<Upload>(JsonSerializer.Serialize(upload))!;

        // assert
        upload.KeepMetadata.Should().BeTrue();
        result.KeepMetadata.Should().BeTrue("UploadsBackend stores uploads as System.Text.Json metadata files");
        new Upload(UploadId.New(), default!, 10, "", MetadataBag.Empty).KeepMetadata.Should().BeFalse();
    }
}
```

In `ImageUploadProcessorTest.ShouldSupportExpectedContentTypes`, change the two `MediaKind.ChatEntryAttachment` rows that expect `true`:

```csharp
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, false)] // chat attachment → AttachmentImageUploadProcessor
    [InlineData("image/avif", MediaKind.Unknown, true)]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~AttachmentImageUploadProcessorTest|FullyQualifiedName~ImageUploadProcessorTest"` and `dotnet test tests/Media.UnitTests --filter "FullyQualifiedName~UploadTest"`
Expected: FAIL - `AttachmentImageUploadProcessor`, `UploadedFile.KeepMetadata` and `Upload.KeepMetadata` don't exist.

- [ ] **Step 3: Add the 8K bound and `KeepMetadata`**

`Constants.cs` - in `Attachments`, after `FileCountLimit`:

```csharp
        // 8K: the client re-encodes a longer side down to it, even for "Original"
        public const int MaxImageSize = 7680;
        public const long MaxImagePixelCount = (long)MaxImageSize * MaxImageSize;
```

`ImageLimits.cs` - let a caller raise the pixel limit (the rest of the method stays as is):

```csharp
    public static ImageInfo RequireWithinLimits(this ImageInfo imageInfo, long maxPixelCount = MaxPixelCount)
    {
        var pixelCount = (long)imageInfo.Width * imageInfo.Height;
        if (pixelCount > maxPixelCount)
            throw StandardError.Constraint($"Image is too big: {imageInfo.Width}x{imageInfo.Height}.");
```

`UploadedFile.cs` - in the record body, after `Length`:

```csharp
    public bool KeepMetadata { get; init; }
```

`Upload.cs` - after the `ContentType` property:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool KeepMetadata {
        get => this.GetMetadataValue(false);
        init => this.SetMetadataValue(value);
    }
```

`UploadsBackend.GetUploadedStreamFileFrom` - set it on both files:

```csharp
    private UploadedFile GetUploadedStreamFileFrom(Upload upload, CancellationToken cancellationToken)
    {
        if (IsGoogleStorage) {
            var blobPath = UploadsStorage.GetDataFileId(upload.Id);
            return new UploadedBlobFile(
                upload.FileName,
                upload.ContentType,
                upload.Length!.Value,
                blobPath,
                () => UploadsStorage.GetDataFile(upload.Id, cancellationToken)) {
                KeepMetadata = upload.KeepMetadata,
            };
        }
        return new UploadedStreamFile(
            upload.FileName,
            upload.ContentType,
            upload.Length!.Value,
            () => UploadsStorage.GetDataFile(upload.Id, cancellationToken)) {
            KeepMetadata = upload.KeepMetadata,
        };
    }
```

- [ ] **Step 4: Write the processor** (`src/dotnet/Core.Server/Uploads/AttachmentImageUploadProcessor.cs`)

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ActualChat.Uploads;

/// <summary>
/// Chat attachment images arrive already resized and re-encoded by the client, so they are stored as-is:
/// only dimensions are read and metadata is stripped losslessly, unless the upload keeps it.
/// </summary>
public sealed class AttachmentImageUploadProcessor(IServiceProvider services) : IUploadProcessor
{
    private const long MaxStrippableLength = 64 * 1024 * 1024;

    private ILogger Log => field ??= services.LogFor(GetType());

    public bool Supports(string contentType, MediaKind mediaKind)
        => mediaKind == MediaKind.ChatEntryAttachment
            && MediaTypeExt.IsImage(contentType)
            && !MediaTypeExt.IsGif(contentType)
            && !MediaTypeExt.IsSvg(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        var imageInfo = await Identify(upload, cancellationToken).ConfigureAwait(false);
        if (imageInfo is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        // Nothing is decoded here, so the bound is the client's 8K limit rather than the decode limit
        imageInfo.RequireWithinLimits(Constants.Attachments.MaxImagePixelCount);
        var size = GetDisplaySize(imageInfo);
        if (upload.KeepMetadata)
            return new ProcessedFile(upload, size);
        if (upload.Length > MaxStrippableLength) {
            Log.LogWarning("'{FileName}' is too large to strip metadata ({Length} bytes)", upload.FileName, upload.Length);
            return new ProcessedFile(upload, size);
        }

        var data = await ReadAll(upload, cancellationToken).ConfigureAwait(false);
        var stripped = ImageMetadataStripper.Strip(data);
        if (ReferenceEquals(stripped, data))
            return new ProcessedFile(upload, size);

        var tempFilePath = UploadedFileExt.NewTempFilePath();
        await File.WriteAllBytesAsync(tempFilePath, stripped, cancellationToken).ConfigureAwait(false);
        var strippedFile = new UploadedTempFile(upload.GetDisplayFileName(), upload.ContentType, tempFilePath);
        return new ProcessedFile(strippedFile, size);
    }

    // Private methods

    private async Task<ImageInfo?> Identify(UploadedFile upload, CancellationToken cancellationToken)
    {
        try {
            var stream = await upload.Open().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            return await Image.IdentifyAsync(ImageLimits.DecoderOptions, stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", upload.FileName);
            return null;
        }
    }

    private static Size2D GetDisplaySize(ImageInfo imageInfo)
    {
        // The file keeps its EXIF Orientation, and 5-8 rotate the image by 90 degrees
        var orientation = imageInfo.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true
            ? value.Value
            : (ushort)1;
        return orientation is >= 5 and <= 8
            ? new Size2D(imageInfo.Height, imageInfo.Width)
            : new Size2D(imageInfo.Width, imageInfo.Height);
    }

    private static async Task<byte[]> ReadAll(UploadedFile upload, CancellationToken cancellationToken)
    {
        var stream = await upload.Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
```

- [ ] **Step 5: Route chat attachments to it**

`ImageUploadProcessor.Supports`:

```csharp
    public bool Supports(string contentType, MediaKind mediaKind)
        // GIF is passed through to preserve animation. Icons go to IconUploadProcessor,
        // chat attachments to AttachmentImageUploadProcessor.
        => MediaTypeExt.IsImage(contentType)
            && !MediaTypeExt.IsGif(contentType)
            && !MediaTypeExt.IsSvg(contentType)
            && !mediaKind.IsChatIcon
            && mediaKind != MediaKind.ChatEntryAttachment;
```

`CoreServerModule.cs` - between the icon and image processor registrations:

```csharp
        services.AddSingleton<IUploadProcessor, AttachmentImageUploadProcessor>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~Uploads"` and `dotnet test tests/Media.UnitTests --filter "FullyQualifiedName~UploadTest"`
Expected: PASS. If `UploadTest` fails because the deserialized metadata value isn't a `bool`, add a `bool` conversion next to the existing `int` workaround in `src/dotnet/Api/Media/MetadataExt.cs` (`if (typeof(T) == typeof(bool) && value is not bool) value = Convert.ToBoolean(value);`) and re-run.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Core.Server src/dotnet/Api/Media src/dotnet/Media.Service/UploadsBackend.cs tests/Testing/TestImages/TestImages.cs tests/Core.Server.UnitTests/Uploads tests/Media.UnitTests/UploadTest.cs
git commit -m "feat(uploads): store chat images without re-encoding, honour KeepMetadata"
```

### Task 10: C# image processing service

The new `ImageQualityPreset` lives in `ActualChat.UI.Blazor.App.Services`. The PR's enum in `ActualChat.UI.Blazor.App.Components` stays until Task 12; code in the `Components` namespace keeps resolving to the old one (types in the enclosing namespace win over usings), so every task compiles.

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageQualityPreset.cs`, `ImageProcessRequest.cs`, `ProcessedImage.cs`, `IProcessedImageStore.cs`, `ImageAttachmentProcessor.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/FileProviders/WebFileProvider.cs`, `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs:318`
- Test: `tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs`

**Interfaces:**
- Consumes: `MauiFileProvider.GetContentUrl` (Task 1), `blazorApp.ImageProcessingInterop.processUrl` and JS `WebFileProvider.processImage` (Task 7).
- Produces:
  - `enum ImageQualityPreset { Uhd4K = 0, FullHd, Original, OriginalWithExif }`; extensions `GetMaxSize(): int?` (null for the Original presets), `ToRequest(): ImageProcessRequest`.
  - `record ImageProcessRequest(ImageOutputSpec[] Outputs)`, `record ImageOutputSpec(string Kind, int? MaxSize, string Codec, bool StripMetadata, int? MaxPassthroughSize = null)` with `Main(int)`, `Estimate(int)`, `Original(bool stripMetadata)`.
  - `record ImageSizeEstimate(long Uhd4KLength, long FullHdLength)`, `record ImageProcessingResult(IFileProvider? FileProvider, Size2D Size, ImageSizeEstimate? SizeEstimate)` (`FileProvider` is `null` when the output is the unchanged source).
  - `interface IProcessedImageStore { Task<MauiFileProvider> Save(Stream content, FileMetadata metadata, CancellationToken cancellationToken); }` (implemented in Task 11).
  - `ImageAttachmentProcessor.Process(IFileProvider source, Size2D sourceSize, ImageQualityPreset preset, CancellationToken cancellationToken): Task<ImageProcessingResult?>` - `null` means the provider type is unsupported or processing failed (logged); a result with a `null` `FileProvider` means the output is the unchanged source (upload the source as-is) while still carrying `SizeEstimate`.
  - `WebFileProvider.ProcessImage(ImageProcessRequest request, CancellationToken cancellationToken): ValueTask<ProcessedWebImage>`.

- [ ] **Step 1: Write the failing test** (`tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs`)

```csharp
using System.Text.Json;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImageQualityPresetTest
{
    [Fact]
    public void ReencodingPresetsShouldRequestMainAndEstimateOutputs()
    {
        // act
        var uhd4K = ImageQualityPreset.Uhd4K.ToRequest()!;
        var fullHd = ImageQualityPreset.FullHd.ToRequest()!;

        // assert
        uhd4K.Outputs.Should().Equal(ImageOutputSpec.Main(3840), ImageOutputSpec.Estimate(1920));
        fullHd.Outputs.Should().Equal(ImageOutputSpec.Main(1920), ImageOutputSpec.Estimate(3840));
    }

    [Fact]
    public void OriginalPresetsShouldPassFilesThroughUnlessAbove8K()
    {
        // act
        var original = ImageQualityPreset.Original.ToRequest().Outputs.Single();
        var originalWithExif = ImageQualityPreset.OriginalWithExif.ToRequest().Outputs.Single();

        // assert
        original.Should().Be(new ImageOutputSpec("main", 7680, "passthrough", true, 7680));
        originalWithExif.Should().Be(new ImageOutputSpec("main", 7680, "passthrough", false, 7680));
        ImageQualityPreset.Original.GetMaxSize().Should().BeNull();
        ImageQualityPreset.Uhd4K.GetMaxSize().Should().Be(3840);
    }

    [Fact]
    public void RequestShouldSerializeToTheShapeTheWorkerReads()
    {
        // act
        var json = JsonSerializer.Serialize(
            ImageQualityPreset.FullHd.ToRequest(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // assert
        json.Should().Be(
            """{"outputs":[{"kind":"main","maxSize":1920,"codec":"auto","stripMetadata":true,"maxPassthroughSize":null},{"kind":"estimate","maxSize":3840,"codec":"auto","stripMetadata":true,"maxPassthroughSize":null}]}""");
    }

    [Fact]
    public void DefaultPresetShouldBeUhd4K()
        => default(ImageQualityPreset).Should().Be(ImageQualityPreset.Uhd4K);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageQualityPresetTest"`
Expected: FAIL - types don't exist (the old `ActualChat.UI.Blazor.App.Components.ImageQualityPreset` has no `Uhd4K`).

- [ ] **Step 3: Write the contracts**

`ImageQualityPreset.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public enum ImageQualityPreset
{
    Uhd4K = 0,
    FullHd,
    Original,
    OriginalWithExif,
}

public static class ImageQualityPresetExt
{
    public static int? GetMaxSize(this ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Uhd4K => 3840,
            ImageQualityPreset.FullHd => 1920,
            _ => null,
        };

    public static ImageProcessRequest ToRequest(this ImageQualityPreset preset)
        // Re-encoding presets also encode the other size from the same decode, so the menu can show it
        => preset switch {
            ImageQualityPreset.Uhd4K => new([ImageOutputSpec.Main(3840), ImageOutputSpec.Estimate(1920)]),
            ImageQualityPreset.FullHd => new([ImageOutputSpec.Main(1920), ImageOutputSpec.Estimate(3840)]),
            ImageQualityPreset.Original => new([ImageOutputSpec.Original(stripMetadata: true)]),
            _ => new([ImageOutputSpec.Original(stripMetadata: false)]),
        };
}
```

`ImageProcessRequest.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public sealed record ImageProcessRequest(ImageOutputSpec[] Outputs);

public sealed record ImageOutputSpec(
    string Kind,
    int? MaxSize,
    string Codec,
    bool StripMetadata,
    int? MaxPassthroughSize = null)
{
    public static ImageOutputSpec Main(int maxSize)
        => new("main", maxSize, "auto", true);

    public static ImageOutputSpec Estimate(int maxSize)
        => new("estimate", maxSize, "auto", true);

    public static ImageOutputSpec Original(bool stripMetadata)
        // A longer side than 8K is re-encoded down to 8K even for "Original"
        => new("main", Constants.Attachments.MaxImageSize, "passthrough", stripMetadata, Constants.Attachments.MaxImageSize);
}

public sealed record ImageSizeEstimate(long Uhd4KLength, long FullHdLength);
```

`ProcessedImage.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public record ProcessedImage
{
    public string MimeType { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public long Size { get; init; }
    public long[] EstimateSizes { get; init; } = [];
    public bool IsSource { get; init; }
}

public sealed record ProcessedWebImage : ProcessedImage
{
    public IJSObjectReference? FileProvider { get; init; }
    public string PreviewUrl { get; init; } = "";
}

public sealed record ProcessedStreamImage : ProcessedImage
{
    public IJSStreamReference? Stream { get; init; }
}

public sealed record ImageProcessingResult(IFileProvider? FileProvider, Size2D Size, ImageSizeEstimate? SizeEstimate);
```

`IProcessedImageStore.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Stores a processed attachment image as a local file, so native uploads (and their resume
/// after an app restart) work the same as for picked files. Implemented by the MAUI host.
/// </summary>
public interface IProcessedImageStore
{
    Task<MauiFileProvider> Save(Stream content, FileMetadata metadata, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add `ProcessImage` to the web provider** (`WebFileProvider.cs`)

Add to `WebFileProvider`, after `WhenFileStreamReady()`:

```csharp
    public ValueTask<ProcessedWebImage> ProcessImage(ImageProcessRequest request, CancellationToken cancellationToken)
        => DemandWebFileProviderInternal().ProcessImage(request, cancellationToken);
```

Add to `IWebFileProviderInternal`:

```csharp
    ValueTask<ProcessedWebImage> ProcessImage(ImageProcessRequest request, CancellationToken cancellationToken);
```

Add to `WebFileProviderInternal`:

```csharp
    public ValueTask<ProcessedWebImage> ProcessImage(ImageProcessRequest request, CancellationToken cancellationToken)
        => _jsRef.InvokeAsync<ProcessedWebImage>("processImage", cancellationToken, request);
```

Add to `NoFileAccessWebFileProviderInternal`:

```csharp
    public ValueTask<ProcessedWebImage> ProcessImage(ImageProcessRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException();
```

- [ ] **Step 5: Write the service** (`ImageAttachmentProcessor.cs`)

```csharp
using ActualChat.UI.Blazor.App.Module;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Runs an attachment image through the JS image processor (resize, jpegli, metadata strip) and
/// returns a provider for the result; <c>null</c> means the source should be uploaded as-is.
/// </summary>
public sealed class ImageAttachmentProcessor(IServiceProvider services)
{
    private static readonly string JSProcessUrlMethod
        = $"{BlazorUIAppModule.ImportName}.ImageProcessingInterop.processUrl";

    private IServiceProvider Services { get; } = services;
    private IJSRuntime JS => field ??= Services.JSRuntime();
    private IProcessedImageStore ProcessedImageStore => field ??= Services.GetRequiredService<IProcessedImageStore>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<ImageProcessingResult?> Process(
        IFileProvider source,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var request = preset.ToRequest();
        try {
            return source switch {
                WebFileProvider webSource
                    => await ProcessWeb(webSource, request, sourceSize, preset, cancellationToken).ConfigureAwait(false),
                MauiFileProvider mauiSource
                    => await ProcessMaui(mauiSource, request, sourceSize, preset, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e,
                "Failed to process image '{FileName}' with {Preset}, uploading the source instead",
                source.Metadata.FileName, preset);
            return null;
        }
    }

    // Private methods

    private async Task<ImageProcessingResult> ProcessWeb(
        WebFileProvider source,
        ImageProcessRequest request,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var image = await source.ProcessImage(request, cancellationToken).ConfigureAwait(false);
        if (image.IsSource || image.FileProvider is null)
            return CreateResult(null, image, sourceSize, preset);

        var provider = new WebFileProvider {
            Metadata = CreateMetadata(source.Metadata, image),
            WebFileProviderInternal = new WebFileProviderInternal(
                image.FileProvider, image.PreviewUrl, false, Task.FromResult(true)),
        };
        provider.Initialize(Services);
        return CreateResult(provider, image, sourceSize, preset);
    }

    private async Task<ImageProcessingResult> ProcessMaui(
        MauiFileProvider source,
        ImageProcessRequest request,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var url = await source.GetContentUrl(preset.GetMaxSize(), cancellationToken).ConfigureAwait(false);
        var image = await JS
            .InvokeAsync<ProcessedStreamImage>(JSProcessUrlMethod, cancellationToken, url, request)
            .ConfigureAwait(false);
        if (image.IsSource || image.Stream is null)
            return CreateResult(null, image, sourceSize, preset);

        var stream = await image.Stream
            .OpenReadStreamAsync(Constants.Attachments.FileSizeLimit, cancellationToken)
            .ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var metadata = CreateMetadata(source.Metadata, image);
        var provider = await ProcessedImageStore.Save(stream, metadata, cancellationToken).ConfigureAwait(false);
        return CreateResult(provider, image, sourceSize, preset);
    }

    private static FileMetadata CreateMetadata(FileMetadata source, ProcessedImage image)
    {
        var extension = image.MimeType switch {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => null,
        };
        if (extension is null)
            return new FileMetadata { FileName = source.FileName, FileType = source.FileType, Length = image.Size };

        return new FileMetadata {
            FileName = ((FilePath)source.FileName).ChangeExtension(extension),
            FileType = image.MimeType,
            Length = image.Size,
        };
    }

    private static ImageProcessingResult CreateResult(
        IFileProvider? provider,
        ProcessedImage image,
        Size2D sourceSize,
        ImageQualityPreset preset)
    {
        var size = image.Width > 0 && image.Height > 0 ? new Size2D(image.Width, image.Height) : sourceSize;
        var hasEstimate = image.EstimateSizes.Length > 0;
        var sizeEstimate = preset switch {
            ImageQualityPreset.Uhd4K when hasEstimate => new ImageSizeEstimate(image.Size, image.EstimateSizes[0]),
            ImageQualityPreset.FullHd when hasEstimate => new ImageSizeEstimate(image.EstimateSizes[0], image.Size),
            _ => null,
        };
        return new ImageProcessingResult(provider, size, sizeEstimate);
    }
}
```

- [ ] **Step 6: Register the service** (`BlazorUIAppModule.cs`, after the `AttachmentsController` registration)

```csharp
        services.AddScoped(c => new ImageAttachmentProcessor(c));
```

- [ ] **Step 7: Run the test and build**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageQualityPresetTest"` then `dotnet build ActualChat.CI.slnf`
Expected: PASS (4 tests); build 0 errors. The style hook may report issues in edited files; fix them.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ImageProcessing src/dotnet/UI.Blazor.App/Services/FileProviders/WebFileProvider.cs src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs
git commit -m "feat(attachments): ImageAttachmentProcessor over the JS image pipeline"
```

---

### Task 11: MAUI processed-image storage and Android HEIF decoding

**Files:**
- Create: `src/dotnet/App.Maui/Services/MauiProcessedImageStore.cs`, `src/dotnet/App.Maui/Platforms/Android/AndroidHeifDecoder.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/AndroidFileProviderImpl.cs`, `src/dotnet/App.Maui/Platforms/Android/AndroidContentDownloader.cs:64-76`, `src/dotnet/App.Maui/Platforms/Windows/WindowsFileProviderImpl.cs:19-20`, `src/dotnet/App.Maui/Module/MauiAppModule.cs:190`

**Interfaces:**
- Consumes: `IProcessedImageStore` (Task 10), `IMauiFileProviderImpl.GetContentUrl` (Task 1).
- Produces: `MauiProcessedImageStore` (`RootDirectory`, `Contains(FilePath)`); on Android, `GetContentUrl(decodeMaxSize: int, ...)` of a HEIC/HEIF file returns the URL of a q100 JPEG decoded natively and downscaled to `decodeMaxSize`; `AndroidContentDownloader.DeleteCachedFile(string uri)` (renamed from `DeleteCachedShareFile`) also deletes files under `processed-images`.

- [ ] **Step 1: Write the store** (`MauiProcessedImageStore.cs`)

```csharp
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

public sealed class MauiProcessedImageStore(IServiceProvider services) : IProcessedImageStore
{
    public static readonly FilePath RootDirectory = new FilePath(FileSystem.CacheDirectory) | "processed-images";

    public async Task<MauiFileProvider> Save(Stream content, FileMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootDirectory);
        var filePath = RootDirectory | ((FilePath)metadata.FileName).ToUnique();
        try {
            var file = File.Create(filePath);
            await using (file.ConfigureAwait(false))
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch {
            filePath.DeleteSilently();
            throw;
        }

        var fileProvider = new MauiFileProvider { FileRef = ToFileRef(filePath), Metadata = metadata };
        fileProvider.Initialize(services);
        return fileProvider;
    }

    public static bool Contains(FilePath filePath)
        => filePath.IsSubPathOf(RootDirectory);

    // Private methods

    private static FilePath ToFileRef(FilePath filePath)
#if ANDROID
        // AndroidFileProviderImpl opens file refs through ContentResolver, which takes URIs
        => Android.Net.Uri.FromFile(new Java.IO.File(filePath.Value))!.ToString()!;
#else
        => filePath;
#endif
}
```

- [ ] **Step 2: Register it** (`MauiAppModule.cs`, after the `IMauiFileProviderImplFactory` registration)

```csharp
        services.AddScoped<IProcessedImageStore>(c => new MauiProcessedImageStore(c));
```

- [ ] **Step 3: Clean processed files up on Windows** (`WindowsFileProviderImpl.cs`; add `using ActualChat.App.Maui.Services;`)

```csharp
    public Task ClearBeforeRemoving()
    {
        // A picked file is the user's own; only files this app wrote may be deleted
        if (MauiProcessedImageStore.Contains(filePath))
            filePath.DeleteSilently();
        return Task.CompletedTask;
    }
```

Apple needs no change: `AppleFileProviderImpl.ClearBeforeRemoving` already deletes its file, and every Apple file ref lives in the app cache.

- [ ] **Step 4: Delete processed files on Android** (`AndroidContentDownloader.cs`; add `using ActualChat.App.Maui.Services;`)

Rename `DeleteCachedShareFile` to `DeleteCachedFile` and widen the check:

```csharp
    public static void DeleteCachedFile(string uri)
    {
        if (!uri.StartsWith(System.Uri.UriSchemeFile))
            return;

        try {
            var path = (FilePath)new System.Uri(uri).LocalPath;
            var isCachedFile = path.IsSubPathOf(IncomingShareCacheDir) || MauiProcessedImageStore.Contains(path);
            if (isCachedFile && File.Exists(path))
                File.Delete(path);
        }
        catch {
            // Best-effort cleanup; the OS evicts the cache directory under storage pressure anyway.
        }
    }
```

- [ ] **Step 5: Write the HEIF decoder** (`AndroidHeifDecoder.cs`)

```csharp
using ActualChat.App.Maui.Services;
using ActualLab.Generators;
using ActualLab.IO;
using Android.Graphics;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

/// <summary>
/// Android WebView can't decode HEIF, so HEIC/HEIF photos are decoded natively, downscaled to the
/// preset size and handed to the JS image processor as a q100 JPEG.
/// </summary>
public static class AndroidHeifDecoder
{
    private static readonly FilePath DecodedDirectory = MauiProcessedImageStore.RootDirectory | "decoded";

    public static bool IsHeif(string uri)
    {
        var mimeType = Platform.AppContext.ContentResolver!.GetType(Uri.Parse(uri)!);
        return mimeType is "image/heic" or "image/heif"
            || uri.EndsWith(".heic", StringComparison.OrdinalIgnoreCase)
            || uri.EndsWith(".heif", StringComparison.OrdinalIgnoreCase);
    }

    public static Task<string> DecodeToJpeg(string uri, int maxSize, CancellationToken cancellationToken)
        => Task.Run(() => {
            var source = ImageDecoder.CreateSource(Platform.AppContext.ContentResolver!, Uri.Parse(uri)!);
            using var bitmap = ImageDecoder.DecodeBitmap(source, new TargetSizeListener(maxSize));
            Directory.CreateDirectory(DecodedDirectory);
            var filePath = DecodedDirectory | $"{RandomStringGenerator.Default.Next(12)}.jpg";
            using (var output = File.Create(filePath))
                bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 100, output);
            return Uri.FromFile(new Java.IO.File(filePath.Value))!.ToString()!;
        }, cancellationToken);

    // Nested types

    private sealed class TargetSizeListener(int maxSize) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            // Bitmap.Compress needs pixels in software memory, not a hardware bitmap
            decoder.Allocator = ImageDecoderAllocator.Software;
            var size = info.Size!;
            var longSide = Math.Max(size.Width, size.Height);
            if (longSide <= maxSize)
                return;

            var scale = (double)maxSize / longSide;
            decoder.SetTargetSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));
        }
    }
}
```

If `ImageDecoderAllocator` doesn't exist in the binding, the property takes the Java int constant: `decoder.Allocator = ImageDecoder.AllocatorSoftware;`.

- [ ] **Step 6: Use it from the Android provider** (`AndroidFileProviderImpl.cs`)

Add a field after the constructor-assigned properties and replace `GetContentUrl` and `ClearBeforeRemoving`:

```csharp
    private string? _decodedUri;
```

```csharp
    public async Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
    {
        if (decodeMaxSize is not { } maxSize || !AndroidHeifDecoder.IsHeif(Uri))
            return AndroidContentDownloader.CreateWebRequestUri(Uri);

        DeleteDecodedFile();
        _decodedUri = await AndroidHeifDecoder.DecodeToJpeg(Uri, maxSize, cancellationToken).ConfigureAwait(false);
        return AndroidContentDownloader.CreateWebRequestUri(_decodedUri);
    }

    public Task ClearBeforeRemoving()
    {
        AndroidFilePermissionsKeeper.ReleaseReadPermission(Uri, this);
        AndroidContentDownloader.DeleteCachedFile(Uri);
        DeleteDecodedFile();
        return Task.CompletedTask;
    }
```

Add at the end of the class:

```csharp
    // Private methods

    private void DeleteDecodedFile()
    {
        if (_decodedUri is null)
            return;

        AndroidContentDownloader.DeleteCachedFile(_decodedUri);
        _decodedUri = null;
    }
```

- [ ] **Step 7: Build the MAUI targets**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-android` and, on Windows, `-f net11.0-windows10.0.19041.0`; iOS via `ssh macmini` and the `ios-run` skill's build step.
Expected: 0 errors on each target.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/App.Maui
git commit -m "feat(maui): processed-image cache storage and native HEIF decoding on Android"
```

### Task 12: Attachment flow, quality menu and localization

Replaces PR #4472's "defer upload until Send" flow: images are processed right after they're added and upload as soon as processing ends; a preset change cancels, releases the upload and processes again from the source.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/Attachment/Attachment.cs`, `AttachmentCleanup.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/AttachmentList.cs`, `FileAttachments.cs`, `AttachmentListView.razor`, `AttachmentItem.razor:13`, `ImageQualitySelector.razor`, `ImageQualityMenu.razor`, `ChatMessageEditor.razor`
- Modify: `src/dotnet/UI.Blazor.App/Events/ImageQualityPresetSelectedEvent.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/FileUploads/UploadSessions.cs:79-92,133-154,199-207`, `src/dotnet/UI.Blazor.App/Services/FileUploads/UploadOperations.cs:135-162`
- Modify: `src/dotnet/UI.Blazor.App/Services/FileProviders/WebFileProvider.cs`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs:357-359`, `src/dotnet/Localization/Resources/Strings.{bg,bs,cs,de,en,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`

**Interfaces:**
- Consumes: `ImageAttachmentProcessor.Process`, `ImageQualityPreset`, `ImageSizeEstimate` (Task 10), `Upload.KeepMetadata` (Task 9).
- Produces: `FileAttachments.SetImageQuality(AttachmentList, ImageQualityPreset): Task`, `FileAttachments.WhenImagesProcessed(AttachmentList): Task`; `AttachmentList.ImageQuality`, `AttachmentList.HasProcessableImages`; `Attachment.Source`, `IsProcessing`, `SelectedQuality`, `SizeEstimate`, `IsProcessableImage`; `AttachmentCleanupKind.SourceFile`; `UploadSessions.ReleaseReference(string sessionId, bool cancel = true, bool mustKeepFile = false)`.

- [ ] **Step 1: Attachment model** (`Attachment.cs` - full file)

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string FileName, string FileType, long Length, Size2D Size)
{
    public AttachmentId Id { get; init; } = AttachmentId.New();
    public int Width => Size.Width;
    public int Height => Size.Height;
    public long DurationMs { get; init; }

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";
    public AttachmentCleanupCollection Cleanups { get; } = new ();
    // Set for processable images: what the attachment is re-processed from when the preset changes
    public AttachmentSource? Source { get; init; }
    public bool IsProcessing { get; init; }
    public ImageQualityPreset SelectedQuality { get; init; }
    public ImageSizeEstimate? SizeEstimate { get; init; }

    public bool IsSupportedImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsSupportedVideo => MediaTypeExt.IsSupportedVideo(FileType);
    public bool IsProcessableImage => IsSupportedImage && !MediaTypeExt.IsGif(FileType) && !MediaTypeExt.IsSvg(FileType);

    public string DemandUploadSessionId()
        => !UploadSessionId.IsNullOrEmpty() ? UploadSessionId : throw new InvalidOperationException("Upload session not assigned");

    public MetadataBag GetMetadataForUploadSession()
    {
        var metadata = new MetadataBag()
            .Set(nameof(Media.Media.FileName), FileName)
            .Set(nameof(Media.Media.ContentType), FileType)
            .Set(nameof(Media.Media.Length), Length);
        if (IsSupportedImage || IsSupportedVideo)
            metadata = metadata
                .Set(nameof(Media.Media.Width), Size.Width)
                .Set(nameof(Media.Media.Height), Size.Height);
        if (Source is not null && SelectedQuality == ImageQualityPreset.OriginalWithExif)
            metadata = metadata.Set(nameof(Media.Upload.KeepMetadata), true);
        return metadata;
    }
}

public sealed record SourceAttachment(string FileName, string FileType, long Length, FilePreview? Preview)
    : Attachment(FileName, FileType, Length, Preview?.Dimensions ?? default);

public sealed record AttachmentSource(IFileProvider FileProvider, string FileName, string FileType, long Length, Size2D Size);
```

- [ ] **Step 2: Source-file cleanup kind** (`AttachmentCleanup.cs`)

```csharp
public enum AttachmentCleanupKind { File, UploadSession, PersistedPostMessageRequest, SourceFile }
```

Add to `AttachmentCleanupFactory`, after `ForFile`:

```csharp
    // Unlike ForFile, InitUploadSession doesn't replace it: the source outlives the processed file's session
    public static AttachmentCleanup ForSourceFile(IFileProvider fileProvider)
        => new (AttachmentCleanupKind.SourceFile, fileProvider.ClearForRemoving);
```

- [ ] **Step 3: Attachment list** (`AttachmentList.cs`)

Add `using ActualChat.UI.Blazor.App.Services;` at the top. Replace the PR's `GlobalQuality`, `HasCompressibleAttachments`, `CompressibleCount`, `NonCompressibleTotalLength` and `SetGlobalQuality` with:

```csharp
    public ImageQualityPreset ImageQuality { get; private set; }
    public bool HasProcessableImages => _attachments.Any(a => a.Source is not null);
```

```csharp
    public void SetImageQuality(ImageQualityPreset preset)
    {
        ImageQuality = preset;
        RaiseChanged();
    }
```

Keep `Replace`. Add a blank line after `throw StandardError.Internal("Attachment not found.");` in `Replace` and after `return;` in `Clear` (control-flow rule).

- [ ] **Step 4: Release an upload without deleting its file** (`UploadSessions.cs`)

```csharp
    public void ReleaseReference(string sessionId, bool cancel = true, bool mustKeepFile = false)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionRef))
            throw new InvalidOperationException($"Session {sessionId} not found");

        var newCount = Interlocked.Decrement(ref sessionRef.ReferenceCount);
        if (newCount != 0)
            return;

        if (!cancel)
            return;

        ReleaseSessionInternal(sessionRef.Session, mustKeepFile);
    }
```

```csharp
    private void ReleaseSessionInternal(UploadSession session, bool mustKeepFile = false)
    {
        Log.LogDebug("Releasing reference for session '{SessionId}' ('{FileName}')", session.SessionId, session.FileProvider.Metadata.FileName);
        var completed = session.Cancel();
        _ = BackgroundTask.Run( async () => {
            await completed.WaitAsync(TimeSpan.FromSeconds(30)).SilentAwait(false);
            await DeleteSessionInternal(session, mustKeepFile).ConfigureAwait(false);
        });
    }

    private async Task DeleteSessionInternal(UploadSession session, bool mustKeepFile)
    {
        var sessionId = session.SessionId;
        _sessions.TryRemove(sessionId, out _);
        var fileProvider = session.FileProvider;
        await DeleteSessionResources(
            sessionId,
            fileProvider,
            session.TranscodedFilePath,
            session.UploadId,
            mustKeepFile).ConfigureAwait(false);
        Log.LogDebug("Deleted session '{SessionId}' ('{FileName}')", sessionId, fileProvider.Metadata.FileName);
    }
```

```csharp
    private async Task DeleteSessionResources(
        string sessionId,
        IFileProvider fileProvider,
        string? transcodedFilePath,
        UploadId? uploadId,
        bool mustKeepFile = false)
    {
        if (uploadId is not null)
            await _uploadOperations.RemoveUpload(uploadId, CancellationToken.None).ConfigureAwait(false);
        if (!mustKeepFile)
            await fileProvider.ClearForRemoving().ConfigureAwait(false);
        DeleteFile(transcodedFilePath);
        await _repo.Delete(sessionId).ConfigureAwait(false);
        UploadSessionsState.Remove(sessionId);
    }
```

- [ ] **Step 4b: Forward `KeepMetadata` to the server upload** (`UploadOperations.cs`)

The server reads `Upload.KeepMetadata` from `Uploads_Create.Metadata`, which `RegisterUploadId` builds from the upload source only; the session's `MetadataBag` (where `Attachment.GetMetadataForUploadSession` puts the flag) never reaches it. In `GetOrRegisterUpload`, pass the snapshot's metadata:

```csharp
        uploadId = await RegisterUploadId(source.Metadata, snapshot.Metadata, cancellationToken).ConfigureAwait(false);
```

and replace `RegisterUploadId` with:

```csharp
    private async Task<UploadId> RegisterUploadId(
        UploadSourceMetadata sourceMetadata,
        MetadataBag sessionMetadata,
        CancellationToken cancellationToken)
    {
        var length = sourceMetadata.Length;
        var metadata = new MetadataBag()
            .Set(nameof(ActualChat.Media.Media.FileName), sourceMetadata.FileName.Value)
            .Set(nameof(ActualChat.Media.Media.ContentType), sourceMetadata.ContentType);
        // The server decides whether to strip metadata from the upload itself, not from the reserved media
        if (sessionMetadata[nameof(ActualChat.Media.Upload.KeepMetadata)] is true)
            metadata = metadata.Set(nameof(ActualChat.Media.Upload.KeepMetadata), true);
        return await Commander.Call(new Uploads_Create {
            Session = Session,
            Length = length,
            Tag = "",
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 5: Rewrite the attachment flow** (`FileAttachments.cs` - full file)

```csharp
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FileAttachments : UIServiceBase<AppUIHub>
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.WebFileProviders.createFromFileId";

    private readonly ConcurrentDictionary<AttachmentId, ImageProcessing> _imageProcessings = new();

    private AttachmentsController AttachmentsController { get; }
    private AttachmentsState AttachmentsState { get; }
    private FilePreviews FilePreviews { get; }
    private ImageAttachmentProcessor ImageAttachmentProcessor { get; }
    private UploadSessions UploadSessions => Hub.UploadSessions;
    public ChatId ChatId { get; }

    public FileAttachments(AppUIHub hub, ChatId chatId) : base(hub)
    {
        AttachmentsController = Hub.Services.GetRequiredService<AttachmentsController>();
        AttachmentsState = Hub.AttachmentsState;
        FilePreviews = Hub.Services.GetRequiredService<FilePreviews>();
        ImageAttachmentProcessor = Hub.Services.GetRequiredService<ImageAttachmentProcessor>();
        ChatId = chatId;
    }

    public async Task<bool> TryAddWebFileAttachments(AttachmentList list, WebFileInfo[] fileInfos)
    {
        var hasAdded = false;
        foreach (var fileInfo in fileInfos) {
            var prevHasAdded = hasAdded;
            hasAdded = await TryAddWebFileAttachment(
                list,
                fileInfo.Id,
                fileInfo.FileName,
                fileInfo.FileType,
                fileInfo.Size);
            if (!prevHasAdded && hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
        }
        return hasAdded;
    }

    public async Task<bool> TryAddFileAttachments(AttachmentList list, AttachFileInfo[] fileInfos)
    {
        // The files are created concurrently and added in pick order: a native gallery pick loads
        // every file in the background, and a preview may take seconds per file (macOS generates
        // it from the loaded file), so a serial loop would show each item only after the previous one.
        var createTasks = new List<Task<Attachment?>>();
        foreach (var fileInfo in fileInfos) {
            if (CheckCanAdd(list, fileInfo.FileProvider.Metadata.Length, createTasks.Count) is { } e) {
                UICommander.ShowError(e);
                continue;
            }

            var fileProvider = fileInfo.FileProvider;
            fileProvider.Initialize(Hub.Services);
            createTasks.Add(TryCreateAttachment(fileProvider));
        }

        var hasAdded = false;
        foreach (var createTask in createTasks) {
            if (await createTask is not { } attachment)
                continue;

            await AddAttachment(list, attachment);
            if (!hasAdded)
                _ = TuneUI.Play(Tune.ChangeAttachments);
            hasAdded = true;
        }
        return hasAdded;
    }

    public async Task SetImageQuality(AttachmentList list, ImageQualityPreset preset)
    {
        if (list.ImageQuality == preset)
            return;

        list.SetImageQuality(preset);
        var reprocessTasks = list.Items
            .Where(a => a.Source is not null)
            .Select(a => Reprocess(list, a.Id, preset))
            .ToList();
        await Task.WhenAll(reprocessTasks);
    }

    public Task WhenImagesProcessed(AttachmentList list)
        => Task.WhenAll(list.Items.Select(a => _imageProcessings.TryGetValue(a.Id, out var p) ? p.Task : Task.CompletedTask));

    // Private methods

    private async Task<bool> TryAddWebFileAttachment(AttachmentList list, int id, string fileName, string fileType, long size)
    {
        // A browser knows a File's size upfront, and 0 there is what a paste of an image whose
        // clipboard data is already gone yields; the server can't create an upload for it either.
        if (size <= 0) {
            UICommander.ShowError(StandardError.Upload.FileEmpty());
            return false;
        }
        if (CheckCanAdd(list, size) is { } e) {
            UICommander.ShowError(e);
            return false;
        }

        // Browser's File System Access API may return empty MIME type for some files (e.g., MOV).
        // Fall back to detecting from file extension.
        if (fileType.IsNullOrEmpty())
            fileType = MediaMimeTypes.GetMimeType(fileName);
        var webFileProvider = await CreateWebFileProvider(id, fileName, fileType, size);
        if (webFileProvider is null)
            return false;

        webFileProvider.Initialize(Hub.Services);
        if (await TryCreateAttachment(webFileProvider) is not { } attachment)
            return false;

        await AddAttachment(list, attachment);
        return true;
    }

    private static Exception? CheckCanAdd(AttachmentList list, long length, int pendingCount = 0)
    {
        if (length > Constants.Attachments.FileSizeLimit)
            return StandardError.Upload.FileTooBig(Constants.Attachments.FileSizeLimit);

        if (list.Count + pendingCount >= Constants.Attachments.FileCountLimit)
            return StandardError.Upload.TooManyFiles(Constants.Attachments.FileCountLimit);

        return null;
    }

    private async Task<WebFileProvider?> CreateWebFileProvider(int id, string fileName, string fileType, long length)
    {
        WebFileProviderInternal? webFileProviderInternal;
        try {
            var webFileAttachment = await JS
                .InvokeAsync<CreateWebFileProviderResult>(JSCreateMethod, id)
                .ConfigureAwait(true); // Continue on Blazor context.
            webFileProviderInternal = new WebFileProviderInternal(
                webFileAttachment.FileProvider,
                webFileAttachment.PreviewUrl,
                true,
                Task.FromResult(true));
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to create file provider");
            return null;
        }
        var webFileProvider = new WebFileProvider {
            Metadata = new () {
                FileName = fileName,
                FileType = fileType,
                Length = length,
            },
            WebFileProviderInternal = webFileProviderInternal,
        };
        return webFileProvider;
    }

    private async Task<Attachment?> TryCreateAttachment(IFileProvider fileProvider)
    {
        try {
            return await CreateAttachment(fileProvider);
        }
        catch (Exception ex) {
            await AttachmentCleanupFactory.ForFile(fileProvider)
                .Cleanup.Invoke()
                .WithErrorLog(Log, "Failed to cleanup file provider")
                .SilentAwait();
            Log.LogError(ex, "Failed to add file attachment");
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
            return null;
        }
    }

    private async Task<Attachment> CreateAttachment(IFileProvider fileProvider)
    {
        var fileMetadata = fileProvider.Metadata;
        var preview = await FilePreviews.Get(fileProvider, fileMetadata.FileType, Hub.StopToken);
        var attachment = new SourceAttachment(
            fileMetadata.FileName,
            fileMetadata.FileType,
            fileMetadata.Length,
            preview) {
            FileProvider = fileProvider,
            DurationMs = preview?.DurationMs ?? 0,
        };
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForFile(fileProvider));
        return attachment;
    }

    private async Task AddAttachment(AttachmentList list, Attachment attachment)
    {
        if (!attachment.IsProcessableImage) {
            list.Add(await StartUpload(attachment, list.MediaScope));
            return;
        }

        var fileProvider = attachment.FileProvider!;
        attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.File);
        attachment.Cleanups.Add(AttachmentCleanupFactory.ForSourceFile(fileProvider));
        attachment = attachment with {
            Source = new AttachmentSource(
                fileProvider, attachment.FileName, attachment.FileType, attachment.Length, attachment.Size),
            IsProcessing = true,
        };
        SetSourcePreview(attachment);
        list.Add(attachment);
        _ = StartImageProcessing(list, attachment.Id, list.ImageQuality);
    }

    private async Task Reprocess(AttachmentList list, AttachmentId id, ImageQualityPreset preset)
    {
        if (_imageProcessings.TryRemove(id, out var previous)) {
            previous.CancellationTokenSource.CancelAndDisposeSilently();
            await previous.Task.SilentAwait();
        }
        if (list.Items.FirstOrDefault(a => a.Id == id) is not { Source: { } source } attachment)
            return;

        if (!attachment.UploadSessionId.IsNullOrEmpty()) {
            AttachmentsState.Unregister(id);
            attachment.Cleanups.RemoveByKind(AttachmentCleanupKind.UploadSession);
            // The source is processed again below, so its file must survive the released session
            var isSourceUpload = ReferenceEquals(attachment.FileProvider, source.FileProvider);
            UploadSessions.ReleaseReference(attachment.UploadSessionId, mustKeepFile: isSourceUpload);
        }
        var reset = attachment with { UploadSessionId = "", IsProcessing = true };
        list.Replace(attachment, reset);
        SetSourcePreview(reset);
        await StartImageProcessing(list, id, preset);
    }

    private Task StartImageProcessing(AttachmentList list, AttachmentId id, ImageQualityPreset preset)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var task = ProcessImageAndUpload(list, id, preset, cancellationTokenSource.Token);
        var processing = new ImageProcessing(cancellationTokenSource, task);
        _imageProcessings[id] = processing;
        _ = task.ContinueWith(
            _ => {
                // Reprocess removes (and disposes) a superseded entry itself
                if (_imageProcessings.TryRemove(new KeyValuePair<AttachmentId, ImageProcessing>(id, processing)))
                    cancellationTokenSource.Dispose();
            },
            TaskScheduler.Default);
        return task;
    }

    private async Task ProcessImageAndUpload(
        AttachmentList list,
        AttachmentId id,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        try {
            if (list.Items.FirstOrDefault(a => a.Id == id) is not { Source: { } source })
                return;

            var result = await ImageAttachmentProcessor.Process(source.FileProvider, source.Size, preset, cancellationToken);
            if (cancellationToken.IsCancellationRequested || list.Items.FirstOrDefault(a => a.Id == id) is not { } attachment) {
                // Only a processed file is ours to delete; a null provider means the source itself
                if (result?.FileProvider is { } processedFileProvider)
                    await processedFileProvider.ClearForRemoving();
                return;
            }

            var processed = result?.FileProvider is null
                ? attachment with {
                    FileProvider = source.FileProvider,
                    FileName = source.FileName,
                    FileType = source.FileType,
                    Length = source.Length,
                    Size = source.Size,
                    SizeEstimate = result?.SizeEstimate ?? attachment.SizeEstimate,
                }
                : attachment with {
                    FileProvider = result.FileProvider,
                    FileName = result.FileProvider.Metadata.FileName,
                    FileType = result.FileProvider.Metadata.FileType,
                    Length = result.FileProvider.Metadata.Length,
                    Size = result.Size,
                    SizeEstimate = result.SizeEstimate ?? attachment.SizeEstimate,
                };
            processed = processed with { IsProcessing = false, SelectedQuality = preset };
            processed = await StartUpload(processed, list.MediaScope);
            if (list.Items.FirstOrDefault(a => a.Id == id) != attachment) {
                // Removed while its upload session was being created: release what StartUpload registered
                AttachmentsState.Unregister(id);
                var isSourceUpload = ReferenceEquals(processed.FileProvider, source.FileProvider);
                UploadSessions.ReleaseReference(processed.UploadSessionId, mustKeepFile: isSourceUpload);
                return;
            }

            list.Replace(attachment, processed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // A newer preset took over
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to process or upload attachment '{AttachmentId}'", id);
            UICommander.ShowError(StandardError.Constraint("Failed to add file attachment."));
        }
    }

    private async Task<Attachment> StartUpload(Attachment attachment, string mediaScope)
    {
        attachment = await AttachmentsController.InitUploadSession(attachment, mediaScope);
        AttachmentsState.Register(attachment);
        AttachmentsController.ResumeUpload(attachment);
        return attachment;
    }

    private void SetSourcePreview(Attachment attachment)
    {
        if (attachment is SourceAttachment sourceAttachment)
            AttachmentsState.SetPreview(attachment.Id, AttachmentPreview.From(sourceAttachment.Preview));
    }

    // Nested types

    private struct CreateWebFileProviderResult
    {
        public string PreviewUrl { get; init; }
        public IJSObjectReference FileProvider { get; init; }
    }

    private sealed record ImageProcessing(CancellationTokenSource CancellationTokenSource, Task Task);
}
```

(The dead `IsImagePreviewUrl` helper is dropped with this rewrite.)

- [ ] **Step 6: Drop the PR's resize members from the C# web provider** (`WebFileProvider.cs`)

Delete `ResizeImage`, `EstimateResizedSizes`, the `ReplaceBlob` / `EstimateResizedSizes` members of `IWebFileProviderInternal`, `WebFileProviderInternal` and `NoFileAccessWebFileProviderInternal`, and the `ImageResizeResult` / `ImageResizePreset` record structs. Change `Metadata` back to `{ get; init; }`.

- [ ] **Step 7: Quality selector and menu**

`ImageQualitySelector.razor` - full file:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.UI.Blazor.App.Events
@using ActualChat.UI.Blazor.App.Services
@using ActualLab.Generators
@inherits ComponentBase<AppUIHub>
@implements IDisposable

<OnUIEvent TEvent="ImageQualityPresetSelectedEvent" Handler="@OnPresetSelectedEvent"/>

@if (Attachments.HasProcessableImages) {
    <div class="mqs-wrapper">
        <button class="mqs-chip"
                data-menu="@(MenuRef.New<ImageQualityMenu>(_selectorId).ToString())"
                data-menu-trigger="@MenuTrigger.Primary">
            <span class="mqs-chip-label">@GetLabel(Attachments.ImageQuality)</span>
            <i class="icon-chevron-up text-lg"></i>
        </button>
    </div>
}

@code {
    private static readonly ImageQualityPreset[] Presets = [
        ImageQualityPreset.Uhd4K,
        ImageQualityPreset.FullHd,
        ImageQualityPreset.Original,
        ImageQualityPreset.OriginalWithExif,
    ];

    private readonly string _selectorId = RandomStringGenerator.Default.Next();

    [Parameter, EditorRequired] public AttachmentList Attachments { get; set; } = null!;
    [Parameter] public EventCallback<ImageQualityPreset> OnPresetChanged { get; set; }

    public void Dispose() {
        Attachments.Changed -= OnAttachmentsChanged;
        ImageQualityMenu.MenuModels.Remove(_selectorId);
    }

    protected override void OnParametersSet() {
        Attachments.Changed -= OnAttachmentsChanged;
        Attachments.Changed += OnAttachmentsChanged;
        RegisterMenuModel();
    }

    private void OnAttachmentsChanged(object? sender, EventArgs e) {
        RegisterMenuModel();
        StateHasChanged();
    }

    private void RegisterMenuModel() {
        var items = Attachments.Items.ToList();
        var images = items.Where(a => a.Source is not null).ToList();
        var otherFilesLength = items.Where(a => a.Source is null).Sum(a => a.Length);
        var options = Presets
            .Select(p => new ImageQualityMenu.OptionModel(
                p, GetLabel(p), GetSpec(p), FormatLength(GetTotalLength(images, p, otherFilesLength))))
            .ToArray();
        var scopeText = otherFilesLength > 0 ? L.Editor_QualityAppliesToPhotos(images.Count, images.Count) : null;
        ImageQualityMenu.MenuModels.Register(
            _selectorId, new ImageQualityMenu.Model(Attachments.ImageQuality, options, scopeText));
    }

    private async Task OnPresetSelectedEvent(ImageQualityPresetSelectedEvent @event, CancellationToken cancellationToken) {
        if (@event.SelectorId != _selectorId)
            return;

        await OnPresetChanged.InvokeAsync(@event.Preset);
    }

    private string GetLabel(ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Uhd4K => "4K",
            ImageQualityPreset.FullHd => "1080p",
            ImageQualityPreset.Original => L.Editor_QualityOriginal,
            _ => L.Editor_QualityOriginalWithExif,
        };

    private string GetSpec(ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Uhd4K => "3840 px",
            ImageQualityPreset.FullHd => "1920 px",
            ImageQualityPreset.Original => L.Editor_QualityFullSize,
            _ => L.Editor_QualityKeepsMetadata,
        };

    private static long GetTotalLength(List<Attachment> images, ImageQualityPreset preset, long otherFilesLength) {
        // 0 = unknown: a re-encoded size is known only once some preset produced its estimate
        var total = otherFilesLength;
        foreach (var image in images) {
            long? length = preset switch {
                ImageQualityPreset.Uhd4K => image.SizeEstimate?.Uhd4KLength,
                ImageQualityPreset.FullHd => image.SizeEstimate?.FullHdLength,
                _ => image.Source!.Length,
            };
            if (length is null)
                return 0;

            total += length.Value;
        }
        return total;
    }

    private static string FormatLength(long length)
        => length > 0 ? FileSizeFormatter.Format(length) : "—";
}
```

`ImageQualityMenu.razor` - add `@using ActualChat.UI.Blazor.App.Services` after the existing `@using` line; nothing else changes.

`ImageQualityPresetSelectedEvent.cs` - replace `using ActualChat.UI.Blazor.App.Components;` with `using ActualChat.UI.Blazor.App.Services;`.

- [ ] **Step 8: Wire the list view and editor**

`AttachmentListView.razor`:
- Add `@using ActualChat.UI.Blazor.App.Services` after `@namespace`.
- Replace the `@if (Attachments.HasCompressibleAttachments) { <ImageQualitySelector ... /> }` block with:

```razor
                <ImageQualitySelector
                    Attachments="@Attachments"
                    OnPresetChanged="@OnImageQualityChanged"/>
```

- In `@code`, add `[Parameter] public EventCallback<ImageQualityPreset> OnImageQualityChanged { get; set; }` after `OnAddClick`; delete `OnPresetChanged` and `FormatSize`; change `CountLabel` to return `$"{L.Editor_Files(count, count)} · {FileSizeFormatter.Format(total)}"`.

`AttachmentItem.razor:13`:

```razor
    var plugClass = progress.IsReady || attachment.IsProcessing ? "" : "blurred-plug";
```

`ChatMessageEditor.razor`:
- Add `OnImageQualityChanged="@OnImageQualityChanged"` to the `<AttachmentListView ... />` element (next to `OnAddClick`).
- In `Post`, replace the two lines that call `ApplyQualityAndStartUploads` with:

```csharp
        // Uploads of photos start only once they're resized and re-encoded
        await FileAttachments.WhenImagesProcessed(_attachmentListHolder.Attachments);
```

- After `OnAttachFilesPicked` add:

```csharp
    private Task OnImageQualityChanged(ImageQualityPreset preset)
        => FileAttachments.SetImageQuality(_attachmentListHolder.Attachments, preset);
```

- [ ] **Step 9: Localization**

`LocalizedStringsLocalizerExt.cs`, after `Editor_QualityFullSize`:

```csharp
        public string Editor_QualityOriginalWithExif => l["Editor_QualityOriginalWithExif"].Value;
        public string Editor_QualityKeepsMetadata => l["Editor_QualityKeepsMetadata"].Value;
```

In each hand-written catalog, insert two lines after `"Editor_QualityAppliesToPhotos": ...,`:

| file | `Editor_QualityOriginalWithExif` | `Editor_QualityKeepsMetadata` |
|---|---|---|
| `Strings.en.json` | Original with EXIF | Keeps location and camera data |
| `Strings.bg.json` | Оригинал с EXIF | Запазва местоположението и данните от камерата |
| `Strings.bs.json` | Original s EXIF-om | Zadržava lokaciju i podatke kamere |
| `Strings.cs.json` | Originál s EXIF | Zachová polohu a údaje fotoaparátu |
| `Strings.de.json` | Original mit EXIF | Behält Standort und Kameradaten |
| `Strings.es.json` | Original con EXIF | Conserva la ubicación y los datos de la cámara |
| `Strings.fr.json` | Original avec EXIF | Conserve la position et les données de l'appareil photo |
| `Strings.hi.json` | EXIF के साथ मूल | स्थान और कैमरा डेटा बनाए रखता है |
| `Strings.id.json` | Asli dengan EXIF | Menyimpan lokasi dan data kamera |
| `Strings.it.json` | Originale con EXIF | Mantiene posizione e dati della fotocamera |
| `Strings.ja.json` | オリジナル（EXIF付き） | 位置情報とカメラ情報を保持します |
| `Strings.ko.json` | 원본(EXIF 포함) | 위치 및 카메라 정보 유지 |
| `Strings.pl.json` | Oryginał z EXIF | Zachowuje lokalizację i dane aparatu |
| `Strings.pt.json` | Original com EXIF | Mantém a localização e os dados da câmera |
| `Strings.ru.json` | Оригинал с EXIF | Сохраняет геопозицию и данные камеры |
| `Strings.tr.json` | EXIF ile orijinal | Konum ve kamera bilgilerini korur |
| `Strings.uk.json` | Оригінал з EXIF | Зберігає геопозицію та дані камери |
| `Strings.vi.json` | Bản gốc kèm EXIF | Giữ vị trí và dữ liệu máy ảnh |
| `Strings.zh.json` | 原图（含 EXIF） | 保留位置和相机信息 |

For example, in `Strings.en.json`:

```json
  "Editor_QualityAppliesToPhotos": "Applies to {0} photo|Applies to {0} photos",
  "Editor_QualityOriginalWithExif": "Original with EXIF",
  "Editor_QualityKeepsMetadata": "Keeps location and camera data",
```

Then run `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd`.

- [ ] **Step 10: Build and run the localization and unit tests**

Run: `dotnet build ActualChat.CI.slnf`, `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest|FullyQualifiedName~ImageQualityPresetTest"`, `scripts/derive-bcms.cmd --check`, `scripts/derive-max.cmd --check`, `npm run build:Verify`
Expected: 0 build errors; tests PASS; both `--check` runs report no differences. `grep -rn "IsUploadPending\|GlobalQuality\|ApplyQualityAndStartUploads\|ImageResizeResult" src/dotnet` returns nothing.

- [ ] **Step 11: Commit**

```bash
git add src/dotnet/UI.Blazor.App src/dotnet/Localization
git commit -m "feat(attachments): process photos on add with 4K/1080p/Original presets"
```

---

### Task 13: End-to-end verification and docs

**Files:**
- Modify: `docs/api-index-ts.md`, `docs/superpowers/specs/2026-09-11-client-image-pipeline-design.md`

- [ ] **Step 1: Full automated run**

Run: `npm run build:Verify`, `npm run test:unit`, `dotnet build ActualChat.CI.slnf`, `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~Uploads"`, `dotnet test tests/Media.UnitTests`, `dotnet test tests/Chat.UI.Blazor.UnitTests`
Expected: all PASS. Report any failure with its output; don't mark the task done.

- [ ] **Step 2: Web (Chrome) with `/debug-ui` and `/server-loop`**

Sign in, open a chat, attach a 4000x3000 JPEG with GPS EXIF and a PNG screenshot with transparency. Check, in the network panel or the upload size shown on the attachment:
- default chip reads `4K`; the JPEG uploads as `*.jpg` at ≤3840 px, roughly 40% of the original size; the transparent PNG stays PNG;
- the menu shows sizes for 4K and 1080p after processing;
- switching to `1080p` restarts the upload with a smaller file; `Original` uploads the original size without GPS (download it and check with `exiftool` or an EXIF viewer); `Original with EXIF` keeps GPS;
- sending while processing waits and then sends; the stored image renders with the right aspect ratio, including a portrait photo with EXIF Orientation 6.

- [ ] **Step 3: Safari on the Mac Mini** (`macmini` skill)

Repeat Step 2 with a HEIC photo: Safari decodes it, so 4K produces a JPEG.

- [ ] **Step 4: Devices**

- Android device: pick a JPEG and a Samsung/Xiaomi HEIC from the gallery; both upload as JPEG ≤3840 px with the correct orientation. Kill the app mid-upload, reopen: the upload resumes from the processed file.
- iPhone (`ios-run` skill): pick a HEIC and a Live Photo; both upload as JPEG. `Original with EXIF` uploads the HEIC unchanged.
- Windows app: first confirm the app starts and renders at all (Task 1's WebView2 custom-scheme registration changes environment creation; a rejected registration is a startup failure, not a fallback — if so, drop the registration and switch Windows to a same-origin path or `DotNetStreamReference`). Then pick a large JPEG with the file picker; confirm processing works (Task 1's WebView2 registration) and that the processed file under `%LOCALAPPDATA%\...\cache\processed-images` is deleted after the message is sent or the attachment is removed.

- [ ] **Step 5: Docs**

In `docs/api-index-ts.md`, add under the shared (`src/nodejs/src`) section:

```markdown
- `ImageProcessor` (class) - Resizes, re-encodes (jpegli WASM) or strips metadata of images in a worker.
- `JpegliEncoder` (class) - jpegli WebAssembly JPEG encoder.
- `stripImageMetadata` (function) - Lossless JPEG/PNG/WebP metadata removal.
- `sniffImageFormat` (function) - Detects an image format from its bytes.
```

and under `UI.Blazor.App`:

```markdown
- `ImageProcessingInterop` (class) - Blazor entry point to `ImageProcessor` for local content URLs.
```

Append an "Implementation notes" section to the spec listing the six deviations from this plan's header, plus two known gaps: a HEIC/HEIF picked with an Original preset stays a file attachment (the server doesn't decode HEVC), and `IncomingShareUI.SendFiles`' multi-chat / more-than-10-files share path bypasses the pipeline, so those photos upload at full resolution and the server no longer resizes them.

- [ ] **Step 6: Commit**

```bash
git add docs/api-index-ts.md docs/superpowers/specs/2026-09-11-client-image-pipeline-design.md
git commit -m "docs(image-processing): index new APIs, record implementation deviations"
```

- [ ] **Step 7: Hand back**

Report the verification results. Ask the user before pushing to `feat/media-quality-selector` (iqmulator's branch) and whether to run `/prepare-merge`.
