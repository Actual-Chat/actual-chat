# 03 — Codecs, simulcast layers, and keyframe policy

This doc covers how the system picks a codec, builds a simulcast ladder, and
decides when to emit keyframes. These three are tied together because a layer
switch on the receiver is only safe at a keyframe boundary.

## Codec detection (sender side)

File: `src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts`.

`detectSupportedCodecs(width, height)` probes one representative encoder per
category to keep startup cheap. Categories:

- **AV1** (`av01.*`) — currently disabled (mobile parity issues).
- **HEVC** (`hev1.*` / `hvc1.*`).
- **VP9** (`vp09.*`) — disabled by default; H.264 is preferred over VP9 for
  consistent HW support.
- **H.264** (`avc1.*`) — universal fallback.

For each enabled category, detection probes the one profile
`getEncoderCodecLadder` returns and reports whether it passes, so we never
advertise a profile that wasn't probed. H.264 is **Constrained Baseline only**
(`avc1.42E01F` / `42E028` / `42E034` by resolution): Main and High carry CABAC
and B-frames, which this project does not emit, and CBP is the only profile
Chromium's software encoder implements, so the hardware→software fallback keeps
the same profile. A device that rejects it falls back to another *category*.
Each entry is probed by `isCodecSupported()`, which first tries
`prefer-hardware`, then `no-preference` (Firefox is bad about the former).

Detection also measures **encoder latency** (`probeEncoderLatencyFrames`):
frames submitted at the frame interval, counting how many stay in flight once
the encoder is warm. Startup is excluded deliberately — Chromium's hardware
encoders are silent for ~215 ms and then track submissions exactly, while
Firefox's H.264 stays ~18 frames behind for the whole stream and is the case
this disqualifies. A codec that fails carries `realtime: false` and never
reaches candidate selection.

Result: `CodecInfo { name, codec, category, supported, hardwareAccelerated,
realtime }`.

Temporal SVC is gone — `scalabilityMode` is not probed, not on `LayerConfig`,
and appears nowhere in `src/`. It was removed in `3ae12d7f8`; only vestigial
`TemporalLayerId` / `TemporalLayerCount` remain on the receive-side wire DTO.

A profile that fails `configure()` at runtime is excluded by **codec string**
(`excludeEncoderCodecString`), not by category, so a level one resolution
rejects doesn't cost the codec everywhere. Only `FLOOR_CATEGORY` (VP9) is
protected from exclusion — H.264 lost that protection when it stopped being the
floor.

`getDefaultCodec(supported, w, h)` is only a last resort now: the sender picks
from `listCodecCandidatesByEfficiency`, which filters to what the *audience*
can decode, drops anything with `realtime: false`, and honours the
"prefer encode codec" debug override. `getDefaultCodec` answers without
reference to the audience, so it runs only when no candidate qualifies at all,
and even then the floor is preferred over its answer.

`getCodecForCategory(category, w, h)` always returns the highest level in the
category (e.g. H.264 CBP 5.2 above 1080p) and keeps it constant within a
session, so a
single `VideoEncoder` can absorb dim/bitrate reconfigures mid-run without a
cold NVENC re-init. Encoders are NOT pooled across `start/stop` cycles —
every recorder run constructs a fresh `VideoEncoder` so its first chunk is
guaranteed to be an intra-coded keyframe (see `02-sender.md`).

`probeEncoder(codec, layers)` measures steady-state encode time (median of
last 5 frames after 3 warm-up). The 33 ms budget = one frame at 30 fps.
Cache key: `codec@WxH×layer-count`.

### Receiver-side codec selection

The receiver registers its decoder support every 30 s
(`ILiveVideoStreams.RegisterMember(session, chatId, supportedDecoderCodecs)`).
The server's `LiveVideoBackend` intersects all members' codecs and exposes the
result via `GetSupportedCodecs(chatId)`. Senders use that to pick the codec
for the encoder. Codec **upgrades** are delayed by
`CodecSwitchHysteresisWindow = 10 s` to avoid flapping; **downgrades** apply
immediately (e.g., a Safari user joining forces H.264).

