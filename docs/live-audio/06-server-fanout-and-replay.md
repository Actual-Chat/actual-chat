# 06 — Server fan-out and replay

The server has three audio-distribution paths:

1. **Per-stream pull** via `ILiveAudioStreams.GetStream(streamId)` —
   raw `RpcStream<AudioFrame>`.
2. **Per-chat live multiplex** via `LegacyGetStream(chatId)` — the live
   "Listening" path; mixes multiple authors into one
   `RpcStream<LiveStreamItem>`.
3. **Replay** via `GetReplayStream(chatId, startAt, rewindOffset, speed)`
   — rebuilds historical audio from blob storage, paced for playback.

All three live in `Streaming.Service/Services/`. They share `StreamStore`
for memoization and `RemoteAudioStreamCache` for cross-shard fan-out.

## Chat registry — `LiveAudioBackend`

File: `src/dotnet/Streaming.Service/Backend/LiveAudioBackend.cs`.

`ILiveAudioBackend` is the sharded compute service that tracks active
audio streams per chat. It's simpler than the video equivalent:

- One Redis-backed registry: `live-audio:state:{chatId}` (TTL 5 min).
- No member registry, no codec negotiation (audio is always Opus mono).

`State` (line 164):

```csharp
public sealed partial record State(
    long Version,
    ApiArray<LiveStreamInfo> Streams);
```

### `Register(chatId, streamInfo)`

- Per-chat lock via `_changeLocks` to serialise concurrent registrations.
- **Per-author merge**: if a stream already exists with the same
  `AuthorId`, evict it. (The video side does this only for the same
  `SourceKind`; audio just dedupes by author because there's only ever
  one audio source per author.)
- Reject if the new stream is already older than `StreamTtl = 3 min`.
- Persist to Redis, prime the `[ComputeMethod]` so subscribers see the
  invalidation.

### `Unregister`

Remove the entry, prime the compute method, propagate invalidation.

### Fallback recovery

If Redis is unavailable, `LiveAudioBackend` reconstructs state from
`ChatsBackend.ListEntries` (entries that have `entry.Audio?.StreamId` and
`BeginsAt > now - MaxEntryDuration`). The recovered state is
re-persisted to Redis as soon as it comes back.

## `LiveStreamMuxer` — the live multiplex

File: `src/dotnet/Streaming.Service/Services/LiveStreamMuxer.cs`.

One `LiveStreamMuxer` per chat per subscribing API pod. It watches
`LiveAudioBackend.List(chatId)` (the compute method auto-invalidates on
register/unregister) and dispatches per-stream `ProcessStream` tasks.

### Frame multiplexing

Each muxed stream has its own `StreamIndex` (assigned sequentially per
muxer). Output is a `Channel<LiveStreamItem>`:

```
LiveStreamStart { StreamIndex, LiveStreamInfo, PlaysAt }
LiveAudioFrame  { StreamIndex, Data, Offset }   (zero-or-more)
LiveStreamEnd   { StreamIndex }
```

The client's `LiveStreamDemuxer` matches frames to `StreamIndex` and
routes them to per-author audio tracks.

### Live-edge trim

```csharp
var skipTo = GetSkipTo(streamEntry.IsPreexisting, streamInfo, CatchUpFrom);
// preexisting -> Constants.Audio.SkipToLive, otherwise TimeSpan.Zero
```

A stream the muxer finds in the **first snapshot after it establishes its watch**
is served from the producer's current position; anything that starts while the
muxer is already watching is served whole. So a listener joining 30 s into
someone's monologue hears it from now, not from its start, while every utterance
that begins after they joined arrives complete.

"Live edge" is a position, not a timestamp: `AudioStreamingBackend.SkipToLive`
replays from the memoizer's tail (`Replay(0)`), so no clock is read on either
side. The stream header is prepended so the delivered shape is unchanged.

The flag is scoped to the connect attempt rather than the muxer's lifetime — a
stream that began during a watch outage is new to the muxer on re-establish, and
serving it whole would deliver a stale monologue. This is also what makes the
client's sleep/resume re-subscribe work: it builds a new muxer, whose first
snapshot is cold by construction.

