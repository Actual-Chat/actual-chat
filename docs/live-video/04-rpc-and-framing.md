# 04 — RPC and frame envelopes

This doc covers the wire format and transport between sender, server, and
receiver. There is one underlying mechanism — Fusion `RpcStream<VideoFrame>`
over the API pod's WebSocket — used for both publish and subscribe.

## The RPC contract

File: `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`.

```csharp
public interface ILiveVideoStreams : IRpcService
{
    [RpcMethod(RemoteExecutionMode = AwaitForConnection | AllowReconnect)]
    Task PushStream(
        Session session, string chatId, double clientStartAt,
        VideoFormat format, VideoSourceKind sourceKind,
        RpcStream<VideoFrame> frameStream, CancellationToken ct);

    Task<RpcStream<VideoFrame>?> GetStream(Session session, StreamId streamId, CancellationToken ct);

    Task RegisterMember(Session session, string chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken ct);
    Task UnregisterMember(Session session, string chatId, CancellationToken ct);
    Task<ApiArray<VideoStreamInfo>> List(Session session, string chatId, CancellationToken ct);

    Task ChangeRecordingQuality(Session, RecordingQualityState?, RecordingQualityInfo?, CancellationToken);
    Task ChangePlaybackQuality(Session, ApiMap<string, ReceiveQuality>?, PlaybackQualityInfo?, CancellationToken);

    [ComputeMethod]
    Task<Moment> LastKeyframeRequestAt(StreamId streamId, CancellationToken ct);
}
```

`RpcStream<T>` is a Fusion primitive that carries an `IAsyncEnumerable<T>` over
the wire with explicit ACK-based flow control. For video the tuning is:

- `AckPeriod = Constants.Video.RpcStreamAckPeriod = 5` (ACK every 5 frames).
- `BufferSize = Constants.Video.RpcStreamBufferSize = 10` (≤ 10 unACK'd frames
  in flight).
- `AllowReconnect = false` — peer reconnects do not resume the stream; the
  worker must re-publish/subscribe.
- Compaction with `canSkipTo: f => f.IsKeyFrame` is enabled on the publisher
  side so a slow consumer skips forward to the most recent keyframe rather
  than dragging its buffer into multi-second delays.

Net effect: ≈ 333 ms server-side outstanding-frame budget at 30 fps.
Latency-over-reliability bias: a 3-frame loss burst can stall the pipeline
until the next keyframe.

## `VideoFrame` (server / wire type)

File: `src/dotnet/Api/Video/VideoFrame.cs`.

```csharp
public sealed partial class VideoFrame
{
    public byte[] Data;                  // raw encoded bytes
    public TimeSpan Offset;              // from sourceStartedAt
    public int? OffsetEpoch;             // sender's MonotonicClock epoch
    public TimeSpan Duration;
    public bool IsKeyFrame;

    // keyframes only
    public int Width, Height;            // encoded dims
    public int? SourceWidth, SourceHeight;  // pre-downscale
    public int MaxLayerWidth, MaxLayerHeight;
    public byte[]? Description;          // SPS/PPS or HVCC

    public string? Codec;
    public int? LayerId;                 // 0 = base
    public int? MaxLayerId;              // producer's current top tier
    public int? TemporalLayerId;         // SVC temporal layer

    public long KeyFrameNumber;          // server-assigned per-layer counter

    public byte[]? SerializedData;       // serialize-once cache (formatter)
}
```

### `CachingVideoFrameFormatter`

File: `src/dotnet/Api/Video/CachingVideoFrameFormatter.cs`.

Custom MessagePack formatter:

- **Serialize**: if `SerializedData` is already populated (from a previous
  fan-out hop), copy it via `WriteRaw`. Otherwise encode to a pooled scratch
  buffer and stash the final bytes on the frame. This makes fan-out to N
  subscribers an O(N) memcpy instead of N MessagePack encodes.
- **Deserialize**: reads into a plain `byte[]`. `Data` and `Description` are
  slices into that array; lifetime is GC-driven, no pooling, no
  use-after-free.

Wire format is a 16-entry map with PascalCase keys (`Data`, `Offset`,
`OffsetEpoch`, `Duration`, `IsKeyFrame`, `Width`, `Height`, `Description`,
`Codec`, `LayerId`, `MaxLayerId`, `TemporalLayerId`, `SourceWidth`,
`SourceHeight`, `MaxLayerWidth`, `MaxLayerHeight`). Forward-compatible —
unknown keys are skipped.

`KeyFrameNumber` is **server-only**: assigned in `ProcessFrames`
(`VideoStreamingBackend`), not produced by the sender, not seen by the
receiver. It exists for `ReceiveQualityFilter`'s gap-detection.

## `VideoFormat` and `VideoStreamInfo`

File: `src/dotnet/Api/Video/VideoFormat.cs`, `…/VideoStreamInfo.cs`.

```csharp
public sealed partial record VideoFormat(string Codec, string CodecSettings,
    int LayerId, Size Size, Size SourceSize);

public sealed partial record VideoStreamInfo(StreamId StreamId, ChatId ChatId,
    AuthorId AuthorId, VideoFormat[] Formats, Moment StartedAt,
    VideoSourceKind SourceKind, Moment SourceStartedAt);
```

