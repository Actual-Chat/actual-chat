# 10 — Glossary

Quick lookup for terms, types, and abbreviations used across the live-video
docs.

## Concepts

- **Simulcast layer ladder.** A list of bottom-first encoded layers
  `[L0 (lowest), L1, …, LN-1 (highest)]`, all sharing the same codec, each
  with its own resolution and bitrate. Built by
  `layer-ladder.ts:buildLadder()`.
- **Bundle (publisher leg).** One captured source moment carried as
  `VideoFrameBundle { Layers[] }` — all simulcast layers of that moment
  travel as one wire item between sender and API pod. Bundles do not exist
  past `VideoStreamingBackend.ProcessFrames`, which decomposes them into
  per-frame items for the memoizer.
- **Temporal layer.** Within one spatial layer, an SVC frame pattern that
  lets a receiver decode a subset of frames (e.g. KFs only) without losing
  decodability. Probed via `scalabilityModes` in WebCodecs.
- **PLI (Picture Loss Indication).** A "force keyframe now" hint to the
  publisher. Implemented as the server invalidating the
  `LastKeyframeRequestAt(streamId)` Fusion compute method.
- **GOP (Group of Pictures).** Frames from one keyframe up to (but not
  including) the next keyframe in the same layer.
- **Memoizer.** `VideoStreamMemoizer` — server-side per-stream rolling
  buffer of ~3.3 s of frames per layer with keyframe-anchored eviction.
- **Late joiner.** A consumer that subscribes after publishing has begun.
  Catches up via `memoizer.Replay` from the minimum per-layer latest
  keyframe.
- **Flood gate.** `FloodGate` — a hysteresis valve in the sender pipeline.
  Closed at `pushPullBufferSize / 2` capacity, opened below
  `pushPullBufferSize / 4`. Drops captured frames at the cheapest possible
  point so the encoder/downscaler don't see them.
- **MSTG.** `MediaStreamTrackGenerator` — Web API that turns
  `VideoFrame`s written into a `WritableStream` into a `MediaStreamTrack`
  you can attach to a `<video>`.
- **`MonotonicClock`.** Strictly-increasing wall-clock with an `epoch` that
  flips on system clock discontinuities (sleep/NTP step). Sender stamps
  `capturedAt`; receiver stamps `arrivedAt`/`decodedAt`.
- **AIMD.** Additive Increase, Multiplicative Decrease. The control loop
  pattern used both for sender layer count and receiver capacity estimate.
- **`StreamId`.** Server-allocated id of one publish session. Pinned to a
  `NodeRef` so any node can locate the publisher's backend shard.
- **`ReceiveQuality`.** A per-stream pair `{ MaxLayerId, MaxTemporalLayerId }`
  the viewer asks for via `ChangePlaybackQuality`. The server-side
  `ReceiveQualityFilter` is the gate that enforces it on the per-frame
  outbound stream.
- **`RenderVideoSize`.** Computed property on `PlaybackHealthSnapshot`
  derived from `RenderCssLongSide × RenderDevicePixelRatio`. Drives the
  per-stream `MaxLayerId` cap (no point sending 720p to a 200-px tile).

## TypeScript types (sender)

| Type | File | Purpose |
|---|---|---|
| `CapturedFrame` | `frame-envelopes.ts` | Raw `VideoFrame` + capturedAt + index + sourceDims + stats ref |
| `CapturedBundle` | same | `{ layers: CapturedFrame[], stats }`; bottom-first |
| `EncodedFrame` | same | `EncodedVideoChunk` + metadata + capturedAt + index + layer info |
| `EncodedBundle` | same | `{ layers: EncodedFrame[], stats }`; bottom-first |
| `VideoStreamFrame` | `operators/wire-send.ts` | DTO for one per-layer wire frame |
| `VideoStreamFrameBundle` | same | `{ layers: VideoStreamFrame[] }`; per-source-moment wire item |
| `VideoRecordingStats` | `frame-envelopes.ts` | Mutable shared counters (run-level) |
| `RecorderWorkerOptions` | `recorder-worker-contract.ts` | Wire-safe config (no closures, no track refs) |
| `EncoderConfigPerLayer` | `operators/encode.ts` | `{ width, height, bitrate, framerate, codec }` |
| `DownscalerLike` | `operators/downscale.ts` | Per-slot downscaler interface; `process(input, layers) → frames[]` |
| `CanvasDownscaler` | `canvas/downscaler.ts` | Production 2D-canvas downscaler with higher-tier reuse |
| `WebGpuDownscaler` | `webgpu/downscaler.ts` | Lab WebGPU downscaler (not production) |
| `EncoderPool` | `sender/encoder-pool.ts` | Per-category parking of WebCodecs encoders |
| `SenderSession` | `sender/session.ts` | Owns clock + pool + preview writer; survives stop/start |
| `FloodGate` | `operators/flood-gate.ts` | Capture-side backpressure valve |
| `parallelMap` | `operators/parallel-map.ts` | Ordered parallel-map operator (drives downscale slots) |
| `pushPullBuffer` (Denque + RpcStream) | `streaming/push-to-pull-buffer.ts` | Sync wireSend ↔ async RpcStream rendezvous |

