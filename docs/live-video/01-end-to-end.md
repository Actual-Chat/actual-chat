# 01 — End-to-end walkthrough

This is the "follow one frame across the whole system" tour. Each later doc
zooms into one stage. References are to source paths under
`/proj/ActualChat`.

## Cast of characters

| Layer | Process | Entry point |
|---|---|---|
| Sender DOM | Main browser thread | `VideoTrackPlayer.razor` (preview), `VideoRecorder.cs` |
| Sender worker | `recorderWorker.js` | `recorder-worker-host.ts` → `recorder.ts` |
| API pod | .NET | `LiveVideoStreams` (`ILiveVideoStreams`) |
| Backend pod | .NET, sharded by `ChatId` | `VideoStreamingBackend`, `LiveVideoBackend` |
| Receiver DOM | Main browser thread | `VideoTrackPlayer.razor`, `video-player.ts` |
| Receiver worker | `playerWorker.js` | `player-worker-host.ts` → `player.ts` |

## End-to-end timeline (one captured source moment)

```mermaid
sequenceDiagram
    autonumber
    participant DOMA as Sender DOM
    participant WA as Sender worker
    participant API as ILiveVideoStreams
    participant VSB as VideoStreamingBackend
    participant Memo as VideoStreamMemoizer
    participant WB as Receiver worker
    participant DOMB as Receiver DOM

    Note over DOMA: getUserMedia → MediaStreamTrack
    DOMA->>WA: postMessage(track) (Tier 2)<br/>or pushFrame(VideoFrame) (Tier 1)
    Note over WA: capture → floodGate → stampCaptureTime →<br/>attachSourceDims → downscale (parallelMap)<br/>→ applyKeyframePolicy
    WA->>WA: encode all layers in parallel<br/>(Promise.allSettled, bottom-first)
    WA->>WA: build VideoStreamFrameBundle<br/>(all per-layer DTOs in one item)
    WA->>API: PushStream(session, chatId, sourceStartAt,<br/>topFormat, sourceKind, RpcStream&lt;VideoFrameBundle&gt;)
    API->>VSB: PushVideo(VideoRecord, bundleStream)
    Note over VSB: silence watchdog (5 s × 2)<br/>negative-offset filter<br/>per-layer KeyFrameNumber
    VSB->>Memo: yield each layer's VideoFrame
    Note over Memo: per-layer KF queue,<br/>~3.3 s rolling tail

    DOMB->>WB: start({streamId, decoderConfig, backend})
    WB->>API: GetStream(session, streamId)
    Note over API: streamId.NodeRef local? GetVideoRaw : RemoteVideoStreamCache (deduped)
    API->>VSB: GetVideoRaw(streamId)
    VSB->>Memo: Replay()
    Memo-->>VSB: IAsyncEnumerable&lt;VideoFrame&gt;
    VSB-->>API: RpcStream&lt;VideoFrame&gt;<br/>(SkipWhile !IsKeyFrame)
    API->>API: ReceiveQualityFilter.Apply<br/>(per-frame getQuality())
    API-->>WB: VideoFrameDto stream
    WB->>WB: pull → resetOnEpochChange →<br/>pacedEncodedBuffer (span ≥ 333 ms)<br/>→ decode → present
    WB-->>DOMB: MSTG track or canvas frames
    Note over API: PLI fired in parallel<br/>(rate-limited 1 s cooldown)
```

## Stage-by-stage data shapes

```
                CapturedFrame              CapturedBundle            EncodedBundle
                ┌──────────────┐           ┌────────────────┐        ┌────────────────┐
camera ─▶ MSTP ─▶│ frame        │── down  ▶│ layers[]       │── enc ▶│ layers[]       │
                │ capturedAt   │  scale   │   bottom-first │        │   bottom-first │
                │ index        │  (parall │   per-tier     │        │ stats (ref)    │
                │ source W/H   │  -elMap) │   VideoFrame   │        └────────────────┘
                │ stats (ref)  │          │ stats (ref)    │                │
                └──────────────┘          └────────────────┘                ▼
                                                                  VideoStreamFrameBundle
                                                                  ┌─────────────────────┐
                                                                  │ layers[]:           │
                                                                  │   VideoStreamFrame  │
                                                                  │     offset, dur     │
                                                                  │     isKeyFrame      │
                                                                  │     width, height   │
                                                                  │     description?    │
                                                                  │     data (bytes)    │
                                                                  │     layerId, …      │
                                                                  └─────────────────────┘
                                                                            │
                                              ─── bundleToDto ──▶  VideoFrameBundleDto
                                                                  (MessagePack on wire)
```

On the server, each item on `RpcStream<VideoFrameBundle>` is a single source
moment. `VideoStreamingBackend.ProcessFrames` decomposes it into per-layer
`VideoFrame`s before publishing into the `VideoStreamMemoizer`, so the
memoizer, fan-out cache, and `ReceiveQualityFilter` keep their per-frame
contract. On the receiver `VideoFrameDto` is reconstructed into `ArrivedChunk`
(`frame-envelopes.ts`) → `EncodedVideoChunk` for WebCodecs.

## Why this shape

- **One worker per recorder** isolates each camera/screencast and gives a
  clean place to pin GPU resources, encoder pools, and the capture clock.
- **Bundle on PushStream, frames on GetStream.** The publisher leg ships all
  per-layer chunks for one source moment as a single bundle — half the wire
  envelopes, half the ACK chatter, and one place to enforce all-or-none
  keyframe policy. The fan-out leg stays per-frame so memoizer eviction,
  cross-shard caching, and `ReceiveQualityFilter` continue to operate at
  layer granularity.
- **Bottom-first emission within a bundle** lets the RPC layer treat L0
  keyframes as compaction sync points: a slow consumer can skip forward to
  the latest L0 KF without losing decodability.
- **Memoizer holds ~3.3 s per layer** so a late joiner gets a keyframe
  immediately and doesn't have to wait up to one full keyframe period.
- **`ReceiveQualityFilter` is per-consumer** so two viewers of the same
  stream can ask for different layers; the filter switches layers only on a
  keyframe to avoid mid-GOP corruption, and clamps the consumer cap into
  `[0, frame.MaxLayerId]` so a temporary layer drop on the producer doesn't
  poison decoding.

## Two control planes

There are two side-channels riding parallel to the data plane:

1. **Quality control** (covered in [08-quality-control.md](./08-quality-control.md)):
   - Sender → server: `ChangeRecordingQuality(state, info)` — encoder health
     (1 Hz) plus active layer count.
   - Receiver → server: `ChangePlaybackQuality(qualityByStream, info)` —
     per-stream `ReceiveQuality { MaxLayerId, MaxTemporalLayerId }` plus
     `RenderVideoSize` hint per stream. Triggers a server-side
     `RequestKeyFrame` only when the stream's desired layer/temporal is
     **upgraded** (downgrades skip the PLI).

2. **Member registration** (covered in [06-server-fanout.md](./06-server-fanout.md)):
   - Receiver → server: `RegisterMember(session, chatId, supportedDecoderCodecs)`
     every 30 s. Server intersects all members' codecs to compute the codec
     the publisher should use; surfaces it through `LiveVideoBackend.GetSupportedCodecs`.

Both ride `ILiveVideoStreams` over Fusion RPC over WebSocket.
