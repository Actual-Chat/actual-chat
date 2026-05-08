# 05 — Server publish path

This doc covers the server side of `PushStream` — from the moment frames hit
the API pod to the point where they sit in the per-stream memoizer ready for
fan-out.

## API entry: `LiveVideoStreams.PushStream`

File: `src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`.

```csharp
public async Task PushStream(
    Session session, string chatId, double clientStartAt,
    VideoFormat format, VideoSourceKind sourceKind,
    RpcStream<VideoFrame> frameStream, CancellationToken ct)
{
    using var stopCts = new CancellationTokenSource(Constants.Video.MaxLiveDuration); // 8 h
    var chatIdTyped = ChatId.Parse(chatId);
    var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
    var record = new VideoRecord(streamId, session, chatIdTyped, clientStartAt, format, sourceKind);
    var newFrameStream = RpcStream.New(frameStream);
    await VideoStreamingBackend.PushVideo(record, newFrameStream, stopCts.Token);
}
```

A new `StreamId` is allocated, **pinned to the current node** (the
`NodeRef` is part of the id), so any subscriber knows which backend shard owns
the stream. Max stream lifetime is hardcoded to 8 hours.

## Backend: `VideoStreamingBackend.PushVideo`

File: `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`.

The handler runs on the backend pod whose shard owns the publisher's chat (the
service is registered with `BackendShardScheme(HostRole.LiveBackend)`).

### 1. Permission and membership

```csharp
var rules = await Chats.GetRules(record.Session, record.ChatId, ct);
rules.Require(ChatPermissions.Write);
rules.Require(ChatPermissions.WriteVideo);
var author = await Authors.EnsureJoined(record.Session, record.ChatId, ct);
```

The publisher must have `WriteVideo` on the chat. Publishing auto-joins the
chat (creates an author if missing) so that subsequent RegisterMember /
chat-author lookups work.

### 2. Source-clock validation (TIMING_ANCHOR)

```csharp
var serverNow = Clocks.SystemClock.Now;
var beginsAt = Moment.FromUnixSeconds(record.ClientStartAt);
var clockDelta = serverNow - beginsAt;
if (Math.Abs(clockDelta.TotalSeconds) > 5) {
    Log.LogWarning("TIMING_ANCHOR: ... source clock skew={ClockDeltaMs}");
    beginsAt = serverNow;
}
```

Senders pass their `MonotonicClock` start time. If it differs from server time
by > 5 s the server overrides it with its own clock. Without this, badly
clock-skewed clients would (a) generate stale-looking offsets that make the
quality controller drop layers, (b) confuse late-joiner replay timing.

### 3. Stream registration

```csharp
var streamInfo = new VideoStreamInfo(
    record.StreamId, record.ChatId, author.Id,
    [record.Format],          // base layer only at first
    beginsAt, record.SourceKind, sourceStartedAt);
await LiveVideoBackend.Register(record.ChatId, streamInfo, ct);
```

The base-layer `VideoFormat` is published immediately so subscribers can find
the stream (via `LiveVideoBackend.List(chatId)` Fusion compute method).
Higher layers' formats are added as their first keyframes arrive.

### 4. The `ProcessFrames` async generator

```csharp
async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrame> source) {
    var keyFrameNumberByLayer = new Dictionary<int, long>();
    var startedLayers = new HashSet<int>();
    var silenceDeadline = ResetSilenceTimer();

    await foreach (var frame in source.WithCancellation(ct)) {
        silenceDeadline = ResetSilenceTimer();

        if (frame.Offset < TimeSpan.Zero) { /* drop, count, log periodically */ continue; }

        var layerId = frame.LayerId ?? 0;

        if (!startedLayers.Contains(layerId)) {
            if (!frame.IsKeyFrame) continue;        // pre-keyframe deltas are useless
            startedLayers.Add(layerId);
            if (layerId > 0) RegisterAdditionalLayer(streamInfo, frame); // grow Formats[]
        }

        if (frame.IsKeyFrame)
            keyFrameNumberByLayer[layerId] = keyFrameNumberByLayer.GetValueOrDefault(layerId) + 1;
        frame.KeyFrameNumber = keyFrameNumberByLayer[layerId];

        yield return frame;
    }
}
```

Five things this generator does:

1. **Silence watchdog.** Camera streams: 10 s without frames cancels the
   stream (`Constants.Video.CameraFrameSilenceTimeout`). Screencast:
   3 minutes (`ScreenCastFrameSilenceTimeout`). Tabs going to background can
   stop producing frames for a while; the longer screencast budget covers
   that.
2. **Drop frames with `Offset < 0`.** Should be impossible, but happens with
   clock-skew bugs; log every 3rd then every 30th to avoid log floods.
3. **Drop pre-keyframe deltas.** Receivers can't decode them. This is per-layer
   so a higher tier whose first keyframe hasn't arrived yet doesn't poison the
   base layer.
