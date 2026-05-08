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

For each enabled category, `isCodecSupported()`:

1. First tries `prefer-hardware`, then `no-preference` (Firefox is bad about
   the former).
2. Probes scalability modes (`L1T1`, `L1T2`, `L1T3`) to learn whether SVC
   temporal layers are available.

Result: `CodecInfo { codec, hardwareAccelerated, scalabilityModes }`.

`getDefaultCodec(supported, w, h)` picks in priority order:
**AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile-tuned) > H.264 SW**.
On Firefox, H.264 Main 3.1 is forced (only profile that works reliably). On
mobile, the policy prefers Main over High for power efficiency.

`getCodecForCategory()` always returns the highest level in the category
(e.g. H.264 High 5.2). The string is then **constant** across sessions for that
category — Chrome re-initialises NVENC when the string changes, so keeping it
constant lets the encoder pool actually reuse hardware slots.

`probeEncoder(codec, layers)` measures steady-state encode time (median of last
5 frames after 3 warm-up). The 33 ms budget = one frame at 30 fps. Cache key:
`codec@WxH×layer-count`.

### Receiver-side codec selection

The receiver registers its decoder support every 30 s
(`ILiveVideoStreams.RegisterMember(session, chatId, supportedDecoderCodecs)`).
The server's `LiveVideoBackend` intersects all members' codecs and exposes the
result via `GetSupportedCodecs(chatId)`. Senders use that to pick the codec for
the encoder. Codec **upgrades** are delayed by
`CodecSwitchHysteresisWindow = 10 s` to avoid flapping; **downgrades** apply
immediately (e.g., a Safari user joining forces H.264).

When a receiver decoder fails repeatedly, `getCodecCategory(codecString)` is
mapped, and `excludeDecoderCodec(category)` adds it to a localStorage exclusion
set; subsequent `RegisterMember` calls report a smaller list and the server
re-negotiates.

## Hardware acceleration

File: `src/dotnet/UI.Blazor.App/Services/Video/support/gpu.ts` and `webgpu/`.

WebGPU is used **only for the downscaler**, not for encode/decode (that's
WebCodecs). `WebGpuManager` lazy-initialises a single GPU adapter per worker.
The downscaler runs one render pass per non-identity tier, with the source
frame as a texture and the output frame produced via `VideoFrame` from a
texture. On platforms without WebGPU, the pipeline falls back to a single-tier
identity downscaler — simulcast is effectively disabled, and the AIMD
controller (08) won't try to use it.

The encoder picks `hardwareAcceleration: 'prefer-hardware'`. WebCodecs decides
whether HW is actually used; the result is reported back through
`hardwareAccelerated` in `CodecInfo`.

## Simulcast layers — the "ladder"

