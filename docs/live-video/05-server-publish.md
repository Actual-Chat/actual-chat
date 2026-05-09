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
    RpcStream<VideoFrameBundle> frameStream, CancellationToken ct)
{
    using var stopCts = new CancellationTokenSource(Constants.Video.MaxLiveDuration); // 8 h
    try {
        var chatIdTyped = ChatId.Parse(chatId);
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var record = new VideoRecord(streamId, session, chatIdTyped, clientStartAt, format, sourceKind);
        var newFrameStream = RpcStream.New(frameStream);
        await VideoStreamingBackend.PushVideo(record, newFrameStream, stopCts.Token);
    }
    finally {
        frameStream.Disconnect();   // release the producer's far end
    }
}
```

A new `StreamId` is allocated, **pinned to the current node** (the
`NodeRef` is part of the id), so any subscriber knows which backend shard
owns the stream. Max stream lifetime is hardcoded to 8 hours.

The wire payload is `RpcStream<VideoFrameBundle>` — one item per source
moment, all simulcast layers in `Layers[]`. The bundle abstraction does not
escape the publisher leg: backend decomposition (below) yields per-layer
`VideoFrame`s into the memoizer.

## Backend: `VideoStreamingBackend.PushVideo`

File: `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`.

The handler runs on the backend pod whose shard owns the publisher's chat
(the service is registered with `BackendShardScheme(HostRole.LiveBackend)`).

### 1. Permission and membership

```csharp
var rules = await Chats.GetRules(record.Session, record.ChatId, ct);
rules.Require(ChatPermissions.Write);
rules.Require(ChatPermissions.WriteVideo);
var author = await Authors.EnsureJoined(record.Session, record.ChatId, ct);
```

The publisher must have `WriteVideo` on the chat. Publishing auto-joins the
chat (creates an author if missing) so that subsequent `RegisterMember` /
chat-author lookups work.

### 2. Source-clock validation (TIMING_ANCHOR)

```csharp
var serverNow = Clocks.ServerClock.Now;
var beginsAt = sourceStartedAt;
var clockDelta = serverNow - beginsAt;
if (Math.Abs(clockDelta.TotalSeconds) > 5) {
    Log.LogWarning("TIMING_ANCHOR: ... source clock skew={ClockDeltaMs}");
    beginsAt = serverNow;
}
```

Senders pass their `MonotonicClock` start time. If it differs from server
time by > 5 s the server overrides it with its own clock. Without this, badly
clock-skewed clients would (a) generate stale-looking offsets that make the
quality controller drop layers, (b) confuse late-joiner replay timing.

### 3. Stream registration

```csharp
var streamInfo = new VideoStreamInfo(
    record.StreamId, record.ChatId, author.Id,
    record.Format,                   // top-tier format only
    beginsAt, record.SourceKind, sourceStartedAt);
await LiveVideoBackend.Register(record.ChatId, streamInfo, ct);
```

The top-tier `VideoFormat` is published immediately so subscribers can find
the stream (via `LiveVideoBackend.List(chatId)` Fusion compute method).
Per-layer formats are not stored — the receiver derives ladder dimensions
from `SourceKind`.

### 4. Stream-silence watchdog

File: `src/dotnet/Streaming.Service/Services/StreamSilenceWatchdog.cs`.

```csharp
silenceWatchdogTask = StreamSilenceWatchdog.Run(
    consumeFrameCount: () => Interlocked.Exchange(ref bundleCounter, 0),
    onSilenceCts: watchdogCts,
    interval: Constants.Video.StreamSilenceCheckInterval,                  // 5 s
    maxConsecutiveZeroIntervals: Constants.Video.StreamSilenceMaxConsecutiveZeroIntervals, // 2
    onSilenceDetected: zeroIntervals => Log.LogWarning(...),
    cancellationToken: cancellationToken);