When a receiver decoder fails repeatedly, `getCodecCategory(codecString)` is
mapped, and `excludeDecoderCodec(codec)` adds it to a localStorage exclusion
set; subsequent `RegisterMember` calls report a smaller list and the server
re-negotiates.

## Hardware acceleration

File: `src/dotnet/UI.Blazor.App/Services/Video/support/gpu.ts` and
`canvas/`, `webgpu/`.

The downscaler choice is independent of encode:

- **Production**: `CanvasDownscaler` (`canvas/downscaler.ts`). 2D-canvas
  draw-image based, top-down processing with per-tier source reuse when
  `longEdge(higher) ≥ 2 × longEdge(target)`. Lazy per-slot init via the
  `parallelMap` operator (default 2 slots).
- **Lab / single-tier**: `WebGpuDownscaler` (`webgpu/downscaler.ts`) is still
  in the tree but not the default — production uses `CanvasDownscaler`
  because WebGPU pacing on iOS forced too many per-frame drains.
- **Single-tier P2P**: `identityDownscaler()` clones once.

The encoder picks `hardwareAcceleration: 'prefer-hardware'`. WebCodecs decides
whether HW is actually used; the result is reported back through
`hardwareAccelerated` in `CodecInfo`.

## Capture frame rate

`Constants.Video.FrameRate = 30` is the single source of truth, exposed to TS
via `VIDEO.frameRate`. `getUserMedia` for camera and `getDisplayMedia` for
screencast both request `{ ideal: 30, max: 30 }`; whatever the source delivers
is what the pipeline encodes (variable framerate is honoured — see the
recording-pipeline invariants in
`memory/project_video_pipeline_invariants.md`). The earlier 15-fps screencast
fallback has been removed.

## Simulcast layers — the "ladder"

