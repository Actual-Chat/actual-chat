# Video Simulcast (long-term)

## Goal

Produce **multiple encoded versions of the same source frame** concurrently and let each receiver subscribe to the tier its link can sustain. One weak peer no longer drags the call to the lowest common denominator.

This is how Google Meet, Zoom, Teams, and every serious conferencing product scale. It is the structural prerequisite for aggressive per-receiver adaptation (Zoom-style quality matrix).

## Why

Our current architecture publishes ONE bitstream per sender; the adaptive-quality system steps it down when ANY outlier peer struggles. Cross-continent + local peers coexisting in the same chat forces us to a compromise: either the local peer sees blurry HEVC or the cross-continent peer drops frames. Simulcast breaks the coupling.

## Design (high level)

Target: single sender emits 3 spatial layers × 3 temporal layers = 9 quality rungs. Receivers subscribe per-frame to any spatial × temporal combination that fits their bandwidth budget.

### Sender side

Per source frame:

```
camera → MSTP → one VideoFrame
  ↓
WebGpuDownscaler.configure([
    { width: 1280, height: 720 },   // high
    { width:  640, height: 360 },   // mid
    { width:  320, height: 180 },   // low
])
  → 3× GPU textures → 3× wrapped VideoFrames
  ↓
3× VideoEncoder instances, each with scalabilityMode: 'L1T3' (3 temporal layers)
  ↓
3× encoded streams to server, each chunk tagged with (spatialLayerId, temporalLayerId)
```

The existing `WebGpuDownscaler` class already accepts `DownscaleTarget[]`. API is simulcast-ready from day one. The only new moving parts:

1. **Multi-encoder orchestration** in `video-processing.ts`. Today one `encoder: WebCodecsEncoder`. Becomes `encoders: WebCodecsEncoder[]`, one per layer, each with its own codec string / dims / bitrate (from `VideoBitrateTable`).
2. **Per-layer stream plumbing** — `VideoStreamer` currently serializes a single sequence. Extend the wire format so each frame carries `spatialLayerId` and `temporalLayerId`. Either (a) open 3 RPC streams in parallel, or (b) multiplex into one stream with a layer tag on each chunk. Option (b) fewer sockets but requires router-level layer extraction.
3. **VAD adaptive framerate** works per-encoder already — no change.

### Server side

**`IVideoStreamingBackend.GetVideo`** gains a `layerMask` argument: receiver says which spatial+temporal layers it wants. Default = highest available.

**`StreamStore<VideoFrame>`** stores frames per (streamId, spatialLayer). On retrieval:
- Receiver's `PeerLatencyState.MaxTemporalLayer` (already exists at `StreamLatencyStore.cs:165`) → select temporal subset.
- New `MaxSpatialLayer` property → select spatial subset.

**`StreamLatencyState.EvaluateQuality`** changes from emitting a single preset to computing **per-peer** layer caps. Sender's encoder config stops depending on peer health — it always produces all 3 tiers. Each receiver gets its own cap.

### Codec negotiation

`LiveVideoBackend.ChatState` already intersects peer decoder capabilities. Extension: each peer advertises its MAX spatial layer it can decode at realtime. Sender only produces layers all peers can decode (HEVC on iOS, H.264 on others → fall back to H.264 × 3 layers).

### Wire format (simplest variant)

Extend `VideoFrameDto` (or equivalent) with `byte SpatialLayer, byte TemporalLayer` at the start. Receivers filter on read. Dumb SFU, smart receiver.

## Codec support for true simulcast

| Codec | Spatial layers | Temporal layers | HW on iPhone |
|-------|----------------|-----------------|--------------|
| H.264 | Multiple encoder instances | `L1T3` HW | Yes (concurrent encoders OK) |
| HEVC  | Multiple encoder instances | `L1T3` HW (variable) | Yes |
| VP9   | Native spatial SVC via `scalabilityMode: 'L3T3'` | Native | Android only |
| AV1   | Native SVC | Native | iPhone 15 Pro+, recent Android |

