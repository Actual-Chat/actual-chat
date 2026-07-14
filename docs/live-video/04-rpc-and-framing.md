# 04 — RPC and frame envelopes

This doc covers the wire format and transport between sender, server, and
receiver. Two RpcStreams are involved: a `RpcStream<VideoFrameBundle>` from
the publisher to the API pod (one item = one source moment, all simulcast
layers), and a `RpcStream<VideoFrame>` from the API pod to each viewer (one
item = one per-layer frame, after server-side decomposition and per-consumer
filtering).

## The RPC contract

File: `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`.

```csharp
public interface ILiveVideoStreams : IComputeService
{
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session, StreamId streamId, CancellationToken ct);

    [ComputeMethod, RemoteComputeMethod(CacheMode = NoCache)]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken ct);
    [ComputeMethod, RemoteComputeMethod(CacheMode = NoCache)]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken ct);
    [ComputeMethod, RemoteComputeMethod(CacheMode = NoCache)]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken ct);

    [ComputeMethod, RemoteComputeMethod(CacheMode = NoCache)]
    Task<Moment> LastKeyframeRequestAt(Session session, StreamId streamId, CancellationToken ct);

    Task RegisterMember(Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken ct);
    Task UnregisterMember(Session session, ChatId chatId, CancellationToken ct);

    [RpcMethod(RemoteExecutionMode = AwaitForConnection | AllowReconnect)]
    Task PushStream(Session, string chatId, double clientStartAt,
        VideoFormat format, VideoSourceKind sourceKind,
        RpcStream<VideoFrameBundle> frameStream, CancellationToken ct);

    [RpcMethod(RemoteExecutionMode = AwaitForConnection, ConnectTimeout = 10)]
    Task RequestKeyFrame(Session, string streamId, CancellationToken ct);

    [RpcMethod(RemoteExecutionMode = AwaitForConnection, ConnectTimeout = 10)]
    Task ChangeRecordingQuality(Session, RecordingQualityState?, RecordingQualityInfo?, CancellationToken);
    [RpcMethod(RemoteExecutionMode = AwaitForConnection, ConnectTimeout = 10)]
    Task ChangePlaybackQuality(Session, ApiMap<string, ReceiveQuality>?, PlaybackQualityInfo?, CancellationToken);
}
```

`RpcStream<T>` is a Fusion primitive that carries an `IAsyncEnumerable<T>`
over the wire with explicit ACK-based flow control. The two directions are
tuned independently.

### Sender → API pod (bundle stream)

Built in `streaming/push-to-pull-buffer.ts` via
`MediaRpcStreamOptions.videoRealtime`:

| Param | Value | Source |
|---|---|---|
| `isRealTime` | `true` | `api.ts:101` |
| `allowReconnect` | `true` | same |
| `ackPeriod` | `floor(targetBufferSize / 3)` = 2 frames | `app-constants.ts` (`expandVideo`) |
| `ackAdvance` | `ceil(frameRate × 1.5)` = 45 bundles — BDP-sized: credits are per bundle, so throughput ≤ `ackAdvance / RTT`; 1.5 s of frames sustains 30 fps up to ~750 ms RTT | same |
| `bufferSize` | `senderBufferSize ≈ keyFramePeriodSize × 4/3` (~120 source moments) | same |
| `canSkipTo` | `bundle ⇒ bundle.Layers[0].IsKeyFrame` | `api.ts:111` |

The sender ring is large by design: a stalled wire is absorbed by skipping
older non-keyframe bundles inside the ring (compaction), not by blocking
the capture pipeline. Compaction always picks bundle-aligned keyframe
boundaries, so simulcast layers never desync.

### API pod → Viewer (per-frame stream)

Built in `LiveVideoStreams.GetStream` and `VideoStreamingBackend.GetVideoRaw`:

| Param | Value | Source |
|---|---|---|
| `AllowReconnect` | `false` (per-consumer, per-subscribe) | `LiveVideoStreams.cs` |
| `AckPeriod` | `Constants.Video.RpcStreamAckPeriod = 5` | `Constants.Video.cs` |
| `AckAdvance` | `Constants.Video.RpcStreamAckAdvance = 16` (= `AckPeriod*3 + 1`) | same |

`AllowReconnect = false` on the consumer leg means that on a peer change the
viewer re-subscribes (receives a fresh keyframe via the standard PLI path)
rather than resuming an in-progress stream from server state.

## `VideoFrame` (server / wire type)

File: `src/dotnet/Api/Video/VideoFrame.cs`.

