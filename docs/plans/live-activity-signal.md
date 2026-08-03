# Unified live-activity signal

## Goal

Give the "stop listening when idle" and "stop recording when idle" timers a
single, honest answer to *"is anyone live in this chat right now?"* — one that
depends on **audio**, not on the transcription/persistence pipeline, and that
treats a disconnected client as "no activity".

## Why

The listening auto-off timer reads `LiveStreamUI.GetLastActivityServerTime`,
which is just *"is `ILiveAudioStreams.List` non-empty"*. That list is controlled
by the transcription pipeline in two ways, and both are wrong:

1. **Over-reports when transcription is on.** `LiveAudioBackend.Unregister` runs
   in a `finally` that wraps `SaveAndCreateMedia` — the audio-blob save. So the
   chat still looks live for the whole persistence latency after the last word.
   `ListeningStreamMuxer.OnRun` already documents this race and works around it.
2. **Under-reports when voice is off.** `Register` is gated on `mustStreamVoice`,
   so a `JustText` author is never in `List` at all: an actively-speaking
   participant is invisible and listening shuts off mid-conversation.

Two more problems in the same area:

3. The latch is client-local (`LiveStreamUI._lastActivityTimes`), so a cached
   timestamp can leak across listening sessions — which is why the watcher needs
   the `watcherStartedAt` clamp.
4. A quiet chat's activity value invalidates on **every text message**:
   `LiveAudioBackend.ListRaw` falls through to `ChatsBackend.ListEntries` →
   `IChatsBackend.GetTile` whenever Redis returns no key, taking a Fusion
   dependency on the chat's tail tile.

## Design

### Aggregation lives at the API level

`ILiveSessions` already documents itself as the facade that "aggregates
`ILiveAudioStreams` and `ILiveVideoStreams` at the backend", so the unified
signal goes there:

```csharp
[ComputeMethod]
[RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
Task<bool> HasActivity(Session session, ChatId chatId, CancellationToken cancellationToken);
```

`LiveSessions.HasActivity` does the permission check, then delegates to a
**session-less** `HasActivityByChat(ChatId)` that owns the aggregate and its
cache key. This mirrors `LiveVideoStreams.MaxRequestedLayerId` →
`MaxRequestedLayerIdByStream`: `session` must not reach an aggregate that spans
every participant, or each viewer subscribes under its own key and misses the
others' changes. Because the inner compute is session-less, one subscription per
active chat per API pod is shared across all sessions — strictly less fan-out
than the per-client derivation it replaces.

It ORs `ILiveAudioBackend.List` with `ILiveVideoBackend.List`, short-circuiting
on audio so it doesn't even take a video dependency while someone is talking.

The result is a **bool**, not a timestamp. The countdown is inherently a
per-client concern, and an edge-observed latch (see below) can't go stale the
way a cached timestamp can.

### Registration must mean "audio", not "transcription"

- `LiveAudioStreamInfo` gains `bool IsTextOnly` (`MemoryPackOrder(7)`). Phrased
  negatively on purpose: entries serialized before this field deserialize to
  `false`, so a rolling deploy keeps treating live streams as voice.
- `ProcessAudio` registers the stream regardless of `mustStreamVoice`, with
  `IsTextOnly = !mustStreamVoice`. Fixes the `JustText` blind spot.
- `ListeningStreamMuxer` skips `IsTextOnly` streams — it fetches real audio, and
  text-only streams are never published.
- `Unregister` moves out of the `finally` wrapping `SaveAndCreateMedia` to the
  point where the audio itself ends. Removes the persistence-latency
  over-report and the race the muxer works around.

`LiveSessionsBackend.Get`'s `IsMicOpen` stays as-is: the mic *is* open.

### The entries fallback must not be a dependency

`ListRaw` uses `ChatsBackend.ListEntries` only when the Redis read actually
threw — a missing key now returns an empty state — and wraps it in
`Computed.BeginIsolation()` so reconstruction never becomes a dependency. This
keeps the promise that a quiet chat's activity value doesn't invalidate on text
traffic, while preserving crash/eviction recovery.

A quiet chat's `ListRaw` then has *no* dependencies at all, which is fine:
`Register` primes the method explicitly, so a new stream still lands.

Shard relocation needs no new work: the state is in shared Redis, and `List`
already depends on `ShardOwner.RequireShardOwnership(addDependency: true)` so
bound clients reroute on handover.

### Client: offline counts as no activity

`LiveStreamUI.HasActivity` is the single client-side entry point:

```csharp
var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken);
if (!isConnected)
    return false;

return await LiveSessions.HasActivity(Session, chatId, cancellationToken);
```

`ConnectivityUI.IsConnected` is driven off `peer.ConnectionState` — literally
"is my RPC peer up", which is exactly the condition under which a cached
`HasActivity = true` goes stale. `IsOnline` (`navigator.onLine`) is not used: it
reports true on captive portals and half-dead links. On Blazor Server
`IsConnected` is pinned to `true`, which is correct — there is no client RPC
peer and the computes are local.

The early return also drops the server-value dependency while offline instead of
holding a subscription that can't be trusted.

`StopListeningWhenIdle` and `ObserveStreamingIdleBoundaries` watch this value
and stamp `ServerClock.Now` on the observed true→false edge. That deletes
`GetLastActivityServerTime`, `_lastActivityTimes`, and the `watcherStartedAt`
clamp. Disconnect produces such an edge, so an offline client stops listening
after the same `ListeningMode` duration — one signal, not a parallel timer.

`ChatVideoUI.IsWatching` stays as a local suppressor; it isn't server-knowable.

## Decisions taken

**Transcript tail is not a signal.** Extending activity until the transcript
stream completes would need a mid-stream mutation of the stream record — a
second Redis write and invalidation per utterance, per speaker — to buy 1–3 s on
a ≥60 s countdown. Audio is the sole signal in both modes, which is what keeps
the design storage-free.

**Mic-open-but-silent counts as activity.** Bounded by the 2 s
`Constants.Audio.FrameSilenceTimeout` watchdog. Real VAD is device-local and
never leaves the client; plumbing it to the server is a much larger change.

**Video streaming counts, chat-wide.** A silent screenshare keeps listening
alive. Intended.

## Non-goals

- No new stored state, no new Redis keys, no new backend service, no new record
  type. The contract delta is one interface method and one field.
- `ListeningStreamMuxer`'s `EvictionDelay` workaround is left in place; the race
  it guards against is gone, but removing it is a separate change.

## Files

| File | Change |
|---|---|
| `Api.Contracts/Streaming/ILiveSessions.cs` | `HasActivity` |
| `Streaming.Service/Services/LiveSessions.cs` | impl + `HasActivityByChat` |
| `Api/Live/LiveAudioStreamInfo.cs` | `IsVoice` |
| `Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs` | register text-only; unregister at audio end |
| `Streaming.Service/Backend/LiveAudioBackend.cs` | isolate entries fallback |
| `Streaming.Service/Services/ListeningStreamMuxer.cs` | filter `IsVoice` |
| `UI.Blazor.App/Services/LiveStreamUI.cs` | `HasActivity`; drop the timestamp latch |
| `UI.Blazor.App/Services/ChatAudioUI.StateSync.cs` | both idle watchers |

## Tests

`tests/Streaming.IntegrationTests/LiveActivityTest.cs`:

- `HasActivity` follows stream register/unregister;
- a text-only stream (`IsTextOnly = true`) still counts as activity;
- posting a text message to a quiet chat does not invalidate
  `ILiveAudioBackend.List`.
