# 06 — Server fan-out and chat state

The publish path ends with a `VideoStreamMemoizer` registered in a
`StreamStore<VideoFrame>` on the publisher's backend shard. This doc covers
how that memoizer reaches viewers — including viewers connected to different
API or backend pods.

## Three services, three roles

| Service | Where | Purpose |
|---|---|---|
| `ILiveVideoStreams` | API pod | Auth, RPC façade for clients (`PushStream`, `GetStream`, …) |
| `IVideoStreamingBackend` | Backend pod (sharded by `ChatId` via `LiveBackend`) | Per-node `StreamStore` + memoizers |
| `ILiveVideoBackend` | Backend pod (sharded by `ChatId`) | Chat-wide registry (active streams, members, codec negotiation), Redis-backed |

Files:

- API: `src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`
- Backend stream: `…/Backend/VideoStreamingBackend.cs`
- Backend chat state: `…/Backend/LiveVideoBackend.cs`, `LiveVideoBackend.ChatState.cs`
- Cross-shard cache: `…/Services/RemoteStreamCaches.cs`, `…/Services/StreamStore.cs`

## The chat registry — `LiveVideoBackend`

`ILiveVideoBackend` is a Fusion compute service (sharded). It owns two
Redis-backed registries:

```
RedisMultiHashMap<VideoStreamInfo>  key="live-video:streams"   ttl=6 min
RedisMultiHashMap<VideoStreamMemberInfo> key="live-video:members" ttl=6 min
```

### `Register(chatId, streamInfo)`

Called by `VideoStreamingBackend.PushVideo` at start, periodically (every
2.5 min) as a heartbeat, and whenever a new SVC layer becomes active.

Behaviour:

- **Idempotent if entry equals previous**: bumps Redis TTL, no Fusion
  invalidation. Heartbeats are cheap.
- **Single screencast per chat**: rejects a second screencast (different
  author) with `InvalidOperationException`.
- **Camera limit**: max `Constants.Video.MaxCameraStreamsPerChat = 8`. Going
  over throws `VideoStreamLimitExceededException` (which propagates to the
  client and is surfaced in the UI).
- **Stale-stream eviction**: if the same author registers a new stream of the
  same kind, the older `StreamId` is removed (handles camera reconnects).

`List(chatId)` is `[ComputeMethod]` — invalidated by `Register`/`Unregister`,
so subscribers' UI can react to new streams without polling.

### Member registry — codec negotiation

`RegisterMember(session, chatId, supportedDecoderCodecs)` is called by every
*viewer* every 30 s (the timer in `VideoTrackPlayer.razor`). It writes a
`VideoStreamMemberInfo { SupportedDecoderCodecs, RegisteredAt }` row.

`GetSupportedCodecs(chatId)` (also `[ComputeMethod]`) computes the intersection
across all members. Senders use it to pick the best mutually-supported codec.

Hysteresis (`ChatState.cs`):

- Upgrades (e.g. all viewers now have AV1) are delayed by
  `Constants.Video.CodecSwitchHysteresisWindow = 10 s`.
- Downgrades (e.g. a Safari user joins, can only do H.264) apply immediately.

Stale members (no update in 90 s) are pruned periodically.

## Per-node fan-out — `StreamStore`

File: `Services/StreamStore.cs`.

Each backend pod has a single `StreamStore<VideoFrame> _videoStreams`. The
store:

- Maps `StreamId → ExpiringEntry<AsyncMemoizer<VideoFrame>>`.
- `Publish(streamId, memoizer)` — atomic; loser of a race must dispose.
- `Get(streamId, waitForShare)` — returns the memoizer's shared
  `IAsyncEnumerable<VideoFrame>` (multiple consumers are joined onto the same
  stream).
- `ExpirationDelay = 30 s` — tears down the entry once the last consumer
  detaches.
- `OnStreamExpire` callback decrements `AppMeters.VideoStreamCount`.

`StreamId.NodeRef` lets every node figure out who owns a given stream. The
publisher's backend shard has the only authoritative memoizer; everyone else
talks to it via cross-shard RPC.

## Cross-shard fan-out — `RemoteVideoStreamCache`

File: `Services/RemoteStreamCaches.cs`.

When a viewer is on **API pod B** and the publisher's backend shard is
**node A**, simply forwarding viewer-by-viewer would cost N concurrent
cross-shard RPCs. The cache prevents that.

### `LiveVideoStreams.GetStream` flow

```csharp
var isLocal = streamId.NodeRef == MeshWatcher.ThisNode.Ref;
var rawStream = isLocal
    ? await VideoStreamingBackend.GetVideoRaw(streamId, ct)            // direct
    : await GetOrFetchRemoteVideo(streamId, ct);                       // cached
if (rawStream is null) return null;

_ = VideoStreamingBackend.RequestKeyFrame(streamId, default);          // PLI
var filtered = ReceiveQualityFilter.Apply(rawStream, () => GetReceiveQuality(...), Log, ct);
return new RpcStream<VideoFrame>(filtered) { AllowReconnect = false, ... };
```