4. **Per-layer keyframe counter.** Every keyframe in layer L increments
   `keyFrameNumberByLayer[L]`; deltas inherit the current value. The receiver
   filter (`ReceiveQualityFilter`) uses this to detect that intervening frames
   were evicted from the memoizer mid-GOP and wait for the next keyframe.
5. **Heartbeat re-registration.** Every 2.5 minutes, `Register()` is called
   again to keep the Redis TTL (6 min) fresh.

### 5. Memoizer publication

```csharp
var memoizer = new VideoStreamMemoizer(
    ProcessFrames(videoFrames),
    Constants.Video.ServerReplayTailDuration,  // ≈ 3.3 s
    ct);
if (_videoStreams.Publish(record.StreamId, memoizer))
    await (memoizer.WhenRunning ?? Task.CompletedTask);
else
    await memoizer.DisposeAsync();
```

The memoizer wraps `ProcessFrames` and is registered in
`StreamStore<VideoFrame> _videoStreams`. The store is the per-node registry
keyed by `StreamId`; concurrent first-publishers race for the slot, the loser
disposes its copy.

`StreamStore` is a generic ProcessorBase that:

- Times entries out after `ExpirationDelay = 30 s` of no consumers, so a
  publisher whose RPC dies leaves no zombie memoizer.
- Increments `AppMeters.VideoStreamCount` on publish, decrements on expire.
- Validates `streamId.NodeRef` matches the local node (publishers can't leak
  off-node ids).

When `ProcessFrames` ends (watchdog, RPC error, normal stop) the `finally`
calls `LiveVideoBackend.Unregister(chatId, streamId)`.

## `VideoStreamMemoizer`

File: `src/dotnet/Streaming.Service/Backend/VideoStreamMemoizer.cs`.

The memoizer is a specialised `AsyncMemoizer<VideoFrame>` whose retention is
**duration-tracked and keyframe-anchored per layer** instead of count-based.

### Why per-layer

A 3-layer simulcast at 30 fps emits 90 fps total. A naive
"keep last N frames" buffer ends up tied to the noisiest layer's keyframe
cadence. Per-layer accounting means a quiet layer (e.g. base layer paused due
to AIMD backoff) doesn't drag active layers' tails to eviction.

### Eviction algorithm

```
For each incoming frame:
  update _latestEndByLayer[layerId] = frame.Offset + frame.Duration
  if frame.IsKeyFrame:
    enqueue frame.Offset into _kfOffsetsByLayer[layerId]
    _latestKfByLayer[layerId] = frame.Offset

Eviction loop (while any layer overshoots target AND has ≥2 KFs queued):
  pick layer L with largest excess over target
  newAnchor = dequeue oldest KF offset from L
  drop frames with Offset < newAnchor across the chain
  pop matching KF offsets from any other layer's queue
```

Net result: ~3.3 s tail per layer, evicted as whole keyframe spans (full GOPs)
so the chain head is always at a keyframe.

### Replay anchor (late join)

```csharp
public override async IAsyncEnumerable<VideoFrame> Replay(int tailSize, ...)
{
    TimeSpan? startOffset = _latestKfByLayer.IsEmpty
        ? null
        : _latestKfByLayer.Values.Min();
    // yield from chain head, skip until startOffset
}
```

`tailSize` is ignored. The replay starts from the **minimum** of each layer's
latest keyframe offset, so every layer's most recent keyframe is in the
yielded prefix. The receiver's `ReceiveQualityFilter` can then pick whichever
layer it wants without waiting for the next natural keyframe (up to 3 s).

### Constants

| Constant | Value | Source |
|---|---|---|
| `ServerReplayTailDuration` | ~3.3 s (`KeyFramePeriod * 1.1`) | `Constants.Video.cs` |
| `ServerReplayTailSize` | ~360 frames | same |
| `ExpirationDelay` (StreamStore) | 30 s | same |
| `ShareWaitDelay` (StreamStore) | 2 s | `StreamStore.cs` |
| `MaxLiveDuration` | 8 h | `Constants.Video.cs` |
| `CameraFrameSilenceTimeout` | 10 s | same |
| `ScreenCastFrameSilenceTimeout` | 3 min | same |

## Cancellation & cleanup

```csharp
finally {
    await LiveVideoBackend.Unregister(record.ChatId, record.StreamId, default);
}
```

Unregister fires regardless of how the stream ended. The memoizer itself
remains alive in `_videoStreams` for `ExpirationDelay = 30 s` after the last
consumer detaches — this is what gives reconnecting viewers a free rejoin
window.

## Multi-shard topology and where to look next

Everything above runs on the **publisher's** backend shard. Read
[06-server-fanout.md](./06-server-fanout.md) for what happens on a different
node when a viewer wants the same stream — the cross-shard `RemoteStreamCache`
does its own memoizer-on-top to dedupe fan-out without re-streaming over the
mesh once per viewer.