## TypeScript types (receiver)

| Type | File | Purpose |
|---|---|---|
| `VideoFrameDto` | `operators/pull.ts` | Wire DTO from RPC |
| `ArrivedChunk` | `frame-envelopes.ts` | DTO + `arrivedAt` + decoded chunk |
| `DecodedFrame` | same | `VideoFrame` from decoder + `decodedAt` |
| `VideoPlaybackStats` | same | Mutable shared receiver counters (session-level) |
| `LatencySample` | `operators/latency-tap.ts` | Per-`LatencyReportInterval` (~500 ms) latency sample |
| `EncodedFrameBuffer` | `playback/encoded-frame-buffer.ts` | Span-gated jitter buffer, two-state (`reset`/`armed`) |
| `DecoderPool` | `playback/decoder-pool.ts` | Codec-keyed parking of WebCodecs decoders |
| `PlaybackSession` | `playback/session.ts` | Owns clock + pool + stats; per worker |
| `Player` | `playback/player.ts` | Per-stream pipeline runner |
| `RenderBackendConfig` | `playback/render-backends.ts` | `{ kind: 'mstg' | 'canvas', … }` |

## .NET types

| Type | Project | Purpose |
|---|---|---|
| `VideoFrame` | `Api/Video/` | Per-layer wire frame |
| `VideoFrameBundle` | same | One source moment, all layers (publisher leg only) |
| `VideoFormat` | same | Per-layer (or top-tier) codec + dims |
| `VideoStreamInfo` | same | Stream metadata in `LiveVideoBackend.List` (top-tier `Format`) |
| `VideoSource` | same | Helper for video-source kinds |
| `VideoStreamLimitExceededException` | same | Thrown when chat over `MaxCameraStreamsPerChat` |
| `CachingVideoFrameFormatter` | same | MessagePack formatter, serialize-once caching |
| `VideoRecord` | `Streaming.Contracts/` | Publisher's session info passed to backend |
| `VideoStreamMemberInfo` | `Streaming.Service/Backend/` | Subscriber's codec list in chat state |
| `VideoStreamMemoizer` | same | Per-stream rolling buffer with per-layer keyframe-span eviction |
| `LiveVideoBackend` | same | Sharded chat-wide registry |
| `VideoStreamingBackend` | same | Per-node stream store, frame ingestion (decomposes bundles) |
| `LiveVideoStreams` | `Streaming.Service/Services/` | API-pod façade (`ILiveVideoStreams`) |
| `ReceiveQualityFilter` | same | Per-consumer layer/temporal gate |
| `StreamStore<T>` | same | Generic per-node stream registry |
| `RemoteVideoStreamCache` | same | Cross-shard fan-out cache (deduped via `EnsureFetched`) |
| `StreamCacheFetchDeduper` | same | Coalesces concurrent first-fetchers onto one cross-shard RPC |
| `StreamSilenceWatchdog` | same | Interval-based silence detector |
| `ReceiveQuality`, `RecordingQuality*`, `PlaybackQuality*` | `Api.Contracts/Streaming/Quality/` | Wire types for QC |
| `VideoQualityUI` | `UI.Blazor.App/Services/` | Both sender and receiver controllers |
| `PlaybackLagTracker` | same | Per-author audio/video lag tracking (A/V sync) |

## RPC methods at a glance

`ILiveVideoStreams` (API pod, called by clients):

| Method | Direction | Purpose |
|---|---|---|
| `PushStream` | Publisher → server | Open the publish stream (`RpcStream<VideoFrameBundle>`) |
| `GetStream` | Subscriber → server | Open a per-consumer subscribe stream (`RpcStream<VideoFrame>`) |
| `RegisterMember` / `UnregisterMember` | Subscriber → server | Codec support + presence (30 s heartbeat) |
| `List` | Either → server | Active streams in chat (Fusion compute) |
| `GetMemberCount` / `GetSupportedCodecs` | Either → server | Chat membership queries (compute) |
| `RequestKeyFrame` | Either → server | Fire-and-forget PLI |
| `ChangeRecordingQuality` | Publisher → server | Encoder health snapshot (~1 Hz) |
| `ChangePlaybackQuality` | Subscriber → server | `ReceiveQuality` per stream + session info (~2 s; 1 min keep-alive) |
| `LastKeyframeRequestAt` | Server → publisher (via invalidation) | PLI delivery |

`IVideoStreamingBackend` (backend, used internally / cross-shard):

| Method | Purpose |
|---|---|
| `PushVideo` | Backend handler for `PushStream` (consumes bundles) |
| `GetVideoRaw` | Read from local `StreamStore` (used directly + by `RemoteVideoStreamCache`) |
| `RequestKeyFrame` | Trigger a PLI on the publisher |
| `LastKeyframeRequestAt` | Compute method whose invalidation drives the publisher worker's force-keyframe |

