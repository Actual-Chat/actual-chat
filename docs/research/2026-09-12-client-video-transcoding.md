# Client-side video transcoding — feasibility study

**Date:** 2026-09-12. **Status:** research only; nothing implemented, no repo code changed.
**Question asked:** how hard would it be to do for video what the image pipeline now
does for photos — offer presets (480p / 1080p / original), re-encode on the client
with AV1 where available and VP9 otherwise, target a sensible bitrate, and keep the
original audio track where possible?

> **Parked 2026-09-12; no implementation.** The standing conclusion — the decision, the
> platform matrix, what real files look like, the traps, the staging and the probes — is
> distilled into [`docs/plans/on-device-video-transcoding.md`](../plans/on-device-video-transcoding.md),
> which is readable in fifteen minutes and stands on its own. **Start there.** This document
> is the evidence behind it.

**How to read this.** Sections 1–6 are findings. Every claim that could go stale carries
a source and a date. Numbers are labelled **measured** (traced to a primary source or
measured here), **derived** (arithmetic from measured inputs, shown) or **estimated**
(judgement, with the reasoning). Sections 7 and 8 are judgement and recommendation —
disagree with them freely without discarding the facts above.

Measurements taken on this host are in [Appendix A](#appendix-a--measurements-taken-for-this-study);
things I could not verify are in [Appendix B](#appendix-b--what-i-could-not-verify).
Client-generated thumbnails are out of scope — the server's snapshot frame appears only
as context.

> **Two follow-up rounds are appended after section 8.**
> [**Round 2**](#round-2--tests-run-and-the-design-for-an-opportunistic-client-transcoder)
> runs the Dolby Vision test against synthetic files, measures mediabunny against 152 real
> phone videos, designs the feature around the server always re-encoding, and closes most of
> Appendix B. [**Round 3**](#round-3--the-real-iphone-file) repeats the decisive tests
> against a real iPhone 13 Pro recording and settles the Dolby Vision question outright.
>
> **Several round-1 claims are corrected there** — the Dolby Vision reading (§2.2), the
> severity of risk 1 (§8), and the idea that copying audio sidesteps the AAC-priming bug
> (§3.3). One risk round 1 missed entirely is now first: **10-bit HLG HDR is the default
> output of both iPhone 12+ and recent Samsung phones — 45 % of the real corpus — and
> nothing in the pipeline tone-maps it.** Round 3 shows that may be cheaper to solve than
> round 2 assumed, and names the one probe that decides it. The recommendation itself does
> not change in either round.

---

## 0. Starting point — what the repo already has

This matters more than any external survey, because most of the machinery either exists
or is deliberately absent.

### 0.1 The existing transcoder is Apple-only, native, and file-path based

`src/dotnet/UI.App/Services/VideoTranscoder.cs` is a base class whose
`TranscodeInternal` returns `FilePath.Empty` — "no transcoding". The only real
implementation is `src/dotnet/Maui/Apple/AppleVideoTranscoder.cs`, registered in
`src/dotnet/Maui/Module/MauiModule.cs:72`; `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs:315`
registers the no-op base class for everything else. It is also used from the iOS share
extension (`src/dotnet/App.Maui.IosShareExt/Services/ShareUI.cs:349`).

What `AppleVideoTranscoder` does, via AVFoundation:

| condition | action |
|---|---|
| size ≤ 70 MB (`MaxRemuxSize`) and already `.mp4` | nothing |
| size ≤ 70 MB and not `.mp4` | **remux** (`AVAssetExportSessionPreset.Passthrough`) |
| size > 70 MB, not `.mp4` | transcode |
| size > 70 MB, `.mp4`, codec not HEVC/H.264 | transcode |
| size > 70 MB, `.mp4`, long side > 1920 or short side > 1080, or bitrate > 8 Mbps | transcode — but only if `EstimateOutputFileLengthAsync()` < source size |

Transcode means `AVAssetExportSessionPreset.Hevc1920x1080` with
`ShouldOptimizeForNetworkUse = true` into MP4. There is no bitrate control, no preset
choice, no user-visible quality option, and no audio decision — the preset decides
everything. Progress is polled from `session.Progress` every 250 ms.

### 0.2 Client processing is wired for MAUI only, by construction

`src/dotnet/UI.Blazor.App/Services/FileUploads/UploadSession.cs:152` (`RunClientProcessing`):

```csharp
var filePath = (fileProvider as MauiFileProvider)?.FileRef ?? FilePath.Empty;
...
var transcodedPath = await _uploadOperations.VideoTranscoder
    .Transcode(filePath, mimeType, progress, cancellationToken)
```

On the web `filePath` is empty and `Transcode` returns immediately. The state machine
(`Created → Initializing → ClientProcessing → Uploading → ServerProcessing → Completed`)
already has the stage, the progress plumbing, the cancellation, and a resumable snapshot
(`TranscodedFilePath` survives in `UploadSessionSnapshot`). **What it does not have is a
seam that a browser can use** — the contract is a local file path, not a stream or Blob.

### 0.3 The image pipeline already proves the "one WebView worker, all platforms" pattern

This is the strongest architectural signal in the repo. `ImageAttachmentProcessor`
(`src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageAttachmentProcessor.cs`) has two
branches, and **both end in the same JavaScript module worker**:

- `ProcessWeb` → `WebFileProvider.ProcessImage` → `web-file-providers.ts` → `ImageProcessor.process(blob, …)`.
- `ProcessMaui` → `MauiFileProvider.GetContentUrl(maxSize)` → `ImageProcessingInterop.processUrl(url, …)`
  → the same `ImageProcessor`, with the result coming back as an `IJSStreamReference`
  and landing in `ProcessedImageStore`.

So MAUI images are resized and re-encoded by jpegli-in-WASM inside the WebView, not by
native code. Supporting facts: `src/nodejs/jpegli/simd/jpegli.wasm` is **166 KB**; the
worker runs one job at a time with a 30 s-per-job deadline that scales with queue depth;
a worker killed for memory is detected by timeout, not by an `error` event
(`image-processor.ts:30-37`), because "a worker killed for memory … never raises `error`
in Chromium/WebKit".

The preset model to mirror is `ImageQualityPreset` —
`{ Uhd4K = 3840, FullHd = 1920, Original, OriginalWithExif }` — and its request carries
**two outputs**, a `main` and an `estimate`, so the menu (`ImageQualityMenu.razor`,
`ImageQualitySelector.razor`) can show the user what the other preset would weigh.
That trick does not transfer to video: for an image the estimate is a second encode of an
already-decoded bitmap; for video it is a second full transcode. A video preset menu
either shows no size, or shows a sampled extrapolation.

### 0.4 The live-video pipeline: what is reusable, and what is not

`src/dotnet/UI.Blazor.App/Services/Video/` is a mature WebCodecs pipeline, but it is
built for realtime streaming.

**Reusable as-is:**

| component | file | note |
|---|---|---|
| encoder capability probing | `codec-support.ts` (`detectSupportedCodecs`, `isCodecSupported`, `probeEncoder`, `getCodecForCategory`) | probes `prefer-hardware` and `prefer-software` independently and believes only the `supported` flag |
| the async encoder/decoder adapter | `adapters.ts` (`CodecToAsyncAdapter`, `AsyncVideoEncoder`) | turns the callback-based WebCodecs API into an async iterable with backpressure |
| operator pipeline | `operators/` + `ix-ext` `pipe`/`drain` | `decode`, `downscale`, `encode`, `parallel-map` |
| the downscaler | `canvas/downscaler.ts` | 2D-canvas, per-tier source reuse; production choice over WebGPU because "WebGPU pacing on iOS forced too many per-frame drains" |
| thermal state | `ThermalTracker` / `MauiThermalTracker` / `WebThermalTracker`, `ThermalLevel` | already maps to iOS `NSProcessInfo`, Android `PowerManager`, and the web Compute Pressure API |
| codec efficiency policy | `src/dotnet/Core/Media/VideoCodecDef.cs` | H.264 1, HEVC 1.4, VP9 1.41, AV1 1.7, capped at `MaxBitrateEfficiency = 1.4` |

**Not reusable, and each is a real change:**

- **`latencyMode: 'realtime'`** everywhere (`codec-support.ts:291,395,557`,
  `sender/recorder-worker.ts:330`). A file transcode wants `'quality'`. On WebKit
  `latencyMode:'quality'` maps to clearing `kVTCompressionPropertyKey_RealTime`, so this
  is not cosmetic.
- **`avc: { format: 'annexb' }`** (`codec-support.ts:295`, `recorder-worker.ts:334`).
  Annex B is right for the wire and wrong for MP4, which needs length-prefixed samples
  plus an `avcC`/`hvcC` description.
- **The flood gate** drops frames under backpressure (`operators/flood-gate.ts`,
  `streaming/push-to-pull-buffer.ts`). Newest-frame-wins is correct for a camera and
  catastrophic for a file.
- **No demuxer and no file muxer.** The pipeline's input is a
  `MediaStreamTrackProcessor`; its output is `RpcStream<VideoFrameBundle>`. Nothing reads
  an MP4 and nothing writes one.
- **Capability probing happens at realtime settings**, so its answers do not transfer
  unchanged to a quality-mode encode.
- The simulcast ladder (`layer-ladder.ts`) and its bitrates
  (`VideoLayerDef.CameraLayers`: W320 312.5 / W640 1250 / W1280 4000 kbps) are sized for
  three concurrent live tiers, not for a single file output.

The per-device measurements in `docs/live-video/codec-performance.md` (dated 2026-08-30/31)
are the most valuable asset here and are used throughout section 1.

### 0.5 libav.js — ffmpeg in WASM is already vendored, but the wrong build

`src/nodejs/libav/` contains `libav-6.10.9.0-vp9-opus-avf-simd.wasm.wasm` (**3.85 MB**),
its glue (294 KB), a loader (28 KB), and `libavjs-webcodecs-polyfill.mjs` (182 KB, 0BSD).
It is loaded at runtime only when `WebCodecsCompat.resolveLevel()` returns something other
than `'none'` — i.e. on Firefox (`vp9` level, by override) and on engines with no
WebCodecs at all (`full`). The README states the threaded builds are deliberately absent
because "they need cross-origin isolation (COOP/COEP), which this app does not set".

**This build cannot help a file transcoder.** I inspected the wasm's string table: the
registered containers are Matroska/WebM, Ogg and the wav/flac family; the encoders are
libvpx-vp9 and libopus. Every AVOption name belonging to ffmpeg's `movenc.c` is absent
(`movflags`, `faststart`, `frag_keyframe`, `empty_moov`, `write_colr`), as are the mov
*demuxer*'s (`use_editlist`, `ignore_editlist`, `enable_drefs`). There is no libx264 and
no libaom. So: **no MP4 muxer, no MP4 demuxer, no H.264 encoder.** A different libav.js
variant would be needed (see §2.5), which is a new ~1–2 MB wasm asset, not a reuse.

### 0.6 COOP/COEP: the code exists and is switched off

`src/dotnet/App.Server/Module/ApplicationBuilderExt.cs:10` defines `UseCoopHeaders()`
(COOP `same-origin`, COEP `require-corp`, applied only to `/chat`, `/user`, `/settings`
and `/`). The call site, `src/dotnet/App.Server/AppHost.Build.cs:236`, is **commented
out**: `// App.UseCoopHeaders();`. It was added in `578d97c339` for the ONNX WASM runtime
and disabled since. `SharedArrayBuffer` appears in the tree only as a defensive shim
inside a vendored ONNX runtime file. Neither the server CSP
(`ContentSecurityPolicy.cs`) nor the MAUI `index.htm` meta-CSP mentions cross-origin
isolation.

MAUI serves the app from `app://0.0.0.1/` (Apple/macOS) and `https://0.0.0.1/`
(Windows) — see `MauiSettings.LocalHost = "0.0.0.1"` and
`src/dotnet/App.Maui/WebView/MauiWebView.*.cs`. Section 2.6 explains why that makes
cross-origin isolation unreachable in practice on two of the three WebViews, and
pointless on the third.

### 0.7 The server already transcodes — this reframes the whole question

`src/dotnet/Core.Server/Uploads/LocalVideoUploadProcessor.cs` runs ffmpeg: `libx264`,
`WithFastStart()`, `WithVariableBitrate(4)`, scaled to ≤ 1920×1080, plus an mjpeg
snapshot at 10 % of the duration.
`GoogleCloudVideoUploadProcessor.cs` submits a GCP Transcoder job: H.264, CRF 23, AAC
128 kbps, bitrate `pixels × max(fps,24)/30 × 3.5` clamped to 0.5–15 Mbps, 14-minute job
timeout, resumable across server restarts via a GCS state file.

The gate is `UploadProcessorHelper.MustConvertVideo`: **a video is left alone if its codec
is h264 or hevc and its container is MP4 and it is ≤ 1080p.** Two consequences:

1. **Client transcoding does not buy playability.** It buys upload bytes and upload time.
2. **1080p HEVC MP4 from a phone reaches every viewer untranscoded today.** Chrome and
   Safari can usually play it; Firefox cannot. That is a pre-existing issue a client-side
   H.264 path would incidentally fix, and it is arguably worth fixing on the server
   regardless.

Also relevant: `Constants.Attachments.FileSizeLimit = 500 MB`, `FileCountLimit = 10`.
A 500 MB video is well past what a WebView can hold in memory.

### 0.8 No Android or Windows native media code exists

`Xamarin.AndroidX.Media` 1.8.0 is referenced (`Directory.Packages.props:73`) but that is
the legacy `androidx.media` MediaSession library, not Media3. There is no `MediaCodec`,
`MediaMuxer`, `Media3`, `Transformer`, or `Windows.Media.Transcoding` usage anywhere in
`src/dotnet`.

---

## 1. Platform reality

### 1.1 Is WebCodecs there at all

From MDN browser-compat-data (`@mdn/browser-compat-data`, read 2026-09-12):

| target | `VideoDecoder` / `VideoEncoder` | `AudioDecoder` / `AudioEncoder` |
|---|---|---|
| Chrome / Edge desktop | **94** (2021-09) | 94 |
| Chrome Android | 94 | 94 |
| **Android System WebView** | **94** | 94 |
| Safari macOS / iOS | **16.4** (2023-03-27) | **26** (2025-09-15) |
| **iOS / iPadOS WKWebView** | **16.4** | **26** |
| Firefox desktop | 130 | 130 |
| **Firefox Android** | **not supported** | not supported |
| Samsung Internet | 17.0 | 17.0 |

The Chromium numbers are better evidenced than the table suggests: Chrome Platform Status
feature 5669293909868544 records `shipped_milestone: 94`, `shipped_android_milestone: 94`
**and `shipped_webview_milestone: 94`** — so Android System WebView has had WebCodecs since
2021-09-21, not later. MDN's `webview_android: "mirror"` is BCD's automatic inference rather
than a test result, and the often-cited
[mdn/browser-compat-data#18676](https://github.com/mdn/browser-compat-data/issues/18676)
("`VideoEncoder` unsupported in Android Chrome", closed as not planned) is almost certainly
a **secure-context artifact**, not a gap. WebView2 on Windows is the Edge build, on a
two-week cadence since v152 (2026-08-24).

**The secure-context requirement matters for the MAUI heads.** The WebCodecs IDL is
`[Exposed=(Window,DedicatedWorker), SecureContext]`, so the interfaces are absent on
`http://` and on any origin the engine does not consider potentially trustworthy — and
they are **not** exposed on `SharedWorker` or `ServiceWorker`. The Android and Windows MAUI
heads serve from `https://0.0.0.1/`, which is a secure context. The Apple heads serve from
`app://0.0.0.1/` via a `WKURLSchemeHandler`, which in principle is not — but
`docs/live-video/codec-performance.md` measured full encode round-trips in the iPhone 13
Pro WKWebView of a real Voxt build, so WebKit evidently treats it as trustworthy there.
Empirically satisfied on all three; worth keeping in mind if the scheme ever changes.

Two consequences worth stating plainly:

- **Firefox Android has no WebCodecs.** Any client transcoder needs a server fallback, not
  a polyfill: the repo's `WebCodecsCompat` `'full'` level would load a libav.js build that
  (as §0.5 shows) cannot read or write MP4 anyway, and software encode is too slow (§4).
- **On iOS 16.4 – 18.x WKWebView there is `VideoEncoder` but no `AudioEncoder`.** Audio
  passthrough is therefore not an optimisation on those devices; it is the only way the
  pipeline completes at all. See §3.

WKWebView gets WebCodecs on exactly the same terms as Safari, confirmed from WebKit
source: `WebCodecsVideoEnabled` in `UnifiedWebPreferences.yaml` is `status: mature` with a
Cocoa default of `defaultPeerConnectionEnabledAvailable()`, a process-wide and
embedder-independent condition. No entitlement, no app-side flag.
One caveat: a WebCodecs preference block in that yaml carries `disableInLockdownMode: true`
and the agent researching it could not attribute it conclusively (most likely the AV1
pref). **Lockdown Mode is untested.**

### 1.2 What can actually be encoded — the repo's own measurements first

`docs/live-video/codec-performance.md`, measured 2026-08-30/31 on real devices through
`VideoEncoder.isConfigSupported`, probing each acceleration mode independently:

| | AV1 | HEVC | VP9 | H.264 |
|---|---|---|---|---|
| Chrome / Windows (RTX 3090) | hw + sw | hw only | **sw only** | hw + sw |
| Firefox / Windows | sw only | **none** | sw only | sw only, *not realtime* |
| iPhone 13 Pro (WKWebView) | **none** | hw + sw | hw + sw | hw + sw |
| Galaxy SM-S948U1 (Android WebView 151) | **sw only** | hw only | **sw only** | hw + sw |

Read against the brief's premise — "AV1 where available, VP9 otherwise" — this table is
the single most important finding in the study:

- **Hardware AV1 encode exists on none of the phones measured**, and on the desktop only
  because of an RTX 3090.
- **Hardware VP9 encode exists only on the iPhone**, and nowhere on Chromium.
- The only codec with hardware encode on every one of the four is **H.264**.

The same file's measured ms/frame at 720p: iPhone hw VP9 3.88, hw HEVC 4.97, hw H.264
4.60; Galaxy hw H.264 2.43, hw HEVC 2.55, sw VP9 3.75, sw AV1 4.44. Two carry-forwards
from that document: **Android hardware encoders overshoot the bitrate target by 17–39 %**,
while its software encoders hit it; desktop software encoders *undershoot* badly (H.264
software produced 94 kbps against an 800 kbps target). And **HEVC decode fails everywhere
without a `description`** — the `hvcC` bytes must be extracted from the container and
handed to `configure()`.

### 1.3 Apple: encode paths, and where `isConfigSupported` lies

From WebKit source and release notes:

- **Backend routing is documented in code.** `LibWebRTCCodecs::videoEncoderTypeFromWebCodec()`
  maps only `avc1.` → H.264 and `hev1.`/`hvc1.` → H.265, which run in the GPU process on
  VideoToolbox (hardware on Apple Silicon and all modern iPhone/iPad). `vp8`, `vp09.*` and
  `av01.*` fall through to `VideoEncoder::createLocalEncoder` — **libvpx/libaom software in
  the content process**. The repo's measurement that "VP9 encodes on iOS" is correct and is
  software; that it measured *identically* under `prefer-hardware` and `prefer-software`
  is explained by the next point.
- **`hardwareAcceleration` is inert on WebKit.** `VideoEncoder::Config` carries only
  `{width, height, useAnnexB, bitRate, frameRate, isRealtime, scalabilityMode}` — the hint
  is accepted by the IDL and discarded. It lies in both directions.
- **`bitrateMode` is likewise accepted and ignored**, and per-frame quantizer is not
  plumbed. Chromium has honoured `bitrateMode: "quantizer"` since M117; Safari does not.
- **There is no `hevc` member in `WebCodecsVideoEncoderConfig.idl`** — only `avc`.
  `useAnnexB` is computed solely from `config.avc`, so you cannot ask for Annex B HEVC;
  WebKit bug 281945 (filed 2024-10-22, still NEW) documents `isConfigSupported` saying yes
  and then delivering length-prefixed output. For MP4 muxing this happens to be the format
  you want, but it means you must not trust the config echo.
- **AV1 is gated on hardware *decode*.** `WebCodecsAV1Enabled` is a preview flag that
  WebKit turns on at runtime only when `VTIsHardwareDecodeSupported('av01')` holds
  (commit 4ded51ddbf, 2024-02-22; shipped Safari 17.4). So `av01` is exposed for decode
  *and encode* only on A17 Pro / iPhone 16 / M3+ / M4 iPad Pro class devices, and the
  encode is always libaom software. On an iPhone 14 or an M1, `configure()` throws
  `NotSupportedError`. This is exactly why the repo measured "AV1 absent entirely on the
  A15".
- `isConfigSupported` in WebKit actually instantiates an encoder, so codec, dimension,
  alpha and scalability rejections are honest (odd-sized H.264 → `TypeError`, "H264 only
  supports even sized frames").

**Version timeline (Apple release notes):** 16.4 (2023-03-27) video WebCodecs; 17.0
(2023-09-18) temporal `scalabilityMode`; **17.4 (2024-03-05)** "Added WebCodecs HEVC
support. (112067287)" plus AV1-on-AV1-hardware and VP8/VP9/WebM on iOS; **26.0
(2025-09-15)** `AudioEncoder`/`AudioDecoder`; 26.4 (2026-03-24) H.264 decode output
reordering fixed; 26.5/27 HEVC decode reordering fixed. Currently shipping: 26.6
(2026-07-27). 27.0 is dated 2026-09-14.

**Apple-side bugs that matter for a file transcoder:**

- **A malformed `description` crashes the GPU process.** WebKit bug 308901, integer
  underflow in `ComputeH264InfoFromAVC` when `size == 0`; Safari 26 / iOS 26, filed
  2026-02-28, still NEW. Validate `avcC`/`hvcC` bytes before `configure()`.
- **`decoderConfig` is emitted once**, on the first chunk after a new active configuration.
  Capture it or lose the `description`.
- **`flush()` can never settle.** `WebCodecsVideoEncoder::stop()` clears pending flush
  promises *without rejecting* on context teardown, so a `flush()` outstanding at
  `pagehide`/navigation hangs forever. `reset()`/`close()` do reject with `AbortError`.
  Race the final flush against a timeout.
- **WebKit implements no codec reclamation** — `suspend()` is an empty no-op — so the
  Chromium background-reclamation problem does not exist there. What stops an iOS
  transcode is OS process suspension when the host app is backgrounded. Budget for
  resuming mid-job, not for losing the codec.
- **No documented concurrent-instance cap**, but exhaustion surfaces as
  `NotSupportedError`. Create one encoder and one decoder and reuse them; do not fan out
  per segment.
- Decode-side open bugs: 276152 (HEVC decode differs from Chrome/ffmpeg), 297788
  (`VideoDecoder` error starting from a non-IDR CRA_NUT frame), 321880 (`new VideoFrame(canvasImageSource)`
  shifts some timestamps by −1 µs).
- iOS memory: the jetsam limit applies to `com.apple.WebKit.WebContent` and the app cannot
  raise it; the kill surfaces as `webViewWebContentProcessDidTerminate`. Per-device
  figures (~300–450 MB pre-iPhone-15, ~1 GB+ after) are indicative only.

### 1.4 Chromium: what the source says about encode

Chromium's gating is readable in `media/` and it is more restrictive than the codec strings
suggest. Milestone dates from `chromiumdash.appspot.com`; current stable is M153
(2026-09-08).

- **H.264** — hardware essentially everywhere (Windows MediaFoundation, macOS
  VideoToolbox, Android `MediaCodec`, ChromeOS VA-API). **Linux desktop Chrome is software
  only unless the user passes `--enable-features=AcceleratedVideoEncoder`** — VA-API encode
  is off by default. The software fallback is openh264 and it supports
  **Baseline/Main/Extended/High** (the commonly repeated "Baseline only" claim is stale,
  ~2016); 8-bit 4:2:0; minimum dimension 16; maximum ~36,864 macroblocks; it **rejects
  `bitrateMode: "quantizer"`**. It is absent on iOS and on 32-bit-ARM Android builds
  (`media_use_openh264 = true` except `is_ios || (is_android && current_cpu == "arm")`).
  The repo measured Android *software* H.264 as the slowest encoder on any device
  (6.69 ms/frame at 480p versus 1.17 ms hardware) and the worst compressor of the four.
- **HEVC** — **platform encoder only; no software HEVC encoder exists in Chromium.**
  `media/base/supported_types.cc`: *"HEVC only has platform encoder support."* Default-on
  from **M130 (2024-10-15)**, and `media/media_options.gni` limits it to
  `is_win || is_apple || is_android` — so **never on Linux or ChromeOS**. On Android it is
  **Main profile, 8-bit NV12/I420 only, Android 10+**. On macOS also Main 10 via
  `kPlatformHEVCHbdEncoderSupport`. WebView2/Edge use the same path and need the OS HEVC
  stack (the free "from Device Manufacturer" extension or the paid Store app).
- **VP9** — software libvpx is the normal path: `VPX_DL_REALTIME`, `cpu_used = 7`
  ("Higher means faster encoding, but lower quality"), row-MT on, profiles 0–3. Hardware
  VP9 encode exists only on some Intel iGPUs via Windows MF, on ChromeOS, and on a few
  Android `MediaCodec` implementations — **never on macOS**.
- **AV1 — and this is the finding that settles the brief's codec question.** Chromium's
  software AV1 encoder is deliberately crippled for quality work.
  `media/video/av1_video_encoder.cc`: *"libaom is compiled with `CONFIG_REALTIME_ONLY`, so
  we can't use anything but `AOM_USAGE_REALTIME`"*; `AOME_SET_CPUUSED` is 9 for realtime
  and **7 for "quality"**; **8-bit only**; `g_lag_in_frames = 0`, i.e. **no lookahead and
  no ALTREF pyramid**. There is no SVT-AV1 anywhere in Chromium's WebCodecs path. So
  WebCodecs AV1 on a machine without hardware AV1 gives you a realtime-preset encode whose
  compression efficiency is far below libaom `good` or SVT-AV1 preset 6 — **the "AV1 makes
  smaller files" premise is much weaker through WebCodecs than the BD-rate literature in
  §6.3 implies.** Hardware AV1 encode: Intel Arc / Meteor Lake+ iGPU, NVIDIA RTX 40-series+,
  AMD RX 7000+, reachable on Windows through the MF encoder list; flag-gated on Linux;
  **no AV1 encoder in VideoToolbox on macOS at all**; on Android essentially Tensor G3+
  only. Also not built for 32-bit Android (`enable_libaom` requires arm64 or x64).
- **`hardwareAcceleration` means something precise on Chromium, unlike on WebKit.**
  `prefer-hardware` → **accelerated only, fail if unavailable**. `no-preference` → try
  accelerated, then **silently wrap a software fallback**. `prefer-software` → skip
  hardware. For a transcoder the consequence is concrete: with the default you can land on
  openh264/libvpx/libaom at a fraction of the throughput with no event telling you
  ([w3c/webcodecs#492](https://github.com/w3c/webcodecs/issues/492)). Ask for
  `prefer-hardware` and choose the fallback yourself — which is what
  `codec-support.ts` already does by probing each mode independently.
- **`isConfigSupported()` validates the config, not the frame.** The classic failure is
  `supported: true` for AVC `prefer-hardware`, then `OperationError` at `encode()` because
  the `VideoFrame` is I420 while the MFT wants NV12. Firefox is worse (returns true, then
  errors — bugzilla 1918769).
- **`bitrateMode: "quantizer"`** (per-frame QP for AV1, VP9, AVC) is Chromium-only, from
  M117 (2023-09-12). `latencyMode` defaults to `"quality"`.
- **The WebCodecs binding layer costs roughly 3×**: 4K H.264 at ~25 fps through a
  hardware-accelerated `VideoEncoder` versus 65–70 fps for native ffmpeg + VideoToolbox on
  the same 2018 MacBook Pro (w3c/webcodecs#492). One desktop measurement, 2023-era.

**Gotchas that specifically bite file transcoding rather than live streaming:**

- **A keyframe is required after `configure()` *and after every* `flush()`** — normative,
  enforced in both engines. So **never `flush()` mid-file**; flush only at EOF or exactly at
  a keyframe boundary. Chromium additionally parses the bitstream to verify the first chunk
  really is a keyframe, so a mislabelled chunk from the demuxer fails loudly.
- **Unclosed `VideoFrame`s stall decoding with no error.** Per the spec and the maintainers
  ([w3c/webcodecs discussion #680](https://github.com/w3c/webcodecs/discussions/680)),
  dropping a reference without `close()` is the most common way to stall a decoder; output
  simply stops. Reorder windows can reach 16 for H.264 B-frames. There is also a macOS
  Chromium leak — `VTDecoderXPCService` growing to multiple GB over ~10 minutes in a
  backgrounded tab unless you `decoder.close()` on hide (issues.chromium.org/404905689).
- **Android 16×16 alignment.** `ndk_video_encode_accelerator.cc`: *"Non 16x16 aligned
  resolutions don't work well with MediaCodec unfortunately, see
  <https://crbug.com/1084702>"* — Chromium crops to the nearest 16×16 when stride
  information is absent. **1080 is not a multiple of 16** (1080 = 67.5 × 16), so
  1080-tall output is exactly this case. Relatedly,
  [w3c/webcodecs#397](https://github.com/w3c/webcodecs/issues/397) reports
  `avc1.42001e` (Baseline **level 3.0**) encoder creation failing at 720p/1080p on Android
  while working on desktop — match the level to the resolution, as
  `getCodecForCategory` already does.
- **Backpressure:** bound both queues (~16–20) and drive from the `dequeue` event
  (Chrome 106+, Firefox 130, Safari 16.4) rather than polling. For a file you want to
  *block* the demuxer, not drop frames as the live pipeline's flood gate does;
  [w3c/webcodecs#810](https://github.com/w3c/webcodecs/issues/810) documents a RAM spike at
  ~1000 queued frames.
- **Codec reclamation when backgrounded is a Chromium-only problem.** Inactive codecs get
  reclaimed and throw **`QuotaExceededError`**
  ([w3c/webcodecs#889](https://github.com/w3c/webcodecs/issues/889), 2025-04-17). The
  documented workaround is to keep an *active* encoder on the same global, which a
  transcode pipeline naturally has. On Android the OS can additionally reclaim
  `MediaCodec` (`MediaCodec.CodecException`, check `isRecoverable()`/`isTransient()`), so a
  WebView transcode that must survive backgrounding needs to be able to rebuild codecs.
- **Rotation metadata is not applied by decoders**, and `VideoFrame.rotation` /
  `VideoDecoderConfig` orientation only arrived in **Chrome 138 (2025-06-24)** — not in
  Firefox, not in Safari. iPhone recordings come out sideways unless you handle rotation
  yourself.
- **Concurrent hardware codec instances.** Android exposes
  `CodecCapabilities.getMaxSupportedInstances()`, and the docs warn the practical number
  may be lower. CDD Media Performance Class floors bind only devices that claim a class
  (Android 12: 6 concurrent hardware decoder *and* encoder sessions at 720p30; Android 13:
  6 at 1080p30; Android 14: 3×1080p30 + 3×4K30) — non-MPC mid-rangers can be lower, and a
  transcode already uses two. On desktop NVIDIA, **NVENC is capped at 8 concurrent encode
  sessions per system**, shared with OBS and Teams. One encoder and one decoder, reused, is
  the safe design everywhere.
- **WebView2 / Chromium**: if the GPU process is unavailable (`--disable-gpu`, a TDR reset,
  Session 0 service contexts) everything silently drops to software encode.

### 1.5 Android System WebView: the floor, with numbers

WebView's provider is Trichrome (`com.google.android.webview` + a shared
`TrichromeLibrary` APK) on Android 10+, Monochrome (the Chrome APK itself) on 7.0–9.0
phones, and standalone below that. It is updated entirely as a **Play Store app**, so it
inherits Play auto-update behaviour and there is no OS-level forced push.

**The floor has moved twice recently, and WebView shares Chrome's floor** —
`build/config/android/config.gni` sets `default_min_sdk_version = 29` and
`android_webview/BUILD.gn` uses it, so there is no longer-lived WebView track:

| Android | terminal Chromium milestone |
|---|---|
| 5.x | M95 |
| 6.0 | M106 |
| 7.x | M119 (Chrome 120, 2023-12-06, raised the floor to Android 8) |
| **8.0 / 8.1 / 9** | **M138 — forever** (Chrome/WebView 139, 2025-08-05, raised the floor to Android 10) |
| 10 and later | current, M153 |

The shipping artifact confirms it: Android System WebView 153.0.8010.36, published
2026-09-09, "Android 10 or higher required". No Android 11 floor has been announced.

**Distribution, two independent sources agreeing:** Google Play active devices
(2025-12-01) put Android 10+ at **~90.7 %** and 11+ at ~82.9 %; Statcounter web traffic
(April 2026) puts API 29+ at **91.1 %** and API 30+ at 86.9 %. So the population frozen at
Chromium 138 is roughly **8–9 % and shrinking**.

**There is no public per-version WebView telemetry.** Cloudflare Radar folds
`ChromeMobileWebview` into "Google Chrome" with no version split; Statcounter reports one
Chrome-on-Android bucket; caniuse carries a single current version per mobile row. The one
usable raw source is Wikimedia's monthly
[browser family and major version TSVs](https://analytics.wikimedia.org/published/datasets/periodic/reports/metrics/browser/),
where "Chrome Mobile WebView" is a distinct family.

**Structurally stranded populations no version check predicts:** GMS-less Huawei ships
`com.huawei.webview` and the framework rejects Google's WebView signature; HarmonyOS 5 is
not Android (ArkWeb, Chromium base pinned per OS release); Chinese ROMs without Play get
WebView only by OTA; Amazon Fire OS ships an unreplaceable
`com.amazon.webview.chromium` of undisclosed version; and any user with Play auto-update
off.

> **Realistic floor for 2026:** with `minSdkVersion 29` (~91 % of the base), assume
> **Chromium ≥ M138** as a hard, data-backed floor and design for ~M145+ typical.
> WebCodecs (M94) is 44 milestones below that, so it is unconditionally present — including
> `VideoFrame.rotation` (M138) and per-frame quantizer (M117). **But do not infer presence
> from the milestone**: gate on `isConfigSupported()` with the exact codec strings and
> resolutions, fall back to the server, and log the outcome with
> `navigator.userAgentData.brands` — per the paragraph above, your own analytics is the only
> place WebView-version distribution for your users exists. From Android 17 the WebView
> default UA is frozen, so UA parsing is not a substitute.

### 1.6 Decode: the input side is the real constraint

Phone and camera video is frequently HEVC, and sometimes 10-bit HEVC. **Chromium has no
software HEVC decoder on any platform** — HEVC decode is platform/hardware only — so a
device without an HEVC decoder simply fails rather than falling back.

| platform | HEVC 8-bit (Main) | HEVC 10-bit (Main 10) |
|---|---|---|
| Chrome / Edge Windows | yes, WebCodecs from **107.0.5272.0** | yes, from **108.0.5343.0** (M108, 2022-11-29) |
| Chrome macOS | yes, M107+ (macOS 11+) | yes, 108+ |
| Chrome Android / **Android WebView** | yes, M107+ (WebView listed explicitly in the chromestatus entry) | yes where `MediaCodec` exposes a Main 10 decoder — most modern SoCs do |
| Chrome ChromeOS / Linux | conditional (Linux from 108.0.5354.0) | conditional |
| Edge / WebView2 | yes where the OS provides HEVC | yes, same |
| Safari / WKWebView | yes from **17.4** (2024-03-05), `hvc1` and `hev1` | yes — VideoToolbox handles Main 10 and WebKit passes the codec string straight through with no bit-depth gate (**inferred from the routing**, no explicit Apple statement found) |
| Firefox | weak: hardware decode Windows 134, macOS 136, Linux/Android 137 | **broken** — "supported, but currently Firefox won't render them properly" |
| Firefox Android | no WebCodecs at all | — |

Safari is the strongest HEVC platform of the lot; the third-party field dataset puts HEVC
decode at ~91 % of macOS and ~86 % of iOS sessions against ~81–86 % on Chrome — but that
source (`webcodecsfundamentals.org`, Jan–Mar 2026, Zenodo record 19187467, 363 M probes
over 1.14 M sessions) is self-declared as skewed toward users of a video-editing site and
measures browser sessions rather than WebViews, so treat the percentages as shape, not
planning numbers.

Three hard facts:

- **HEVC decode requires a `description`** (`hvcC`) on every engine. Omitting it produces
  the misleading error *"A key frame is required after configure() or flush()"*
  ([w3c/webcodecs#867](https://github.com/w3c/webcodecs/issues/867)). And **validate the
  bytes** — a zero-size description crashes WebKit's GPU process (bug 308901, open).
- **Decoder output order was wrong on Safari until very recently.** H.264 reordering was
  fixed in Safari **26.4** (2026-03-24, bug 287516); HEVC reordering is a separate fix,
  bug 311324, landing in the **26.5 / 27** window. **Sort decoder output by `timestamp`
  yourself** unless you can require Safari ≥ 26.4 / ≥ 26.5.
- **A 10-bit source will normally have to become 8-bit.** You cannot re-encode to 10-bit
  HEVC on Android (Main, 8-bit only), nor on Linux/ChromeOS (no HEVC encode at all);
  Chromium's libaom AV1 path is 8-bit only. VP9 profile 2 is the one 10-bit option libvpx
  offers. Separately, a `VideoFrame` whose `format` is null — which 10-bit HDR content
  produces — makes mediabunny's `VideoSample.allocationSize()` throw `NotSupportedError`
  ([mediabunny#256](https://github.com/Vanilagy/mediabunny/issues/256)). Two of the sample
  clips I probed on this host are 4K60 **HEVC Main 10** (Appendix A).

Other decode paths, for completeness: AV1 decode is effectively universal on Chromium via
dav1d but only ~24 % of macOS and ~33 % of iOS Safari sessions, because Apple gates it on
hardware (A17 Pro / M3+ / M4 iPad Pro). VP9 decode is ~99.99 % on Chromium; on Safari it is
hardware-only where `VTIsHardwareDecodeSupported(VP9)` holds — **Intel Macs with
Kaby-Lake-class iGPUs, not Apple Silicon, not iPhone or iPad** — otherwise libvpx software.

And a device-class cliff, not a platform one: mediabunny
[#445](https://github.com/Vanilagy/mediabunny/issues/445) reports an **iPhone 13 failing to
decode a 4096×1974 MP4 while an iPhone 13 Pro Max on the same iOS 26.5.2 succeeds**
(`EncodingError: Decoding task did not complete`). Capability probing will not predict
this; only a graceful fallback handles it.

---

## 2. Demux and mux

The client must read an MP4/MOV (often HEVC, sometimes with edit lists, timecode tracks,
rotation matrices and PCM audio) and write something a browser and both apps can play.
Neither half exists in the repo today.

### 2.1 The options, measured

Sizes measured from jsDelivr and the npm registry on 2026-09-12 (`gzip` = actual
`Accept-Encoding: gzip` transfer size where available):

| library | version / date | licence | raw | gzip | demux | mux | WASM |
|---|---|---|---|---|---|---|---|
| **mediabunny** | 1.56.2, 2026-09-12 | MPL-2.0 | 657 KB full bundle | **173 KB** full; **~95 KB** for the MP4-in/MP4-out conversion path; ~81 KB hand-rolled | yes | yes | **none** |
| mp4box.js (`mp4box`) | 2.4.1, 2026-06-19 | BSD-3-Clause | 158 KB (`all.min.js`) | 33 KB | yes, excellent | technically, practically no | none |
| mp4-muxer | 5.2.2, 2025-07-02 | MIT | 31 KB | 9 KB | no | MP4 only | none |
| webm-muxer | 5.1.4, 2025-07-02 | MIT | 30 KB | 8 KB | no | WebM only | none |
| **ffmpeg.wasm** `@ffmpeg/core` | 0.12.10, 2025-01-07 | **GPL-2.0-or-later** | **30.7 MB** | **10.29 MB** (brotli 9.29 MB) | yes | yes | yes |
| libav.js `variant-webcodecs` | 6.10.9, 2026-08-18 | LGPL-2.1 | 2.34 MB wasm | 905 KB | yes | yes, with stream copy | yes |
| web-demuxer (bilibili) | 4.0.0, 2025-12-20 | MIT wrapper / LGPL core | 823 KB (`mini`) | ~493 KB | yes | **no** | yes |
| GPAC `gpac.lite.wasm` | nightly 2026-09-10 | LGPL-2.1 | 1.87 MB | — | yes | yes, `store=fstart` | yes |
| @remotion/webcodecs | 4.0.524, 2026-09-12 | **Remotion License** | — | — | yes | MP4/WebM/WAV | none |
| WebAV | `@webav/av-cliper` 1.2.8 | MIT | — | — | yes | fMP4 | none |

For scale: the repo's `dist/bundle.js` is 4.46 MB and `videoRecorderWorker.js` is 716 KB,
so a ~95 KB gzipped addition in a dedicated worker is negligible and a 10 MB wasm is not.

### 2.2 mediabunny is the clear answer

Version 1.56.2 published 2026-09-12; v1.0.0 was 2025-07-02; 181 npm versions, 12 in the
last 30 days; 7,131 stars; ~3.12 M weekly downloads against ~571 k for `@ffmpeg/ffmpeg`.
Licence MPL-2.0, with the README stating it is free for closed-source commercial use and
the obligation limited to publishing modifications of mediabunny's own files.

Why it fits this problem specifically:

- **Audio passthrough is the default, not a feature you configure.** From the conversion
  guide, verbatim: *"Mediabunny differs from FFmpeg in that it will always try to perform a
  copy conversion by default, if the config permits."* `Conversion.init({ input, output,
  video: { codec: 'avc', width: 1280, bitrate: 2e6 } })` re-encodes video and copies the
  AAC track untouched. `copy` takes `'preferred'` (default), `'forced'` or `false`.
- **It tells you in advance what it cannot do.** After `init()` and before `execute()`,
  `conversion.discardedTracks` / `utilizedTracks` carry machine-readable reasons
  (`unknown_source_codec`, `no_encodable_target_codec`, `discarded_by_user`). That is
  exactly the signal needed to decide "fall back to the server" without burning a
  transcode first. `canEncodeVideo`, `canDecodeVideo`, `getFirstEncodableVideoCodec` cover
  the capability half.
- **faststart is a first-class option.** `Mp4OutputFormat({ fastStart })` takes `false`
  (moov at end, cheapest), `'in-memory'` (compact, needs the whole file in RAM),
  `'reserve'` (requires a correct `maximumPacketCount` up front or the conversion fails),
  or `'fragmented'`. Sources include `BlobSource` (random access over a `File`, no full
  load); targets include `StreamTarget` with backpressure.
- **Rotation is handled properly** — it reads the `tkhd` matrix and lets you either write
  rotation metadata or bake the rotation into the frames. That matters for portrait iPhone
  video, which downstream players routinely mis-handle.
- **The formats list is broad**: MP4/M4V/M4A, MOV, MKV/WebM, MPEG-TS, Ogg, MP3, WAV, AAC,
  FLAC, plus HLS and CMAF output; video `avc`, `hevc`, `vp8`, `vp9`, `av1`. Everything
  except PCM goes through WebCodecs, so availability equals platform availability.

Known gaps, all open issues:

| issue | what |
|---|---|
| [#444](https://github.com/Vanilagy/mediabunny/issues/444) | **AAC encoder delay / priming not compensated** on MP4 export → A/V sync drift. Avoided entirely by copying audio instead of re-encoding. |
| [#447](https://github.com/Vanilagy/mediabunny/issues/447), [#492](https://github.com/Vanilagy/mediabunny/issues/492), [#494](https://github.com/Vanilagy/mediabunny/issues/494) | **No edit list written**; MP4/MOV presentation transforms not fully exposed. iPhone recordings commonly carry edit lists. |
| [#356](https://github.com/Vanilagy/mediabunny/issues/356) | audio transcode introduces a fixed start offset via `Conversion` |
| [#431](https://github.com/Vanilagy/mediabunny/issues/431) | no HDR static metadata |
| [#423](https://github.com/Vanilagy/mediabunny/issues/423) | AV1 sync samples can be written without a Sequence Header OBU |
| [#285](https://github.com/Vanilagy/mediabunny/issues/285) | memory bloat with `VideoSampleSink` + `OffscreenCanvas` |
| [#256](https://github.com/Vanilagy/mediabunny/issues/256) | `allocationSize()` throws when `VideoFrame.format` is null — i.e. 10-bit HDR |
| [#445](https://github.com/Vanilagy/mediabunny/issues/445) | iPhone 13 fails a 4K decode that an iPhone 13 Pro Max completes |

**One gap found by reading the shipped code, not the issue tracker.** The demuxer in
`dist/modules/src/isobmff/isobmff-demuxer.js` of 1.56.2 recognises the sample entries
`avc1`, `avc3`, `hvc1`, `hev1`, `av01`, `vp08`, `vp09` — and **not `dvh1`/`dvhe`**. Apple's
*HDR Metadata for Apple Devices* specifies that Dolby Vision content on Apple devices
*shall* use the `dvh1` codec type, and iPhone 12 and later record Dolby Vision Profile 8.4
HDR **by default**. This is an inference, not a tested result, and it is the single
highest-risk unknown in any plan built on mediabunny. It is a ten-minute check with one
real iPhone clip. `mp4box@2.4.1` does know `dvh1`, and `web-demuxer-mini` is another
escape hatch, so a hybrid is possible if it fails.

> **Round 2 update — tested, and this paragraph is half wrong.** The `dvh1`/`dvhe` gap is
> real and mediabunny refuses such a track, but **it fails loudly and safely**, and the
> shape an iPhone actually writes for Profile 8.4 is `hvc1` + `dvvC`, which mediabunny
> handles perfectly. Predicted in [§R1](#r1-the-dolby-vision-test-run) from synthetic files,
> then **confirmed against a real iPhone 13 Pro recording** in [§R8](#r8-the-prediction-held).
> This paragraph's concern does not apply to iPhone camera footage.

Also: **bus factor 1.** Vanilagy has 1,147 of ~1,350 commits; the next human contributor
has 25. Mitigating facts: MPL-2.0 with no WASM means the code is readable and forkable,
every iOS-specific issue listed above is *closed*, and Remotion is migrating onto it and
contributing.

A useful precedent: **`@bsky.app/video-compressor` 0.2.0** (2026-07-23, MIT) — Bluesky's
package whose only runtime dependency is mediabunny, described as *"iOS + Android use
VideoToolbox / MediaCodec; web uses WebCodecs via mediabunny."* A social app with this
exact problem making the split this study recommends.

### 2.3 mp4box.js — good demuxer, not a muxer

Actively maintained (last commit 2026-08-30, eight releases since the TypeScript rewrite
in August 2025, PRs from Google), BSD-3-Clause, and small: 41 KB gzipped bundled from
`mp4box`, 30 KB from the parse-only `mp4box/simple` subpath. Its box registry covers
`hvc1`, `hev1`, `hvcC`, `av01`, `av1C`, `vp09`, `dvh1`, `OpusSampleEntry` — notably
including Dolby Vision.

The muxing side is technically present (`createFile`, `addTrack`, `addSample`, `save`) and
practically unusable for this job: `IsoFileOptions` accepts only `avcDecoderConfigRecord`
and `hevcDecoderConfigRecord`, so muxing AV1 means hand-building an `av1C` box; there is no
faststart utility, no audio-copy helper, no edit-list or priming handling. Issue #43,
"Write as well as read", has been open since **2015**.

### 2.4 ffmpeg.wasm — the obvious answer, and wrong on four independent axes

**Abandoned in practice.** Last release v0.12.15, 2025-01-07. Last code commit to `main`
is `f876f90`, 2025-09-17, a README edit — which removed the "this project is looking for
maintainers" line. 38 open PRs, including one-line fixes for live OOM bugs
([#948](https://github.com/ffmpegwasm/ffmpeg.wasm/pull/948)). Issue
[#939](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/939) "Is this project maintained?"
(2026-04-10) has no maintainer reply. FFmpeg itself is frozen at **5.1.4** (June 2023)
because, per the Dockerfile, "We cannot upgrade to n6.0 as ffmpeg bin only supports
multithread at the moment."

**It is a GPL build, verified from the shipped binary.** The configure string embedded in
`@ffmpeg/core@0.12.10/dist/esm/ffmpeg-core.wasm` reads `--enable-gpl --enable-libx264
--enable-libx265 --enable-libvpx --enable-libmp3lame …`. Both `@ffmpeg/core` and
`@ffmpeg/core-mt` declare `"license": "GPL-2.0-or-later"`; the MIT on `@ffmpeg/ffmpeg` is a
72 KB wrapper that does nothing alone. An LGPL rebuild has **no H.264 encoder** — FFmpeg
ships none natively, libx264 is GPL, and libopenh264's royalty grant covers only Cisco's
own prebuilt binaries. Whether fetching a GPL `.wasm` at runtime constitutes a combined
work is genuinely unsettled and is a question for counsel, not engineering
([#902](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/902), open and unanswered since
2025-09-19). Note also: there is no AV1 encoder in the build at all (no libaom, no
libsvtav1).

**Download cost:** 30.74 MB raw, **10.29 MB gzipped**, 9.29 MB brotli (measured here). The
`toBlobURL()` pattern in every official example defeats HTTP caching unless you add a Cache
API layer yourself; the fix ([PR #940](https://github.com/ffmpegwasm/ffmpeg.wasm/pull/940),
by a Google engineer, 2026-06-05) is unmerged. Both cores require `+simd128 +atomics
+bulk-memory`, so SIMD is mandatory even single-threaded — a floor of Chrome 91 / iOS 16.4.

**Speed:** the project's own benchmark (i5-1135G7, Chrome 116, 5 runs, 720p VP8→H.264, 10 s
clip) gives native 5.2 s, single-thread **128.8 s**, multithread **60.4 s** — i.e.
**0.078× realtime single-threaded, 0.166× multithreaded**, and 24.8×/11.6× slower than
native. Independent desktop measurements range 25 fps at 1080p (BurnSub, 2018 MacBook Pro,
100 % CPU on all cores, >2 GB RAM) to "barely 30 fps" at 720p.

**Threading is unavailable where it matters.** `core-mt` needs `SharedArrayBuffer`, which
needs cross-origin isolation — and §2.6 shows that is architecturally impossible in Android
System WebView and unreachable through a custom scheme in WKWebView. `core-mt` also
pre-allocates exactly 32 pthread workers at load, hardcodes x264 to 4 threads (the build
uses a patched `x264` branch `4-cores`), and is widely reported to hang on Chromium and
Safari ([#772](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/772), eight confirmations).
npm downloads vote 15:1 against it (ST 189 k/week vs MT 12 k/week).

**Memory, parsed from the published wasm:** ST initial 32 MB, maximum 2048 MB, growth
allowed; **MT fixed at 1024 MB with no growth** (min == max). Consequence
([#946](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/946), 2026-09-09, open):
`Aborted(OOM)` decoding a *single* 4K H.264 frame, and in headless Chromium it hangs
silently at `frame=1` rather than erroring, so CI will not catch it. Memory is also not
reclaimed between `exec()` calls ([#563](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/563),
[#820](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/820)). Input *can* stream via
WORKERFS (`ffmpeg.mount('WORKERFS', …)`); **output cannot** — it always lands in MEMFS and
`readFile()` copies it, so peak ≈ output × 2.

**On iOS it falls over on exactly our file sizes.** [#851](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/851)
(2025-03-17): *"crashes while trying to load the initial bigger (500MB+) file … Tried using
WORKERFS for input file but it still crashes."* [#482](https://github.com/ffmpegwasm/ffmpeg.wasm/issues/482)
(open since 2023): *"not working for files bigger than 200MB in safari IOS."* And there are
**zero issues in the repo mentioning "WebView"** — for a hybrid app that is an unknown,
not a known-good.

**Verdict: not viable here.** Its remaining justification is exotic containers and complex
filter graphs, not throughput.

### 2.5 libav.js and GPAC — the LGPL escape hatches

`libav.js` 6.10.9 (2026-08-18, LGPL-2.1, sole maintainer, 0 open issues) ships a
`variant-webcodecs` built for exactly this architecture: *"Designed to serve as a
demuxer/muxer for codecs supported by WebCodecs. Includes parsers (but not codecs) for AAC,
VP8, VP9, AV1, H.264, and H.265. Includes the ogg, WebM, MP4, FLAC, and wav formats."*
905 KB gzipped. Stream copy works (demux → feed the same packets back, preserving the
original `AudioSpecificConfig`); `movflags=faststart` works via `av_dict_set_js` provided
you write to a seekable device. A custom "MP4 demux+mux only, no codecs" variant would be
~1.15 MiB of wasm.

Two documented gotchas from `libavjs-webcodecs-bridge`: you must convert at least one chunk
per stream to a packet *before* starting the muxer (WebCodecs delivers extradata with the
first chunk); and *"FFmpeg and WebCodecs disagree on the definition of keyframe with both
H.264 and H.265"* — FFmpeg takes keyframe status from the container, which marks recovery
frames as keyframes.

GPAC's `gpac.lite.wasm` (1.87 MB, nightly only, no npm package, LGPL-2.1) is configured
almost as a description of this requirement — `--isomedia-only --enable-reframer
--enable-mp4mx --enable-mp4dmx --enable-webcodec` — and `mp4mx`'s `store=fstart` puts moov
before mdat. Worth knowing about; not a first choice while it has no package.

**Relevant to this repo:** the vendored libav.js build is the `vp9-opus-avf` variant, which
as §0.5 establishes has neither an MP4 demuxer nor an MP4 muxer. Switching to
`variant-webcodecs` would roughly halve the wasm (3.85 MB → 2.34 MB) *and* add MP4 support,
but it would break the VP9-encoder-for-Firefox reason the custom build exists. Two variants
is ~3 MB of assets. mediabunny's 95 KB of JavaScript is a better trade.

### 2.6 Cross-origin isolation is not available, and not needed

This closes off every multithreaded-WASM option regardless of which library you pick.

**Android System WebView cannot be cross-origin isolated.** Five independent primary
sources agree, and the root cause is that WebView has a single renderer process:

- Chromium's process-model doc: *"Android WebView does not yet support multiple renderer
  processes or out-of-process iframes."*
- The Chromium Site Isolation page: *"Site Isolation is not yet supported in Android
  WebView."*
- [whatwg/html#6060](https://github.com/whatwg/html/issues/6060), Arthur Sonzogni (Chrome
  security), 2020-10-14: *"Some platforms can't easily support multiple processes (like
  Android Webview). Therefore, they can't really support crossOriginIsolated."* The
  resolution was that WebView would **enforce** COEP without granting the capability.
- MDN BCD records `webview_android: false` for `SharedArrayBuffer`, corrected in response
  to a bug report from a real WebView app.
- The Chrome 137 `Document-Isolation-Policy` Intent to Ship (2025-04-16): *"We have no
  plans on launching the feature in Android WebView in the foreseeable future due to lack
  of process isolation in Android WebView."*

You *can* attach COOP/COEP headers via `shouldInterceptRequest`, and it grants nothing. A
custom scheme makes it strictly worse, since cross-origin isolation needs a secure context
and `app://` is not a potentially-trustworthy origin.

**In WKWebView it depends on the origin, and a custom scheme fails.** WebKit's
`SharedArrayBuffer` gate is embedder-agnostic ([bug 229559](https://bugs.webkit.org/show_bug.cgi?id=229559),
fixed 2021-08-31): seeing COOP+COEP, the UIProcess spins up a fresh never-reused
WebContent process with `CrossOriginMode::Isolated`. But with a `WKURLSchemeHandler`
scheme — which is how this app is served on Apple platforms — the handler's default
headers apply to subresources and **not to the initial app-launch document**, so the top
level never carries COOP/COEP ([ionic-team/capacitor#6182](https://github.com/ionic-team/capacitor/issues/6182);
[discussion #7553](https://github.com/ionic-team/capacitor/discussions/7553) unanswered
after two years). The workaround people land on is a local HTTP server.

**And BlazorWebView gives no header plumbing anyway.** Its origin is `https://0.0.0.1/`
since [dotnet/maui#24884](https://github.com/dotnet/maui/pull/24884) with no COOP/COEP
support. (That origin *is* a secure context, so WebCodecs itself is not blocked by the
scheme.)

**On the web, enabling it would cost real things.** `COEP: require-corp` blocks
cross-origin subresources that do not send `CORP: cross-origin`; `COOP: same-origin`
severs `window.opener`, which is the documented cause of blank OAuth popups — Google is
explicit that Sign In With Google needs `COOP: same-origin-allow-popups`. Third-party
iframes must themselves opt in, which YouTube embeds and reCAPTCHA will not. `COEP:
credentialless` relaxes only `no-cors` subresources, does not fix iframes, and — per MDN
BCD read 2026-09-12 — **is not supported in Safari, desktop or iOS, and StackBlitz reports
the WebKit team does not plan to implement it.** `COOP: restrict-properties` was put on
hold in April 2025.

**Conclusion:** do not enable cross-origin isolation. It is architecturally impossible on
Android WebView, unreachable through the app's custom scheme on Apple, has no Safari
`credentialless` path, and costs OAuth popups and third-party frames on the web. Its only
purpose is multithreaded WASM, which the WebCodecs path does not need.

---

## 3. The audio question

The brief asks to keep the original audio track where possible. The constraint that
answers it is the container.

### 3.1 What each container permits

Verified locally with this host's ffmpeg muxers (Appendix A):

| output | AAC passthrough | Opus | PCM |
|---|---|---|---|
| **MP4**, H.264 / HEVC video | **yes** | allowed by spec, support uneven | **no** |
| **MP4**, AV1 video | **yes** (verified: `av01` + `mp4a` in one file) | uneven | no |
| **MP4**, VP9 video | **yes** (the muxer accepts it) | uneven | no |
| **WebM**, VP9 / AV1 / VP8 video | **no** | **yes** | no |

The WebM refusal is explicit:

```
[webm] Only VP8 or VP9 or AV1 video and Vorbis or Opus audio and WebVTT
       subtitles are supported for WebM.
Could not write header for output file #0 (incorrect codec parameters ?)
```

And PCM into MP4 — the Sony MOV case in the sample set — is equally explicit:

```
[mp4] Could not find tag for codec pcm_s16be in stream #1,
      codec not currently supported in container
```

### 3.2 What that implies

Phone video is overwhelmingly **AAC-LC in MP4/MOV** — all three AAC-bearing samples I
probed are AAC-LC at 127–137 kbps (Appendix A). So:

1. **"Keep the original audio" forces MP4 output.** AAC cannot go into WebM at all. That
   is not a preference; it is a muxer-level prohibition.
2. **MP4 forces a video-codec decision.** H.264 and HEVC in MP4 are universally supported.
   VP9 and AV1 in MP4 are spec-legal and the muxers accept them, but playback support is
   uneven — which means the stated goal "AV1 where available, VP9 otherwise, keep the
   original audio" produces either an MP4 whose video many players will not decode, or a
   WebM whose audio must be re-encoded to Opus. **The two halves of the brief are in
   tension, and the audio half should win.**
3. **On iOS 16.4 – 18.x WKWebView, passthrough is the only option.** There is no
   `AudioEncoder` before Safari 26 (§1.1). A pipeline that needs to re-encode audio simply
   cannot run there.

### 3.3 When audio cannot be kept

| source audio | what must happen |
|---|---|
| AAC-LC in MP4/MOV (the common case) | copy, byte for byte |
| AAC-HE | copy; Signal's transcoder passes HE-AAC through untouched, so this is normal |
| **PCM** (`pcm_s16be`, `twos`) — prosumer cameras | must re-encode. Needs an `AudioEncoder` → **impossible on iOS < 26 in the WebView** → fall back to the server |
| Opus in a MOV/MP4 | copy if the muxer allows; otherwise re-encode |
| multi-track, or AC-3 / DTS | pick the primary track, or fall back |
| audio must be resampled or volume-adjusted | re-encode — avoid; there is no reason to do either here |

Two further reasons to prefer passthrough over re-encoding even where both are possible:

- It is **lossless and fast** — no decode, no encode, no quality loss, and at 128 kbps the
  audio is ~1 MB/minute, so re-encoding saves almost nothing.
- It sidesteps mediabunny [#444](https://github.com/Vanilagy/mediabunny/issues/444), the
  uncompensated AAC priming delay that causes A/V sync drift on MP4 export.

If audio ever must be re-encoded in the WebView, `@mediabunny/aac-encoder` (libavcodec,
MPL-2.0) supplies an AAC encoder independent of the platform — that is the fix for
iOS < 26, at the cost of a WASM asset.

### 3.4 The metadata that travels with the audio

Three container-level details decide whether the output plays correctly, and all three are
easy to lose:

- **Edit lists.** iPhone recordings routinely carry them; mediabunny does not write one
  ([#447](https://github.com/Vanilagy/mediabunny/issues/447)). Dropping an edit list shifts
  A/V sync.
- **Rotation.** Portrait phone video carries a `tkhd` display matrix, and **WebCodecs
  decoders do not apply it** — `VideoFrame.rotation` and the `VideoDecoderConfig`
  orientation fields only arrived in Chrome 138 (2025-06-24) and exist in neither Firefox
  nor Safari. So an unhandled portrait iPhone clip comes out sideways. mediabunny reads the
  matrix and can either re-emit it or bake the rotation into the frames; **prefer baking**,
  since many players ignore the metadata and three different "dimensions" (`codedWidth`,
  `displayAspectWidth`, `width`) are easy to confuse. (None of the samples I probed still
  carried a rotation matrix, because the iPhone ones had been remuxed — so treat this as a
  hazard to assume rather than one these samples prove.)
- **faststart / moov-at-front.** The server's ffmpeg path sets `WithFastStart()` today.
  A client-muxed file with moov at the end still plays, but progressive download and the
  server's ffprobe-based analysis both prefer moov first.

---

## 4. Performance and battery

### 4.1 Software encoding is not a viable path; the numbers say so twice

**Published (measured):** ffmpeg.wasm's own benchmark gives 0.078× realtime
single-threaded and 0.166× multithreaded for a 720p transcode on an i5-1135G7. Scaling to
1080p by pixel count gives ~0.07× multithreaded — **~14 minutes for a 1-minute clip on a
laptop**, ~29 minutes single-threaded. The most favourable independent desktop measurement
is 25 fps at 1080p (0.83× realtime) on a 2018 MacBook Pro with all cores pegged and >2 GB
of RAM.

**Measured here (Appendix A), single-threaded native on a fast 32-thread desktop** — the
ceiling any WASM build must sit below:

| job, 30 s of 1080p30 | wall | × realtime |
|---|---|---|
| decode only, 4K HEVC source, 1 thread | 26 s | **1.15×** |
| decode only, 1080p H.264, 1 thread | 7 s | 4.3× |
| x264 `veryfast`, 1 thread, 2.5 Mbps | 16 s | 1.9× |
| x264 `ultrafast`, 1 thread | 4 s | 7.5× |
| libvpx-vp9 realtime `cpu-used 8`, 1 thread | 15 s | 2.0× |
| libvpx-vp9 realtime `cpu-used 5`, 1 thread | 35 s | 0.86× |
| libaom-av1 realtime `cpu-used 8`, 1 thread | 28 s | 1.07× |
| 1080p → 480p, x264 `veryfast`, 1 thread | 4 s | 7.5× |

The decisive row is the first. **Software 4K HEVC decode alone is barely faster than
realtime on one core of a fast desktop CPU.** A phone core is roughly 2.5–4× slower, and a
WASM build 1.5–3× slower again, so decoding a 4K HEVC phone clip in software on a phone
is 5–15× slower than realtime before any encoding happens. The sample set on this host
(Appendix A) is full of exactly that content. **Whatever else is true, the decode must be
hardware**, which means WebCodecs `VideoDecoder`, not a WASM decoder.

480p is the one place software is comfortable (7.5× realtime single-threaded native, so
plausibly ~1–2× realtime in WASM on a phone), which is worth remembering if a 480p-only
fallback is ever wanted.

### 4.2 Hardware encoding is fast enough that it is not the bottleneck

From the repo's own device measurements (`codec-performance.md`, 2026-08-30/31), encode
time only, steady state, warm encoder:

| device | codec | 720p ms/frame | → fps | derived 1080p fps | → 1-min clip |
|---|---|---|---|---|---|
| Galaxy SM-S948U1 (WebView 151) | hw H.264 | 2.43 | 412 | ~190–410 | 4–9 s |
| Galaxy SM-S948U1 | hw HEVC | 2.55 | 392 | ~180–390 | 5–10 s |
| iPhone 13 Pro (WKWebView) | hw VP9 | 3.88 | 258 | ~115–255 | 7–16 s |
| iPhone 13 Pro | hw HEVC | 4.97 | 201 | ~90–200 | 9–20 s |
| Chrome / Windows (RTX 3090) | hw AV1 | — (1080p measured: 2.18) | 459 | 459 | 4 s |

The 1080p column is **derived**: `codec-performance.md` documents that encode time scales
*sub*-linearly with pixels on the hardware paths, so the honest answer is a band between
frame-rate-limited (unchanged fps) and pixel-limited (÷2.25).

Independent corroboration for Android, from native `MediaCodec` rather than WebCodecs:
Arunruangsirilert & Katto, [arXiv:2511.18686](https://arxiv.org/abs/2511.18686)
(2025-11-24), Table VIII, real phones at **4K**: Snapdragon 888 43 fps H.264 / 48.6 HEVC;
8 Gen 1 47/46; 8 Gen 2 56.3/63.2. Converting 4K fps to 1080p30-equivalents gives
**5.7–8.4× realtime** on those flagships.

**Mid-range is where it gets interesting, and the best measured anchor is Google's own:**
*"a one minute HEVC video file takes roughly 20 seconds to transcode into AVC on a Pixel 3
phone"* ([Android compatible media transcoding docs](https://developer.android.com/media/platform/transcoding))
— **3× realtime** on a 2018 flagship, roughly today's mid-range. SoC specs bound it from
above: a Dimensity 700 caps encode at 2K30 = 110 Mpix/s, a hard **~1.8× ceiling** at
1080p30; Dimensity 900 and Snapdragon 6 Gen 1 cap at 4K30, so ~4×.

> **Estimate: mid-range Android, 1080p30, hardware: 1.5–3× realtime, so a 1-minute clip
> takes 20–40 s. Flagship: 4–8×, so 8–15 s.**

For iPhone via `AVAssetExportSession` **no published measured number exists** — a genuine
gap. Bounds: iPhone 15 Pro records 4K60 HEVC in realtime while running the whole camera
pipeline, and 4K60 is 8× the pixel rate of 1080p30; same-family desktop engines exceed
200 fps at 1080p in HandBrake VideoToolbox. **Estimate 5–15× realtime, i.e. 4–12 s for a
1-minute clip** — worth one afternoon of instrumentation, because it calibrates everything
below.

Two pieces of friction to subtract from the hardware numbers:

- **The WebCodecs binding layer costs roughly 3×** — measured in
  [w3c/webcodecs#492](https://github.com/w3c/webcodecs/issues/492): 4K H.264 at ~25 fps
  through a hardware `VideoEncoder` versus 65–70 fps native ffmpeg + VideoToolbox on the
  same machine. One desktop measurement, 2023-era; unverified on mobile.
- **Silent hardware→software fallback** with no detection hook (same issue). A 5–10×
  slowdown with no error.

### 4.3 Codec choice at software speeds — measured here

Equal target bitrate (1.8 Mbps), 1080p, 10 s, single thread, VMAF against the source.
Corrected numbers; see the methodology warning in Appendix A.

| encoder | encode time | actual bitrate | VMAF |
|---|---|---|---|
| x264 `ultrafast` | 1 s | 1784 kbps | 66.6 |
| x264 `veryfast` | 5 s | 1767 kbps | 73.6 |
| x264 `medium` | 12 s | 1745 kbps | 80.4 |
| x265 `veryfast` | 15 s | 1711 kbps | 84.2 |
| libvpx-vp9 realtime `cpu-used 8` | 4 s | 2064 kbps | 83.0 |
| libvpx-vp9 realtime `cpu-used 5` | 10 s | 2042 kbps | 86.0 |
| libaom-av1 realtime `cpu-used 8` | 8 s | 1787 kbps | 86.0 |
| libaom-av1 realtime `cpu-used 6` | 37 s | 1796 kbps | 88.3 |

One 10-second clip, one content type, a reference that is itself a CRF-18 re-encode — so
treat the ordering as indicative and the absolute values as meaningless. What it does
support: **at the speeds a software encoder must run to be usable, VP9 and AV1 still beat
x264 at comparable bitrate** (VP9 realtime `cpu-used 8` matches x264 `veryfast`'s speed at
+9.4 VMAF and 17 % more bits; libaom realtime `cpu-used 8` beats it by 12.4 VMAF at the
same bitrate and twice the time). The codec-efficiency literature is not invalidated by
fast presets. It just does not matter here, because §4.1 rules software encoding out on
throughput and §1.2 rules hardware AV1/VP9 out on availability.

### 4.4 Thermal and battery

- **Hardware encode power (measured):** Reddy, Herglotz & Kaup,
  [arXiv:2510.12754](https://arxiv.org/abs/2510.12754) (2025-10-14), power-meter
  instrumented, ARM-class Jetson Orin NX hardware encode block: ~12–17 J per ~130 frames
  of 1080p ⇒ ~0.115 J/frame ⇒ **~3.5 W at 30 fps for the encode block**. 720p ~1.6–2.0 J,
  4K ~20 J. Resolution dominates energy far more than codec or preset. A Jetson block is
  not a phone block, so this is directional.
- **Software versus hardware, system level (measured):** arXiv:2511.18686 Table VII,
  i7-13700H at 1080p60: QSV H.264 **28.2 W** / HEVC 29.4 W versus libx264 `slow`
  **85.3 W** / libx265 `faster` 93.5 W. **Software ≈ 3× the system power.**
- **Does a ~1-minute job throttle?** Sahin & Coskun, ESTIMedia 2016, on a real Nexus 5
  running an H.264 encode workload: **37.3 s to hit the 40 °C skin limit, 48.1 % QoS loss**;
  over a 200 s session max CPU frequency fell 2.2 → 1.2 GHz and fps 34 → 20. So a
  *software* encode throttles inside one minute. A hardware encode finishing in 10–40 s at
  2–6 W largely escapes it — but back-to-back transcodes of ten attachments will not.
  (A circulating "iPhone 15 Pro throttles after 4.2 minutes" figure traces to no primary
  source; disregard it.)
- **Uplink radio energy (measured):** Narayanan et al., SIGCOMM 2021, Monsoon power monitor
  on real networks: Galaxy S20U **4G uplink 80.21 mW/Mbps** (5G low-band 29.15, mmWave
  9.42); uplink costs 2.2–5.9× more per Mbps than downlink; RRC tail power 66–178 mW on 4G,
  ~1092 mW on mmWave.

> **Derived battery verdict.** 80.21 mW/Mbps ⇒ 0.08 J/Mbit ⇒ **0.64 J/MB** on LTE uplink.
> Uploading 105 MB ≈ 67 J; uploading 27 MB ≈ 17 J; so transcoding saves ≈ **50 J of radio
> energy**. A hardware transcode at 2–6 W for 10–40 s costs **20–240 J**. **On-device
> transcoding is battery-neutral at best and can be net-negative.** For scale, 50 J is
> 0.014 Wh against a ~17 Wh phone battery — 0.08 %, so none of these numbers are large in
> absolute terms. Software WASM encode is unambiguously worse: minutes at ~9 W is >1000 J,
> ~20× the radio saving, plus a guaranteed thermal event.

**Do this for latency, reliability and server cost. Not for battery.**

### 4.5 The bandwidth crossover — the arithmetic that decides the feature

**Correcting the premise first.** A 1-minute 1080p30 phone clip is not 100–200 MB unless
it is H.264. Measured anchors: iPhone 1080p30 HEVC ≈ 10 Mbps ⇒ **~76 MB/min** (Apple's own
in-Settings figure reads ~60); Android 1080p30 H.264 ≈ 13–14 Mbps ⇒ **~100–105 MB/min**;
iPhone 4K30 HEVC ≈ 29 Mbps ⇒ ~215 MB/min; 4K60 ≈ 53 Mbps ⇒ ~400 MB/min. The sample clips I
probed here sit at 30.3 Mbps (4K30 HEVC) and 75–77 Mbps (4K60 HEVC Main 10). 100–200 MB per
minute is 4K territory.

**Upload bandwidth, 2025–2026.** Ookla Speedtest Global Index, July 2026: US median mobile
upload **18.78 Mbps**. Opensignal USA July 2026: T-Mobile 16.0, Verizon 10.9, AT&T 9.5 Mbps
upload. Opensignal Australia May 2026: Vodafone 4G 8.8 versus 5G 15.6 Mbps up. **The bad
case is common, not an edge case**: Ookla's H1 2026 US report found that on T-Mobile — the
best-performing US carrier — only **79.3 % of 5G samples met 25 Mbps↓/3 Mbps↑**, i.e.
roughly **one session in five is under 3 Mbps upload on the best US network**.

**Crossover.** Source 105 MB (1 min 1080p30 H.264 @14 Mbps); target 27 MB (1080p @3.5 Mbps
+ 128 kbps audio); saving 78 MB = 624 Mbit; effective throughput 0.85 × nominal.
Break-even uplink where transcode time `T` exactly eats the saving is
`R = 624 / (0.85·T) = 734/T` Mbps.

| path | `T` for 1 min | break-even uplink | against the 18.78 Mbps median |
|---|---|---|---|
| flagship hardware, ~6× realtime | 10 s | **73 Mbps** | wins by ~4× margin |
| mid-range hardware, ~3× | 20 s | **37 Mbps** | wins by 2× margin |
| weak mid-range hardware, ~1.5× | 40 s | **18 Mbps** | a wash at the median; wins below it |
| WASM software, ~0.2× | 300 s | **2.4 Mbps** | **loses on ~80 % of sessions** |

Wall clock:

| | at 18.78 Mbps | at 3 Mbps | at 1 Mbps |
|---|---|---|---|
| upload the original | 53 s | 329 s | 990 s |
| flagship hardware transcode + upload | **24 s** | **95 s** | **265 s** |
| mid-range hardware | **34 s** | **105 s** | **275 s** |
| WASM software | 314 s | 385 s | 555 s |

And my own size/time table for a 60 s clip (Appendix A) makes the same point from the other
direction: a 4K30 HEVC minute is 227 MB, which is 10 minutes at 3 Mbps and 30 minutes at
1 Mbps.

> **Hardware transcoding wins on every realistic mobile uplink by 1.6–4×, and the margin
> widens exactly where it matters.** Software WASM encoding breaks even at ~2.5 Mbps: it
> loses on roughly 80 % of sessions and wins only on the worst 20 %, where it is still 2–4×
> slower than the hardware path at ~20× the energy. It is not an alternative; it is
> dominated.

A second-order effect worth naming: a 4× byte reduction is also a 4× reduction in exposure
to connection loss. At 3 Mbps, a 105 MB upload is a 5.5-minute window in which
backgrounding, a Wi-Fi↔cellular handoff, or a lift ride kills the transfer.

### 4.6 The server-side alternative, priced

| service | per output-minute, HD |
|---|---|
| GCP Transcoder API (what this repo already uses) | **$0.030** HD, $0.015 SD, $0.060 UHD; renditions bill additively |
| AWS Elemental MediaConvert, basic tier | ~$0.015/min HD AVC, down to ~$0.008 at volume |
| Mux | $0.0075/min encoded, list |
| Cloudflare Stream | encoding free; storage ~$0.005/min/mo + delivery ~$0.001/min |
| self-hosted ffmpeg, x264 `veryslow`, Graviton | $0.0102/min measured ([Streaming Learning Center](https://streaminglearningcenter.com/codecs/best-aws-cpu-for-ffmpeg.html)); `medium` ≈ $0.0013–0.002 (derived) |
| self-hosted NVENC (`g4dn.xlarge`, ~8–10 concurrent realtime 1080p) | ~$0.0009–0.0011/min (derived) |

The best-sourced CPU figure anywhere: Meta measured transcoding a 23-second clip to one
720p rendition at **86.17 seconds of CPU time**, cut to 0.36 s by repackaging
already-computed frames instead of re-encoding
([engineering.fb.com, 2022-11-04](https://engineering.fb.com/2022/11/04/video-engineering/instagram-video-processing-encoding-reduction/))
— i.e. **~225 CPU-seconds per output-minute of 720p** naively, ~500 for 1080p.

So server-side transcoding is cheap in absolute terms and does nothing for the thing that
actually hurts: a user watching a progress bar for 5.5 minutes on a 3 Mbps uplink.

**What comparable apps do** is a clean split:

| app | client-side compression by default |
|---|---|
| WhatsApp | yes — 480p standard / 720p "HD"; ≤64 MB guaranteed deliverable |
| Telegram | yes, with a 480/854/1280/1920 slider, plus an explicit "Send Without Compression" |
| Signal | yes, aggressively, pre-encryption |
| iMessage | yes above a threshold |
| Discord | no by default; hard size caps with an opt-in compression prompt |
| Slack | **no** — stores the exact uploaded bytes |
| Instagram | **no** — uploads the original, optimises the server side |
| Dropbox | **no** — explicitly rejected upfront transcoding as too expensive at scale |

Every 1:1/group *messaging* app compresses on-device, because the sender's uplink is the
bottleneck and they cannot control it. Every *file-sharing* and *media platform* uploads
the original, because their bottleneck is playback economics. **Voxt is in the first
category.** Resumable chunked upload is orthogonal and needed either way.

---

## 5. The native alternative

### 5.1 Android: Media3 `Transformer`

**Latest stable 1.11.1, released 2026-09-10** (1.11.0 2026-08-05, 1.10.1 2026-05-12,
1.9.0 2025-12-17, 1.8.0 2025-07-30). It does decode → optional GL effects → encode → mux on
`MediaCodec` + OpenGL, and every capability the brief needs has a named API: resize via
`Presentation.createForHeight/createForShortSide`; bitrate via
`VideoEncoderSettings.Builder().setBitrate().setBitrateMode()` through
`DefaultEncoderFactory`; codec via `Transformer.Builder().setVideoMimeType()`; audio removal
via `EditedMediaItem.Builder().setRemoveAudio()`; trim via `MediaItem.ClippingConfiguration`.
**Transmux is automatic** when the input format already matches the requested output —
"copying the compressed samples without modification". Output muxers: MP4 (default
`InAppMp4Muxer` since 1.9.0, off the platform `MediaMuxer`), fragmented MP4, WebM, AAC, Ogg,
WAV.

Two settings that exist precisely because of device fragmentation and are easy to miss:

- **`DefaultEncoderFactory.setEnableCodecDbLite(true)`** — `CodecDbLite` (added 1.8.0) is
  Google's chipset-keyed database of encoder-setting overrides, and it is **opt-in, `false`
  by default**. It is the sanctioned mitigation for per-vendor encoder rejection. Turn it
  on.
- **`setEnableFallback(false)`** — by default Transformer **silently falls back to a
  different supported resolution** when the hardware encoder rejects the request. If
  deterministic output dimensions matter, disable it and handle the exception.

**Maturity is good and the long tail is real.** Google's own adoption post (2025-01-10)
reports Google Photos memory-video median latency −41 % high-end / −27 % mid-range,
rotation-save −79 %, trim-save −64 %; 1 Second Everyday "up to 5× faster"; BandLab migrated
off hand-rolled `MediaCodec` in **12 working days** and "all previously observed native
crashes were no longer occurring". Against that: `androidx.media3.transformer` is **still
`@UnstableApi`** (verified in `Transformer.java:84`, `main`, Sept 2026) after three-plus
years — a non-issue from C# bindings, where no Kotlin lint runs. `androidx/media` has 919
open issues repo-wide, 26 open under the `editing` label. Google publishes no failure-rate
statistic; any quoted percentage is unsourced.

The failure modes that will cost time:

- **[#3399](https://github.com/androidx/media/issues/3399)**, filed 2026-08-31: 1.11.0 added
  strict frame-rate validation, and decoders on many devices report `KEY_FRAME_RATE = 0`, so
  **every export throws**. Affected: SM-A075F/A155M/A156E/A055F/A065M, OPPO/Realme/Xiaomi/vivo
  models, Android 15/16. **Pixels and the API-35 emulator are unaffected — CI will not catch
  it.** Fixed on `main` 2026-08-21; 1.11.1 is the likely carrier. Verify the release notes
  and smoke-test a Samsung A-series device regardless.
- **Per-device encoder rejection of specific parameter combinations** is the dominant
  category and is not hypothetical: [#2362](https://github.com/androidx/media/issues/2362)
  (`c2.qti.avc.encoder` throws on a Galaxy Tab A9+ with `Presentation.createForHeight(1080)`,
  and the same device exports *some* videos fine with identical settings);
  [#2751](https://github.com/androidx/media/issues/2751) (Redmi 8A — both hardware and
  software H.264 decoders reject a non-standard 1080×2340 input);
  [#830](https://github.com/androidx/media/issues/830) (`Presentation` combined with
  `VideoEncoderSettings` bitrate). Many close via the stale bot rather than a fix.
- **Decode-side breakage kills transcode too**:
  [#2711](https://github.com/androidx/media/issues/2711) (open) — HEVC *hardware decode*
  produces black frames on MediaTek Dimensity 700/900/1080 after the Android 15 upgrade;
  worked on Android 14. That is a large mid-range population, and HEVC is what phones
  record.
- **HDR**: `HDR_MODE_KEEP_HDR` needs API 33+ and device support; tone-mapping to SDR is
  available from API 29 via OpenGL. [#723](https://github.com/androidx/media/issues/723):
  overlays on HDR content fail.
- **Google's own throughput ceiling statement:** "the limiting factor in Transformer's
  throughput is hardware `MediaCodec` encoder throughput for use cases without heavyweight
  effects processing". **You cannot beat this by writing your own `MediaCodec` code.**

**The alternatives are dead.** `LightCompressor` is archived; `deepmedia/Transcoder` last
pushed 2024-11-05; `ypresto/android-transcoder` 2022-12-20. And the native-ffmpeg escape
hatch closed: **FFmpegKit was retired 2025-01-06 with all prebuilt binaries pulled from
Maven Central, CocoaPods and npm on 2025-04-01.**

**Hardware AV1/VP9 encode on Android, 2026:** Tensor G3 (Pixel 8) had a hardware AV1
encoder that Google never wired to capture; **Tensor G5 / Pixel 10 (Aug 2025) is the first
phone to actually expose hardware AV1 *and* VP9 encode for capture**, via a licensed
Chips&Media WAVE677DV core. **Qualcomm Snapdragon 8 Elite lacks hardware encoders for both
AV1 and VP9**, and Qualcomm said publicly it would skip AV1 encode. MediaTek's Dimensity
9000–9400 AV1-encode status is unconfirmed by any primary source; treat it as absent.

**What the CDD mandates** (Android 14/15/16 CDDs, identical): encoders that MUST be
supported — `[5.2/H-0-1]` H.264, `[5.2/H-0-2]` VP8, `[5.2/H-0-3]` **AV1 since Android 14**.
Decoders — H.264, HEVC, MPEG-4 SP, VP8, VP9, **AV1 since Android 14**. So HEVC encode is
*not* required (only decode) and **VP9 encode is not required at all**. The crucial caveat:
§5.2.6 `[C-2-1]` says *if* the AV1 encoder is hardware-accelerated it must do
1080p@30@16 Mbps — the performance table is conditional on hardware existing, which is how
non-Tensor flagships pass compliance with a *software* AV1 encoder. **`MediaCodec` will hand
you a working-but-useless AV1 encoder on any Android 14+ handheld. Always gate on
`MediaCodecInfo.isHardwareAccelerated()`.**

**A free codec-compatibility shim worth knowing:** Android 12+ will transparently transcode
HEVC/HDR10/HDR10+ → AVC SDR when the app declares it cannot handle them, via
`ApplicationMediaCapabilities` on `openTypedAssetFileDescriptor`. Zero code beyond the
declaration — but it only changes codec, with no resize and no bitrate reduction, and
Google's own cost is the 20 s/minute Pixel 3 figure. Use the per-call API, not the manifest
flag, or it fires during thumbnailing.

**MAUI bindings exist and have real gaps** (nuget.org, 2026-09-12):

| package | latest | date |
|---|---|---|
| `Xamarin.AndroidX.Media3.Transformer` | **1.11.0** | 2026-09-01 |
| `Xamarin.AndroidX.Media3.Common` | 1.9.0.1 | 2026-01-08 |
| `Xamarin.AndroidX.Media3.Effect` | 1.9.0.1 | 2026-01-08 |

Microsoft-maintained, MIT, sync lag ~10 days to ~4 weeks. Three risks:

1. **`ExportException.CodecInfo` is not bound** —
   [dotnet/android-libraries#1263](https://github.com/dotnet/android-libraries/issues/1263),
   open since 2025-08-25. That is precisely the diagnostic field needed to triage the
   device-specific encoder failures that *are* the dominant failure mode. From C# you are
   reduced to reflection or parsing `ToString()`.
2. **The modules do not ship in lockstep** — Transformer 1.11.0 depends on `Common` and
   `Effect`, still on 1.9.0.1 from January. Pinning a coherent set is a recurring chore, and
   [#1151](https://github.com/dotnet/android-libraries/issues/1151) documents
   version-constraint conflicts with `AndroidX.Activity`/`Core` causing restore failures.
3. **The bindings are mechanically generated** — a Microsoft engineer on that issue: "We
   just mirror maven dependencies to NuGet." Missing members recur; already-fixed examples
   include `EditedMediaItem.Builder` absent entirely
   ([#940](https://github.com/dotnet/android-libraries/issues/940)) and `Transformer.IListener`
   not implementable ([#1268](https://github.com/dotnet/android-libraries/issues/1268)).

There is **no ready-made cross-platform MAUI transcoding library**; per-platform native code
is the realistic route.

### 5.2 Windows: `Windows.Media.Transcoding.MediaTranscoder`

The cheapest platform by a wide margin.
`PrepareFileTranscodeAsync(IStorageFile, IStorageFile, MediaEncodingProfile)` returns a
`PrepareTranscodeResult` with `.CanTranscode` / `.FailureReason` (`CodecNotFound`,
`InvalidProfile`), then `.TranscodeAsync()` is an `IAsyncActionWithProgress<double>` — so
progress is built in. Properties include `HardwareAccelerationEnabled`, `AlwaysReencode`,
`TrimStartTime`/`TrimStopTime`, and `VideoProcessingAlgorithm` (MrfCrf supersampling for
better downscales). Profiles: `MediaEncodingProfile.CreateMp4` (H.264+AAC), `CreateHevc`,
`CreateAv1`, `CreateVp9`. The docs are actively maintained (`ms.date: 2026-08-23`) and the
current sample targets WinUI desktop, not UWP.

**Reachable from .NET with no extra package** — `Windows.Media.Transcoding` is projected
automatically for `net9.0-windows10.0.19041.0` and higher, which a MAUI Windows head
already targets. No packaging identity needed for `MediaTranscoder` itself.

Caveats:

- **HEVC encode needs the Store extension** on stock retail Windows (the free "from Device
  Manufacturer" variant ships OEM-bundled; otherwise $0.99). You can build a profile the
  device cannot honour and the encode fails — the most-hit shipping gotcha.
- **AV1 encode is 24H2+ only**: `CreateAv1`/`CreateVp9` arrived in build 10.0.23504.0 /
  UniversalApiContract v15.0. Runtime-check with `ApiInformation.IsMethodPresent`; the API
  does not exist on 23H2 or Windows 10. Whether it reaches hardware AV1 encode end-to-end
  through `MediaTranscoder` is plausible but unconfirmed — smoke-test it.
- **[WindowsAppSDK#4804](https://github.com/microsoft/WindowsAppSDK/issues/4804)**: with
  `HardwareAccelerationEnabled = true` on an AMD 9950X iGPU + 24H2,
  `MediaEncodingProfile.Video.Bitrate` is **silently ignored** — 10 Mbps requested, ~30 Mbps
  produced. Assert output bitrate in a test.
- **[CsWinRT#1386](https://github.com/microsoft/CsWinRT/issues/1386)**: native `IBuffer`
  leak when feeding `PrepareStreamTranscodeAsync` through `.AsRandomAccessStream()`. Use the
  `StorageFile` path.
- Hardware encode availability: H.264 effectively universal since 2011–2014; HEVC from
  Intel Skylake / NVENC Maxwell 2nd-gen; **AV1 encode only on NVIDIA Ada/Blackwell, Intel
  Arc / Meteor Lake, AMD RDNA3** — a minority of machines. The real gap is VMs/VDI without
  GPU passthrough.
- Windows is also where this matters least: US median *fixed* upload is ~56 Mbps.

### 5.3 Apple: already built

`AppleVideoTranscoder` works. Two maintenance items:

- **HDR passthrough is a cross-platform bug.** Apple: "all HEVC presets have been upgraded
  to support HDR. The output format will match the source format, so if the source file is
  Dolby Vision Profile 8.4, the exported movie will maintain that format." For a chat app
  that means a Dolby Vision file reaching Android and web viewers, where it renders wrong.
  Explicit tone-mapping to SDR BT.709 is wanted — and skipping the Rec.2020→BT.709 primaries
  conversion is what produces the classic washed-out result.
- `AVAssetExportSession` is not deprecated, but `.status` and `.progress` are as of iOS 18,
  in favour of `progressStates(updateInterval:)` / `states(updating:)` plus an async
  `export(to:as:)`.
- `MaxRemuxSize = 70 MB` is the current gate for "do nothing". If presets arrive, that
  heuristic should become a function of the chosen preset rather than a constant.

### 5.4 Native versus one WebView path — the comparison

| | native per platform | one WebCodecs path in the WebView |
|---|---|---|
| **reach** | MAUI only — iOS, macOS, Android, Windows. **Nothing for the web.** | every platform including the web, except Firefox Android |
| **effort** | Apple done; Windows 1–2 d; **Android ~10–15 d** with binding gaps and a device matrix | one implementation, ~13–21 d including a new web-side client-processing seam |
| **speed** | the platform ceiling (Google: `MediaCodec` encoder throughput is the limit) | same hardware codecs, minus ~3× binding overhead (one desktop measurement) |
| **quality control** | full — bitrate mode, profile/level, HDR tone-mapping, edit lists via platform muxers | mediabunny gives bitrate and codec; HDR metadata, edit lists and AAC priming are open issues |
| **container correctness** | platform muxers; `ShouldOptimizeForNetworkUse` / `InAppMp4Muxer` handle faststart and edit lists | mediabunny's `fastStart` is good; edit lists are not written; `dvh1` may not parse |
| **robustness** | per-device encoder rejection is the known pain (`CodecDbLite`, `setEnableFallback`) | per-device decode/memory cliffs are the known pain; a WebView OOM kills the render process |
| **maintenance** | three platform codebases plus binding churn; `@UnstableApi`; FFmpegKit gone | one TypeScript codebase, one dependency with **bus factor 1** |
| **precedent in this repo** | `AppleVideoTranscoder` | **the image pipeline — jpegli in the WebView serves MAUI too** |

**Judgement, per platform:**

- **Web** — the WebView path is the only option. Not a choice.
- **Android** — the WebView path *first*. Media3 Transformer is better on paper, but the
  repo measured Android WebView hardware H.264 at 2.43 ms/frame at 720p, which is already
  fast enough (§4.2); the Android native leg is the most expensive of the three (§5.1) and
  the one whose diagnostic field is unbound in C#; and the same code serves the web. Go
  native only if measurement on real mid-range devices shows the WebView path failing or
  too slow.
- **iOS/macOS** — keep native. It exists, it works, it avoids the WKWebView memory cliff
  and the `AudioEncoder`-before-26 problem entirely, and it is the platform where the
  WebView path is most likely to be killed by jetsam on a 4K clip.
- **Windows** — either. `MediaTranscoder` is 1–2 days and gives better bitrate control;
  the WebView path costs nothing extra once it exists. Upload speed makes this the lowest
  priority platform either way.

---

## 6. Bitrate targets

### 6.1 What comparable apps actually send

**Signal** — exact constants from `VideoConstants.kt` (Signal-Android `main`, 2026-09-12,
AGPL-3.0); `resolution` is the **short edge**:

| tier | short edge | video | audio | max duration |
|---|---|---|---|---|
| Standard (most of the world) | **480p** | **1.0 Mbps** | **128 kbps AAC** | 900 s |
| Standard, ≤600 s, select locales | 720p | 2.0 Mbps | 128 kbps | 600 s |
| Standard, >600 s, select locales | 720p | 1.5 Mbps | 128 kbps | 900 s |
| "high quality" toggle | 720p | **4.0 Mbps** | 128 kbps | 360 s |

H.264 only (`VIDEO_CODEC_H264`), `OUTPUT_VIDEO_IFRAME_INTERVAL = 1` (**1-second GOP**),
`BITRATE_MODE_CBR`, frame rate matching the source, AAC-LC with sample rate and channels
copied from the source and HE-AAC passed through. Limits: 100 MiB send, 100 MiB transcode
target, 1 GiB max input, all remote-config overridable.

**Telegram Android** (DrKLO/Telegram `main`, 2026-09-12) — H.264 for all chat sends
(`outputMimeType = isWebm ? vp9 : shouldUseHevc ? hevc : "video/avc"` with
`shouldUseHevc = isStory`), `KEY_I_FRAME_INTERVAL = 1`. Slider → longest-edge box
**480 / 854 / 1280 / 1920**. `MediaController.makeVideoBitrate` caps at **6.8 Mbps** (min
side ≥1080), **2.6** (≥720), **1.0** (≥480), **0.75** below, with
`minBitrate = minCompressFactor × 2.26 Mbps × (w·h)/921600`. Derived bands: 640×360
**0.40–0.75**, 854×480 **0.9–1.0**, 1280×720 **2.26–2.6**, 1920×1080 **5.1–6.8 Mbps**.
**AAC and MP3 source tracks are copied through**; re-encodes use AAC 44.1 kHz 128 kbps.
And a detail worth stealing: `extractRealEncoderBitrate()` configures a throwaway
`MediaCodec` and reads `KEY_BIT_RATE` back, because "Some encoders (e.g. OMX.Exynos) can
forcibly raise bitrate during encoder initialization" — which is the same phenomenon
`codec-performance.md` measured as Android hardware encoders overshooting by 17–39 %.

**Telegram iOS** — a flat preset table (`TGMediaVideoConverter.m`), max side / video kbps /
audio kbps / channels: 480/400/32/1, 640/700/32/1, **848/1600/64/2 (default)**,
1280/3000/64/2, 1920/6600/64/2. H.264 High with CABAC, BT.709, `useH265 = false`. Note the
asymmetry: the same 848×480 tier is **1.6 Mbps on iOS versus 0.9–1.0 on Android**.

**WhatsApp** — documented: "maximum file size for videos sent or forwarded … is **64MB on
all platforms**"; "For users with a faster internet connection, the default video size
limit is **100MB and 720p**… slower … **64MB and 480p**" (WhatsApp Help Center, read
2026-09-12). The Cloud API media reference states "**Only H.264 video codec and AAC audio
codec supported. Single audio stream or no audio stream only**" and recommends Main or
Baseline because High-with-B-frames is unsupported on Android. **The familiar
"848×480 / Baseline / ~1 Mbps" has no primary source** — and 848 is Telegram's constant, so
the folklore looks like cross-contamination.

**Discord** — mobile transcode tiers are user-visible: Best = 720p free / 1080p Nitro,
**Standard (default) = 480p** free / 720p Nitro, Data Saver = 360p; no original-resolution
mobile upload. Bitrates unpublished.

### 6.2 Reference ladders

- **YouTube upload** recommendations (H.264 High, 2 B-frames, closed GOP, CABAC; AAC-LC
  48 kHz): 480p **2.5 Mbps**, 720p 5, 1080p **8 Mbps** at 24–30 fps; 1.5× at 48–60 fps.
  These are mezzanine numbers — a ceiling, not a delivery target.
- **Apple HLS Authoring Spec** H.264 16:9 average bitrates: 640×360 365, 768×432
  **730 / 1100**, 960×540 2000, 1280×720 **3000 / 4500**, 1920×1080 **6000 / 7800** kbps.
  Peak rule: "For VOD content, the peak bit rate SHOULD be no more than **200 %** of the
  average." HEVC SDR is roughly 25–30 % lower at each rung.
- **Google VP9 VOD** table (updated 2025-01-15), the only vendor table with min/target/max
  (min = 50 %, max = 145 % of target): 640×480 **512** LQ / **750** MQ, 720p30 **1024**,
  1080p30 **1800**, 1080p60 3000 kbps.
- **libwebrtc** `kSimulcastFormats` max/target/min kbps: 1080p 5000/4000/800, 720p
  2500/2500/600, 640×360 700/500/150. The real cap is
  `GetMaxDefaultVideoBitrateKbps`: ≤640×480 → 1700, ≤960×540 → 2000, larger → **2500**.
  Notably, VP9 and AV1 share one formula with VP8 there — **libwebrtc gives AV1 no
  efficiency discount in default bitrate selection**, only a lower QP cap.

### 6.3 Codec efficiency — what is measured and what is marketing

- **Meta, 2018-04-10**, 400 real smartphone-uploaded videos, 360p–1080p30, PSNR and SSIM:
  AV1 BD-rate versus x264 main **50.0 %**, versus x264 high **45.8 %**, versus libvpx-VP9
  **32.9 %** (CRF/PSNR). The widely quoted "34 % versus VP9 on VMAF" from this study **is
  not in the paper**.
- **Against reference encoders the AV1/HEVC gap collapses**: Nguyen & Marpe, APSIPA
  2021-07-13, luma PSNR, HM-16.21 versus AV1 — **AV1 over HEVC only ~10–15 %**.
- **Deployed savings run below BD-rate**: libvpx-VP9 measured at −21.06 % BD-rate (VMAF)
  delivered only 6.5–15.2 % realised bandwidth saving, because the codec advantage
  concentrates at low bitrates where little bandwidth sits.
- "AV1 is 30 % better than HEVC and 50 % better than H.264" is an **undocumented design
  target repeated by vendors** — treat as marketing.
- This repo's own policy (`VideoCodecDef`) is already the conservative read: HEVC 1.4,
  VP9 1.41, AV1 1.7, **capped at 1.4 for bitrate purposes**. That cap is well judged and
  should carry over.

### 6.4 Recommended targets

Three independent codebases converge on **~1 Mbps at 480p H.264** (Signal 1.0,
Telegram-Android 0.9–1.0, Telegram-iOS 1.6) and 1.5–2.6 Mbps at 720p. That convergence is
the most robust number in this section. Short clips also tolerate more than a streaming
ladder assumes — 60 s at 4 Mbps is 30 MB and there is no ABR switching to protect.

| preset | codec | video target / cap | audio | anchors |
|---|---|---|---|---|
| **854×480 @30** | H.264 High (Main for old-Android safety) | **1000 / 1400 kbps** | AAC-LC 96 kbps stereo (64 mono) — or **copy** | Signal 1.0; TG-Android 0.9–1.0; Apple 768×432 = 730–1100 |
| | HEVC | 700 / 1000 | copy | ÷1.4 per `VideoCodecDef` |
| **1280×720 @30** | H.264 High | **2000 / 2600 kbps** | AAC-LC 128 kbps — or **copy** | Signal LVL2 2.0; TG cap 2.6; Apple 3000 |
| | HEVC | 1400 / 1900 | copy | |
| **1920×1080 @30** | H.264 High | **4000 / 6000 kbps** | AAC-LC 128 kbps — or **copy** | Bitmovin avg 4800; Mux 5000; Apple 6000; TG 5.1–6.8 |
| | HEVC | 2900 / 4300 | copy | matches the existing `AppleVideoTranscoder` ballpark better than its 8 Mbps gate |
| 60 fps | × **1.5** on video | | | YouTube / Facebook Live scaling |

**In practice "copy" is the audio answer in every row**, because phone audio is already
AAC-LC at 128 kbps and re-encoding it saves under 1 MB/minute while risking the priming
bug. Spend the 96/128 kbps numbers only when the source audio cannot be copied.

Encoder settings worth copying from the peers:

- **1-second GOP.** Both Telegram (`KEY_I_FRAME_INTERVAL = 1`) and Signal
  (`OUTPUT_VIDEO_IFRAME_INTERVAL = 1`) do this. Chat clips get scrubbed and thumbnailed
  constantly; 1 s keyframes cost ~5–10 % bitrate and buy instant seek. Note this is very
  different from the live pipeline's `KeyFramePeriod = 3 s`.
- **Preserve source frame rate, cap at 30.**
- **CBR** for a predictable size budget (Signal) or VBR with a 145–200 % cap
  (Google / Apple).
- **Key the ladder on the short edge** (Signal) or a **longest-edge bounding box**
  (Telegram); both handle portrait correctly. Pixel-count bucketing does not. The repo's
  `UploadProcessorHelper.ScaleToFullHd` already does longest-edge with even rounding and is
  the right shape to reuse.
- **faststart / moov first.**
- **Read the encoder's actual bitrate back after `configure()`** — Telegram does this
  because Exynos encoders silently raise it, and `codec-performance.md` measured the same
  17–39 % overshoot.

---

## 7. A staged plan

*This section is judgement. Day estimates are engineer-days for one experienced engineer
including tests and device verification, and assume the measurement in Stage 0 does not
invalidate the approach.*

### Stage 0 — Measure before building (3–4 d)

Four unknowns decide the shape of everything after, and none needs production code:

1. **Does mediabunny read a real iPhone HDR `.MOV`?** The shipped demuxer does not list
   `dvh1`/`dvhe` (§2.2) and iPhone 12+ records Dolby Vision 8.4 by default. One clip, ten
   minutes.
2. **What is the real end-to-end throughput in each WebView?** A harness like the one
   `docs/live-video/codec-performance.md` documents, extended to demux→decode→downscale→
   encode→mux a real 1-minute clip, on the iPhone, a flagship Android, **a mid-range
   Android**, and desktop Chrome/Safari/Firefox. This is the number the whole feature rests
   on.
3. **Where is the memory cliff?** 1080p, 4K30, 4K60 10-bit, and a 500 MB file, on an older
   iPhone and a 4 GB Android. Expect a WebContent process kill; find out when.
4. **`AVAssetExportSession` 1080p throughput on real iPhones** — currently an estimate
   (§4.2) and the calibration point for whether the WebView path can replace native on
   Apple.

Device matrix: iPhone (one older, one current), one flagship Android, **one mid-range
Android on Android 15** (the Media3 #3399 population overlaps here), Windows desktop
Chrome + Edge, macOS Safari, Firefox.

### Stage 1 — The web-side client-processing seam (4–6 d)

Today `RunClientProcessing` takes a `FilePath` and the web path is a no-op (§0.2). This
stage is pure plumbing and has value independent of video:

- Generalise client processing so a `WebFileProvider` can participate — parallel to
  `ImageAttachmentProcessor`'s two-branch shape, which already handles both providers.
- On MAUI, decide the handoff: `GetContentUrl` + `fetch` into a Blob is what images do, and
  it will not survive a 200 MB video. A range-capable `BlobSource` over the
  `AndroidContentDownloader` / `ContentSchemeHandler` URL is the shape to aim for, so the
  demuxer reads incrementally.
- Result handling: an `IJSStreamReference` back through interop is what images use;
  verify it at 30 MB before committing.
- Preset enum + UI mirroring `ImageQualityPreset` / `ImageQualityMenu.razor`, with **no size
  estimate** (§0.3) — show resolution and, after the fact, the achieved size.

### Stage 2 — Minimum viable transcoder (6–8 d)

The smallest version that delivers real value:

- mediabunny `Conversion` in a dedicated module worker, mirroring
  `ImageProcessor`'s one-job-at-a-time discipline and its timeout-means-the-worker-died
  detection.
- **H.264 output into MP4, audio copied.** Not AV1, not VP9 — §1.2 and §3.2 together make
  that the only combination that is both widely encodable and able to keep the original
  audio.
- Presets 480p / 720p / 1080p / original, using the §6.4 table and
  `UploadProcessorHelper.ScaleToFullHd`-style longest-edge scaling.
- Capability gate **before** starting: `getFirstEncodableVideoCodec`, then inspect
  `conversion.discardedTracks` after `init()` and before `execute()`; any discard →
  upload the original and let the server transcode.
- `latencyMode: 'quality'`, 1 s GOP, `avc: { format: 'avc' }`,
  `hardwareAcceleration: 'prefer-hardware'` (so a missing hardware encoder fails loudly
  instead of silently becoming openh264 — §1.4), and read the achieved bitrate back.
- The implementation details from §1.4 that are cheap to get right up front and expensive
  to retrofit: **never `flush()` mid-file**; `close()` every `VideoFrame` in the output
  callback; bound both queues at ~16–20 and drive from the `dequeue` event rather than the
  live pipeline's frame-dropping flood gate; **sort decoder output by `timestamp`** (Safari
  reorders below 26.4/26.5); round output dimensions to a **multiple of 16** on Android,
  not just to even numbers as `ScaleToFullHd` does, because `MediaCodec` crops otherwise
  and 1080 is not a multiple of 16; handle `QuotaExceededError` as codec reclamation and
  rebuild; match the H.264 *level* to the resolution.
- Progress into the existing `StageProgress`; cancellation through the existing
  `CancellationToken`; failure always falls back to uploading the source.
- Hook `ThermalTracker` so a `Serious`/`Critical` device skips client transcoding.
- Ship behind a feature flag, Chromium targets and WKWebView 16.4+ only; Firefox Android
  and anything that fails the gate stay on the server path. **Keep `AppleVideoTranscoder`
  as-is** — on MAUI Apple it runs first and the WebView path never sees the file.

### Stage 3 — Raise the ceiling (4–6 d)

- 4K and 10-bit HEVC sources: detect, and decide per device whether to attempt or defer to
  the server (mediabunny #256, #445).
- Long clips: `StreamTarget` with `fastStart: false`, and let the server do the faststart
  pass — or accept moov-at-end. Needs a decision about what the server's ffprobe analysis
  and snapshot extraction tolerate.
- Rotation and edit lists: verify against real portrait iPhone footage; bake rotation into
  frames if metadata proves unreliable (mediabunny #447).
- A hard input-size ceiling above which the client does not try at all.

### Stage 4 — Windows native (1–2 d)

`MediaTranscoder` with `CreateMp4`, `StorageFile` paths (not `.AsRandomAccessStream()`),
capability detection for HEVC and AV1, and a test asserting the output bitrate
(WindowsAppSDK#4804). Cheap, and gives better bitrate control than the WebView path.

### Stage 5 — Android native, only if Stage 0/2 says so (10–15 d)

Media3 Transformer 1.11.1 via `Xamarin.AndroidX.Media3.Transformer`, with
`setEnableCodecDbLite(true)`, a deliberate `setEnableFallback` decision, forced SDR
tone-mapping, a coherent module version pin, and a workaround for the unbound
`ExportException.CodecInfo`. Budget the device matrix, not the API.

### Stage 6 — Revisit AV1/VP9 (deferred)

Reopen when hardware AV1 encode is common (today: Pixel 10, RTX 40+, Arc, RDNA3) **and**
either Opus-in-MP4 playback is dependable or audio re-encoding to Opus in WebM becomes
acceptable. Neither holds in 2026.

### Total

| path | days |
|---|---|
| Stages 0–2 (the real first version) | **13–18** |
| + Stage 3 | 17–24 |
| + Stage 4 (Windows native) | 18–26 |
| + Stage 5 (Android native) | 28–41 |

---

## 8. Recommendation

**Do a reduced version.** Build the client transcoder, but not the one described in the
brief.

### What to build

**H.264 into MP4 with the original AAC audio copied, via WebCodecs + mediabunny in a
worker, on Chromium targets and WKWebView 16.4+, with Apple staying native and everything
else falling back to the server that already transcodes.** Stages 0–2 above, 13–18 days.

### Why not the brief's version

- **"AV1 where available, VP9 otherwise" does not survive the repo's own measurements.**
  On the four devices in `codec-performance.md`, hardware AV1 encode exists on one (a
  desktop RTX 3090) and hardware VP9 on one (an iPhone). Everywhere else those codecs mean
  *software* encode, which §4.1 shows is 5–15× too slow on a phone once 4K HEVC decode is
  included, and §4.5 shows loses to simply uploading the original on ~80 % of sessions.
- **And where software AV1 does run, it does not deliver the file-size win it is chosen
  for.** Chromium builds libaom with `CONFIG_REALTIME_ONLY`, forces `AOM_USAGE_REALTIME`,
  uses cpu-used 7–9, and sets `g_lag_in_frames = 0` — no lookahead, no ALTREF pyramid,
  8-bit only (§1.4). The 30–50 % BD-rate advantages in §6.3 were measured with `good`-mode
  libaom; the encoder WebCodecs actually exposes is a different, much weaker thing. So
  choosing AV1 buys a slow encode *and* a smaller-than-advertised saving, on the devices
  where it is available at all.
- **"Keep the original audio" and "VP9/AV1" are mutually exclusive in practice.** AAC
  cannot be muxed into WebM at all (verified, §3.1), so keeping the audio forces MP4, and
  VP9/AV1 in MP4 has uneven playback support. The audio requirement is the more valuable
  of the two and should win.
- A third reason to prefer H.264: the server currently passes 1080p HEVC MP4 through
  untranscoded (§0.7), so some viewers already receive files Firefox cannot play. A
  client-side H.264 path incidentally fixes that.

### Why build it at all, given the server already transcodes

Because the value is **latency on bad uplinks**, and that value is large. At the US median
18.78 Mbps, a hardware client transcode takes a 1-minute 1080p send from 53 s to 24–34 s.
At 3 Mbps — roughly one session in five on the best US network — it takes it from 5.5
minutes to 1.6–1.8 minutes, and shrinks the window in which a handoff or a backgrounding
can kill the transfer by the same 4×. It also cuts ingress, GCS storage, and GCP Transcoder
spend by roughly 4×. Every messaging app in the comparison set does this; every
file-sharing and media platform does not; Voxt is a messaging app.

It is **not** a battery win (§4.4) and it should not be sold as one.

### Why the WebView path rather than three native ones

Three reasons, in order:

1. **The web has no alternative**, and the web is a first-class target.
2. **The repo already proves the pattern.** The image pipeline runs jpegli-in-WASM inside
   the WebView for MAUI as well as the browser. One codebase, already precedented.
3. **The Android native leg is the expensive one** — 10–15 days, a device matrix, a
   `@UnstableApi` surface, mechanically generated bindings, and the one diagnostic field you
   most need unbound in C# — while the repo has *already measured* Android WebView hardware
   H.264 at 2.43 ms/frame at 720p. Do the cheap thing first and go native only where
   measurement demands it.

Apple stays native because `AppleVideoTranscoder` already exists, works, and avoids both
the WKWebView memory cliff and the missing-`AudioEncoder`-before-iOS-26 problem.

### Top three risks

> **Round 2 update — this ranking changed.** Risk 1 below is **downgraded**: edit lists,
> rotation and faststart were all tested end to end against real phone files and work
> (§R3). A risk round 1 missed is now first: **45 % of real phone footage is 10-bit HLG
> HDR**, which needs tone mapping nothing in the pipeline provides (§R2). The current
> ranking is in [§R7](#r7-does-the-recommendation-change).

1. **Container and metadata correctness — the silent failure class.** Rotation, edit lists,
   AAC priming, faststart and HDR metadata all decide whether a video plays *correctly*
   rather than whether it plays. The server currently launders all of this by re-encoding
   with ffmpeg; bypassing the server means owning it, and mediabunny has open issues on
   exactly these points (#444 priming, #447/#492/#494 edit lists, #431 HDR) plus the
   undocumented `dvh1` gap. **Mitigation:** Stage 0's ten-minute `dvh1` test; copy audio
   rather than re-encode it; bake rotation into frames if metadata proves unreliable; and
   verify that the server's ffprobe analysis and snapshot extraction still work on
   client-muxed output.
2. **Device memory cliffs, whose failure mode is a WebView process kill.** mediabunny #445
   has an iPhone 13 failing a 4K decode that an iPhone 13 Pro Max completes on the same iOS
   build, capability probing cannot predict it, and the app's own attachment limit is
   500 MB. An OOM takes the whole render process, not just the upload. **Mitigation:** a
   hard input-size ceiling, incremental (range-based) reading rather than a whole-file Blob,
   one encoder and one decoder reused, eager `close()` on every `VideoFrame`, and the
   existing timeout-means-the-worker-died detection from `image-processor.ts`.
3. **The measured win may be smaller than the estimate, because the bottleneck is decode,
   not encode.** Every favourable number in §4.2 is an *encode* number. The sources users
   actually send include 4K30 and 4K60 10-bit HEVC (Appendix A), and hardware HEVC decode
   is itself fragile — Media3 #2711 has HEVC hardware decode producing black frames across
   Dimensity 700/900/1080 after the Android 15 upgrade. If real end-to-end throughput on a
   mid-range phone lands near 1× realtime rather than 1.5–3×, the break-even uplink moves to
   ~18 Mbps and the feature stops paying at the median. **Mitigation:** Stage 0 measures
   end-to-end, not encode-only, on a mid-range device, and Stage 2 ships behind a flag with
   an unconditional fall-back-to-server path. If Stage 0 disappoints, stop after Stage 1 —
   the seam is useful regardless.

A fourth, smaller risk worth naming: **mediabunny has bus factor 1.** Mitigating it:
MPL-2.0, no WASM, readable TypeScript, every iOS-specific issue already closed, and
Remotion migrating onto it. The fallback if it stalls is mp4box.js for demuxing (which
knows `dvh1`) plus a muxer, at materially more work.

---

---

# Round 2 — tests run, and the design for an opportunistic client transcoder

Added 2026-09-12 after the first pass. Everything below was executed locally: `ffprobe`
and byte-level MP4 parsing over a real 152-file phone corpus, and **mediabunny 1.56.2 run
under Node 20** in a temp directory (nothing installed into any project). Node has no
WebCodecs, so decode and encode could not be exercised — but demuxing, muxing, metadata,
edit lists, rotation and the whole pre-flight gate are pure JavaScript and were all tested
for real. What genuinely needs a browser is written out as runnable probes in
[§R6](#r6-probes-that-need-a-browser--pending) and marked pending.

## R1. The Dolby Vision test, run

### There is no real Dolby Vision file in the accessible corpus

`M:\Downloads\2025-Turkey` holds 152 videos, and they are not iPhone footage:

| source | evidence | what it produces |
|---|---|---|
| **Samsung Galaxy, Android 15** | `com.android.version=15`, `com.samsung.android.utc_offset`, and the Samsung-private `smta` / `cami` / `SDLN` / `sefd` boxes | HEVC `hvc1`, 1080p and 4K, **Main 10 / HLG BT.2020** or Main 8-bit |
| **Ray-Ban Meta Smart Glasses** | `comment=app=Meta AI&device=Ray-Ban Meta Smart Glasses`, `composer=Meta AI`, `encoder=Lavf56.40.101` | HEVC `hvc1` Main 8-bit at mod-16 portrait sizes (1376×1824 and neighbours) |

The only Apple-originated video anywhere under `M:\Own\Pictures` is iPad footage from 2012
(`com.apple.quicktime.model=iPad`, iOS 5.1.1/6.0.1, H.264 **Baseline**). The `IMG_*.mp4`
files in `2025.05` were rewritten by `Lavf59.27.100` and are not iPhone container output.

**`dvh1`, `dvhe`, `dvvC` and `dvcC` appear in zero files.** Confirmed by byte-scanning the
`moov` of every candidate. So the test had to use synthetic inputs, labelled as such.

### The synthetic files

Built from a 5-second `-c copy` cut of a real Samsung HEVC Main 10 HLG clip
(`20250628_114151.mp4`). ffmpeg 5.1 refuses the tags outright —
`Tag dvh1 incompatible with output codec id '173' (hev1)` — so the sample entries were
patched at the byte level with a small box editor, keeping `moov` after `mdat` so that
inserting bytes into `moov` could not invalidate `stco` chunk offsets:

| file | how it was made | ffprobe sees |
|---|---|---|
| `base_tail.mp4` | real Samsung clip, `-c copy` | `hevc, Main 10, hvc1` |
| `syn_dvh1.mp4` | sample-entry fourcc `hvc1` → `dvh1` (no size change) | `hevc, Main 10, dvh1` |
| `syn_dvhe.mp4` | fourcc → `dvhe` | `hevc, Main 10, dvhe` |
| `syn_dv84.mp4` | `hvc1` kept, a spec-shaped 24-byte **`dvvC`** appended inside the sample entry (dv_version 1.0, profile 8, level 6, `rpu_present=1`, `el_present=0`, `bl_present=1`, `bl_signal_compatibility_id=4`), all 7 ancestor box sizes grown by 32 | `hevc, Main 10, hvc1` |
| `syn_dvh1_dvvC.mp4` | both of the above | `hevc, Main 10, dvh1` |

All five parse cleanly in ffprobe, so the patches are structurally sound.

### Result

| input | mediabunny behaviour |
|---|---|
| `base_tail.mp4` | full success — `codec: "hevc"`, `internalCodecId: "hvc1"`, 158-byte `hvcC` description, `rotation: 90`, `display 1080x1920`, `hdr: true`, colour space `{bt2020, hlg, bt2020-ncl}` |
| **`syn_dv84.mp4`** (hvc1 + dvvC) | **byte-identical result to the base.** The unknown `dvvC` box is simply ignored |
| `syn_dvh1.mp4` | `console.warn`: *"Unsupported video codec (sample entry type 'dvh1')."* → `codec: null`, `codecParameterString: null`, `getDecoderConfig()` returns no description, `getMimeType()` drops the video codec and returns `codecs="mp4a.40.2"` alone. **The track is still enumerated** with correct dims, rotation, HDR flag and packet stats |
| `syn_dvhe.mp4` | identical |
| `syn_dvh1_dvvC.mp4` | identical — the fourcc decides, the `dvvC` changes nothing |

**Nothing threw. Nothing produced a wrong file. The failure is visible before any work.**

### Verdict

**The gap is real, it fails safely, and the shape an iPhone actually writes is probably not
the failing one.** Dolby Vision Profile 8 is the *cross-compatible* family: a profile-8
stream is a conformant HEVC Main 10 base layer plus an RPU, and the ISOBMFF carriage for
cross-compatible profiles keeps the **base-layer** sample entry (`hvc1`/`hev1`) and adds a
`dvvC` box, precisely so a non-Dolby-Vision player sees plain HEVC and plays it. That is
exactly `syn_dv84`, and mediabunny handles it perfectly. `dvh1`/`dvhe` are the
*non*-cross-compatible carriage, used by Profile 5 — streaming-delivery content, not iPhone
capture. iPhone records 8.4.

So my round-1 framing ("an iPhone HDR clip may present as an unrecognised track") was the
pessimistic reading of an Apple statement that most likely concerns delivery content rather
than camera output. **This is a correction, not a confirmation.**

What remains genuinely unknown is narrow: whether an iPhone 12-or-later HDR `.MOV` really
carries `hvc1` + `dvvC` rather than `dvh1`. I could not test it because no such file exists
on this machine. The probe is one `ffprobe` invocation on a real file
([§R6](#r6-probes-that-need-a-browser--pending), probe 0) and it does not even need a
browser — just an iPhone clip copied onto the machine.

### A trap worth more attention than the Dolby Vision question

`Conversion.init()` on a file whose video track was discarded returned **`isValid: true`**:

```
isValid          : true
discardedTracks  : [{ type: "video", codec: null, internal: "dvh1", reason: "unknown_source_codec" }]
```

That conversion would have succeeded and produced an **audio-only MP4**. `isValid` means
"the output format can hold what is left", not "the job is worth doing". **Gate on
`discardedTracks`, never on `isValid`.**

The `reason` codes are usefully precise and distinguish the two failure classes:

- `unknown_source_codec` — the demuxer could not identify the track (the `dvh1` case).
- `undecodable_source_codec` — identified, but this platform cannot decode it (what Node
  reports for every HEVC file, since it has no WebCodecs).

## R2. What the real corpus is, and the two things it changes

152 videos probed, `M:\Downloads\2025-Turkey`:

| property | distribution |
|---|---|
| codec | **149 HEVC**, 3 H.264 High |
| profile | 80 Main (8-bit), **69 Main 10 (10-bit) — 45 %** |
| pixel format | 83 `yuv420p`, **69 `yuv420p10le`** |
| resolution | **45 × 3840×2160**, 39 × 1920×1080, 65 × mod-16 portrait (1360–1504 wide), 3 × 1080×1920 |
| rotation matrix | **29 of 152 (19 %)** |
| audio | **100 % AAC-LC, 48 kHz, stereo** — 256 kbps (Samsung) or 128 kbps (glasses) |
| video bitrate | 4K: min 33.3 / **median 33.6** / max 76.6 Mbps. 1080p: min 7.2 / **median 12.0** / max 60.0 Mbps. Glasses ~15–19 Mbps |
| faststart | **none** — Samsung writes `ftyp → mdat → moov` |

### Change 1 — HDR is not an edge case, and it needs tone mapping nothing provides

**45 % of this corpus is 10-bit HEVC Main 10 with HLG transfer and BT.2020 primaries.**
mediabunny reads that correctly (`hdr: true`,
`colorSpace: {primaries: "bt2020", transfer: "hlg", matrix: "bt2020-ncl", fullRange: false}`,
`codecParameterString: "hev1.2.4.L120.B0"` — the `2` is Main 10). Reading it is not the
problem; converting it is. Measured on the real clip:

| path | result |
|---|---|
| naive re-encode, no pixel format forced | **H.264 High 10 / `yuv420p10le`** — a profile WebCodecs cannot produce at all and most hardware H.264 decoders cannot play |
| forced 8-bit, no transfer conversion | 8-bit `yuv420p` still tagged `arib-std-b67` / `bt2020`; **mean saturation 48** |
| proper `zscale → tonemap=hable → bt709` | correctly tagged `bt709/bt709/bt709`; **mean saturation 78** |

That is a **39 % loss of saturation** — the classic washed-out, flat result — in the path a
naive decode→encode pipeline would take. Tone mapping cost nothing here (4 s versus 5 s for
5 s of 1080p on a desktop CPU), but **WebCodecs has no tone-mapping primitive.** A decoded
10-bit `VideoFrame` would have to pass through a shader that applies the HLG inverse OETF,
gamut-maps BT.2020 → BT.709 and re-applies the BT.709 transfer, before reaching an 8-bit
encoder. The repo already has the right home for it — `Services/Video/webgl/` and
`webgpu/` hold the existing downscalers — but it is real work, and getting it wrong is worse
than not transcoding, because a desaturated video is a visible regression where an untouched
upload is not.

Round 1 missed this entirely. It is now the top risk in [§R7](#r7-does-the-recommendation-change).

### Change 2 — the savings on real content are larger than round 1 assumed

Round 1's crossover used a 105 MB source shrinking to 27 MB, a 3.9× reduction. Against the
real medians:

| real source | MB/min | → 1080p H.264 @4 Mbps | reduction |
|---|---|---|---|
| 4K30 HEVC, 33.6 Mbps (30 % of corpus) | **252** | 30 MB | **8.4×** |
| 1080p30 HEVC, 12.0 Mbps (26 %) | **90** | 30 MB | **3.0×** |
| glasses 1376×1824, ~15 Mbps (43 %) | 113 | ~23 MB at 3 Mbps | **4.9×** |

Recomputing the break-even uplink `R = saving_Mbit / (0.85 · T)` for the 4K case
(saving 222 MB = 1776 Mbit per minute of video): even at a pessimistic **T = 35 s** for a
mid-range phone (4K hardware HEVC decode plus a 1080p encode), break-even is **60 Mbps** —
far above any mobile uplink in §4.5. For the 1080p case (saving 60 MB = 480 Mbit) at
T = 20 s, break-even is **28 Mbps**, still above the 18.78 Mbps US median.

**The 4K third of the corpus is where this feature pays for itself several times over.**
That also argues for treating 4K input as the *priority* case rather than the scary one.

### Ladder check

The §6.4 table holds against real sources. One adjustment: audio arrives at **256 kbps**
from Samsung, not the 128 kbps I assumed — so re-encoding audio to 128 would save ~1 MB per
minute against a video saving of 60–220 MB. **Copy it**, as recommended, and the case is
stronger than it was.

## R3. Edit lists, rotation, priming and faststart — measured end to end

mediabunny can remux without any codec, so a packet-copy `Conversion` runs fine in Node.
That is the ideal fidelity test: it isolates the container layer from encode entirely.

| file | source v.start / a.start / duration | mediabunny remux | rotation |
|---|---|---|---|
| `20250628_114151.mp4` | 0.000000 / 0.000000 / 17.273500 | **0.013854** / 0.000000 / 17.273542 | −90 → −90 |
| `20250628_173940.mp4` | 0.000000 / 0.000000 / 17.107400 | **0.013542** / 0.000000 / 17.107448 | −90 → −90 |
| `20250702_121241.mp4` | 0.000000 / **0.092583** / 77.989800 | 0.000000 / **0.092583** / 77.989826 | −90 → −90 |
| `20250703_215521.mp4` | 0.000000 / **0.006208** / 34.420500 | 0.000000 / **0.006208** / 34.421215 | −90 → −90 |
| `20250627_091252_…mp4` (glasses) | 0.000000 / 0.000000 / 90.417000 | 0.000000 / 0.000000 / **90.449253** | none → none |

### Correction: mediabunny does write an edit list

Round 1 listed [#447](https://github.com/Vanilagy/mediabunny/issues/447) ("mediabunny does
not write an edit list") as a leading risk. **It writes one.** `20250702_121241.mp4`'s audio
track carries `elst v0 [(926, −1 = EMPTY EDIT), (773970, 0)]`; the remux output carries
`elst v0 [(5333, −1), (4458067, 0)]`, and `5333 / 57600` (the output's `mvhd` timescale)
`= 0.092587 s` — reproducing the source offset to within 4 µs, which is why ffprobe reads
back exactly `0.092583`. So #447 is either narrower than its title or fixed by 1.56.2.
**Risk 1 in §8 should be downgraded accordingly.**

### But not perfectly

The two files whose *video* track carries an `elst` with a `media_time` trim
(`media_time = 19773` at timescale 90000) came back with **video starting 13.5–13.9 ms late
while audio stayed at 0** — so audio leads video by ~14 ms that the source did not have.
ITU-R BT.1359-1 puts the perceptibility threshold for audio leading video at about 45 ms, so
this is not a practical sync defect. It is a real and reproducible fidelity loss, and a
source with a larger video-side edit could exceed the threshold. Worth one deliberate test
with a large synthetic edit before shipping.

The glasses file also came back **32 ms longer** (90.417 → 90.449). Benign, but it shows
duration is reconstructed rather than copied.

**Rotation round-tripped exactly in all five cases.** That closes the round-1 rotation worry
at the container level — what remains is the §1.4 fact that *decoders* do not apply rotation,
which is a decode-path concern, not a mux one.

### Faststart: the client path improves on the source

Samsung writes `ftyp → mdat → moov`. Measured on the 71.6 MB / 78 s file:

| `fastStart` | time | box order | `arrayBuffers` peak | RSS |
|---|---|---|---|---|
| `false` | 170 ms | `ftyp → mdat → moov` | 68 MB | 108 MB |
| `'in-memory'` | 156 ms | **`ftyp → moov → mdat`** | 88 MB | 151 MB |
| `'fragmented'` | 174 ms | `ftyp → moov → moof → mdat …` | **55 MB** | 112 MB |

Two things follow. **The container layer is free** — 170 ms to rewrite 71.6 MB, so all the
cost in a real transcode is decode and encode. And `'in-memory'` costs roughly
`0.3 × filesize` of extra buffering on top of the source buffering, so extrapolating to a
500 MB input gives ~600 MB RSS — over the iOS WebContent jetsam budget discussed in §1.3.
The §7 Stage 3 policy (`'in-memory'` for small clips, `false` or `'fragmented'` above a
threshold) is confirmed, with `'fragmented'` an attractive middle option since it is
moov-first *and* the cheapest, at +13 KB of size.

Caveat: these are `FilePathSource` numbers in Node. A browser `BlobSource` over a `File`
does random access without loading the body, so the browser's baseline should be lower — but
the `'in-memory'` delta will still scale with output size.

## R4. Designing for "the server always re-encodes"

With the owner's confirmation that server-side re-encoding stays, client transcoding is
strictly **opportunistic**: a best-effort optimisation whose failure mode is "behave exactly
as today". That is a much easier thing to build than a pipeline anything depends on, and it
changes three design points.

### R4a. The pre-flight gate: what to probe, and what it costs

Ordered cheapest-first, and **nothing here reads the file body**:

| # | check | how | cost |
|---|---|---|---|
| 1 | worth attempting at all | MIME type starts `video/`; size between a floor (say 2 MB — below that there is nothing to save) and a hard ceiling; `ThermalLevel < Serious`; a user preset other than *Original* | free |
| 2 | container parses | `new Input({ source: new BlobSource(file), formats: [MP4, QTFF] })`, then `getFormat()` + `getPrimaryVideoTrack()` | a few range reads of `moov`; milliseconds |
| 3 | **demuxer understood the video codec** | `track.codec !== null` **and** `await track.getDecoderConfig()` returns a `description` | free once 2 is done |
| 4 | platform can decode it | `await track.canDecode()`, or `canDecodeVideo(codec, { width, height })` | one `VideoDecoder.isConfigSupported`, cacheable per codec+size |
| 5 | platform can encode the *output* | `getFirstEncodableVideoCodec(['avc'], { width: outW, height: outH, bitrate })` with `prefer-hardware`, so a silent openh264 fallback cannot win (§1.4) | one `VideoEncoder.isConfigSupported` |
| 6 | output dimensions are legal | round to **mod-16 on Android**, even elsewhere; H.264 level matched to resolution (§1.4, w3c/webcodecs#397) | arithmetic |
| 7 | 10-bit / HDR policy | `track.hasHighDynamicRange()` or `colorSpace.transfer ∈ {hlg, pq}` → defer to the server until tone mapping exists (§R2) | free once 2 is done |
| 8 | the conversion itself agrees | `Conversion.init(...)`, then **abort if the video track appears in `discardedTracks`** — not `isValid` (§R1) | no frames touched |
| 9 | it is actually worth it | predicted output size ≈ `(videoBitrate + audioBitrate) × duration`; abandon if not ≤ ~85 % of the source | arithmetic |

Total: a handful of `isConfigSupported` calls plus a few `moov` range reads. Measured proxy:
a **full** 71.6 MB packet-copy remux took 170 ms, so metadata-only inspection is comfortably
inside a frame budget. Cache steps 4–5 per (codec, resolution) in the same place
`codec-support.ts` already caches `probeEncoder` results.

The gate is cheap enough to run on every video attachment and drive a preset menu with
*Original* pre-selected and the reduced presets disabled when it fails — which is how the
existing `ImageQualityMenu` would naturally extend.

### R4b. What the server is told, and what it must verify regardless

**The image pipeline's shape fits, with one simplification.** For images the client sets a
flag and the server re-identifies the file from the decoded bitmap. For video the server
already re-identifies unconditionally: both `LocalVideoUploadProcessor` and
`GoogleCloudVideoUploadProcessor` begin with `FFProbe.AnalyseAsync` and then consult
`UploadProcessorHelper.MustConvertVideo(videoStream)` + `MustConvertVideo(mediaInfo.Format)`
+ `ExceedsFullHd(size)`.

So the useful observation is: **no server change is required at all.** A client-produced
H.264 ≤1080p MP4 already satisfies every condition in the existing gate and falls through to
the no-transcode path:

```csharp
// UploadProcessorHelper.MustConvertVideo
return !string.Equals(codecName, "h264", …) && !…"libx264"… && !…"hevc"… && !…"h265"…;
```

A declared hint (preset, target codec, output dimensions, bitrate, whether audio was copied)
is therefore **an optimisation, not a contract** — the safest possible shape for untrusted
input, because the server's decision never depends on it. Uses for the hint: skipping any
second-guessing of whether a re-encode would help, recording in telemetry how often the
client path fires and what it achieved, and letting the server log a mismatch between
declared and observed properties as a client bug.

What the server must verify regardless, and already does or nearly does:

| check | status today |
|---|---|
| container parses, has a video stream | `FFProbe.AnalyseAsync` + the `PrimaryVideoStream is null` guard |
| codec is in the allowlist | `MustConvertVideo` — transcodes anything that is not h264/hevc |
| container is MP4 | `MustConvertVideo(MediaFormat)` checks `major_brand ∈ {isom, iso2, mp41, mp42}` |
| resolution within limits | `ExceedsFullHd` |
| a frame can be decoded | `UploadProcessorHelper.Snapshot` is the de facto validator — if ffmpeg cannot extract a frame at 10 % of the duration, the file is unusable |
| declared vs actual size | would need adding if a hint is introduced |
| **faststart** | the server's ffmpeg path sets `WithFastStart()`; a client file that skipped the server keeps whatever the client wrote — which is why `'in-memory'` or `'fragmented'` matters (§R3) |

One decision this surfaces and does not answer: `MustConvertVideo` passes **HEVC** through,
so HEVC uploads reach Firefox users unplayable (§0.7). If clients start emitting H.264, the
HEVC arrivals become disproportionately the files where the client *declined* — so the gap
does not close on its own. Worth deciding on its own merits.

### R4c. Mid-flight failures

Because the source is never consumed — `UploadSession` holds the `FileProvider` throughout
and `TranscodedFilePath` is only set on success — **abandoning a transcode is free**, and
`GetTranscodedSource()` already throws if a recorded transcode file has vanished. The rule
for every failure is therefore the same: *log, discard, upload the original.* Specifics:

| failure | signal | response |
|---|---|---|
| encoder rejects the first frames | `OperationError` at `encode()` from an I420-vs-NV12 mismatch (§1.4) | one retry converting the frame's pixel format; then abandon |
| `configure()` rejected at this resolution | `NotSupportedError` / per-device Android rejection (§1.4) | retry once at a mod-16, level-matched size; then abandon. Record the codec string in the same `excludeEncoderCodecString` localStorage set `codec-support.ts` already maintains |
| codec reclaimed while backgrounded | `QuotaExceededError` (Chromium), `CodecException` (Android), process suspension (iOS) | **do not resume** — abandon and upload the original. The upload matters more than the saving |
| decode stalls with no error | output simply stops (§1.4, unclosed `VideoFrame`s) | a watchdog — the live pipeline already has the pattern in `operators/downscale.ts` (1.5 s, ≤ 4 in a row), and `image-processor.ts` treats a worker timeout as a crash |
| worker killed for memory | the RPC call times out with no `error` event — exactly what `image-processor.ts:30-37` documents | recreate the worker, abandon this job |
| **output not smaller** | compare before committing | keep only if ≤ ~85 % of source. `AppleVideoTranscoder` already does the moral equivalent with `EstimateOutputFileLengthAsync() < sourceSize`; apply it to the remux-only case too, since remuxing an already-compact file can grow it — the glasses file gained 32 ms of duration and a few KB in §R3 |
| user cancels | the existing `CancellationToken` path | already handled; `UploadSession` transitions to `Cancelled` |

Two notes on the state machine. First, a client transcode interrupted by backgrounding
should **restart from the original rather than resume**, because partial output is worthless
and `UploadSessionSnapshot` has no way to describe a half-finished transcode. Second, the
`ClientProcessing` stage currently either sets `TranscodedFilePath` or does not; an
opportunistic design wants a third outcome recorded — *attempted and declined, with a
reason* — so telemetry can answer "how often does this actually fire" without guessing.

## R5. Appendix B, worked through

| round-1 item | status now |
|---|---|
| 1. per-version Android WebView telemetry | **unresolvable here.** No public dataset exists; Wikimedia's TSVs are the only raw source. The answer is your own `isConfigSupported` + UA-Client-Hints logging |
| 2. Safari HEVC Main 10 decode | **still inferred.** Needs a browser — probe 1 in §R6 |
| 3. mediabunny and `dvh1`/`dvhe` | **answered for all four synthetic shapes** (§R1). The real iPhone container shape remains open and needs only an iPhone file plus `ffprobe` — probe 0 |
| 4. iPhone `AVAssetExportSession` throughput | **needs a device.** Unresolvable here |
| 5. any mobile WebCodecs encode benchmark | **needs a device.** The harness is now fully specified — probe 4 |
| 6. the `webcodecsfundamentals.org` dataset | unchanged; used only as shape |
| 7. Netflix codec-efficiency figures | unchanged; excluded from §6.3 |
| 8. Windows `CreateAv1` → hardware end to end | **needs a device** |
| 9. GPL / LGPL questions | unchanged; avoided by using MPL-2.0 JavaScript |
| 10. WebCodecs under iOS Lockdown Mode | **needs a device** — probe 3 |

**Newly resolved, none of which needed a browser:**

- Edit lists are written, and round-trip an empty edit to within 4 µs (§R3). Round-1 risk 1
  downgraded.
- Rotation round-trips exactly on five real files (§R3).
- faststart is available and cheap, and the client output is *better* than the Samsung source
  (§R3).
- Audio passthrough is viable for **100 %** of a 152-file real corpus — all AAC-LC 48 kHz
  stereo (§R2).
- The container layer costs ~170 ms per 71.6 MB, so it is not a throughput factor (§R3).
- The pre-flight gate's exact semantics, including the `isValid` trap and the two distinct
  `discardedTracks` reason codes (§R1, §R4a).
- mediabunny's Node-side API names, verified against the shipped build: the methods are
  `computeDuration()`, `getPrimaryVideoTrack()`, `computePacketStats()`,
  `computeFrameRateMetrics()`, `hasHighDynamicRange()`, `getCodecParameterString()` — the
  round-1 sketch used `getDuration()`, which does not exist.

**Newly opened:** HLG / BT.2020 tone mapping (§R2) — the one thing round 1 missed.

## R6. Probes that need a browser — pending

None of these were run. Each is self-contained.

### Probe 0 — the remaining Dolby Vision question (no browser needed, just an iPhone file)

Copy one HDR video from an iPhone 12-or-later camera roll onto the machine, then:

```bash
ffprobe -v error -select_streams v:0 \
  -show_entries stream=codec_name,codec_tag_string,profile,pix_fmt,color_transfer \
  -of default "IMG_XXXX.MOV"
# then check which Dolby boxes exist:
for tag in dvh1 dvhe dvvC dvcC hvcC; do
  printf '%s: ' "$tag"
  grep -c "$tag" "IMG_XXXX.MOV" 2>/dev/null || echo 0
done
```

`codec_tag_string=hvc1` plus a `dvvC` hit confirms the §R1 reading and closes the question in
favour of mediabunny. `dvh1`/`dvhe` means the `mp4box`-as-demuxer fallback is needed for
iPhone HDR.

### Probe 1 — can this engine decode the real phone bitstream, and what comes out?

Paste into the console of the browser or WebView under test. The `hvcC` is the real
158-byte description from `20250628_114151.mp4` (HEVC Main 10, HLG, 1920×1080).

```js
const HVCC_B64 = 'AQIgAAAAsAAAAAAAePAA/P36+gAAAwOgAAEAHkABDAf//wIgAAADALAAAAMAAAMAeAAAlJKSUkpIEqEAAQBNQgEHAiAAAAMAsAAAAwAAAwB4AACgA8CAEQfK2WUkpJSSk3JkE1ESRJJNVJInqSb1ySSqT9SXlESkklUn6kvKSVSXlUupqEiQSBdoUJSiAAEADUQBweMMMhkJwYDHsJQ=';
const desc = Uint8Array.from(atob(HVCC_B64), c => c.charCodeAt(0));
const base = {
  codedWidth: 1920, codedHeight: 1080, description: desc,
  colorSpace: { primaries: 'bt2020', transfer: 'hlg', matrix: 'bt2020-ncl', fullRange: false },
};
for (const codec of ['hev1.2.4.L120.B0', 'hvc1.2.4.L120.B0', 'hev1.1.6.L120.B0', 'dvh1.08.06', 'avc1.640028']) {
  for (const hw of ['no-preference', 'prefer-hardware', 'prefer-software']) {
    try {
      const r = await VideoDecoder.isConfigSupported({ ...base, codec, hardwareAcceleration: hw });
      console.log('DEC', codec, hw, '->', r.supported);
    } catch (e) {
      console.log('DEC', codec, hw, '-> THREW', e.name, e.message);
    }
  }
}
```

**What to record:** whether `hev1.2.4.*` (Main 10) is supported, whether `dvh1.08.06` is
accepted or rejected, and whether `prefer-hardware` differs from `prefer-software`
(meaningful on Chromium; inert on WebKit per §1.3).

### Probe 2 — what a decoded 10-bit frame actually looks like

This is the one that decides whether the HDR path is possible in the WebView at all. Needs
the real file served same-origin — the repo's `wwwroot` is the documented trick, see
`codec-performance.md`, "Reproducing this".

```js
// after fetching the file and demuxing one keyframe into an EncodedVideoChunk `chunk`
const dec = new VideoDecoder({
  output: f => {
    console.log('format      :', f.format);          // null for 10-bit HDR is the #256 case
    console.log('colorSpace  :', JSON.stringify(f.colorSpace));
    console.log('display     :', f.displayWidth, 'x', f.displayHeight);
    console.log('rotation    :', f.rotation, 'flip:', f.flip);  // Chrome 138+ only
    try { console.log('allocSize   :', f.allocationSize()); }
    catch (e) { console.log('allocationSize THREW', e.name, e.message); }
    f.close();
  },
  error: e => console.log('decoder error:', e.name, e.message),
});
dec.configure({ codec: 'hev1.2.4.L120.B0', codedWidth: 1920, codedHeight: 1080, description: desc });
dec.decode(chunk);
await dec.flush();
```

**What to record:** `format` (`null` means mediabunny #256 bites and the frame cannot be read
back on the CPU), `colorSpace.transfer` (does the engine surface `hlg`?), and whether the
frame can be drawn to a canvas — the only route to a tone-mapping shader.

### Probe 3 — encoder config, including the 1080-is-not-mod-16 question

```js
const rows = [];
for (const [w, h] of [[1080,1920],[1088,1920],[1080,1440],[1088,1440],[608,1080],[608,1088],[1920,1080],[1920,1088]]) {
  for (const codec of ['avc1.640028','avc1.4d0028','avc1.42e028','hev1.1.6.L120.B0','vp09.00.41.08','av01.0.08M.08']) {
    for (const hw of ['prefer-hardware','prefer-software']) {
      let r;
      try {
        r = (await VideoEncoder.isConfigSupported({
          codec, width: w, height: h, bitrate: 4e6, framerate: 30,
          latencyMode: 'quality', hardwareAcceleration: hw,
          ...(codec.startsWith('avc') ? { avc: { format: 'avc' } } : {}),
        })).supported;
      } catch (e) { r = 'THREW ' + e.name; }
      rows.push({ size: `${w}x${h}`, codec, hw, supported: r });
    }
  }
}
console.table(rows);
console.log('crossOriginIsolated:', self.crossOriginIsolated, '| SharedArrayBuffer:', typeof SharedArrayBuffer);
console.log('UA brands:', JSON.stringify(navigator.userAgentData?.brands));
```

**What to record:** whether the mod-16 and non-mod-16 rows differ (they should not — §1.4
says `MediaCodec` *crops* silently rather than refusing, so this probe is expected to pass
and the real test is probe 4's output dimensions); whether `latencyMode: 'quality'` is
accepted everywhere; and the expected `false` / `undefined` for cross-origin isolation in the
MAUI WebViews.

### Probe 4 — the end-to-end measurement Stage 0 needs

Serve a real 1-minute clip from `wwwroot` so it is same-origin, then:

```js
import { Input, Output, Conversion, ALL_FORMATS, BlobSource, Mp4OutputFormat, BufferTarget }
  from '/dist/mediabunny.min.mjs';
const file = await (await fetch('/clips/1080p-hlg.mp4')).blob();
const input = new Input({ source: new BlobSource(file), formats: ALL_FORMATS });
const output = new Output({ format: new Mp4OutputFormat({ fastStart: 'in-memory' }), target: new BufferTarget() });
const c = await Conversion.init({
  input, output,
  video: { codec: 'avc', width: 1080, height: 1920, bitrate: 4e6 },
});
console.log('discarded:', c.discardedTracks.map(d => `${d.track.type}:${d.reason}`));
if (c.discardedTracks.some(d => d.track.type === 'video')) throw new Error('video discarded');
const dur = await input.computeDuration();
const t0 = performance.now();
await c.execute();
const s = (performance.now() - t0) / 1000;
console.log(`${s.toFixed(1)} s for ${dur.toFixed(1)} s of video => ${(dur / s).toFixed(2)}x realtime`);
console.log('in:', file.size, 'out:', output.target.buffer.byteLength,
  `(${(100 * output.target.buffer.byteLength / file.size).toFixed(0)}% of source)`);
```

Run on: an iPhone (one older, one current) in the app's WKWebView, a flagship Android, a
**mid-range Android on Android 15**, desktop Chrome, desktop Safari. Record the ×realtime
figure, the output size, and whether the WebContent/renderer process survives a 4K input.
That single number decides the feature, per §4.5.

## R7. Does the recommendation change?

**No.** H.264 into MP4 with the original AAC copied, via WebCodecs + mediabunny in a worker,
Apple staying native, server fallback everywhere — unchanged, and better supported than it
was:

- Audio passthrough works for **100 %** of a real 152-file corpus, and the sources carry
  256 kbps AAC, so copying saves more than estimated.
- Edit lists and rotation round-trip (round-1 risk 1 was overstated).
- faststart is cheap, and the output beats the source.
- The real-content savings are **3–8.4×**, not ~4×, so the crossover is more favourable.
- The Dolby Vision blocker is smaller than feared, and fails safely either way.

Two amendments:

**1. Defer 10-bit / HDR sources to the server in the first version.** 45 % of the corpus is
HLG BT.2020 10-bit, and transcoding it without tone mapping produces either an unplayable
H.264 High 10 file or a visibly desaturated one (−39 % saturation, measured). Shipping that
is worse than not transcoding. So Stage 2 gains a gate (`hasHighDynamicRange()` → decline)
and Stage 3 gains the tone-mapping shader. That halves the initial hit rate on this corpus —
but the 8-bit half includes all the glasses footage and the 8-bit Samsung files, where the
saving is 5–8×, so the first version still pays.

**2. Revised risk ranking.**

| rank | risk | change from round 1 |
|---|---|---|
| 1 | **HDR / HLG tone mapping** — 45 % of real content; failure is a visible quality regression, not an error | **new** |
| 2 | **Device memory cliffs** — 4K input, the 500 MB attachment limit, a WebContent process kill; `'in-memory'` faststart extrapolates to ~600 MB RSS on a 500 MB file | unchanged, now quantified |
| 3 | **The measured win may be smaller than estimated**, because decode dominates and HEVC hardware decode is itself fragile (Media3 #2711) | unchanged — probe 4 settles it |
| 4 | Container / metadata correctness | **downgraded** — edit lists, rotation and faststart all verified working; what remains is a ~14 ms video-side edit-list shift, well under the 45 ms threshold |
| 5 | mediabunny bus factor 1 | unchanged |

**Revised effort** (deltas on §7):

| stage | round 1 | round 2 | why |
|---|---|---|---|
| Stage 0 — measure | 3–4 d | **2–3 d** | the Dolby Vision test, the demux/remux fidelity tests and the gate semantics are done; what remains is device measurement |
| Stage 1 — web-side seam | 4–6 d | 4–6 d | unchanged |
| Stage 2 — MVP transcoder | 6–8 d | **7–9 d** | + the HDR decline gate, mod-16 rounding, the `discardedTracks` (not `isValid`) gate, the not-smaller-than-source check |
| Stage 3 — raise the ceiling | 4–6 d | **8–11 d** | + an HLG→BT.709 tone-mapping shader in `webgl/`, and verifying it on a real HDR file |
| **Stages 0–2 (first version)** | **13–18 d** | **13–18 d** | unchanged in total |
| + Stage 3 | 17–24 d | **21–29 d** | |

Stages 4–6 are unchanged.

---

# Round 3 — the real iPhone file

Added 2026-09-12. `IMG_0226.MOV`, pulled off the owner's **iPhone 13 Pro, iOS 26.6.2,
default camera settings** — 2.13 s, 3,395,160 bytes, copied to a temp dir and read
read-only. This replaces the synthetic `hvc1` + `dvvC` variant of [§R1](#r1-the-dolby-vision-test-run)
with the genuine article.

```
video  hevc Main 10, tag hvc1, 1920x1080, yuv420p10le, 12.48 Mbps, CFR 30
       color_primaries=bt2020  color_space=bt2020nc  color_transfer=arib-std-b67 (HLG)
       DOVI record: dv_version 1.0, dv_profile 8, dv_level 4,
                    rpu_present=1, el_present=0, bl_present=1, bl_signal_compatibility_id=4
       display matrix: rotation=-90
audio  aac LC, 48 kHz, stereo, 169 kbps, tag mp4a
+ 5 × mebx timed-metadata tracks
container: major_brand=qt, com.apple.quicktime.model=iPhone 13 Pro, software=26.6.2,
           com.apple.quicktime.location.ISO6709=+33.5004-117.6963+157.984/
```

## R8. The prediction held

**The synthetic test predicted this exactly.** §R1 argued that iPhone Profile 8.4 would be
carried as `hvc1` + `dvvC` rather than `dvh1`, because profile 8 is the cross-compatible
family. The real file is `hvc1` with `dv_profile=8` and `dv_bl_signal_compatibility_id=4`
— the HLG-compatible variant, 8.4 — which is the `syn_dv84` shape, and that shape worked
perfectly in §R1.

mediabunny 1.56.2 under Node, on the real file:

| | result |
|---|---|
| format | `QuickTime File Format` |
| `getMimeType()` | **`video/quicktime; codecs="hev1.2.4.L120.B0, mp4a.40.2"`** — the video codec is present, not dropped |
| video track | listed. `codec: "hevc"`, `internalCodecId: "hvc1"`, `codecParameterString: "hev1.2.4.L120.B0"` |
| decoder config | 124-byte `hvcC` description, `codedWidth/Height 1920x1080` |
| rotation | `rotation: 90`, `display 1080x1920` (correctly swapped) |
| HDR | `hdr: true`, `colorSpace {primaries: bt2020, transfer: hlg, matrix: bt2020-ncl, fullRange: false}` |
| frame rate | `frameRateIsConstant: true`, 30 fps exactly — unlike the Samsung files, which were all VFR |
| `discardedTracks` | video, reason **`undecodable_source_codec`** |

That last row is the important one. The reason code is `undecodable_source_codec` — "I
understand this codec, but this platform cannot decode it" — which is correct for Node,
which has no WebCodecs. It is **not** `unknown_source_codec`, which is what the synthetic
`dvh1` produced. So the demuxer fully understands the real iPhone container, and the
`dvh1` failure path is not the one iPhone users will hit.

**Conclusion: the Dolby Vision question is closed.** Probe 0 is answered, mediabunny is
the right choice for iPhone input, and the `mp4box`-as-fallback-demuxer contingency in
§2.2 is not needed for camera footage. `dvh1`/`dvhe` remains a real gap for Profile 5
content (streaming rips, some Android DV modes), and it still fails safely.

## R9. The `mebx` tracks, and everything else that is dropped

A packet-copy remux through mediabunny (`Conversion.init` with no options, `fastStart:
'in-memory'`) produced a 3,376,673-byte MP4 from the 3,395,160-byte source. What survived
and what did not:

| property | source | remux | verdict |
|---|---|---|---|
| video track | HEVC Main 10 `hvc1` 1920×1080 `yuv420p10le` | identical | preserved |
| colour tags | `bt2020nc` / `arib-std-b67` / `bt2020` | identical | preserved |
| **rotation** | −90 | **−90** | **preserved** |
| audio | AAC-LC 48 kHz stereo | identical | preserved |
| **5 × `mebx` tracks** | present | **gone** | **silently dropped** |
| **DOVI configuration record** | profile 8, level 4, compat 4 | **gone** | **dropped** |
| **Apple metadata** | GPS, make, model, iOS version | **gone** | **dropped** |
| faststart | `ftyp → mdat → moov …` | `ftyp → moov → mdat` | improved |

### `mebx`: silently dropped, and not even reported

The five `mebx` timed-metadata tracks — Apple's carriage for things like per-frame gyro,
the still-image time, camera intrinsics and the "full frame rate playback intent" data —
are **not in `getTracks()` at all** (mediabunny returns 2 tracks where ffprobe sees 7), emit
**no warning**, and do **not** appear in `discardedTracks`. A caller has no way to learn they
existed without parsing the container with something else.

**This should be a decision, and the decision is probably "fine".** None of the app's
features read `mebx`; the server's ffmpeg path does not preserve it either (`libx264`
re-encode with no `-map` for data streams drops it too), so nothing downstream regresses
versus today. But two caveats: it means a client-transcoded file is *not* a faithful archive
of what the user shot, and if Voxt ever wants stabilisation metadata or Apple's
spatial-video tracks, the client path is where they vanish. Worth one line in the preset UI
copy if the *Original* preset is meant to mean "untouched".

### The DOVI record: dropped, and that is correct for 8.4

The `dvvC` box is gone, so the output is signalled as plain HEVC Main 10 HLG rather than
Dolby Vision. **For Profile 8.4 that is the designed fallback, not a defect** — the base
layer *is* the HLG presentation, and dropping the RPU signalling yields exactly what a
non-DV player would have rendered anyway. The RPU NAL units themselves survive a packet copy
inside the bitstream; only the box that advertises them is lost. Losing DV on a chat video
that will be re-encoded to 8-bit H.264 anyway costs nothing.

### Apple metadata: dropped, and that is a privacy improvement

mediabunny *reads* the tags (`getMetadataTags().raw` returns
`com.apple.quicktime.location.ISO6709`, `make`, `model`, `software`) but writes none of them.
The source carries **GPS coordinates to four decimal places, the device model and the iOS
version**; the remux carries only `creation_time`. That mirrors what the image pipeline does
deliberately via `ImageOutputSpec.stripMetadata`, and it is the behaviour you want — but it
should be stated, because it means the video presets have an implicit privacy behaviour the
image presets make explicit. Note the asymmetry this creates: `ImageQualityPreset` has a
separate `OriginalWithExif` precisely so a user can keep metadata, and a video *Original*
preset (which uploads the source untouched) would keep GPS while every reduced preset strips
it. That is defensible, and it should be intentional.

## R10. AAC priming — correcting round 1 and round 2

Round 1 said copying audio "avoids the priming half of the problem"
([#444](https://github.com/Vanilagy/mediabunny/issues/444)). **That is not quite right, and
the real iPhone file shows why.** Edit lists, decoded:

| track | source | remux |
|---|---|---|
| video | `elst media_time=0` (a no-op) | **no elst** — correctly dropped |
| audio | `elst media_time=2112 @ 48000` = **44.0 ms** trimmed | `elst media_time=1088 @ 48000` = **22.7 ms** trimmed |

`2112 − 1088 = 1024` samples — **exactly one AAC access unit at 48 kHz, 21.33 ms.** So
mediabunny re-derives the priming trim and lands one audio frame short of Apple's. The
output therefore begins with ~21 ms of priming silence that should have been trimmed, and
its audio duration grows correspondingly (2.1317 → 2.1533 s).

Consequence: **audio leads video by ~21 ms** in the output. ITU-R BT.1359-1 puts the
perceptibility threshold for audio leading video at about 45 ms, so this is below the
threshold and not a practical sync defect — but it is larger than the ~14 ms video-side
shift measured on the Samsung files in §R3, it happens on a **pure packet copy** where no
re-encoding is involved, and it is the majority of the error budget. Two deliberate tests
before shipping: a long clip (does the offset stay constant or accumulate?) and a
source with a larger priming trim.

The honest summary across all six real files now tested: **mediabunny's edit handling is
accurate to roughly ±1 frame — one audio access unit or one video frame — and always in the
direction of audio leading video.** Acceptable; worth asserting in a test rather than
trusting.

## R11. The end-to-end conversion, and the trap made concrete

Running the actual v1 shape in Node — `Conversion.init({ input, output, video: { codec:
'avc', width: 1080, height: 1920, bitrate: 4e6, fit: 'contain' } })` — on the real iPhone
file:

```
isValid         : true
utilizedTracks  : audio
discardedTracks : [{"t":"video","r":"undecodable_source_codec"}]
--- executing anyway ---
execute() RESOLVED — no error thrown.
```

**What landed on disk was a 47,707-byte audio-only MP4**, from a 3.4 MB video. No exception,
no rejected promise, `isValid` true throughout.

This is the §R1 trap, now demonstrated end to end on a real file rather than inferred from a
synthetic one. A caller that checked `isValid`, awaited `execute()`, saw no error and
uploaded the result **would have replaced the user's video with its soundtrack**. It is the
single most important implementation note in this document:

> **Abort if `discardedTracks` contains the video track. `isValid` and a resolved
> `execute()` prove nothing.** And assert the output has a video track before committing it
> — belt and braces, because this failure is silent by construction.

Two smaller API findings from the same run, both worth knowing before writing the worker:

- Passing both `video.width` and `video.height` **requires** `video.fit`
  (`'contain' | 'cover' | 'fill'`), or `Conversion.init` throws a `TypeError`. Resize
  semantics are explicit, which is good — `'contain'` is the one that matches
  `ScaleToFullHd`'s longest-edge behaviour.
- The video-encode leg could not be exercised at all here, because Node has no WebCodecs.
  Everything in R8–R10 is the container layer; the decode→encode legs still need probe 4.

## R12. What this means for iPhone coverage in v1

This is the part that changes the plan, and it needs stating plainly.

**The file is 10-bit HLG with a DV 8.4 RPU, shot on default settings.** Apple has recorded
HDR video by default since the iPhone 12. So this is not a corner case — **essentially all
default-settings iPhone 12-or-later video is 10-bit HLG**, which is exactly what §R7's v1
rule ("defer 10-bit/HDR to the server") declines. Taken literally, v1 would transcode
**~0 %** of iPhone video.

Three things stop that from being as bad as it sounds.

**1. The recommendation already routes Apple to native code.** §8 says Apple stays native:
on MAUI iOS/macOS, `AppleVideoTranscoder` runs in `RunClientProcessing` and the WebView path
never sees the file. So for iPhone users **in the app**, v1 coverage is whatever
`AppleVideoTranscoder` already does today — unchanged by this work. On this specific file
that means: a 2 s / 3.4 MB clip is under `MaxRemuxSize` and not `.mp4`, so it gets a
Passthrough remux; a 60 s clip at 12.5 Mbps would be ~94 MB, over the threshold, so it gets
the `Hevc1920x1080` preset. The pre-existing HDR concern in §5.3 applies — Apple's HEVC
presets preserve Dolby Vision, so that output reaches Android and web viewers as HDR — and
it is a pre-existing issue, not one this feature introduces.

**2. The affected population is iPhone users on the web**, i.e. Safari in a browser rather
than the app, plus any future non-MAUI client. For them v1 would decline nearly everything
and fall back to the server — which is the designed behaviour and costs nothing but a missed
optimisation.

**3. The 39 % saturation figure may not apply to the WebCodecs path at all, and that is
testable.** §R2 measured it through ffmpeg's raw pixel path with no colour conversion, which
is the worst case by construction. A WebCodecs pipeline that draws the decoded `VideoFrame`
through a 2D canvas — which is exactly what the existing `CanvasDownscaler` does — goes
through the browser's own colour management on rasterisation, and Chromium and WebKit both
apply a transfer-function conversion when drawing a tagged `VideoFrame` into an sRGB canvas.
**If that conversion is adequate, v1 gets HDR handling nearly for free and the deferral rule
is unnecessary.** I cannot test it here; it is probe 2 plus one visual comparison, and it is
now the highest-value unknown left.

### Revised expectation for v1 coverage

| population | v1 coverage if canvas colour management is adequate | v1 coverage if it is not |
|---|---|---|
| **iPhone in the MAUI app** | n/a — already handled by `AppleVideoTranscoder` | same |
| **iPhone on the web (Safari)** | **~100 %** | **~0 %** |
| **Android in the app / on the web** | ~100 % | **~55 %** (the 8-bit share of the real corpus) |
| desktop web | ~100 % | ~100 % for 8-bit sources; most desktop-uploaded video is 8-bit |

So the answer to "what fraction of iPhone uploads would v1 transcode" is: **in the app,
this feature does not change iPhone behaviour at all; on the web it is either ~100 % or
~0 %, and a single probe decides which.** That probe should move to the front of Stage 0 —
ahead of the throughput measurement — because it determines whether Stage 3's tone-mapping
shader is a nice-to-have or a prerequisite.

### Staging change

| | before round 3 | after |
|---|---|---|
| Stage 0 first task | end-to-end throughput (probe 4) | **probe 2 — what a decoded 10-bit HLG frame looks like, and whether a 2D-canvas round trip preserves saturation.** Then probe 4 |
| Stage 2 HDR rule | "10-bit → decline" | **"10-bit → decline *unless* probe 2 showed canvas colour management is adequate"**, decided before Stage 2 starts |
| Stage 3 tone-mapping shader | 3–5 d, optional quality work | **either unnecessary, or a prerequisite for web-Safari coverage** — same day count, different priority |

No change to the total for stages 0–2, and no change to the recommendation.

## R13. Round-3 corrections to earlier text

| claim | where | correction |
|---|---|---|
| iPhone HDR may present as an unrecognised `dvh1` track | §2.2, §R1 | **Closed: it does not.** Real file is `hvc1` + DOVI record and demuxes fully. The `mp4box` fallback is not needed for camera footage |
| copying audio "avoids the priming half" of #444 | §3.3, §R3 | **Partly wrong.** A packet copy still re-derives the trim and lands 1024 samples — one AAC frame, 21.3 ms — short of Apple's. Still inside the 45 ms tolerance |
| the ~39 % saturation loss applies to the client path | §R2, §R7 | **Unproven for the WebCodecs path.** Measured via ffmpeg's raw path; a canvas round trip may apply colour management. Probe 2 decides |
| rotation is "a hazard to assume rather than one these samples prove" | §3.4 | **Now proven both ways**: 29 of 152 Samsung files and the iPhone file all carry a display matrix, and mediabunny round-trips it exactly |
| `mebx` / metadata behaviour | not covered | **New**: five `mebx` tracks and all Apple metadata including GPS are dropped silently. Privacy-positive, archival-negative, needs to be a decision |

## Appendix A — measurements taken for this study

Host: Windows 11, 32 logical cores, host ffmpeg 5.1.1 (gyan.dev build, libx264/libx265/
libvpx/libaom/libvmaf/NVENC/AMF). No server, browser or device was started; nothing in
`M:\Own\Pictures` was modified.

### A.1 What phones and cameras actually produce

`ffprobe`, read-only, on `M:\Own\Pictures`:

| file | container | video | audio | size |
|---|---|---|---|---|
| `2025.06/VID_20250607_190950_00_012.mp4` | MOV/MP4 | HEVC Main L5.0 `hvc1`, 3840×2160, 29.97 fps, **30.2 Mbps** | AAC-LC 128 kbps, 48 kHz, stereo | 58.1 s / **220 MB** |
| `2025.06/VID_20250611_184151_00_005.mp4` | MOV/MP4 | HEVC Main `hvc1` 4K30, 30.2 Mbps | AAC-LC 127 kbps | 74.3 s / 282 MB; + a `tmcd` timecode track |
| `2025.07/C0158-Mirage.MP4` | MOV | HEVC **Main 10** `hvc1`, 3840×2160, 59.94 fps, **76.6 Mbps**, `yuv420p10le` | **PCM `pcm_s16be`**, 1536 kbps | 21.5 s / 235 MB; + an `rtmd` metadata track |
| `2025.07/C0160-Sea-Trip.MP4` | MOV | HEVC Main 10 4K60, 75.4 Mbps | PCM s16be | 58.6 s / **604 MB** |
| `2025.05/IMG_5873.mp4` | MP4 (`isom`, Lavf-remuxed) | H.264 High L4.0 `avc1`, 1080×1920 portrait, 29.97 fps, 3.41 Mbps | AAC-LC 128 kbps, 44.1 kHz | 37.4 s / 16.6 MB |
| `2025.05/IMG_5945.mp4` | MP4 (Lavf-remuxed) | H.264 High L4.0, 1080×1920, 30 fps, 4.83 Mbps | AAC-LC 137 kbps | 9.6 s / 5.9 MB |

Caveats: these are action-cam, mirrorless and drone files plus two remuxed iPhone clips —
no untouched camera-roll capture. The `IMG_*` files were rewritten by `Lavf59.27.100`, so
they carry no rotation side data and their container is not what an iPhone writes. What the
set does establish: **4K HEVC at 30–77 Mbps is normal, 10-bit HEVC is normal, PCM audio and
extra data tracks happen, and a single minute can be 220–604 MB** — well inside the app's
500 MB attachment limit.

### A.2 Container and codec passthrough, verified against real muxers

```
VP9 video + -c:a copy (AAC) → WebM
  [webm] Only VP8 or VP9 or AV1 video and Vorbis or Opus audio and WebVTT
         subtitles are supported for WebM.
  Could not write header for output file #0 (incorrect codec parameters ?)

VP9 video + libopus → WebM                     OK
VP9 video + -c:a copy (AAC) → MP4              OK
libaom AV1 + -c:a copy (AAC) → MP4             OK  (streams: av01 + mp4a)
H.264 + -c:a copy (AAC 48 kHz) → MP4           OK  (verified in output)

PCM s16be (Sony MOV) + -c:a copy → MP4
  [mp4] Could not find tag for codec pcm_s16be in stream #1,
        codec not currently supported in container
```

### A.3 Single-threaded software throughput (the WASM ceiling)

Test clip `src1080.mp4`: 30 s, 1920×1080, 29.97 fps, H.264 High at 18.7 Mbps, produced by
downscaling the 4K HEVC sample with `libx264 -crf 18`.

| job | wall | × realtime | fps |
|---|---|---|---|
| decode only, 4K HEVC source, `-threads 1` | 26 s | **1.15×** | 34 |
| decode only, 1080p H.264, `-threads 1` | 7 s | 4.3× | 128 |
| x264 `veryfast`, 1 thread, 2.5 Mbps | 16 s | 1.9× | 56 |
| x264 `ultrafast`, 1 thread | 4 s | 7.5× | 225 |
| x264 `veryfast`, all threads | 2 s | 15× | 450 |
| libvpx-vp9 realtime `cpu-used 8`, 1 thread | 15 s | 2.0× | 60 |
| libvpx-vp9 realtime `cpu-used 5`, 1 thread | 35 s | 0.86× | 26 |
| libaom-av1 realtime `cpu-used 8`, 1 thread | 28 s | 1.07× | 32 |
| 4K HEVC → 1080p + x264 `veryfast`, 1 thread (MT decode) | 20 s | 1.5× | 45 |
| 1080p → 480p + x264 `veryfast`, 1 thread, 800 kbps | 4 s | 7.5× | 225 |
| 1080p → 480p + libvpx-vp9 rt `cpu8`, 1 thread, 700 kbps | 5 s | 6× | 180 |
| 1080p → 480p + libaom-av1 rt `cpu8`, 1 thread | 8 s | 3.75× | 113 |
| full 58 s 4K HEVC → 1080p, x264 `veryfast`, 1 thread, AAC copy | 41 s | 1.4× | — |
| full 58 s 4K HEVC → 480p, x264 `veryfast`, 1 thread, AAC copy | 9 s | 6.5× | — |

Hardware reference on the same host: `h264_nvenc` 1080p, 30 s → **2 s**; `h264_amf` → 4 s.

### A.4 Quality versus speed at equal bitrate

1080p, 10 s, target 1800 kbps, single thread, VMAF against the source.

| encoder | encode | actual | VMAF |
|---|---|---|---|
| x264 `ultrafast` | 1 s | 1784 kbps | 66.57 |
| x264 `veryfast` | 5 s | 1767 | 73.55 |
| x264 `medium` | 12 s | 1745 | 80.42 |
| x265 `veryfast` | 15 s | 1711 | 84.24 |
| libvpx-vp9 realtime `cpu-used 8` | 4 s | 2064 | 82.97 |
| libvpx-vp9 realtime `cpu-used 5` | 10 s | 2042 | 86.04 |
| libaom-av1 realtime `cpu-used 8` | 8 s | 1787 | 85.98 |
| libaom-av1 realtime `cpu-used 6` | 37 s | 1796 | 88.25 |

**Methodology warning, and it is the kind that invalidates published results.** My first
pass compared the encoded files directly (`-i dist -i ref -lavfi "[0:v][1:v]libvmaf"`) and
scored the WebM outputs at **49** instead of 83 — a 34-point error caused by frame pairing
against the WebM container's timestamps, which PSNR reproduced (29.3 dB versus 35.1 for
x264) and which survived every colour-range and container hypothesis I tested. Decoding
both sides to raw Y4M first removed it. `docs/live-video/codec-performance.md` warns that
"any 'they're all identical' result" is a broken harness; this is the same lesson in the
other direction. Treat the table as indicative ordering on one 10-second clip of one
content type against a reference that is itself a CRF-18 re-encode.

### A.5 Size and upload time for a 60-second clip

| source | MB | @1 Mb/s | @3 | @10 | @30 |
|---|---|---|---|---|---|
| iPhone 1080p30 H.264, 3.55 Mbps (measured sample) | 27 | 213 s | 71 s | 21 s | 7 s |
| iPhone 1080p30 HEVC, ~8 Mbps | 60 | 480 s | 160 s | 48 s | 16 s |
| Android flagship 1080p30 H.264, ~17 Mbps | 128 | 1020 s | 340 s | 102 s | 34 s |
| 4K30 HEVC, 30.3 Mbps (measured sample) | 227 | 1818 s | 606 s | 182 s | 61 s |
| target 1080p H.264, 2.5 Mbps | 19 | 150 s | 50 s | 15 s | 5 s |
| target 1080p AV1/VP9, 1.6 Mbps | 12 | 96 s | 32 s | 10 s | 3 s |
| target 480p H.264, 0.8 Mbps | 6 | 48 s | 16 s | 5 s | 2 s |

### A.6 Library payloads

Measured from jsDelivr (`Accept-Encoding`) and the npm registry, 2026-09-12:

| artifact | raw | gzip | brotli | licence |
|---|---|---|---|---|
| `@ffmpeg/core@0.12.10` `ffmpeg-core.wasm` | 30.74 MB | **10.29 MB** | 9.29 MB | GPL-2.0-or-later |
| `@ffmpeg/core-mt@0.12.10` `ffmpeg-core.wasm` | 31.20 MB | — | — | GPL-2.0-or-later |
| `mediabunny@1.56.2` `mediabunny.min.mjs` (full) | 657 KB | **173 KB** | — | MPL-2.0 |
| `mediabunny` MP4-in/MP4-out conversion path (tree-shaken) | 374 KB | **~95 KB** | — | MPL-2.0 |
| `mp4box@2.4.1` `mp4box.all.min.js` | 158 KB | 33 KB | 35.5 KB | BSD-3-Clause |
| `mp4-muxer@5.2.x` / `webm-muxer@5.1.x` | 31 / 30 KB | 9 / 8 KB | — | MIT, both deprecated |
| vendored `libav.js` 6.10.9 `vp9-opus-avf-simd` wasm | **3.85 MB** | — | — | LGPL-2.1 |
| `libav.js` `variant-webcodecs` wasm | 2.34 MB | 905 KB | — | LGPL-2.1 |
| repo baseline: `jpegli.wasm` (SIMD) | 166 KB | — | — | |
| repo baseline: `dist/bundle.js` | 4.46 MB | — | — | |

### A.7 The vendored libav.js build, inspected

String-table inspection of
`src/nodejs/libav/libav-6.10.9.0-vp9-opus-avf-simd.wasm.wasm`: registered containers are
Matroska/WebM, Ogg and the wav/flac family; encoders present are libvpx-vp9 and libopus.
Absent: every `movenc.c` AVOption name (`movflags`, `faststart`, `frag_keyframe`,
`empty_moov`, `write_colr`), every mov-demuxer option name (`use_editlist`,
`ignore_editlist`, `enable_drefs`), `libx264`, `libaom`. **No MP4 muxer, no MP4 demuxer, no
H.264 encoder.** The `mov,mp4,m4a,3gp,3g2,mj2` string is present but unaccompanied by any
mov-specific option, which is consistent with an extension/mime table rather than a linked
(de)muxer.

---

## Appendix B — what I could not verify

Listed so a reader knows where the floor is soft.

1. **Per-version Android System WebView telemetry does not exist publicly.** The floor
   (≥ M138 on Android 10+, ~91 % of the base) is well evidenced; the *distribution above*
   that floor is not. Wikimedia's browser-family TSVs are the only usable raw source, and
   your own `isConfigSupported` + UA-Client-Hints logging would be better.
2. **Safari HEVC Main 10 decode** is inferred from WebKit's routing (the codec string goes
   straight to VideoToolbox with no bit-depth gate), not from an Apple statement or a test.
3. **mediabunny and `dvh1`/`dvhe`.** An inference from reading the shipped demuxer, not a
   test. Stage 0 settles it in ten minutes, and it is the highest-risk unknown in the plan.
4. **iPhone `AVAssetExportSession` 1080p throughput.** No published number exists anywhere;
   my 5–15× realtime is derived from the 4K60-capture bound and desktop VideoToolbox
   figures.
5. **Any mobile WebCodecs encode benchmark.** None exists in public, from any source. The
   repo's own `codec-performance.md` is the closest thing to one and it measures encode time
   only, on generated frames, on two flagship phones. This is why Stage 0 exists.
6. **The `webcodecsfundamentals.org` 2026 codec dataset** (363 M probes, 1.14 M sessions,
   Zenodo 19187467) is the only source for per-codec field support percentages. Its author
   declares the traffic skewed toward video-tool users, it measures browser sessions rather
   than WebViews, and its per-family aggregates are diluted across codec-string variants.
   Used here only as an indication of relative platform shape.
7. **Netflix's published codec-efficiency figures** could not be checked — netflixtechblog
   returns 403 and the Wayback Machine was unreachable. Only secondary coverage was
   available, so those numbers are excluded from §6.3.
8. **Whether Windows `MediaTranscoder`'s `CreateAv1` reaches hardware AV1 encode
   end-to-end** on 24H2 with a current driver. Plausible, unconfirmed. Likewise whether
   Apple's M5 Pro/Max added an AV1 *encoder* — only low-quality secondary sources claim it.
9. **The GPL-WASM-over-HTTP question for ffmpeg.wasm and the LGPL-§6 static-relink
   question for libav.js/GPAC** are unsettled legal questions, not engineering ones. The
   recommendation avoids both by using MPL-2.0 JavaScript.
10. **WebCodecs under iOS Lockdown Mode.** A WebCodecs preference in WebKit's
    `UnifiedWebPreferences.yaml` carries `disableInLockdownMode: true` and it could not be
    attributed conclusively (most likely the AV1 pref, but `WebCodecsVideoEnabled` is not
    ruled out). Untested.