```

Interval-based: every 5 s the watchdog samples-and-resets the bundle
counter. After 2 consecutive intervals with zero arrivals it cancels the
linked `watchdogCts`, which tears down `ProcessFrames` and frees the
single-screencast-per-chat slot. The unified watchdog replaced the older
split `CameraFrameSilenceTimeout` / `ScreenCastFrameSilenceTimeout`.

The counter advances **once per bundle** (= one source moment), regardless
of how many simulcast layers it carries. On a clean source end the
`finally` cancels `watchdogCts` and awaits the watchdog task so it doesn't
keep us blocked for up to one full interval.

### 5. The `ProcessFrames` async generator

```csharp
async IAsyncEnumerable<VideoFrame> ProcessFrames(IAsyncEnumerable<VideoFrameBundle> source) {
    await foreach (var bundle in source.WithCancellation(ct)) {
        Interlocked.Increment(ref bundleCounter);
        if (bundle.Layers.Length == 0) continue;

        foreach (var frame in bundle.Layers) {
            var layerId = frame.LayerId;
            if (frame.Offset < TimeSpan.Zero) { /* drop, count, log periodically */ continue; }

            if (frame.IsKeyFrame) {
                if (startedLayers.Add(layerId)) Log.LogWarning("first KF ...");
            }
            else if (!startedLayers.Contains(layerId)) {
                /* drop pre-keyframe deltas, count + log periodically */ continue;
            }

            if (frame.IsKeyFrame)
                keyFrameNumberByLayer[layerId] = keyFrameNumberByLayer.GetValueOrDefault(layerId) + 1;
            frame.KeyFrameNumber = keyFrameNumberByLayer[layerId];
            yield return frame;
        }

        if (lastHeartbeat.Elapsed >= TimeSpan.FromMinutes(2.5))     // half ChatStateTtl
            await LiveVideoBackend.Register(record.ChatId, streamInfo, CancellationToken.None);
    }
}
```

Five things this generator does:

1. **Bundle decomposition.** Each input `VideoFrameBundle` is decomposed
   into its per-layer `VideoFrame`s and yielded as separate items, so the
   memoizer / filter / `GetStream` chain stays per-frame.
2. **Drop frames with `Offset < 0`.** Should be impossible, but happens with
   clock-skew bugs; log every 3rd then every 30th to avoid log floods.
3. **Drop pre-keyframe deltas.** Receivers can't decode them. Per-layer
   tracking so a higher tier whose first keyframe hasn't arrived yet doesn't
   poison the base layer.
4. **Per-layer keyframe counter.** Every keyframe in layer L increments
   `keyFrameNumberByLayer[L]`; deltas inherit the current value. The
   receiver filter (`ReceiveQualityFilter`) uses this to detect that
   intervening frames were evicted from the memoizer mid-GOP and wait for
   the next keyframe.
5. **Heartbeat re-registration.** Every 2.5 minutes, `Register()` is called
   again to keep the Redis TTL fresh (Redis hash TTL = 6 min;
   `LiveVideoBackend.ChatStateTtl` = 5 min).

### 6. Memoizer publication

```csharp
var memoizer = new VideoStreamMemoizer(
    ProcessFrames(videoBundles),
    Constants.Video.ServerReplayTailDuration,    // ~3.3 s
    cancellationToken);
if (_videoStreams.Publish(record.StreamId, memoizer))
    await (memoizer.WhenRunning ?? Task.CompletedTask);
else
    await memoizer.DisposeAsync();
```

The memoizer wraps `ProcessFrames` and is registered in
`StreamStore<VideoFrame> _videoStreams`. The store is the per-node registry
keyed by `StreamId`; concurrent first-publishers race for the slot, the
loser disposes its copy.

`StreamStore` is a generic ProcessorBase that:

- Times entries out after `ExpirationDelay = 30 s` of no consumers, so a
  publisher whose RPC dies leaves no zombie memoizer.
- Increments `AppMeters.VideoStreamCount` on publish, decrements on expire.
- Validates `streamId.NodeRef` matches the local node (publishers can't
  leak off-node ids).

When `ProcessFrames` ends (silence watchdog, RPC error, normal stop) the
`finally` cancels the watchdog and unconditionally calls
`LiveVideoBackend.Unregister(chatId, streamId)`.

## `VideoStreamMemoizer`

File: `src/dotnet/Streaming.Service/Backend/VideoStreamMemoizer.cs`.

The memoizer is a specialised `AsyncMemoizer<VideoFrame>` whose retention is
**duration-tracked and keyframe-anchored per layer** instead of count-based.

### Why per-layer

A 3-layer simulcast at 30 fps emits 90 fps total. A naive
"keep last N frames" buffer ends up tied to the noisiest layer's keyframe
cadence. Per-layer accounting means a quiet layer (e.g. base layer paused
due to AIMD backoff) doesn't drag active layers' tails to eviction.

### Eviction algorithm

```
For each incoming frame:
  update _latestEndByLayer[layerId] = max(prev, Offset + Duration)
  if frame.IsKeyFrame:
    enqueue Offset into _kfOffsetsByLayer[layerId]
    _latestKfByLayer[layerId] = Offset

