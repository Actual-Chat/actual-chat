# On-device video transcoding

Status: **researched, not implemented, parked 2026-09-12.** No code was written. This
document is the distilled, standing conclusion; the evidence behind every number is in
[`docs/research/2026-09-12-client-video-transcoding.md`](../research/2026-09-12-client-video-transcoding.md)
(2676 lines, three rounds, commit `7831658811`). Read this first and go there only when you
want to check a claim or disagree with one.

**The question asked:** the client-side *image* pipeline resizes and re-encodes photos in a
worker before upload (jpegli in WASM, quality presets). Could video do the same — presets,
client re-encode, sensible bitrate, keep the original audio track?

**The answer:** yes, in a reduced form, and it is worth roughly three engineer-weeks for the
first version. But it is an *optimisation*, not a capability: the server already transcodes
every video it needs to, so the client path only ever saves upload time and server work, and
every failure path is "upload the original exactly as today".

Throughout: **measured** means traced to a primary source or measured locally; **judgement**
means an opinion you may reasonably disagree with without disturbing the measurements.

---

## 1. The decision, and the two facts that settle it

**Build:** H.264 into MP4, with the original AAC audio track **copied, not re-encoded**, via
WebCodecs driven by [mediabunny](https://mediabunny.dev) in a dedicated module worker.
Presets 480p / 720p / 1080p / Original, mirroring `ImageQualityPreset`.

**Do not build:** AV1 or VP9 output. This was the original request and it does not survive
contact with the data. Two independent facts kill it, and they are listed here so nobody
re-derives them from first principles:

1. **There is no hardware AV1 or VP9 encoder on any phone the team has measured.**
   `docs/live-video/codec-performance.md` (measured 2026-08-30/31, real devices, via
   `VideoEncoder.isConfigSupported` probing each acceleration mode independently):

   | device | AV1 | HEVC | VP9 | H.264 |
   |---|---|---|---|---|
   | Chrome / Windows (RTX 3090) | hw + sw | hw only | **sw only** | hw + sw |
   | Firefox / Windows | sw only | **none** | sw only | sw only, *not realtime* |
   | iPhone 13 Pro (WKWebView) | **none** | hw + sw | hw + sw | hw + sw |
   | Galaxy SM-S948U1 (WebView 151) | **sw only** | hw only | **sw only** | hw + sw |

   Hardware AV1 encode exists on exactly one row, and only because of a desktop GPU. The
   only codec with hardware encode everywhere is **H.264**. Worse, Chromium's *software* AV1
   encoder is deliberately crippled for quality work — `media/video/av1_video_encoder.cc`
   builds libaom with `CONFIG_REALTIME_ONLY`, forces `AOM_USAGE_REALTIME`, uses cpu-used 7–9
   and sets `g_lag_in_frames = 0` (no lookahead) — so even where AV1 runs it does not deliver
   the file-size win it is chosen for. On Android, hardware AV1 encode is a Pixel-10-only
   feature as of 2026; Qualcomm's Snapdragon 8 Elite has neither AV1 nor VP9 encode.

2. **AAC cannot be muxed into WebM. At all.** Verified locally against real muxers:

   ```
   [webm] Only VP8 or VP9 or AV1 video and Vorbis or Opus audio and WebVTT
          subtitles are supported for WebM.
   Could not write header for output file #0 (incorrect codec parameters ?)
   ```

   Phone audio is universally AAC-LC (see §3), so **"keep the original audio" forces MP4
   output**, and VP9/AV1 in MP4 is spec-legal but unevenly played. The two halves of the
   original request — "AV1/VP9" and "keep the original audio" — are mutually exclusive in
   practice. *Judgement:* the audio half is worth more, because re-encoding audio buys under
   1 MB per minute and risks a sync bug (§4.3).

**Apple stays native.** `src/dotnet/Maui/Apple/AppleVideoTranscoder.cs` already works
(AVFoundation, `Hevc1920x1080` preset, Passthrough remux under 70 MB). On MAUI iOS/macOS it
runs in `UploadSession.RunClientProcessing` and the WebView path never sees the file. Keeping
it avoids the WKWebView memory cliff and the fact that **WKWebView has no `AudioEncoder`
before Safari 26** entirely. It has two known maintenance items: Apple's HEVC presets
*preserve* Dolby Vision, so HDR output reaches Android and web viewers where it renders
wrong; and `.status`/`.progress` are deprecated as of iOS 18.

**The server keeps re-encoding, and nothing depends on the client.**
`LocalVideoUploadProcessor` (ffmpeg, libx264, `WithFastStart()`, scale to ≤1080p) and
`GoogleCloudVideoUploadProcessor` (GCP Transcoder, H.264 CRF 23 + AAC 128k) already run
unconditionally, gated by `UploadProcessorHelper.MustConvertVideo`. Firefox on Linux has no
H.264 decoder, Firefox Android has no WebCodecs at all, and plenty of clients will be handed
a source they cannot decode — so the server is the universal fallback, permanently.

### Why do it at all, if the server already transcodes

Because the value is **latency on bad uplinks**, and it is large. At the US median mobile
upload of 18.78 Mbps (Ookla, July 2026), a 1-minute 1080p send goes from 53 s to 24–34 s. At
3 Mbps — roughly **one session in five on the best US network** (Ookla H1 2026: only 79.3 %
of T-Mobile 5G samples met 25↓/3↑) — it goes from 5.5 minutes to about 1.7, and the window in
which a handoff or a backgrounding can kill the transfer shrinks by the same factor. Against
real source files the reduction is **3× to 8.4×** (§3), not the ~4× first assumed.

It also cuts ingress, GCS storage and Transcoder spend by the same factor. It is **not** a
battery win: LTE uplink costs ~0.64 J/MB (Narayanan et al., SIGCOMM 2021), so saving 78 MB
saves ~50 J, while a hardware transcode at 2–6 W for 10–40 s costs 20–240 J. Do not sell it
as one.

Every 1:1 messaging app compresses on-device by default (WhatsApp, Telegram, Signal,
iMessage); every file-sharing and media platform does not (Slack, Instagram, Dropbox —
Dropbox explicitly rejected upfront transcoding as too expensive). Voxt is the first
category.

---

## 2. Platform matrix — encode side

One line per target. **Version floors are the numbers that matter**; the codec columns are
what `isConfigSupported` reports, and hardware availability is per-device, not per-engine.

| target | WebCodecs floor | encode-side reality |
|---|---|---|
| **Chrome / Edge desktop** | `VideoEncoder` **M94** (2021-09-21) | H.264 hardware everywhere (software openh264 fallback does Baseline→High, 8-bit, min dim 16, rejects `bitrateMode:"quantizer"`). HEVC **platform-encoder only, default-on from M130**, Win/macOS/Android only — never Linux or ChromeOS. VP9/AV1 software libvpx/libaom. Hardware AV1 needs RTX 40+, Intel Arc, or RDNA3. **Linux has no hardware encode by default** (VA-API is behind a flag) |
| **Safari desktop** | **16.4** (2023-03-27), audio only from **26.0** | `avc1.*` and `hev1.*` route to VideoToolbox hardware; `vp8`/`vp09`/`av01` fall through to software in the content process. `av01` is exposed **only on devices with a hardware AV1 *decoder*** (A17 Pro / M3+). `hardwareAcceleration` and `bitrateMode` are accepted and **discarded** — they lie in both directions |
| **Android System WebView** | **M94**; realistic floor **≥ M138** (Android 10+, ~91 % of the base). Android 8/9 are capped at M138 forever | H.264 hw + sw; HEVC **hw only, Main 8-bit, Android 10+**; VP9 and AV1 **software only**. Hardware encoders **overshoot the bitrate target by 17–39 %** (measured). **16×16 alignment trap** — see §4.4 |
| **WebView2 (Windows)** | Edge build, M94+, 2-week cadence since v152 | as Chrome desktop. HEVC needs the OS extension; AV1 encode is 24H2+ only |
| **WKWebView (iOS/macOS)** | **16.4**, no entitlement, identical to Safari | as Safari desktop. **No `AudioEncoder` before iOS 26** — so audio passthrough is mandatory there, not an optimisation. Verified working from `app://0.0.0.1` despite the custom scheme |
| **Firefox desktop** | **130** (2024-09-03) | H.264 encoder sits ~18 frames behind and is unusable; no HEVC encoder at all |
| **Firefox Android** | **none** | no WebCodecs. Server fallback, permanently |

Two cross-cutting rules:

- **`prefer-hardware` means hardware-or-fail on Chromium**; `no-preference` silently wraps a
  software fallback at a fraction of the throughput with no event. Always ask for
  `prefer-hardware` and choose the fallback yourself. On WebKit the hint is inert.
- **`isConfigSupported()` validates the config, not the frame.** The classic failure is
  `supported: true` then `OperationError` at `encode()` because the `VideoFrame` is I420 and
  the encoder wants NV12.

**Do not enable cross-origin isolation.** It is architecturally impossible in Android
System WebView (single renderer process — five primary sources agree, including a Chrome
security engineer on whatwg/html#6060), unreachable through the app's custom scheme on Apple,
Safari will never implement `COEP: credentialless`, and on the web it breaks OAuth popups and
third-party iframes. `UseCoopHeaders()` exists in
`src/dotnet/App.Server/Module/ApplicationBuilderExt.cs` and its call site in
`AppHost.Build.cs:236` is **commented out** — leave it that way. Its only purpose is
multithreaded WASM, which this design does not need.

**ffmpeg.wasm is not an option**, for four independent reasons: abandoned (last code commit
2025-09-17, which removed the "looking for maintainers" line), a **GPL** build (verified from
the configure string inside the published `.wasm`), **10.29 MB gzipped**, and **0.078×
realtime** single-threaded at 720p by the project's own benchmark. Its threaded core needs
`SharedArrayBuffer`, which the previous paragraph rules out.

---

## 3. What real files actually look like

This section exists so nobody designs against imagined inputs. Two corpora, both read-only.

### 152 videos, `M:\Downloads\2025-Turkey` — Samsung Galaxy (Android 15) and Ray-Ban Meta glasses

| property | distribution |
|---|---|
| codec | **149 HEVC**, 3 H.264 High |
| bit depth | 83 × 8-bit, **69 × 10-bit HEVC Main 10 — 45 %** |
| colour | the 10-bit files are **HLG / BT.2020 / bt2020-ncl** |
| resolution | **45 × 3840×2160**, 39 × 1920×1080, 65 × mod-16 portrait (1360–1504 wide, the glasses), 3 × 1080×1920 |
| rotation matrix | **29 of 152 (19 %)** |
| audio | **100 % AAC-LC, 48 kHz, stereo** — 256 kbps (Samsung) or 128 kbps (glasses) |
| bitrate | 4K: median **33.6** Mbps (max 76.6). 1080p: median **12.0** Mbps (max 60). Glasses ~15–19 Mbps |
| frame rate | variable (`frameRateIsConstant: false` on every Samsung file) |
| faststart | **none** — Samsung writes `ftyp → mdat → moov` |

### `IMG_0226.MOV` — iPhone 13 Pro, iOS 26.6.2, default camera settings

```
video  hevc Main 10, tag hvc1, 1920x1080, yuv420p10le, 12.48 Mbps, CFR 30
       bt2020 primaries / arib-std-b67 (HLG) transfer / bt2020nc matrix
       DOVI record: profile 8, level 4, rpu_present=1, bl_signal_compatibility_id=4
       display matrix: rotation = -90
audio  aac LC, 48 kHz, stereo, 169 kbps
+ 5 x mebx timed-metadata tracks
container  major_brand=qt, com.apple.quicktime.model=iPhone 13 Pro,
           com.apple.quicktime.location.ISO6709=+33.5004-117.6963+157.984/
```

### What follows from this

- **HEVC input is the norm, not an exception** — 149 of 152 Samsung/glasses files and the
  iPhone file. Hardware HEVC *decode* is therefore on the critical path. Note
  [androidx/media#2711](https://github.com/androidx/media/issues/2711): HEVC hardware decode
  produces black frames on MediaTek Dimensity 700/900/1080 after the Android 15 upgrade.
- **10-bit HLG HDR is the default output of both major phone vendors.** Apple has recorded
  HDR by default since the iPhone 12; recent Samsungs do the same. This is the single most
  consequential fact in the document — see §4.1 and §5.
- **Audio passthrough is viable for 100 % of both corpora.** Every file is AAC-LC 48 kHz
  stereo. Samsung's 256 kbps also means re-encoding to 128 kbps would save ~1 MB/min against
  a video saving of 60–220 MB. Copy it.
- **Dolby Vision is not a blocker.** The iPhone file is `hvc1` + a DOVI record with
  `bl_signal_compatibility_id=4` — Profile **8.4**, the cross-compatible family, which keeps
  the base-layer sample entry so non-DV players see plain HEVC. mediabunny demuxes it fully.
  `dvh1`/`dvhe` (Profile 5 — streaming rips, some Android DV modes) **is** refused, but
  safely: see §4.2.
- **The savings are bigger than a first estimate suggests**, because 30 % of the corpus is 4K:

  | real source | MB/min | → 1080p H.264 @4 Mbps | reduction |
  |---|---|---|---|
  | 4K30 HEVC, 33.6 Mbps | 252 | 30 MB | **8.4×** |
  | 1080p30 HEVC, 12.0 Mbps | 90 | 30 MB | **3.0×** |
  | glasses 1376×1824, ~15 Mbps | 113 | ~23 MB | **4.9×** |

  Break-even uplink for the 4K case, even at a pessimistic 35 s of work per minute of video,
  is ~60 Mbps — far above any mobile connection. **4K input is the priority case, not the
  scary one.**

### Recommended bitrate targets

Three independent codebases converge on **~1 Mbps at 480p H.264** (Signal's
`VideoConstants.kt` 1.0; Telegram-Android's `makeVideoBitrate` 0.9–1.0; Telegram-iOS 1.6),
and 1.5–2.6 Mbps at 720p. That convergence is the most robust number available.

| preset | video target / cap | audio |
|---|---|---|
| 854×480 @30 | **1000 / 1400 kbps** H.264 High (Main for old-Android safety) | **copy**; 96 kbps stereo if it must be re-encoded |
| 1280×720 @30 | **2000 / 2600 kbps** | **copy**; else 128 kbps |
| 1920×1080 @30 | **4000 / 6000 kbps** | **copy**; else 128 kbps |
| 60 fps | × **1.5** on video | |

Settings worth copying from the peers: **1-second GOP** (both Telegram's
`KEY_I_FRAME_INTERVAL = 1` and Signal's `OUTPUT_VIDEO_IFRAME_INTERVAL = 1` — chat clips get
scrubbed and thumbnailed constantly, and it costs 5–10 % bitrate); preserve source frame rate
capped at 30; key the ladder on the **short edge** or a **longest-edge bounding box**, never
pixel count, so portrait behaves (`UploadProcessorHelper.ScaleToFullHd` already has the right
shape); faststart; and **read the encoder's achieved bitrate back after `configure()`**,
because Telegram's `extractRealEncoderBitrate` exists precisely because Exynos encoders raise
it silently, and the repo measured Android hardware encoders overshooting by 17–39 %.

---

## 4. Traps, each with its evidence

These cost a day each to rediscover. All were hit during the research.

### 4.1 A 10-bit HLG source transcoded naively is a visible regression

Measured on a real Samsung HLG clip:

| path | result |
|---|---|
| naive re-encode, no pixel format forced | **H.264 High 10 / `yuv420p10le`** — a profile WebCodecs cannot produce and most hardware decoders cannot play |
| forced 8-bit, no transfer conversion | 8-bit, still tagged `arib-std-b67`/`bt2020`; **mean saturation 48** |
| `zscale → tonemap=hable → bt709` | correctly tagged `bt709`; **mean saturation 78** |

A **39 % saturation loss** — the washed-out, flat look. **WebCodecs has no tone-mapping
primitive**, so the conversion would have to be a shader between decode and encode
(`Services/Video/webgl/` and `webgpu/` already host the downscalers and are the right home).

**Important caveat, and it is the open question in §6 probe 2:** that 39 % was measured
through ffmpeg's raw pixel path, which is the worst case by construction. A WebCodecs
pipeline that draws the decoded `VideoFrame` through a 2D canvas — what the existing
`CanvasDownscaler` does — passes through the browser's own colour management on
rasterisation. **If that is adequate, HDR handling is nearly free and §5's Stage 3 is
polish. If it is not, Stage 3 is a prerequisite for covering iPhone and ~45 % of Android
footage.** One probe decides it.

### 4.2 `Conversion.isValid: true` and a resolved `execute()` prove nothing

The worst trap, demonstrated end to end on the owner's real iPhone file. With the video
track undecodable:

```
isValid         : true
utilizedTracks  : audio
discardedTracks : [{"t":"video","r":"undecodable_source_codec"}]
--- executing anyway ---
execute() RESOLVED — no error thrown.
```

**What landed on disk was a 47,707-byte audio-only MP4, from a 3.4 MB video.** A caller that
checked `isValid`, awaited `execute()`, saw no error and uploaded the result would have
replaced the user's video with its soundtrack.

> **Abort if `discardedTracks` contains the video track. Then assert the output has a video
> track before committing it.** `isValid` means "the output format can hold what is left",
> not "the job is worth doing".

The `reason` codes distinguish the two failure classes usefully:
`unknown_source_codec` (the demuxer did not recognise the track — the `dvh1` case) versus
`undecodable_source_codec` (recognised, but this platform cannot decode it).

### 4.3 AAC priming is off by exactly one frame, even on a packet copy

Edit lists from the real iPhone file, source versus a mediabunny packet-copy remux:

| track | source | remux |
|---|---|---|
| video | `elst media_time=0` (a no-op) | no elst — correctly dropped |
| audio | `media_time=2112 @ 48000` = **44.0 ms** trimmed | `media_time=1088 @ 48000` = **22.7 ms** trimmed |

`2112 − 1088 = 1024` samples — **exactly one AAC access unit at 48 kHz, 21.33 ms.** So the
output begins with ~21 ms of priming silence that should have been trimmed, and **audio leads
video by ~21 ms**. ITU-R BT.1359-1 puts the perceptibility threshold for audio leading video
at about 45 ms, so this is below it — but it happens on a *pure copy*, where nothing is
re-encoded, and it is the bulk of the error budget.

Across six real files, mediabunny's edit handling is accurate to **±1 frame — one audio
access unit or one video frame — always in the direction of audio leading video.** The
Samsung files whose *video* track carried an `elst` trim came back with video +13.5–13.9 ms
late. Assert this in a test rather than trusting it, and check a long clip for accumulation.

Note this **corrects** an earlier belief that copying audio sidesteps
[mediabunny#444](https://github.com/Vanilagy/mediabunny/issues/444). It reduces it; it does
not remove it.

### 4.4 Android crops to 16×16, and 1080 is not a multiple of 16

`ndk_video_encode_accelerator.cc`: *"Non 16x16 aligned resolutions don't work well with
MediaCodec unfortunately, see <https://crbug.com/1084702>"* — Chromium crops to the nearest
16×16 when stride information is absent. **1080 = 67.5 × 16**, so 1080-tall output is exactly
this case. Round output dimensions to a multiple of 16 on Android, not merely to even numbers
as `ScaleToFullHd` does. Relatedly,
[w3c/webcodecs#397](https://github.com/w3c/webcodecs/issues/397): `avc1.42001e` (Baseline
**level 3.0**) encoder creation fails at 720p/1080p on Android while working on desktop —
match the level to the resolution, as `getCodecForCategory` already does.

### 4.5 `mebx` tracks and all Apple metadata are dropped silently

The iPhone file's **five `mebx` timed-metadata tracks** are not in `getTracks()` at all
(mediabunny returns 2 tracks where ffprobe sees 7), emit no warning, and do **not** appear in
`discardedTracks`. A caller cannot learn they existed.

Also dropped by a remux: the **DOVI configuration record** (correct for Profile 8.4 — the
base layer *is* the HLG presentation, so this yields the designed fallback), and **all Apple
metadata**, including **GPS coordinates to four decimal places**, device make/model and the
iOS version. The source carries them; the output carries only `creation_time`.

*Judgement:* all three losses are acceptable — nothing downstream reads `mebx`, and the
server's libx264 path drops it too, so nothing regresses. But the GPS stripping should be
**intentional**, because it creates an asymmetry worth designing around: `ImageQualityPreset`
has a separate `OriginalWithExif` so a user can deliberately keep metadata, whereas a video
*Original* preset (which uploads the source untouched) would keep GPS while every reduced
preset strips it.

### 4.6 Smaller API traps

- **`fit` is required** when both `video.width` and `video.height` are passed, or
  `Conversion.init` throws a `TypeError`. `'contain'` matches `ScaleToFullHd`'s longest-edge
  behaviour.
- **Never `flush()` mid-file.** A keyframe is required after `configure()` *and after every*
  `flush()` — normative, enforced in both engines.
- **`close()` every `VideoFrame` in the output callback.** Dropping a reference without
  closing is the most common way to stall a decoder, and the symptom is not an error: output
  just stops.
- **HEVC decode needs the `description`** (`hvcC`) on every engine, and a **zero-size
  description crashes WebKit's GPU process** (bug 308901, open). Validate the bytes.
- **Sort decoder output by `timestamp`.** Safari emitted frames out of order until 26.4
  (H.264) and 26.5/27 (HEVC).
- **`QuotaExceededError` is codec reclamation** on Chromium when backgrounded; Android can
  reclaim `MediaCodec` independently; iOS suspends the whole host app. Do not resume —
  abandon and upload the original.
- **Rotation is not applied by decoders.** `VideoFrame.rotation` arrived in Chrome 138 and
  exists in neither Firefox nor Safari. Prefer baking rotation into the frames: mediabunny
  can do either, and many players ignore the metadata.

### 4.7 What works, verified, so you do not re-litigate it

- **Rotation round-trips exactly** through a mediabunny remux, on all six real files tested.
- **Edit lists are written.** An empty edit round-tripped to within 4 µs. The issue title
  [#447](https://github.com/Vanilagy/mediabunny/issues/447) ("does not write an edit list")
  is narrower than it sounds, or fixed in 1.56.2.
- **faststart is available and cheap**, and the client output is *better* than the Samsung
  source, which has none. Measured on a 71.6 MB file: `false` 170 ms / 68 MB buffered;
  `'in-memory'` 156 ms / 88 MB; `'fragmented'` 174 ms / 55 MB and also moov-first.
- **The container layer is free** — 170 ms to rewrite 71.6 MB. All the cost in a real
  transcode is decode and encode.
- `'in-memory'` buffering scales with file size, so extrapolating to a 500 MB input gives
  ~600 MB RSS — over the iOS WebContent jetsam budget. Use `'in-memory'` for small clips and
  `false` or `'fragmented'` above a threshold.

---

## 5. Staging

*Judgement throughout. Engineer-days for one experienced engineer including tests and device
verification.*

### Stage 0 — measure before building (2–3 d)

Run the probes in §6, **in this order**, because probe 2 decides the shape of Stage 2:

1. **Probe 2 first** — what a decoded 10-bit HLG frame looks like, and whether a 2D-canvas
   round trip preserves saturation. This decides whether HDR is nearly free or needs a
   shader, and therefore whether Stage 3 is polish or a prerequisite.
2. **Probe 4** — end-to-end throughput on real devices: an iPhone (one older, one current)
   in the app's WKWebView, a flagship Android, **a mid-range Android on Android 15**, desktop
   Chrome and Safari. This single ×realtime number decides whether the feature pays at all.
3. Probes 1 and 3 for the capability matrix and the mod-16 question.

Also worth measuring here, since it calibrates everything: **`AVAssetExportSession` 1080p
throughput on a real iPhone.** No published number exists anywhere; the estimate is 5–15×
realtime, derived from the 4K60-capture bound.

Device matrix: iPhone × 2, flagship Android, **mid-range Android on Android 15** (the
population that `androidx/media#3399` breaks and that Pixels and emulators do not reproduce),
Windows desktop Chrome + Edge, macOS Safari, Firefox.

### Stage 1 — the web-side client-processing seam (4–6 d)

Today client processing is **MAUI-only by construction**:

```csharp
// UploadSession.cs:152, RunClientProcessing
var filePath = (fileProvider as MauiFileProvider)?.FileRef ?? FilePath.Empty;
```

On the web `filePath` is empty and `VideoTranscoder.Transcode` returns immediately. This
stage generalises the seam so a `WebFileProvider` can participate — exactly the two-branch
shape `ImageAttachmentProcessor` already has, where `ProcessWeb` takes a Blob and
`ProcessMaui` takes a content URL and **both end in the same JS worker**. That precedent is
the strongest architectural argument in the whole plan: jpegli-in-WASM already serves MAUI
from inside the WebView.

On MAUI the handoff needs care: `GetContentUrl` + `fetch` into a Blob is what images do and
it will not survive a 200 MB video. Aim for a range-capable `BlobSource` over the
`AndroidContentDownloader` / `ContentSchemeHandler` URL so the demuxer reads incrementally.

Also in this stage: the preset enum and menu, mirroring `ImageQualityPreset` and
`ImageQualityMenu.razor` — but **with no size estimate**. For an image the estimate is a
second encode of an already-decoded bitmap; for video it would be a second full transcode.
Show resolution up front and the achieved size afterwards.

This stage has value independent of video.

### Stage 2 — minimum viable transcoder (7–9 d)

mediabunny `Conversion` in a dedicated module worker, mirroring `ImageProcessor`'s
one-job-at-a-time discipline and its timeout-means-the-worker-died detection
(`image-processor.ts:30-37` — a worker killed for memory never raises `error`).

- **H.264 into MP4, audio copied.** Presets per §3.
- The nine-step pre-flight gate (§5.1 below).
- `latencyMode: 'quality'`, 1 s GOP, `avc: { format: 'avc' }`,
  `hardwareAcceleration: 'prefer-hardware'`, achieved bitrate read back.
- Every trap in §4.
- Progress into the existing `StageProgress`; cancellation through the existing
  `CancellationToken`; **failure always falls back to uploading the source.**
- Hook the existing `ThermalTracker` so a `Serious`/`Critical` device skips client work.
- Feature-flagged, Chromium targets and WKWebView 16.4+ only. `AppleVideoTranscoder` stays
  and runs first on MAUI Apple.

#### 5.1 The pre-flight gate — nothing here reads the file body

| # | check | how |
|---|---|---|
| 1 | worth attempting | `video/*`; size between a floor (~2 MB) and a hard ceiling; `ThermalLevel < Serious`; preset ≠ Original |
| 2 | container parses | `new Input({ source: new BlobSource(file), formats: [MP4, QTFF] })` → `getPrimaryVideoTrack()` |
| 3 | codec understood | `track.codec !== null` **and** `getDecoderConfig()` returns a `description` |
| 4 | platform can decode | `await track.canDecode()` |
| 5 | platform can encode the **output** | `getFirstEncodableVideoCodec(['avc'], { width: outW, height: outH, bitrate })`, `prefer-hardware` |
| 6 | dimensions legal | mod-16 on Android, even elsewhere; H.264 level matched to resolution |
| 7 | HDR policy | `track.hasHighDynamicRange()` → decline **unless probe 2 said canvas colour management is adequate** |
| 8 | the conversion agrees | `Conversion.init(...)` → abort if the **video** track is in `discardedTracks` (§4.2) |
| 9 | worth it | predicted output ≈ `(videoBitrate + audioBitrate) × duration`; abandon unless ≤ ~85 % of source |

Cost: a handful of `isConfigSupported` calls plus a few `moov` range reads. For scale, a
*full* 71.6 MB packet-copy remux took 170 ms, so metadata-only inspection is trivial. Cache
steps 4–5 where `codec-support.ts` already caches `probeEncoder` results.

#### 5.2 The server needs no change

`MustConvertVideo` already re-derives codec, container and resolution from `ffprobe` and
skips the transcode when they are acceptable — so **a client-produced H.264 ≤1080p MP4
already falls through the existing gate**. Any declared hint (preset, codec, dims, bitrate,
audio-copied) is therefore an *optimisation, never a contract*, which is the safest shape for
untrusted input: the server's decision never depends on it. Use the hint for telemetry and
for logging declared-versus-observed mismatches as client bugs.

The server must keep verifying what it already verifies: container parses, codec in the
allowlist, MP4 brand, resolution within limits, and a frame extractable — `Snapshot` is the
de facto validator. One pre-existing decision this surfaces but does not answer:
`MustConvertVideo` passes **HEVC** through, so HEVC uploads reach Firefox users unplayable,
and if clients start emitting H.264 the HEVC arrivals become disproportionately the files
where the client declined.

#### 5.3 Mid-flight failure policy

The source is never consumed (`UploadSession` holds the `FileProvider`; `TranscodedFilePath`
is set only on success), so **abandoning is free**. Every failure: log, discard, upload the
original.

| failure | response |
|---|---|
| `OperationError` on the first frames (I420/NV12) | one retry converting pixel format, then abandon |
| `configure()` rejected at this resolution | retry once mod-16 and level-matched; record the codec string in the `excludeEncoderCodecString` localStorage set `codec-support.ts` already maintains |
| codec reclaimed while backgrounded | **do not resume** — abandon; the upload matters more than the saving |
| decode stalls silently | watchdog; the live pipeline's `operators/downscale.ts` has the pattern (1.5 s, ≤ 4 in a row) |
| worker killed for memory | RPC timeout with no `error` event; recreate the worker, abandon the job |
| output not smaller | keep only if ≤ ~85 % of source — `AppleVideoTranscoder` already does the moral equivalent via `EstimateOutputFileLengthAsync()`. Apply it to the remux-only case too |

Record a third `ClientProcessing` outcome — *attempted and declined, with a reason* — so
telemetry can answer "how often does this fire" without guessing.

### Stage 3 — raise the ceiling (8–11 d) — **conditional**

**Whether this is polish or a prerequisite is decided by probe 2.** If a canvas round trip
does not preserve colour, the HLG→BT.709 tone-mapping shader in `webgl/` is required to cover
iPhone-on-web at all and ~45 % of Android footage; if it does, this stage is quality work.

Also here: 4K and 10-bit source handling; long clips via `StreamTarget` with
`fastStart: false` (and a decision about whether the server's `ffprobe` and snapshot
extraction tolerate moov-last); rotation baked into frames if metadata proves unreliable; a
hard input-size ceiling.

### Stage 4 — Windows native (1–2 d), optional

`Windows.Media.Transcoding.MediaTranscoder` — projected automatically for the MAUI Windows
TFM, no extra package. `CreateMp4`, `StorageFile` paths (**not** `.AsRandomAccessStream()`,
per CsWinRT#1386), capability detection for HEVC (Store extension) and AV1 (24H2+ only), and
a test asserting the output bitrate (WindowsAppSDK#4804 silently ignores it on some AMD
iGPUs). Lowest priority — fixed upload is ~56 Mbps, so it matters least.

### Stage 5 — Android native (10–15 d), only if Stage 0 demands it

Media3 `Transformer` 1.11.1 via `Xamarin.AndroidX.Media3.Transformer`. Do this only if the
WebView path measures too slow or too unreliable, because the WebView path already measured
2.43 ms/frame for hardware H.264 at 720p on a Galaxy. If you do: enable
`setEnableCodecDbLite(true)` (opt-in, `false` by default — it is Google's chipset-keyed
encoder-override database and the sanctioned mitigation for per-device rejections); decide
`setEnableFallback` deliberately (it silently changes output resolution by default); force SDR
tone-mapping; pin a coherent version across Transformer/Common/Effect (they do not ship in
lockstep); and budget for `ExportException.CodecInfo` being **unbound in C#**
([dotnet/android-libraries#1263](https://github.com/dotnet/android-libraries/issues/1263)) —
the diagnostic field you most need. Also smoke-test a Samsung A-series device for
[androidx/media#3399](https://github.com/androidx/media/issues/3399).

### Totals

| | days |
|---|---|
| **Stages 0–2 — the real first version** | **13–18** |
| + Stage 3 | 21–29 |
| + Stage 4 (Windows native) | 22–31 |
| + Stage 5 (Android native) | 32–46 |

---

## 6. Open questions — the probes, verbatim

None of these were run; each is self-contained. Probe 0 needs only a shell.

### Probe 0 — confirm Dolby Vision carriage on another iPhone model

Mostly settled: an iPhone 13 Pro on iOS 26.6.2 produced `hvc1` + a DOVI record (profile 8,
`bl_signal_compatibility_id=4`), which mediabunny reads fully. Re-run on a different model or
iOS version if you want reassurance.

```bash
ffprobe -v error -select_streams v:0 \
  -show_entries stream=codec_name,codec_tag_string,profile,pix_fmt,color_transfer \
  -show_entries stream_side_data=dv_profile,dv_bl_signal_compatibility_id \
  -of default "IMG_XXXX.MOV"
```

**Decides:** `codec_tag_string=hvc1` → mediabunny is fine. `dvh1`/`dvhe` → that model needs
`mp4box@2.4.1` as a fallback demuxer (it does know `dvh1`).

### Probe 1 — can this engine decode real phone HEVC, and does `prefer-hardware` mean anything?

The `hvcC` below is the real 158-byte description from a Samsung HEVC Main 10 HLG 1920×1080
file, so the probe is self-contained. Paste into the console of the browser or WebView under
test.

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

**Decides:** whether Main 10 (`hev1.2.4.*`) decodes; whether `dvh1` is accepted or rejected;
and whether the acceleration hint is honoured (meaningful on Chromium, inert on WebKit).

### Probe 2 — **run this first.** What a decoded 10-bit HLG frame looks like

The highest-value unknown. Needs the real file served same-origin — put it in the app's
`wwwroot`, which is the trick `docs/live-video/codec-performance.md` documents under
"Reproducing this".

```js
// after fetching the file and demuxing one keyframe into an EncodedVideoChunk `chunk`
const dec = new VideoDecoder({
  output: f => {
    console.log('format      :', f.format);          // null for 10-bit HDR is the blocking case
    console.log('colorSpace  :', JSON.stringify(f.colorSpace));
    console.log('display     :', f.displayWidth, 'x', f.displayHeight);
    console.log('rotation    :', f.rotation, 'flip:', f.flip);   // Chrome 138+ only
    try { console.log('allocSize   :', f.allocationSize()); }
    catch (e) { console.log('allocationSize THREW', e.name, e.message); }
    // the decisive part: does a canvas round trip preserve colour?
    const c = new OffscreenCanvas(f.displayWidth, f.displayHeight);
    const ctx = c.getContext('2d');
    ctx.drawImage(f, 0, 0);
    const d = ctx.getImageData(0, 0, c.width, c.height).data;
    let rs = 0, gs = 0, bs = 0, sat = 0;
    for (let i = 0; i < d.length; i += 4) {
      const r = d[i], g = d[i+1], b = d[i+2];
      rs += r; gs += g; bs += b;
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
      sat += mx === 0 ? 0 : (mx - mn) / mx;
    }
    const n = d.length / 4;
    console.log(`canvas means R${(rs/n)|0} G${(gs/n)|0} B${(bs/n)|0} meanSat=${(255*sat/n)|0}`);
    f.close();
  },
  error: e => console.log('decoder error:', e.name, e.message),
});
dec.configure({ codec: 'hev1.2.4.L120.B0', codedWidth: 1920, codedHeight: 1080, description: desc });
dec.decode(chunk);
await dec.flush();
```

**Decides everything downstream.** `format === null` means the frame cannot be read back on
the CPU and mediabunny's `allocationSize()` path fails
([#256](https://github.com/Vanilagy/mediabunny/issues/256)). A `meanSat` near the
tone-mapped reference (~78 on the test clip, versus ~48 for an untone-mapped 8-bit
conversion) means **canvas colour management is adequate, Stage 3 is polish, and v1 covers
~100 % of iPhone and Android web uploads.** A `meanSat` near 48 means **Stage 3's shader is a
prerequisite and v1 covers ~0 % of iPhone and ~55 % of Android**. Pair the numbers with a
visual comparison.

### Probe 3 — encoder config, including the mod-16 question

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

**Decides:** the real per-device encode matrix, and whether `latencyMode: 'quality'` is
accepted everywhere. The mod-16 rows are expected *not* to differ here, because `MediaCodec`
crops silently rather than refusing — which is why probe 4 must check output dimensions.
Expect `false` / `undefined` for cross-origin isolation in the MAUI WebViews.

### Probe 4 — the end-to-end measurement that decides the feature

```js
import { Input, Output, Conversion, ALL_FORMATS, BlobSource, Mp4OutputFormat, BufferTarget }
  from '/dist/mediabunny.min.mjs';
const file = await (await fetch('/clips/1080p-hlg.mp4')).blob();
const input = new Input({ source: new BlobSource(file), formats: ALL_FORMATS });
const output = new Output({ format: new Mp4OutputFormat({ fastStart: 'in-memory' }), target: new BufferTarget() });
const c = await Conversion.init({
  input, output,
  video: { codec: 'avc', width: 1080, height: 1920, fit: 'contain', bitrate: 4e6 },
});
console.log('discarded:', c.discardedTracks.map(d => `${d.track.type}:${d.reason}`));
if (c.discardedTracks.some(d => d.track.type === 'video')) throw new Error('video discarded');
const dur = await input.computeDuration();
const t0 = performance.now();
await c.execute();
const s = (performance.now() - t0) / 1000;
console.log(`${s.toFixed(1)} s for ${dur.toFixed(1)} s => ${(dur / s).toFixed(2)}x realtime`);
console.log('in:', file.size, 'out:', output.target.buffer.byteLength,
  `(${(100 * output.target.buffer.byteLength / file.size).toFixed(0)}% of source)`);
// verify the output actually has video, and at the dimensions you asked for
const back = new Input({ source: new BlobSource(new Blob([output.target.buffer])), formats: ALL_FORMATS });
const vt = await back.getPrimaryVideoTrack();
console.log('output video:', vt && `${vt.codec} ${vt.codedWidth}x${vt.codedHeight} rot=${vt.rotation}`);
```

**Decides:** whether the feature pays. Run on an iPhone (older and current) in the app's
WKWebView, a flagship Android, a mid-range Android on Android 15, desktop Chrome and Safari.
Record ×realtime, output size, the output's actual coded dimensions (the mod-16 check), and
whether the renderer survives a 4K input.

**Reference points for interpreting it.** Mid-range Android hardware encode is estimated at
1.5–3× realtime (anchored on Google's measured "one minute of HEVC → AVC in roughly 20
seconds on a Pixel 3"); flagship 4–8×. Break-even uplink is `734 / T` Mbps for a 1080p
source, where `T` is seconds of work per minute of video — so **T below ~40 s beats the
18.78 Mbps median, and anything at or below ~1× realtime does not.** Software WASM encoding
sits at 0.07–0.8× realtime and is strictly dominated; it is not a fallback.

### Still unresolvable without devices

iPhone `AVAssetExportSession` 1080p throughput (no published figure exists anywhere);
per-version Android WebView telemetry (no public dataset — your own logging is the answer);
whether Windows `MediaTranscoder.CreateAv1` reaches hardware end to end; WebCodecs under iOS
Lockdown Mode (a WebCodecs preference carries `disableInLockdownMode: true` and could not be
attributed conclusively).

---

## 7. What would change the answer

Revisit this plan if any of these becomes true.

| change | effect |
|---|---|
| **Hardware AV1 encode reaches mid-range phones** | Reopens AV1, but only together with the audio question: AV1-in-MP4 with AAC copied is already legal and was verified to mux, so the blocker becomes player support for AV1 in MP4, not the codec. Today hardware AV1 encode on Android is Pixel 10 only |
| **WebCodecs gains a tone-mapping primitive, or `VideoFrame` gains an HDR→SDR conversion** | Removes §4.1 entirely and makes Stage 3 unnecessary. Watch the w3c/webcodecs HDR discussions |
| **Opus-in-MP4 playback becomes dependable** | Would let VP9/AV1 output keep a re-encoded audio track in a widely-played container, weakening fact 2 in §1 |
| **Phones stop defaulting to 10-bit HDR capture**, or ship an SDR-compatible dual output | Removes the largest coverage constraint |
| **Probe 2 shows canvas colour management is adequate** | Stage 3 becomes polish; v1 coverage goes from ~55 % of Android and ~0 % of iPhone-on-web to ~100 % of both. **This is the cheapest thing that can improve the plan and should be done first** |
| **mediabunny stalls** (bus factor 1 — 1,147 of ~1,350 commits by one author) | Fallback is `mp4box@2.4.1` for demuxing plus a muxer, at materially more work. Mitigating: MPL-2.0, no WASM, readable TypeScript, every iOS-specific issue closed, and Remotion migrating onto it |
| **The server stops transcoding** | Would invert the whole design: the client path would become load-bearing and would need the reliability budget this plan deliberately avoids spending |
| **Attachment limits change** | The 500 MB `Constants.Attachments.FileSizeLimit` is the upper bound on what a WebView must survive; raising it raises the memory risk, lowering it shrinks it |

**The single most likely reason to abandon this:** probe 4 measures mid-range end-to-end
throughput at or below ~1× realtime — because decode, not encode, dominates, and HEVC
hardware decode is itself fragile. If that happens, stop after Stage 1: the client-processing
seam is useful on its own, and the server already does the work.