File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder.ts`.

A `LayerConfig[]` is bottom-first (index 0 = base, last = top). Each layer
has `{ width, height, bitrateKbps, baseBitrateKbps, layerId? }`. All layers in
a single run share the same codec string, so `wireSend` declares the top tier's
codec for the whole stream and warns if any layer's encoder resolved to a
different one.

`buildLadder({ topWidth, topHeight, tierCount, maxTierCount, bitratesKbps, … })`:

- Top tier is `(topWidth, topHeight)` — the caller's chosen capture size.
- Halves both axes each step down (`/2`), rounding to even numbers.
- Drops any **derived** lower tier whose smallest axis would be below
  `MIN_SIMULCAST_SMALL_AXIS = 150 px`. The top tier is always kept.
- Per-layer bitrates come from the caller's `bitratesKbps` array (different
  curves for camera vs. screencast, sized by `effectiveCount`).
- Capped at `maxTierCount` (3 for camera, 2 for screencast in production).

Example, camera at 1280×720 → 3 layers:

```
L0  320×180   ~250 kbps
L1  640×360   ~600 kbps
L2 1280×720   ~1500 kbps
```

`buildLadder` is called from main-thread `VideoRecorder` and serialised into
the worker's `RecorderWorkerOptions.encoderConfigs`. Changes to the ladder
require a full `Recorder.restart()` (stop, start with fresh encoders) — there
is no in-place reconfigure.

The active count is what the AIMD recording controller adjusts ([08](./08-quality-control.md)):
on persistent encoder overload or peer-ack staleness, it reduces the count;
on consecutive good ticks, it climbs back.

## How the ladder shows up in the data

In the sender pipeline:

- `downscale` produces a `CapturedBundle { layers }` (bottom-first).
- `encode` emits one `EncodedBundle { layers }` per source moment, also
  bottom-first.
- `wireSend` writes one `VideoStreamFrameBundle { layers }` per source
  moment, with `layerId` and `maxLayerId` (the producer's current top tier)
  set on every per-layer DTO.

On the wire and on the server, layers travel **as bundles on the publisher
leg** and are **decomposed into per-frame items on the consumer leg**:

- `RpcStream<VideoFrameBundle>` between sender and API pod (one source
  moment = one bundle item; `canSkipTo` is "Layers[0].IsKeyFrame" so
  compaction happens at full-bundle keyframe boundaries).
- `VideoStreamingBackend.ProcessFrames` decomposes bundles → `VideoFrame`
  per layer, assigns a per-layer `KeyFrameNumber`, and yields into the
  memoizer.
- `RpcStream<VideoFrame>` between API pod and viewer (per-frame, per-layer);
  `ReceiveQualityFilter` selects one spatial layer + temporal cap from this
  stream.

On the server:

- `VideoStreamInfo.Format` is the **top-tier** `VideoFormat`. The per-layer
  ladder is derivable from `SourceKind` via the recorder's ladder builder,
  so the server doesn't store it per stream.
- `VideoStreamMemoizer` tracks per-layer keyframe queues and evicts whole
  keyframe-spans (see [05](./05-server-publish.md)).

On the receiver:

- The decoder is configured from the active layer's first keyframe (codec
  string from `Codec`, dims from `Width/Height`, optional `Description`).
  When a layer switch happens ([08](./08-quality-control.md)), the decoder
  pool may swap to a decoder configured for the new codec string, but for
  spatial-only switches within the same codec, only `configure()` is called.

## Keyframe policy

Sender-side triggers (file: `apply-keyframe-policy.ts` plus a few extras):

| Trigger | Source | Notes |
|---|---|---|
| Frame counter | `apply-keyframe-policy` | every `keyframeIntervalFrames` (= `KeyFramePeriod × frameRate`, 90 frames at 30 fps) |
| Wallclock floor | `apply-keyframe-policy` | every `maxKeyFrameIntervalMs` even if frames are sparse |
| Epoch flip | `stamp-capture-time` | clock discontinuity → mark next frame as keyframe |
| Dim change | `force-keyframe-on-dim-change` | window/screen rotation, source resize |
| Downscaler hang recovery | `downscale` | watchdog fired ⇒ next bundle is a keyframe |
| `requestKeyframe()` | RPC from main, e.g. server PLI observed by the worker | one-shot |

Server-side triggers a sender keyframe by invalidating the
`LastKeyframeRequestAt(streamId)` Fusion compute method on
`VideoStreamingBackend` (the worker's RPC client observes the invalidation
and forces the next bundle as a keyframe). Cooldown:
`Constants.Video.KeyFrameRequestCooldown = KeyFramePeriod / 3 = 1 s` so
concurrent late joiners collapse to a single PLI.

The reasons the server issues a PLI:

- A new viewer subscribes to a stream
  (`LiveVideoStreams.GetStream` always fires one — collapsed by cooldown).
- A viewer's `ChangePlaybackQuality` **upgrades** a stream's `MaxLayerId` or
  `MaxTemporalLayerId` (so the new layer can be picked up immediately rather
  than waiting up to ~3 s for the next natural keyframe). Downgrades skip
  the PLI deliberately — see [08](./08-quality-control.md).

## Constants worth remembering

| Constant | Value | Source |
|---|---|---|
| `Constants.Video.FrameRate` | 30 fps | `Api/Constants.Video.cs` |
| `Constants.Video.KeyFramePeriod` | 3 s | same |
| `Constants.Video.KeyFramePeriodSize` | 90 frames | derived |
| `Constants.Video.KeyFrameRequestCooldown` | 1 s (= `KeyFramePeriod / 3`) | same |
| `Constants.Video.CodecSwitchHysteresisWindow` | 10 s | same |
| `MIN_SIMULCAST_SMALL_AXIS` | 150 px | `layer-ladder.ts` |
| Encoder probe budget | 33 ms / frame | `codec-support.ts` |
| `VideoLayerDef.MaxLayerCount` (camera) | 3 | `Constants.Video.cs` |
| Downscaler concurrency (slots) | 2 | `operators/downscale.ts` |
| Downscaler hang watchdog | 1.5 s, ≤ 4 in a row | `operators/downscale.ts` |