`Formats[i]` describes layer `i`. The array begins with the base layer at
`Register` time and is filled in as higher-layer keyframes arrive.
`CodecSettings` is base64-encoded `Description` (SPS/PPS or HVCC).

## TS-side envelopes

File: `src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes.ts`.

The TS pipeline uses a chain of in-memory envelopes that are *not* the wire
type:

```
sender:    CapturedFrame  ─▶  SimulcastBundle  ─▶  EncodedFrame  ─▶  VideoStreamFrame
                                                                    (DTO that mirrors VideoFrame)
receiver:  VideoFrameDto  ─▶  ArrivedChunk  ─▶  DecodedFrame
```

Notable fields:

- **`CapturedFrame`** wraps a raw `VideoFrame` plus
  `capturedAt: { timeMs, epoch }`, `index`, `sourceWidth/Height`,
  `forceKeyframe`, and a `stats` reference shared across the pipeline.
- **`SimulcastBundle`** holds `primary` (top tier) and `extras[]` (bottom-first
  lower tiers). Same `capturedAt` and `index` across all layers.
- **`EncodedFrame`** holds the `EncodedVideoChunk` plus metadata, the source
  `capturedAt` and `index`, the layer id, and source/encoded dimensions.
- **`VideoStreamFrame`** is the TS DTO that maps 1:1 to `VideoFrame`. Mapping
  to the wire type happens in `streaming-glue.ts` (`frameToDto`).
- **`ArrivedChunk`** wraps an `EncodedVideoChunk` reconstructed from the
  arriving DTO plus `capturedAt` (from the DTO) and `arrivedAt` (receiver-side
  monotonic clock).

`VideoRecordingStats` and `VideoPlaybackStats` are mutable shared references
threaded through every envelope; operators mutate counters in-place. This
avoids per-frame allocations and gives `getStats()` a one-read snapshot.

## Offset / clock domains

Two independent monotonic clocks, both anchored to Unix epoch via the
ServerTime-sync subsystem.

- **Sender**: `MonotonicClock` in the recorder worker. `capturedAt.timeMs` is
  this clock. `Offset` on the wire = `(capturedAt.timeMs - sourceStartedAtMs)
  * 10000` — converted to 100-ns ticks for `TimeSpan` interop.
  `OffsetEpoch` carries the clock epoch so the receiver can detect a
  discontinuity.
- **Receiver**: another `MonotonicClock` in the player worker. `arrivedAt` is
  this clock. The end-to-end latency reported by `latencyTap` is
  `now() - capturedAt.timeMs` and is approximate — it relies on both clocks
  being reasonably aligned to wall time, which the server enforces by
  overriding `clientStartAt` if it differs from server time by more than 5 s
  (see [05](./05-server-publish.md)).

## RPC stream lifecycle

```
Sender                                            Server
──────                                            ──────
ensureRpcPush()
  ▼
ILiveVideoStreams.PushStream(... RpcStreamRef)
                                ─────────────▶  LiveVideoStreams.PushStream
                                                  ▼
                                                StreamId.New(thisNode)
                                                  ▼
                                                VideoStreamingBackend.PushVideo
                                                  ▼
                                                ProcessFrames (silence
                                                  watchdog, KF numbering)
                                                  ▼
                                                StreamStore<VideoFrame>.Publish
                                                  ▼
                                                VideoStreamMemoizer

Receiver                                          Server
────────                                          ──────
ILiveVideoStreams.GetStream(streamId)
                                ─────────────▶  LiveVideoStreams.GetStream
                                                  ▼
                                                local? GetVideoRaw(streamId)
                                                remote? GetOrFetchRemoteVideo
                                                  ▼
                                                memoizer.Replay(...)
                                                  ▼
                                                ReceiveQualityFilter.Apply
                                                  ▼  (per-frame getQuality())
                                                RpcStream<VideoFrame> back
                                ◀─────────────
```

`StreamId` is generated server-side and pinned to the publisher's node
(`MeshWatcher.ThisNode.Ref`). Subscribers learn it from
`ILiveVideoStreams.List(chatId)` (Fusion compute method, invalidated on
register/unregister).

`ChangePlaybackQuality` and `ChangeRecordingQuality` are not in `RpcStream`;
they are normal RPC calls. `LastKeyframeRequestAt` is a Fusion compute method
that uses Fusion invalidations to push a "force keyframe" hint without an
explicit RPC: when the server invalidates it, the publisher's compute-method
client sees the new value and the worker's PLI logic forces the next bundle as
a keyframe.

## Why MessagePack, why not WebRTC

The whole stack runs on top of a single Fusion RPC WebSocket, so encoded video
is just another method's payload. There is no SCTP/SRTP, no separate transport,
no NAT traversal — every frame goes through the API pod, which also makes
fan-out and quality control easy to centralise. The trade-off is that every
viewer's traffic is server-mediated; bandwidth on the API pod scales with the
sum of viewers, not with peers.