(Replay-mode subscribers use `GetReplayStream` instead.)

One exception: `GetListeningStream` takes a `catchUpFrom` moment (default =
none), and streams whose `BeginsAt` is at/after it are served from t=0 with no
trim. The PTT wake passes the trigger utterance's start there so the
cold boot doesn't cost the listener the first seconds — see
[doc 10](10-push-to-talk.md).

### Per-author merge in the muxer

Same logic as `LiveAudioBackend.Register` but at the streaming level: if
two streams from the same author are simultaneously active, the muxer
keeps the one with the larger `BeginsAt` and cancels the older's
`StopTokenSource`. The cancelled stream's `ProcessStream` exits cleanly
and emits `LiveStreamEnd`.

### Eviction delay

Stream entries linger in the muxer's per-stream map for an extra
`EvictionDelay = 4 s` after `LiveStreamEnd` so a flapping publisher
doesn't oscillate the muxer.

## `ReplayStreamMuxer` — historical playback

File: `src/dotnet/Streaming.Service/Services/ReplayStreamMuxer.cs`.

`ILiveAudioStreams.GetReplayStream(chatId, startAt, rewindOffset, speed)`
returns the same `RpcStream<LiveStreamItem>` shape as live, but the
frames come from blob storage instead of the live memoizer.

### Position resolution

Two helpers walk chat entries to find the seek point:

- `FindNearestAudioPosition` — locate the first audio entry at/after
  `startAt`.
- `ResolvePositionInPast/Future` — accumulate audio durations entry-by-
  entry to find the exact `(MediaId, offsetWithinMedia)` for the seek
  position.

### Frame delivery

For each chat entry whose audio overlaps the playback window:

```csharp
var audioSource = await AudioDownloader.Download(blobId, skipTo, ct);
foreach (var frame in audioSource.Frames) {
    if (skipFraming(frame)) continue;       // speed > 1.0 → keep N of M
    yield return new LiveAudioFrame { StreamIndex, Data, Offset };
}
```

`AudioSourceDownloader`
(`Core.Server/Blobs/AudioSourceDownloader.cs`):

```csharp
var stream = await blobStorage.Read(blobId, ct);
var audio = await AudioSource.ReadFromByteStream(byteStream, Clocks, log, ct);
return audio.SkipTo(skipTo, ct);
```

### Speed control

For `speed > 1.0`, the muxer drops frames in a regular pattern:

- 1.5× — keep 2 of every 3 frames.
- 2.0× — keep every other frame.

The decoder still receives valid Opus frames (each is independent), and
the playback rate is determined by the wall-clock pacing on the next
step.

### Pacing

The muxer paces emission based on `CpuTimestamp` so the replay arrives
near real-time playback rate (adjusted for `speed`). If the consumer is
> 1 minute ahead of where it should be, the muxer waits — prevents a
slow consumer from accumulating server-side state.

## `_audioStreams` — `StreamStore<AudioFrame>`

File: `src/dotnet/Streaming.Service/Services/StreamStore.cs`.

Per-node registry of memoizers, keyed by `StreamId`:

- `Publish(streamId, memoizer)` — atomic; loser of a race disposes.
- `Get(streamId, waitForShare)` — returns the memoizer's
  `IAsyncEnumerable<AudioFrame>` shared across consumers.
- `ExpirationDelay = AudioSettings.StreamExpirationDelay = 10 s` —
  garbage-collect entries 10 s after the last consumer detaches.
- `OnStreamExpire` decrements `AppMeters.AudioStreamCount`.

