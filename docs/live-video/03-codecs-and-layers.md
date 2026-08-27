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
- **VP9** (`vp09.*`) — HW VP9 competes by efficiency; SW VP9 is a last-resort
  fallback only (see the eligibility gating below).
- **H.264** (`avc1.*`) — default, but NOT unconditionally trusted: Firefox
  reports it via `isConfigSupported()` while the real `configure()` fails
  (Mozilla bug 1918769), so runtime init failures exclude it like any other
  category.

For each enabled category, `isCodecSupported()` first tries `prefer-hardware`,
then `no-preference` (Firefox is bad about the former).

Result: `CodecInfo { codec, category, supported, hardwareAccelerated }`.

`listEncoderCandidatesByEfficiency(supported, allowedCategories)` is the
selection core: best candidate per category (HW preferred), ordered by codec
efficiency with HW as the tie-break. VP9 gating: SW VP9 is eligible only when
no H.264/HEVC candidate remains (e.g. Firefox with its H.264 encoder excluded
at runtime); mobile requires HW VP9 unconditionally (VP9-SW on Android
silently drops all frames).

`getDefaultCodec(supported, w, h)` is the no-candidate fallback, in priority
order **AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile-tuned) > VP9 SW (desktop)
> H.264 SW**; it respects runtime exclusions and returns `null` when nothing is
left — the caller surfaces a fatal "video not supported in this browser" error
instead of retrying. On mobile, the policy prefers Main over High for power
efficiency.

`getCodecForCategory(category, w, h)` always returns the highest level in the
category (e.g. H.264 High 5.2) and keeps it constant within a session, so a
single `VideoEncoder` can absorb dim/bitrate reconfigures mid-run without a
cold NVENC re-init. Encoders are NOT pooled across `start/stop` cycles —
every recorder run constructs a fresh `VideoEncoder` so its first chunk is
guaranteed to be an intra-coded keyframe (see `02-sender.md`).

`probeEncoder(codec, layers)` validates `isConfigSupported()` AND a real
`configure()` + `flush()` on a live encoder (no frames encoded — frame-level
throughput probing false-failed under GPU contention and lives at the running
pipeline's boundary instead). The configure step catches browsers whose
`isConfigSupported()` lies (Firefox H.264, Mozilla bug 1918769); a probe
timeout counts as PASS. Cache key: `codec@WxH×layer-count×hwAccel`.

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
has `{ width, height, bitrateKbps, baseBitrateKbps }`. All
layers in a single run share the same codec.

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