Our practical path: **multi-encoder simulcast**, not codec-native SVC. HW encoders on iOS support ≥3 concurrent instances at 720p/360p/180p (validated on iPhone 12+).

## Critical files (anticipated)

| File | Role |
|------|------|
| `src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts` | Multi-encoder orchestration in `encodeProcessedFrame` |
| `src/dotnet/UI.Blazor.App/Services/Video/services/recording-service.ts` | Config shape: layer array |
| `src/dotnet/UI.Blazor.App/Services/Video/webgpu-downscaler.ts` | Already N-target; no change |
| `src/dotnet/UI.Blazor.App/Services/Video/video-streamer.ts` | Multi-layer multiplex or N parallel streams |
| `src/dotnet/Api/Video/VideoFrame.cs` | Add `byte SpatialLayer, byte TemporalLayer` |
| `src/dotnet/Streaming.Contracts/IVideoStreamingBackend.cs` | `GetVideo(… layerMask …)` |
| `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs` | Layer-indexed `StreamStore`, per-peer mask computation |
| `src/dotnet/Streaming.Service/Backend/StreamLatencyStore.cs` | `PeerLatencyState.MaxSpatialLayer`; `EvaluateQuality` per-peer layer caps |

## Staged roll-out

1. **Stage 1: single-encoder, multi-target downscaler dry run.** Configure `WebGpuDownscaler` with 3 targets, discard all but the highest. Confirms 3× shader dispatch cost is acceptable on iPhone (GPU time + power).
2. **Stage 2: add second encoder instance (240p).** Publish to a second RPC stream. Nobody consumes yet — just measure total encode load on iPhone 12 and desktop.
3. **Stage 3: extend wire format.** Add layer tags. Receivers parse but still pick top.
4. **Stage 4: server stores per-layer, receiver requests a mask.** End-to-end path, single-layer-per-peer. Validate latency / throughput.
5. **Stage 5: per-peer layer selection.** `StreamLatencyState` emits per-peer `MaxSpatial` / `MaxTemporal`. `GetVideo` honors. Call is now Meet-like.
6. **Stage 6: add temporal scalability.** Enable `scalabilityMode: 'L1T3'` on each encoder. Frame filter on receiver side based on `MaxTemporalLayer`.

## Risks

- **HW encoder concurrency cap** — older iPhones may stall or downgrade to SW when running 3 concurrent HEVC encoders. Gate: only enable simulcast when `pureMedianEncodeTime` for all three encoders < 10 ms on first 10 frames.
- **3× encode battery drain on iPhone** without proportional quality-per-peer win. Worst case = simulcast-off is better. Must measure per Stage 2.
- **Server storage footprint 3×**. `StreamStore` retention buffer grows accordingly. Current 60-frame buffer × 3 layers = 180 frames in RAM. Bounded but notable.
- **Keyframe alignment across layers**. All 3 encoders must produce keyframes at compatible offsets so a late-joining peer can subscribe to any layer. Force keyframe via shared `forceKeyFrame` every N seconds across all encoders.
- **Temporal SVC (L1T3)** support varies on HW — H.264 High profile supports it on iPhone, Baseline/Main may not. Validate per codec × device at Stage 6.

## Dependencies

- **Throughput probing** (see `video-throughput-probing.md`) — wants simulcast for per-peer probing but can ship single-stream first.
- **HEVC codec-category default** on iOS (see prior work) — simulcast relies on HEVC being chosen first.
- **Receiver RAF pacing** (deferred optimization) — multi-layer receiver rendering will amplify any existing RAF inefficiency.

## Non-goals (for the first landing)

- Cloud SFU routing — we already have per-peer streams via Fusion RPC, no media server needed in MVP.
- FEC / forward error correction — not meaningful on reliable transport.
- Dynamic layer count — fix at 3 until measurements say otherwise.

## References

- WebRTC simulcast: https://webrtchacks.com/sfu-simulcast/
- Chrome WebCodecs `scalabilityMode`: https://developer.chrome.com/docs/web-platform/webcodecs#scalability
- Apple's VideoToolbox concurrent encoder caps: https://developer.apple.com/documentation/videotoolbox (model-specific, no authoritative list)
