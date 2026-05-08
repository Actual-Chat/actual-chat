# 01 — End-to-end walkthrough

This is the "follow one frame across the whole system" tour. Each later doc
zooms into one stage. References are to source paths under
`/proj/ActualChat-C1`.

## Cast of characters

| Layer | Process | Entry point |
|---|---|---|
| Sender DOM | Main browser thread | `VideoTrackPlayer.razor` (preview), `VideoRecorder` |
| Sender worker | `recorderWorker.js` | `recorder-worker-host.ts` → `recorder.ts` |
| API pod | .NET | `LiveVideoStreams` (`ILiveVideoStreams`) |
| Backend pod | .NET, sharded by `ChatId` | `VideoStreamingBackend`, `LiveVideoBackend` |
| Receiver DOM | Main browser thread | `VideoTrackPlayer.razor`, `VideoPlayer` |
| Receiver worker | `playerWorker.js` | `player-worker-host.ts` → `player.ts` |

## End-to-end timeline (one camera frame)

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
    DOMA->>DOMA: requestVideoFrameCallback
    DOMA->>WA: pushFrame(VideoFrame, transfer)
    Note over WA: capture → stampCaptureTime →<br/>attachSourceDims → downscale (GPU)<br/>→ applyKeyframePolicy
    WA->>WA: encode each layer (WebCodecs)
    Note over WA: bottom-first: L0, L1, …
    WA->>WA: build VideoStreamFrame
    WA->>API: PushStream(session, chatId, format,<br/>RpcStream&lt;VideoFrame&gt;)
    API->>VSB: PushVideo(VideoRecord, stream)
    VSB->>VSB: ProcessFrames<br/>(silence watchdog, KF numbering)
    VSB->>Memo: yield VideoFrame
    Note over Memo: Per-layer KF queue,<br/>~3.3 s rolling tail

    DOMB->>WB: start({streamId, decoderConfig})
    WB->>API: GetStream(session, streamId)
    API->>VSB: GetVideoRaw(streamId)
    VSB->>Memo: Replay(from min latest-KF per layer)
    Memo-->>VSB: IAsyncEnumerable&lt;VideoFrame&gt;
    VSB-->>API: RpcStream&lt;VideoFrame&gt;<br/>(SkipWhile !IsKeyFrame)
    API->>API: ReceiveQualityFilter.Apply<br/>(per-frame getQuality())
    API-->>WB: VideoFrameDto stream
    WB->>WB: pull → resetOnEpochChange →<br/>pacedEncodedBuffer → decode → present
    WB-->>DOMB: MSTG track or canvas frames
```

## Stage-by-stage data shapes

```
                CapturedFrame              SimulcastBundle           EncodedFrame
                ┌──────────────┐           ┌────────────────┐        ┌────────────────┐
camera ─▶ MSTP ─▶│ frame        │── down  ▶│ primary (top)  │── enc ▶│ chunk + meta   │
                │ capturedAt   │  scale   │ extras[base..] │        │ layerId        │
                │ index        │  (GPU)   │ stats          │        │ source/encoded │
                │ source W/H   │          └────────────────┘        │ dims           │
                │ stats (ref)  │                                    │ stats (ref)    │
                └──────────────┘                                    └────────────────┘
                                                                            │
                                                                            ▼
                                                                   VideoStreamFrame
                                                                   ┌────────────────┐
                                                                   │ offset, dur    │
                                                                   │ isKeyFrame     │
                                                                   │ width, height  │
                                                                   │ description?   │
                                                                   │ data (bytes)   │
                                                                   │ layerId, …     │
                                                                   └────────────────┘
                                                                            │
                                                  ─── frameToDto ──▶  VideoFrameDto
                                                                       (MessagePack on wire)
```

On the server `VideoFrameDto` is decoded into `VideoFrame` (`Api/Video/VideoFrame.cs`)
and on the receiver `VideoFrameDto` is decoded into `ArrivedChunk`
(`frame-envelopes.ts`) → `EncodedVideoChunk` for WebCodecs.

## Why this shape

- **One worker per recorder** isolates each camera/screencast and gives a clean
  place to pin GPU resources, encoder pools, and the capture clock.
- **Bottom-first layer emission** (L0 first within a bundle) lets the RPC layer
  treat L0 keyframes as compaction sync points: a slow consumer can skip
  forward to the latest L0 KF without losing decodability.
- **Memoizer holds ~3.3 s per layer** so a late joiner gets a keyframe
  immediately and doesn't have to wait up to one full keyframe period.
- **`ReceiveQualityFilter` is per-consumer** so two viewers of the same stream
  can ask for different layers; the filter switches layers only on keyframe
  boundaries to avoid mid-GOP corruption.

## Two control planes

There are two side-channels riding parallel to the data plane:

1. **Quality control** (covered in [08-quality-control.md](./08-quality-control.md)):
   - Sender → server: `ChangeRecordingQuality(state, info)` — encoder health
     (1 Hz) plus active layer count.
   - Receiver → server: `ChangePlaybackQuality(qualityByStream, info)` — per-stream
     `ReceiveQuality { MaxLayerId, MaxTemporalLayerId }`. Triggers a server-side
     `RequestKeyFrame` when a stream's desired layer changes.

2. **Member registration** (covered in [06-server-fanout.md](./06-server-fanout.md)):
   - Receiver → server: `RegisterMember(session, chatId, supportedDecoderCodecs)`
     every 30 s. Server intersects all members' codecs to compute the codec the
     publisher should use; SCREENS this through `LiveVideoBackend`.

Both ride `ILiveVideoStreams` over Fusion RPC over WebSocket.
