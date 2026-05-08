# 10 — Glossary

Quick lookup for terms, types, and abbreviations used across the live-video
docs.

## Concepts

- **Simulcast layer ladder.** A list of bottom-first encoded layers
  `[L0 (lowest), L1, …, LN-1 (highest)]`, all sharing the same codec, each
  with its own resolution and bitrate. Built by
  `layer-ladder.ts:buildLadder()`.
- **Temporal layer.** Within one spatial layer, an SVC frame pattern that
  lets a receiver decode a subset of frames (e.g. KFs only) without losing
  decodability. Probed via `scalabilityModes` in WebCodecs.
- **PLI (Picture Loss Indication).** A "force keyframe now" hint to the
  publisher. Implemented as the server invalidating the
  `LastKeyframeRequestAt(streamId)` Fusion compute method.
- **GOP (Group of Pictures).** Frames from one keyframe up to (but not
  including) the next keyframe in the same layer.
- **Memoizer.** `VideoStreamMemoizer` — server-side per-stream rolling buffer
  of ~3.3 s of frames per layer with keyframe-anchored eviction.
- **Late joiner.** A consumer that subscribes after publishing has begun.
  Catches up via `memoizer.Replay` from the minimum per-layer latest keyframe.
- **MSTG.** `MediaStreamTrackGenerator` — Web API that turns
  `VideoFrame`s written into a `WritableStream` into a `MediaStreamTrack` you
  can attach to a `<video>`.
- **`MonotonicClock`.** Strictly-increasing wall-clock with an `epoch` that
  flips on system clock discontinuities (sleep/NTP step). Sender stamps
  `capturedAt`; receiver stamps `arrivedAt`.
- **AIMD.** Additive Increase, Multiplicative Decrease. The control loop
  pattern used both for sender layer count and receiver capacity estimate.
- **`StreamId`.** Server-allocated id of one publish session. Pinned to a
  `NodeRef` so any node can locate the publisher's backend shard.

## TypeScript types (sender)

| Type | File | Purpose |
|---|---|---|
| `CapturedFrame` | `frame-envelopes.ts` | Raw `VideoFrame` + capturedAt + index + sourceDims + stats ref |
| `SimulcastBundle` | same | `{ primary, extras[], stats }`; primary = top tier |
| `EncodedFrame` | same | `EncodedVideoChunk` + metadata + capturedAt + index + layer info |
| `VideoStreamFrame` | same | DTO that maps 1:1 to server's `VideoFrame` |
| `VideoRecordingStats` | same | Mutable shared counters |
| `RecorderWorkerOptions` | `recorder-worker-contract.ts` | Wire-safe config (no closures, no track refs) |
| `EncoderConfigPerLayer` | `operators/encode.ts` | `{ width, height, bitrate, framerate, codec }` |
| `WebGpuDownscaler` | `webgpu/downscaler.ts` | GPU-accelerated multi-tier downscaler |
| `EncoderPool` | `sender/encoder-pool.ts` | Per-category parking of WebCodecs encoders |
| `SenderSession` | `sender/session.ts` | Owns clock + pool + preview writer; survives stop/start |

## TypeScript types (receiver)

| Type | File | Purpose |
|---|---|---|
| `VideoFrameDto` | `streaming/streaming-glue.ts` | Wire DTO from RPC |
| `ArrivedChunk` | `frame-envelopes.ts` | DTO + `arrivedAt` + decoded chunk |
| `DecodedFrame` | same | `VideoFrame` from decoder + decodedAt |
| `VideoPlaybackStats` | same | Mutable shared receiver counters |
| `LatencySample` | `operators/latency-tap.ts` | 1 Hz latency sample |
| `EncodedFrameBuffer` | `playback/encoded-frame-buffer.ts` | Two-state jitter buffer |
| `DecoderPool` | `playback/decoder-pool.ts` | Codec-keyed parking of WebCodecs decoders |
| `PlaybackSession` | `playback/session.ts` | Owns clock + pool + stats; per worker |
| `Player` | `playback/player.ts` | Per-stream pipeline runner |

## .NET types