`ILiveVideoBackend` (backend, sharded):

| Method | Purpose |
|---|---|
| `Register` / `Unregister` | Add/remove `VideoStreamInfo` in chat |
| `RegisterMember` / `UnregisterMember` | Member presence + codec list |
| `List` | Active streams (Fusion compute, invalidated on changes) |
| `GetSupportedCodecs` | Intersection of all members' codec lists |
| `GetVideoStreamMemberCount` | Subscriber count for a chat |

## Process / threads

```
Browser process
├ Main thread
│   ├ Blazor (Razor + JSInterop)
│   └ WebSocket to API pod (Fusion RPC)
│
├ recorderWorker.js          (one per active recorder)
│   ├ MediaStreamTrackProcessor.readable
│   ├ pipeline operators (capture..encode..wireSend)
│   ├ FloodGate + push-to-pull-buffer (Denque)
│   ├ EncoderPool
│   └ Fusion RPC client (PushStream)
│
└ playerWorker.js             (one shared session for all playing streams)
    ├ PlaybackSession (clock, decoderPool, stats)
    ├ Player[]   (one per stream)
    └ Fusion RPC client (GetStream)
```

## Source-tree map

```
src/dotnet/
├ Api.Contracts/Streaming/
│  ├ ILiveVideoStreams.cs
│  └ Quality/
│     ├ ReceiveQuality.cs
│     ├ RecordingQuality.cs
│     └ PlaybackQuality.cs
├ Api/Video/
│  ├ VideoFrame.cs
│  ├ VideoFrameBundle.cs
│  ├ VideoFormat.cs
│  ├ VideoSource.cs
│  ├ VideoStreamInfo.cs
│  ├ VideoStreamLimitExceededException.cs
│  └ CachingVideoFrameFormatter.cs
├ Streaming.Contracts/
│  ├ ILiveVideoBackend.cs
│  ├ IVideoStreamingBackend.cs
│  └ VideoRecord.cs
├ Streaming.Service/
│  ├ Backend/
│  │  ├ LiveVideoBackend.cs           ← chat / member registry
│  │  ├ LiveVideoBackend.ChatState.cs ← codec negotiation
│  │  ├ VideoStreamingBackend.cs      ← PushVideo, GetVideoRaw
│  │  ├ VideoStreamMemberInfo.cs
│  │  └ VideoStreamMemoizer.cs        ← per-layer rolling buffer
│  ├ Services/
│  │  ├ LiveVideoStreams.cs           ← ILiveVideoStreams impl
│  │  ├ ReceiveQualityFilter.cs       ← per-consumer gate
│  │  ├ StreamStore.cs                ← per-node registry
│  │  ├ RemoteStreamCaches.cs         ← cross-shard fan-out cache (deduped)
│  │  └ StreamSilenceWatchdog.cs      ← interval-based silence detector
│  ├ Module/StreamingServiceModule.cs ← DI wiring
│  └ Diagnostics/{StreamingMeters.cs,StreamingInstruments.cs}
└ UI.Blazor.App/
   ├ Components/VideoPanel/
   │  ├ VideoTrackPlayer.razor
   │  ├ VideoStreamingPreview.razor
   │  ├ VideoDiagnosticsModal.razor
   │  ├ VideoDiagnosticsSettingsModal.razor
   │  ├ ScreenCastAlreadyActiveModal.razor
   │  ├ video-recorder.ts             ← main-thread recorder façade
   │  ├ video-player.ts               ← main-thread player façade
   │  ├ video-diagnostics.ts
   │  ├ layer-ladder.ts               ← simulcast ladder builder
   │  └ render-backend{,-canvas,-mstg}.ts
   ├ Services/Video/
   │  ├ codec-support.ts
   │  ├ hevc-codec-selection.ts
   │  ├ hevc-parser.ts
   │  ├ gpu-support.ts
   │  ├ webcodecs-encoder.ts
   │  ├ webcodecs-decoder.ts          ← decoder wrapper
   │  ├ frame-envelopes.ts            ← TS envelope types
   │  ├ adapters.ts                   ← AsyncVideoEncoder/Decoder
   │  ├ streaming-rpc-client.ts
   │  ├ operators/                    ← pipeline operators (both sides)
   │  ├ sender/                       ← sender worker, pool, session
   │  ├ playback/                     ← receiver worker, pool, session, player
   │  ├ services/                     ← media-capture, preview wiring
   │  ├ streaming/push-to-pull-buffer.ts   ← Denque + RpcStream rendezvous + DTO mapping
   │  ├ canvas/downscaler.ts          ← production 2D-canvas downscaler
   │  ├ webgpu/{manager,downscaler}.ts ← lab WebGPU downscaler
   │  └ support/gpu.ts
   ├ Services/VideoQualityUI.cs       ← AIMD controllers (sender + receiver)
   └ Services/PlaybackLagTracker.cs   ← per-author A/V lag tracking
```