`GetVideoRaw(streamId)` is on `IVideoStreamingBackend` (a backend RPC). It
reads from `_videoStreams.Get(...)` and applies `SkipWhile(!IsKeyFrame)` so
late joiners don't see undecodable deltas before the first KF.

### `GetOrFetchRemoteVideo`

```csharp
var store = RemoteVideoCache.Store;                                     // node-local
var stream = await store.Get(streamId, false, ct);
if (stream != null) return stream.SkipWhile(f => !f.IsKeyFrame);

var rawRpcStream = await VideoStreamingBackend.GetVideoRaw(streamId, default);  // cross-shard
if (rawRpcStream == null) return null;

var memoizer = new VideoStreamMemoizer(rawRpcStream,
    Constants.Video.ServerReplayTailDuration, default);                 // detached lifetime
if (!store.Publish(streamId, memoizer))
    await memoizer.DisposeAsync();

return (await store.Get(streamId, true, ct))?.SkipWhile(f => !f.IsKeyFrame);
```

Three properties matter:

1. **One cross-shard RPC per stream per consumer-node**, regardless of how
   many viewers on that node.
2. **`CancellationToken.None` on the cross-shard RPC**: the cached memoizer
   survives the first viewer disconnecting; later viewers benefit from the
   already-warm replay tail.
3. **Same memoizer policy as the publisher node** (~3.3 s tail), so cached
   streams have the same late-join properties locally.

The cache entry idle-expires after 30 s (`StreamStore.ExpirationDelay`) once
no viewers remain.

## Permission and discovery flow

```mermaid
flowchart TD
    A[Viewer's worker calls GetStream]
    A --> B{streamId.NodeRef == this node?}
    B -- yes --> C[VideoStreamingBackend.GetVideoRaw]
    B -- no --> D[RemoteVideoStreamCache.Get]
    D --> E{cached?}
    E -- yes --> F[Return cached memoizer]
    E -- no --> G[Cross-shard call to publisher node's<br/>VideoStreamingBackend.GetVideoRaw]
    G --> H[Wrap in VideoStreamMemoizer<br/>Publish to cache]
    H --> F
    C --> I[SkipWhile NotKeyFrame]
    F --> I
    I --> J[ReceiveQualityFilter.Apply<br/>per-frame getQuality]
    J --> K[RpcStream back to viewer]
    A -.PLI.-> L[VideoStreamingBackend.RequestKeyFrame]
    L --> M[Invalidate LastKeyframeRequestAt]
    M -.observed by publisher worker.-> N[force-keyframe on next bundle]
```

### `RequestKeyFrame` and `LastKeyframeRequestAt`

`VideoStreamingBackend.RequestKeyFrame(streamId)`:

```csharp
var elapsed = now - await LastKeyframeRequestAt(streamId, ct);
if (elapsed < Constants.Video.KeyFrameRequestCooldown) return;       // 1 s cooldown
using (Invalidation.Begin())
    _ = LastKeyframeRequestAt(streamId, default);                    // bump compute method
```

`LastKeyframeRequestAt` is a Fusion compute method on the publisher's backend
shard. Its result is just `Clocks.SystemClock.Now`, but invalidation is what
matters: the publisher worker's RPC client has a subscription to it (the
worker uses Fusion-style `IComputed.WhenInvalidated` via the streaming-glue
layer) and forces the next bundle as a keyframe when it changes.

PLI fires on:

- Every `GetStream` call (collapsed by the 1 s cooldown into one PLI per
  burst of new joiners).
- Every `ChangePlaybackQuality` that actually changes a stream's `MaxLayerId`
  or `MaxTemporalLayerId`.

## Stream limit and chat-side errors

`VideoStreamLimitExceededException` (`Api/Video/`) is thrown when:

- A 9th camera tries to register in one chat
  (`MaxCameraStreamsPerChat = 8`).
- A second author tries to start a screencast while another is active.

The exception propagates from `LiveVideoBackend.Register` up through
`VideoStreamingBackend.PushVideo` to `LiveVideoStreams.PushStream` and out to
the client, where the recorder treats it as a fatal start-up error and surfaces
a UI message ("ScreenCastAlreadyActiveModal" for the screencast case).

## Module wiring

File: `Streaming.Service/Module/StreamingServiceModule.cs`.

- API pods register: `LiveVideoStreams` (the API service), `RemoteVideoStreamCache`.
- Backend pods register: `VideoStreamingBackend`, `LiveVideoBackend`, the
  Redis client (`StreamingContext`).

The same process can play both roles depending on `HostInfo.Roles` — in
single-node dev, every backend service is local and the cross-shard cache
hot-path collapses to "is local? yes, direct".

## Where to look next

`ReceiveQualityFilter` is the per-consumer gate that turns a memoizer's
all-layer all-temporal stream into a viewer's specific layer/temporal subset;
it's covered in [08-quality-control.md](./08-quality-control.md). The
receiver-side player that drinks the resulting `RpcStream` is in
[07-receiver.md](./07-receiver.md).