Eviction loop (while any layer overshoots target AND has ≥ 2 KFs queued):
  pick layer L* with largest excess over target
  newAnchor = dequeue oldest KF offset from L*
  advance head past every node with Offset < newAnchor
  pop matching offsets from any other layer's queue (and clear that
    layer's _latestKfByLayer entry if its queue went empty, so a paused
    layer's stale anchor doesn't poison Replay)
```

Net result: ~3.3 s tail per layer, evicted as whole keyframe spans (full
GOPs) so the chain head is always at a keyframe.

### Replay anchor (late join)

```csharp
public override async IAsyncEnumerable<VideoFrame> Replay(int tailSize, ...)
{
    var kfSnapshot = _latestKfByLayer.Values.ToArray();
    TimeSpan? startOffset = kfSnapshot.Length == 0 ? null : kfSnapshot.Min();
    // yield from chain head, skip until startOffset
}
```

`tailSize` is ignored — the keyframe anchor drives the start position. The
replay starts from the **MIN** of each layer's latest keyframe offset, so
every active layer's most recent keyframe is in the yielded prefix. The
caller's `SkipWhile(!IsKeyFrame)` (in `GetVideoRaw`) handles the cold-path
case where no keyframe has arrived yet.

The snapshot is taken to a single array first because
`ConcurrentDictionary.IsEmpty` / `Values` / `Min` are individually
thread-safe but not atomic together — without the snapshot a concurrent
producer-side eviction could remove the only entry between `IsEmpty` and
`Min` and surface as a `KeyNotFoundException`.

### Constants

| Constant | Value | Source |
|---|---|---|
| `ServerReplayTailDuration` | ~3.3 s (`KeyFramePeriod * 1.1`) | `Constants.Video.cs` |
| `ServerReplayTailSize` | 360 frames (3 × 30 × 4) | derived |
| `StreamExpirationDelay` (StreamStore) | 30 s | `Constants.Video.cs` |
| `MaxLiveDuration` | 8 h | same |
| `StreamSilenceCheckInterval` | 5 s | same |
| `StreamSilenceMaxConsecutiveZeroIntervals` | 2 | same |
| `CancellationDelay` (PushVideo cleanup) | 5 s | same |

## `GetVideoRaw` — local fan-out

```csharp
public virtual async Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken ct)
{
    var stream = await _videoStreams.Get(streamId, ct);
    if (stream == null) return null;

    // SkipWhile diagnostics: counts non-KF chunks dropped at the head and logs
    // the wait until the first decodable KF surfaces. Direct evidence of a
    // late-subscriber-wait when ServerReplayTailSize is too narrow.
    stream = stream.SkipWhile(f => { if (f.IsKeyFrame) { /* log first KF */ return false; } skipCount++; return true; });

    return new RpcStream<VideoFrame>(stream) {
        AckPeriod  = Constants.Video.RpcStreamAckPeriod,   // 5
        AckAdvance = Constants.Video.RpcStreamAckAdvance,  // 16
    };
}
```

`SkipWhile(!IsKeyFrame)` is the safety net for the cold replay path. Skip
diagnostics surface in the log as
`GetVideoRaw: #{StreamId} first decodable KF after dropping {SkipCount} non-KF chunks in {ElapsedMs}ms`
so a too-narrow replay tail is visible immediately.

## Cancellation & cleanup

```csharp
finally {
    if (silenceWatchdogTask is not null) {
        await watchdogCts.CancelAsync();
        await silenceWatchdogTask;          // expected OperationCanceledException
    }
    await LiveVideoBackend.Unregister(record.ChatId, record.StreamId, CancellationToken.None);
}
```

Unregister fires on every termination — clean stop, watchdog cancellation,
or RPC error. The memoizer itself remains alive in `_videoStreams` for
`ExpirationDelay = 30 s` after the last consumer detaches, which is what
gives reconnecting viewers a free rejoin window.

## Multi-shard topology and where to look next

Everything above runs on the **publisher's** backend shard. Read
[06-server-fanout.md](./06-server-fanout.md) for what happens on a different
node when a viewer wants the same stream — `RemoteVideoStreamCache` runs its
own memoizer-on-top to dedupe fan-out so N viewers on the same API pod
collapse to one cross-shard pull.