File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder.ts`.

A `LayerConfig[]` is bottom-first (index 0 = base, last = top). Each layer has
`{ width, height, bitrate, framerate, codec }`. All layers in a single run
share the same codec string.

`buildLadder(sourceW, sourceH, targetLayerCount, sourceKind)`:

- Starts with the source dimensions (top tier).
- Halves both axes each step down (`/2`), rounding to even numbers.
- Drops any tier whose smallest axis would be below
  `MIN_SIMULCAST_SMALL_AXIS = 150 px`.
- Per-layer bitrates come from `getVideoLayerBitratesKbps(...)` — different
  curves for camera vs. screencast, and different totals depending on
  `targetLayerCount`.
- Maximum 3 layers; cameras typically run 1 (poor link) → 3 (great link).
  Screencast peaks at 1080p.

Example, camera at 1280×720 → 3 layers:

```
L0  320×180   ~250 kbps
L1  640×360   ~600 kbps
L2 1280×720   ~1500 kbps
```

`buildLadder` is called from main-thread `VideoRecorder` and serialised into
the worker's `RecorderWorkerOptions.encoderConfigs`. Changes to the ladder
require a full `Recorder.restart()` (stop + start the pipeline) — there is no
in-place reconfigure.

The active count (`targetLayerCount`) is what the AIMD recording controller
adjusts (08): on persistent encoder overload or peer-ack staleness, it reduces
the count; on consecutive good ticks, it climbs back.

## How the ladder shows up in the data

In the sender pipeline:

- `downscale` produces a `SimulcastBundle { primary, extras[] }`.
  `primary` = top tier, `extras[i]` are bottom-first lower tiers.
- `encode` emits `EncodedFrame` per layer **bottom-first** (L0, L1, …, top).
- `wireSend` writes one `VideoStreamFrame` per layer with `LayerId` and
  `MaxLayerId` (the producer's current top tier).

On the wire and on the server, layers share one `RpcStream<VideoFrame>` —
they are interleaved, not separate streams. The first keyframe at `LayerId == 0`
is the canonical sync point; the RPC layer's `canSkipTo = isKeyFrame`
compaction skips forward to the latest L0 keyframe when a consumer falls
behind.

On the server:

- `VideoStreamInfo.Formats[]` (Api/Video/VideoStreamInfo.cs) holds one
  `VideoFormat` per layer. It starts with the base layer at `Register` time
  and is extended as higher-layer keyframes arrive.
- `VideoStreamMemoizer` tracks per-layer keyframe queues and evicts whole
  keyframe-spans (see [05](./05-server-publish.md)).

On the receiver:

- The decoder is configured from the active layer's `VideoFormat` (codec +
  description). When a layer switch happens (08), the decoder pool may swap to
  a decoder configured for the new codec string, but for spatial-only switches
  within the same codec, only `configure()` is called.

## Keyframe policy

Sender-side triggers (file: `apply-keyframe-policy.ts` plus a few extras):

| Trigger | Source | Notes |
|---|---|---|
| Frame counter | `apply-keyframe-policy` | every `keyframeIntervalFrames` (≈ `KeyFramePeriod * fps`, 90 frames at 30 fps) |
| Wallclock floor | `apply-keyframe-policy` | every `maxKeyFrameIntervalMs` even if frames are sparse |
| Epoch flip | `stamp-capture-time` | clock discontinuity → mark next frame as keyframe |
| Dim change | `force-keyframe-on-dim-change` | window/screen rotation, source resize |
| `requestKeyframe()` | RPC from main, e.g. server PLI | one-shot |

Server-side triggers a sender keyframe by invalidating the
`LastKeyframeRequestAt(streamId)` Fusion compute method on
`VideoStreamingBackend` (sender's worker observes the invalidation and forces
the next bundle as a keyframe). Cooldown:
`Constants.Video.KeyFrameRequestCooldown = 1 s` (≈ `KeyFramePeriod / 3`) so
concurrent late joiners collapse to a single PLI.

The reasons the server issues a PLI:

- A new viewer subscribes to a stream
  (`LiveVideoStreams.GetStream` always fires one — collapsed by cooldown).
- A viewer's `ChangePlaybackQuality` changes the desired layer for a stream
  (so the new layer can be picked up immediately rather than waiting for the
  next natural keyframe up to ~3 s away).

## Constants worth remembering

| Constant | Value | Source |
|---|---|---|
| `Constants.Video.FrameRate` | 30 fps | `Api/Constants.Video.cs` |
| `Constants.Video.KeyFramePeriod` | 3 s | same |
| `Constants.Video.KeyFrameRequestCooldown` | 1 s | same |
| `Constants.Video.CodecSwitchHysteresisWindow` | 10 s | same |
| `MIN_SIMULCAST_SMALL_AXIS` | 150 px | `layer-ladder.ts` |
| Encoder probe budget | 33 ms / frame | `codec-support.ts` |
| Encoder pool TTL | 5 s | `encoder-pool.ts` |
| Top simulcast layers | 3 | `layer-ladder.ts` |