```csharp
public sealed partial class VideoFrame : MediaFrame
{
    public override TimeSpan Offset { get; init; }       // Key 1
    public int OffsetEpoch { get; init; }                // Key 2 — sender clock epoch
    public override TimeSpan Duration { get; init; }     // Key 3
    public override bool IsKeyFrame { get; init; }       // Key 4
    public int Width  { get; init; }                     // Key 5
    public int Height { get; init; }                     // Key 6
    public ReadOnlyMemory<byte> Description { get; init; } // Key 7 (KF only)
    public string? Codec { get; init; }                  // Key 8 (KF only)
    public byte LayerId { get; init; }                   // Key 9
    public byte MaxLayerId { get; init; }                // Key 10
    public byte TemporalLayerId { get; init; }           // Key 11
    public int SourceWidth  { get; init; }               // Key 12 (KF only)
    public int SourceHeight { get; init; }               // Key 13 (KF only)
    public int MaxLayerWidth  { get; init; }             // Key 14
    public int MaxLayerHeight { get; init; }             // Key 15

    // Not serialized:
    [IgnoreMember] public long KeyFrameNumber { get; set; }
    [IgnoreMember] public ReadOnlyMemory<byte> SerializedData { get; set; }
}
```

The encoded payload itself (`Data`) is inherited from `MediaFrame`. Per-layer
ids are `byte` (cheaper on the wire; layer counts max out at 3 today).

### `VideoFrameBundle` (publisher leg only)

File: `src/dotnet/Api/Video/VideoFrameBundle.cs`.

```csharp
public sealed partial class VideoFrameBundle(VideoFrame[] layers)
{
    public VideoFrame[] Layers { get; init; } = layers;  // Key 0 — bottom-first
    public int LayerCount => Layers.Length;
    public VideoFrame TopLayer => Layers[^1];
    public VideoFrame BottomLayer => Layers[0];
}
```

A bundle is one captured source moment: 1..N per-layer `VideoFrame`s sharing
`Offset`, `Duration`, keyframe flag, source dims, and codec settings; only
`Data`, `Width`/`Height`, `Description`, and `LayerId` differ.

`VideoStreamingBackend.ProcessFrames` decomposes the bundle into individual
`VideoFrame`s before publishing into the memoizer; **the bundle type does not
exist past the publisher leg.**

### `CachingVideoFrameFormatter`

File: `src/dotnet/Api/Video/CachingVideoFrameFormatter.cs`.

Custom MessagePack formatter:

- **Serialize**: if `SerializedData` is already populated (from a previous
  fan-out hop), copy it via `WriteRaw`. Otherwise encode to a pooled scratch
  buffer and stash the final bytes on the frame. Fan-out to N subscribers is
  then O(N) memcpy instead of N MessagePack encodes.
- **Deserialize**: reads into a plain `byte[]`. `Data` and `Description`
  are slices into that array; lifetime is GC-driven, no pooling, no
  use-after-free.

Wire format: PascalCase keys (`Offset`, `OffsetEpoch`, `Duration`,
`IsKeyFrame`, `Width`, `Height`, `Description`, `Codec`, `LayerId`,
`MaxLayerId`, `TemporalLayerId`, `SourceWidth`, `SourceHeight`,
`MaxLayerWidth`, `MaxLayerHeight`, plus `Data` from `MediaFrame`).
Forward-compatible — unknown keys are skipped.

`KeyFrameNumber` is **server-only**: assigned in `ProcessFrames`
(`VideoStreamingBackend`), per-layer counter, not produced by the sender,
not seen by the receiver. It exists for `ReceiveQualityFilter`'s
gap-detection (see [08](./08-quality-control.md)).

## `VideoFormat` and `VideoStreamInfo`

File: `src/dotnet/Api/Video/VideoFormat.cs`, `…/VideoStreamInfo.cs`.

```csharp
public sealed partial record VideoFormat : MediaFormat
{
    public string Codec { get; init; } = "avc1";
    public string CodecSettings { get; init; } = "";  // base64 description
    public byte LayerId { get; init; }
    public Size2D Size { get; init; }
    public Size2D SourceSize { get; init; }
}

public sealed partial record VideoStreamInfo(
    StreamId StreamId, ChatId ChatId, AuthorId AuthorId,
    VideoFormat Format,                           // top-tier format only
    Moment StartedAt,
    VideoSourceKind SourceKind = VideoSourceKind.Camera,
    Moment SourceStartedAt = default);
```

`VideoStreamInfo.Format` carries the **top-tier** layer's codec, dims, and
codec settings only. The per-layer ladder for non-top layers is derivable
from `SourceKind` via the recorder's ladder builder, so the server doesn't
carry it per stream.

## TS-side envelopes

File: `src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes.ts`.

```
sender:    CapturedFrame  ─▶  CapturedBundle  ─▶  EncodedBundle  ─▶  VideoStreamFrameBundle
                                                                    (DTO that mirrors VideoFrameBundle)
receiver:  VideoFrameDto  ─▶  ArrivedChunk  ─▶  DecodedFrame
```

Notable types:

- **`CapturedFrame`** — raw `VideoFrame` plus `capturedAt: { timeMs, epoch }`,
  `index`, `sourceWidth/Height`, `forceKeyframe`, and the shared `stats` ref.