`StreamStore` is generic — there's also `_transcriptStreams:
StreamStore<TranscriptDiff>` for the per-segment live transcript
streams.

## `RemoteAudioStreamCache` — cross-shard

File: `src/dotnet/Streaming.Service/Services/RemoteStreamCaches.cs`.

```csharp
public sealed class RemoteAudioStreamCache : IDisposable
{
    public StreamStore<AudioFrame> Store { get; }
    // ExpirationDelay = AudioSettings.StreamExpirationDelay (10 s)
}
```

When `LiveAudioStreams.GetStream` runs on API pod B for a stream owned
by node A, it calls `GetOrFetchRemoteAudio`:

1. Check the local `RemoteAudioStreamCache.Store` for `streamId`.
2. Cache hit → return shared memoizer (skip-to applied).
3. Cache miss → call `Backend.GetAudio(streamId, TimeSpan.Zero,
   CancellationToken.None)` (note the detached cancel — the cached
   memoizer's lifetime survives the requesting consumer).
4. Wrap in `((IAsyncEnumerable<AudioFrame>)rawRpcStream).Memoize()` and
   `Publish` into the cache. Race losers dispose.
5. Return the cached memoizer.

Same shape as the video equivalent. One cross-shard RPC per stream per
consumer-node, regardless of viewer count.

## How `GetStream` ties it together

```csharp
var isLocal = streamId.NodeRef == MeshWatcher.ThisNode.Ref;
var rawStream = isLocal
    ? await Backend.GetAudio(streamId, skipTo, ct)
    : await GetOrFetchRemoteAudio(streamId, skipTo, ct);
return rawStream is null ? null : MediaRpcStreamOptions.AudioDelivery(rawStream);
```

`MediaRpcStreamOptions.AudioDelivery` wraps the result with
`AckPeriod = 5` and `AllowReconnect = true` for reasonable network
behaviour.

## `LegacyGetStream` topology

```mermaid
flowchart TD
    Sub[Subscriber: ChatListener<br/>LegacyGetStream(chatId, settings)]
    Sub --> ApiB[API pod B]
    ApiB --> Mux[LiveStreamMuxer for chat<br/>watches LiveAudioBackend.List]
    Mux --> List[ILiveAudioBackend.List<br/>(Fusion compute, sharded)]
    List --> Redis[(Redis live-audio:state:{chatId})]
    Mux --> ProcA["ProcessStream(streamId 1)<br/>(may be remote)"]
    Mux --> ProcB["ProcessStream(streamId 2)"]
    ProcA --> GetA[GetStream(streamId 1)]
    ProcB --> GetB[GetStream(streamId 2)]
    GetA --> Local{local?}
    Local -- yes --> NodeA[Backend.GetAudio<br/>same node]
    Local -- no --> RAC[RemoteAudioStreamCache]
    RAC --> NodeC[Cross-shard RPC<br/>to publisher node]
    NodeA --> Out[per-author<br/>LiveStreamItem stream]
    NodeC --> Out
    GetB --> Out
    Out --> ApiB
    ApiB --> Sub
```

The muxer hides the local/remote distinction from the subscriber. The
first viewer of a remote author pays the cross-shard fetch cost; later
viewers within `ExpirationDelay = 10 s` ride the cache.

## What about the transcript stream?

`ILiveAudioStreams.GetTranscriptStream(streamId)` follows the same
pattern but with `_transcriptStreams: StreamStore<TranscriptDiff>` and
its own `RemoteTranscriptStreamCache`. Subscription is **per-segment,
not per-chat** — a viewer who wants live captions for a specific
in-flight chat entry calls `GetTranscriptStream` with the entry's
`Audio.StreamId`. Once the entry is finalised, `ContentStreamId` is
unset and the stream is no longer needed.

## Diagnostics

- `AppMeters.AudioStreamCount` — current live audio streams.
- `AppMeters.AudioLatency` — receiver-reported end-to-end latency.
- Log markers: `"GetAudio: ..."`, `"GetOrFetchRemoteAudio: caching ..."`,
  `"Register: evicting stale stream ..."`,
  `"ListenAtFromRedisFailed - falling back to chat entries ..."`.

## Summary

| Path | Returns | Used by |
|---|---|---|
| `GetStream(streamId, skipTo)` | `RpcStream<AudioFrame>` | per-message audio playback |
| `GetTranscriptStream(streamId)` | `RpcStream<TranscriptDiff>` | live captions |
| `LegacyGetStream(chatId, settings)` | `RpcStream<LiveStreamItem>` | "Listening" (live group audio) |
| `GetReplayStream(chatId, startAt, …)` | `RpcStream<LiveStreamItem>` | replay/seek |