| Type | Project | Purpose |
|---|---|---|
| `VideoFrame` | `Api/Video/` | Wire frame |
| `VideoFormat` | same | Per-layer codec + dims |
| `VideoStreamInfo` | same | Stream metadata in `LiveVideoBackend.List` |
| `VideoSource` | same | (helper for video-source kinds) |
| `VideoStreamLimitExceededException` | same | Thrown when chat over `MaxCameraStreamsPerChat` |
| `CachingVideoFrameFormatter` | same | MessagePack formatter, serialize-once caching |
| `VideoRecord` | `Streaming.Contracts/` | Publisher's session info passed to backend |
| `VideoStreamHeader` | `Streaming.Service/` | Internal stream header |
| `VideoStreamMemberInfo` | `Streaming.Service/Backend/` | Subscriber's codec list in chat state |
| `VideoStreamMemoizer` | same | Per-stream rolling buffer |
| `LiveVideoBackend` | same | Sharded chat-wide registry |
| `VideoStreamingBackend` | same | Per-node stream store, frame ingestion |
| `LiveVideoStreams` | `Streaming.Service/Services/` | API-pod façade (`ILiveVideoStreams`) |
| `ReceiveQualityFilter` | same | Per-consumer layer/temporal gate |
| `StreamStore<T>` | same | Generic per-node stream registry |
| `RemoteVideoStreamCache` | same | Cross-shard fan-out cache |
| `ReceiveQuality`, `RecordingQuality*`, `PlaybackQuality*` | `Api.Contracts/Streaming/Quality/` | Wire types for QC |
| `VideoQualityUI` | `UI.Blazor.App/Services/` | Both sender and receiver controllers |

## RPC methods at a glance

`ILiveVideoStreams` (API pod, called by clients):

| Method | Direction | Purpose |
|---|---|---|
| `PushStream` | Publisher → server | Open the publish stream |
| `GetStream` | Subscriber → server | Open a per-consumer subscribe stream |
| `RegisterMember` / `UnregisterMember` | Subscriber → server | Codec support + presence |
| `List` | Either → server | Active streams in chat (Fusion compute) |
| `ChangeRecordingQuality` | Publisher → server | Encoder health snapshot (1 Hz) |
| `ChangePlaybackQuality` | Subscriber → server | `ReceiveQuality` per stream + session-level info (~2 s) |
| `LastKeyframeRequestAt` | Server → publisher (via invalidation) | PLI |

`IVideoStreamingBackend` (backend, used internally / cross-shard):

| Method | Purpose |
|---|---|
| `PushVideo` | Backend handler for `PushStream` |
| `GetVideoRaw` | Read from local `StreamStore` (used directly + by `RemoteVideoStreamCache`) |
| `RequestKeyFrame` | Trigger a PLI on the publisher |

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
│   ├ ReplaceableSlot<VideoFrame>
│   ├ pipeline operators (capture..encode..wireSend)
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
│  │  └ RemoteStreamCaches.cs         ← cross-shard fan-out cache
│  ├ VideoStreamHeader.cs
│  ├ Module/StreamingServiceModule.cs ← DI wiring
│  └ Diagnostics/{StreamingMeters.cs,StreamingInstruments.cs}
└ UI.Blazor.App/
   ├ Components/VideoPanel/
   │  ├ VideoTrackPlayer.razor
   │  ├ RemoteStreamPlayer.razor
   │  ├ VideoStreamingPreview.razor
   │  ├ VideoDiagnosticsModal.razor
   │  ├ VideoDiagnosticsSettingsModal.razor
   │  ├ ScreenCastAlreadyActiveModal.razor
   │  ├ video-recorder.ts             ← main-thread recorder façade
   │  ├ video-player.ts               ← main-thread player façade
   │  ├ video-diagnostics.ts
   │  ├ layer-ladder.ts               ← simulcast ladder builder
   │  ├ render-backend{,-canvas,-mstg}.ts
   │  └ IVideoPlayerBackend.cs
   ├ Services/Video/
   │  ├ codec-support.ts
   │  ├ hevc-codec-selection.ts
   │  ├ hevc-parser.ts
   │  ├ gpu-support.ts
   │  ├ webcodecs-encoder.ts          ← (legacy reference)
   │  ├ webcodecs-decoder.ts          ← decoder wrapper
   │  ├ frame-envelopes.ts            ← TS envelope types
   │  ├ adapters.ts                   ← AsyncVideoEncoder/Decoder
   │  ├ streaming-rpc-client.ts
   │  ├ operators/                    ← pipeline operators (both sides)
   │  ├ sender/                       ← sender worker, pool, session
   │  ├ playback/                     ← receiver worker, pool, session, player
   │  ├ services/                     ← media-capture, preview wiring
   │  ├ streaming/streaming-glue.ts   ← TS↔C# DTO mapping, Fusion RPC bootstrap
   │  ├ support/gpu.ts
   │  └ webgpu/{manager,downscaler}.ts
   └ Services/VideoQualityUI.cs       ← AIMD controllers (sender + receiver)
```