- **`CapturedBundle`** — `{ layers: CapturedFrame[], stats }`, layers
  bottom-first; all entries share the source `capturedAt`/`index`. Single-tier
  streams produce a length-1 bundle. `disposeCapturedBundle` closes every
  layer's frame.
- **`EncodedFrame`** — `EncodedVideoChunk` + metadata + source `capturedAt` +
  `index` + `layerId` + source/encoded dimensions.
- **`EncodedBundle`** — `{ layers: EncodedFrame[], stats }`, bottom-first.
  `disposeEncodedBundle` closes every chunk.
- **`VideoStreamFrame`** — TS DTO for one per-layer wire frame
  (`offset/offsetEpoch/duration/isKeyFrame/width/height/data/description?/codec?/temporalLayerId?/layerId?/maxLayerId/sourceWidth?/sourceHeight?`).
- **`VideoStreamFrameBundle`** — `{ layers: VideoStreamFrame[] }`. Mapping to
  the wire type happens in `streaming/push-to-pull-buffer.ts`
  (`bundleToDto` / `frameToDto`).
- **`ArrivedChunk`** — `{ chunk, arrivedAt, capturedAt: { timeMs, epoch },
  isKeyFrame, description?, layerId, width, height, rawByteLength, stats }`.
  `arrivedAt` is the receiver's `MonotonicClock`; `capturedAt` is parsed from
  the wire `Offset`/`OffsetEpoch`.

`VideoRecordingStats` and `VideoPlaybackStats` are mutable shared references
threaded through every envelope; operators mutate counters in-place. This
avoids per-frame allocations and gives `getStats()` a one-read snapshot.

## Offset / clock domains

Two independent monotonic clocks, both anchored to Unix epoch via the
ServerTime-sync subsystem.

- **Sender**: `MonotonicClock` in the recorder worker. `capturedAt.timeMs` is
  this clock. `Offset` on the wire =
  `(top.capturedAt.timeMs - sourceStartMs) × 10000` — converted to 100-ns
  ticks for `TimeSpan` interop. `OffsetEpoch` carries the clock epoch so the
  receiver can detect a discontinuity and call `buffer.reset()` on the
  encoded buffer (see [07](./07-receiver.md)).
- **Receiver**: another `MonotonicClock` in the player worker. `arrivedAt`
  is this clock. The end-to-end latency reported by `latencyTap` is
  `now() - capturedAt.timeMs` and is approximate — it relies on both clocks
  being reasonably aligned to wall time, which the server enforces by
  overriding `clientStartAt` if it differs from server time by more than 5 s
  (see [05](./05-server-publish.md)).

## RPC stream lifecycle

```
Sender                                          Server
──────                                          ──────
ensureRpcPush()
  ▼
ILiveVideoStreams.PushStream(... RpcStream<VideoFrameBundle>)
                                ─────────────▶  LiveVideoStreams.PushStream
                                                  ▼
                                                StreamId.New(thisNode)
                                                  ▼
                                                VideoStreamingBackend.PushVideo
                                                  ▼
                                                ProcessFrames
                                                  - silence watchdog
                                                  - decompose bundle → frames
                                                  - per-layer KeyFrameNumber
                                                  ▼
                                                StreamStore<VideoFrame>.Publish
                                                  ▼
                                                VideoStreamMemoizer

Receiver                                        Server
────────                                        ──────
ILiveVideoStreams.GetStream(streamId)
                                ─────────────▶  LiveVideoStreams.GetStream
                                                  ▼
                                                local? GetVideoRaw(streamId)
                                                remote? RemoteVideoStreamCache (deduped)
                                                  ▼
                                                memoizer.Replay()
                                                  ▼
                                                ReceiveQualityFilter.Apply
                                                  ▼  (per-frame getQuality())
                                                RpcStream<VideoFrame> back
                                ◀─────────────
```

`StreamId` is generated server-side and pinned to the publisher's node
(`MeshWatcher.ThisNode.Ref`). Subscribers learn it from
`ILiveVideoStreams.List(chatId)` (Fusion compute method, invalidated on
`Register`/`Unregister`).

`ChangePlaybackQuality`, `ChangeRecordingQuality`, and `RequestKeyFrame` are
not stream methods — they are normal RPCs (`RpcNoWait` for the quality calls,
plain `Task` for the keyframe request). `LastKeyframeRequestAt` is a Fusion
compute method whose value is just the current server clock; its
**invalidation** is what drives the publisher worker to force the next
bundle as a keyframe.

## Why MessagePack, why not WebRTC

The whole stack runs on top of a single Fusion RPC WebSocket, so encoded
video is just another method's payload. There is no SCTP/SRTP, no separate
transport, no NAT traversal — every frame goes through the API pod, which
also makes fan-out and quality control easy to centralise. The trade-off is
that every viewer's traffic is server-mediated; bandwidth on the API pod
scales with the sum of viewers, not with peers.
