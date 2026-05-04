# Video Pipeline Quality Control Rewrite — Implementation Plan

This document is a self-contained handoff for the multi-step rewrite of
recording and playback quality control. It captures the design (already
locked in via the conversation that produced it), the discoveries made
during code exploration, and the concrete file/symbol-level changes
needed for each task.

## Reference docs

Read these first — they contain the locked-in design:

- [`video-pipeline.md`](video-pipeline.md) — target high-level design.
  - §"Recording Quality Control" — controller spec.
  - §"Playback Quality Control" — controller spec.
  - §"API Surface" — `ILiveVideoStreams` / `ILiveAudioStreams` /
    `IStreamServer` (legacy) shapes.
- [`video-pipeline-wip.md`](video-pipeline-wip.md) — refactor tracker.
  §"Planned next steps — quality control rewrite" lists Steps 8/9/10
  with sub-bullets.
- [`video-pipeline-now.md`](video-pipeline-now.md) — current state.
- [`CODING_STYLE.md`](CODING_STYLE.md) — **mandatory read** before
  writing C# or TypeScript here. No `Async` suffix, no XML docs on
  members, file-scoped namespaces, 120-char lines, 4-space indent, LF.

## Scope

Three steps, in order:

| Step | What | Status |
|---|---|---|
| 8 | API restructure + thin server-side filter (no controllers) | partially scaffolded |
| 9 | Recording quality controller (client-side) | not started |
| 10 | Playback quality controller (client-side) | not started |

Step 8 must land first — Steps 9 and 10 both call new methods on
`ILiveVideoStreams` introduced by Step 8.

## State on disk

**Nothing has been written.** The previous session created four files
and edited `ILiveVideoStreams.cs`; the user reverted all of those
changes. Start from a clean working tree.

The four files that were drafted and reverted (write them again from
the specs in this plan):

- `src/dotnet/Api.Contracts/Streaming/Quality/ReceiveQuality.cs`
- `src/dotnet/Api.Contracts/Streaming/Quality/RecordingQuality.cs`
- `src/dotnet/Api.Contracts/Streaming/Quality/PlaybackQuality.cs`
- `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`

Their target shapes are detailed in the per-step sections below.

The previous session also rewrote
`src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`. The rewrite
was reverted because it was too aggressive — it dropped `[LegacyName]`
attributes from non-obsolete methods (e.g.
`List`/`ListActiveStreams`, `RegisterMember`/`RegisterVideoStreamMember`,
`GetSupportedCodecs`/`GetSupportedDecoderCodecs`) which would break
v2.6 clients that call those non-video methods.

**Lesson**: when modifying `ILiveVideoStreams`:
- Removing video-only legacy methods is OK (no v2.6 client speaks
  video).
- Removing `[LegacyName]` from non-video methods is **not** OK — those
  cover audio-only and chat-membership compat.
- The `[Obsolete]` on `GetStream` itself was authorised to remove by the
  user.

### Required shapes for the four files

`Api.Contracts/Streaming/Quality/ReceiveQuality.cs`:

```csharp
using MemoryPack;
using MessagePack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable, MessagePackObject]
public partial record ReceiveQuality(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int MaxSpatialLayer,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int MaxTemporalLayer)
{
    public static readonly ReceiveQuality Lowest = new(0, 0);
    public static readonly ReceiveQuality Default = new(2, int.MaxValue);

    public bool IsLowest => MaxSpatialLayer <= 0 && MaxTemporalLayer <= 0;
}
```

`Api.Contracts/Streaming/Quality/RecordingQuality.cs`:

```csharp
using MemoryPack;
using MessagePack;

namespace ActualChat.Streaming;

public enum RecordingQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    StuckAtFloor,
    ColdStartTick,
    ReconnectPush,
}

[DataContract, MemoryPackable, MessagePackObject]
public partial record RecordingQualityState(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int TargetLayerCount,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int EffectiveLayerCount);

[DataContract, MemoryPackable, MessagePackObject]
public partial record RecorderHealthSnapshot(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] double EncodeRatioAvg,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double EncodeRatioP90,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] double SlotReplacementRate,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] double SenderFrameDropRatio,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] double LastAckAgeMs,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] bool IsConnected);

[DataContract, MemoryPackable, MessagePackObject]
public partial record RecordingQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] RecordingQualityReason Reason,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] RecorderHealthSnapshot Health);
```

`Api.Contracts/Streaming/Quality/PlaybackQuality.cs`:

```csharp
using MemoryPack;
using MessagePack;

namespace ActualChat.Streaming;

public enum PlaybackQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    FloorReached,
    ActiveSetChanged,
    ReconnectPush,
    ColdStartTick,
}

public enum PlaybackStreamPriority
{
    Secondary = 0,
    Primary = 1,
}

[DataContract, MemoryPackable, MessagePackObject]
public partial record PlaybackStreamInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long IncomingByteRate,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int BufferDurationMsP50,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] int KeyframeSkipsInWindow,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] int DecoderQueueDepthP90,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] int CurrentMaxSpatial,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] int CurrentMaxTemporal,
    [property: DataMember(Order = 6), MemoryPackOrder(6), Key(6)] PlaybackStreamPriority Priority,
    [property: DataMember(Order = 7), MemoryPackOrder(7), Key(7)] int Verdict);

[DataContract, MemoryPackable, MessagePackObject]
public partial record PlaybackQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long EstimatedCapacityBytesPerSec,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double AggregateHealth,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] PlaybackQualityReason Reason,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] bool IsColdStart,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] ApiMap<string, PlaybackStreamInfo> Streams);
```

`Streaming.Service/Services/ReceiveQualityFilter.cs`:

```csharp
using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer thin video filter that clamps a raw stream to client-requested
/// spatial and temporal caps. Forwards exactly one spatial layer (highest
/// available not exceeding MaxSpatialLayer) and drops frames above
/// MaxTemporalLayer. Skip-until-keyframe on cap change and on
/// keyframe-number gaps for decoder safety.
/// </summary>
public static class ReceiveQualityFilter
{
    private static readonly TimeSpan CapRefreshInterval = TimeSpan.FromMilliseconds(500);

    public static async IAsyncEnumerable<VideoFrame> Apply(
        IAsyncEnumerable<VideoFrame> source,
        Func<ReceiveQuality> getQuality,
        ILogger? log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxSpatial = -1;
        var maxTemporal = int.MaxValue;
        var observedMaxSpatial = 0;
        var selectedLayer = -1;
        long lastKeyFrameNumber = -1;
        var skipping = true;
        var capRefreshAt = CpuTimestamp.Now;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (maxSpatial < 0 || capRefreshAt.Elapsed >= CapRefreshInterval) {
                var q = getQuality();
                if (q.MaxSpatialLayer != maxSpatial || q.MaxTemporalLayer != maxTemporal) {
                    maxSpatial = q.MaxSpatialLayer;
                    maxTemporal = q.MaxTemporalLayer;
                    skipping = true;       // reselect on next keyframe
                }
                capRefreshAt = CpuTimestamp.Now;
            }

            if (frame.IsKeyFrame && frame.SpatialLayerId > observedMaxSpatial)
                observedMaxSpatial = frame.SpatialLayerId;

            var desiredLayer = Math.Min(maxSpatial, observedMaxSpatial);

            if (frame.TemporalLayerId > maxTemporal)
                continue;

            if (frame.IsKeyFrame && frame.SpatialLayerId == desiredLayer) {
                selectedLayer = desiredLayer;
                lastKeyFrameNumber = frame.KeyFrameNumber;
                skipping = false;
                yield return frame;
                continue;
            }

            if (skipping || selectedLayer < 0)
                continue;

            if (frame.SpatialLayerId != selectedLayer)
                continue;

            if (frame.KeyFrameNumber != lastKeyFrameNumber) {
                skipping = true;
                continue;
            }

            yield return frame;
        }
    }
}
```

## Tasks 8.1 — 10.4

The TaskCreate list from the previous session (re-create in the new
session via TaskCreate):

```
8.1  ILiveAudioStreams: audio + transcript surface
8.2  ILiveVideoStreams: video write methods, IStreamServer video removal
8.3  ReceiveQuality + ReceiveQualityFilter + ILiveVideoStreams.GetStream
8.4  TS streaming-api rebind + JS callsite swaps
8.5  Deletions: VideoStreamFilter, ChatState pause logic, RecordFrameBytes
9.1  RpcStream API additions (OnAck etc)
9.2  RecorderHealth DTO + worker 1 Hz aggregation
9.3  ILiveVideoStreams.ChangeRecordingQuality server stub
9.4  VideoQualityUI service + recording branch (with nested testable types + tests)
10.1 PlaybackHealth per-stream DTO + decoder-worker sampling
10.2 PlaybackQualityInfo / PlaybackStreamInfo / RecordingQualityState DTOs (DONE)
10.3 ILiveVideoStreams.ChangePlaybackQuality server impl
10.4 VideoQualityUI playback branch (with nested testable types + tests)
```

**Note**: User added a late requirement — quality adjustment algorithms
(both recording and playback) should be implemented as **nested types
inside `VideoQualityUI`** that are **unit-testable**, with **robust unit
tests**. The controller's outer state machine can stay in
`VideoQualityUI` itself; the pure-logic pieces (ternary classifier,
AIMD aggregation, allocator, capacity estimator) become small classes
that take inputs and return decisions, decoupled from UI / RPC / JS
interop.

## Step 8.1 — `ILiveAudioStreams` audio + transcript surface

### Files involved

- `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`
- `src/dotnet/Api.Contracts/Streaming/IStreamClient.cs`
- `src/dotnet/Api.Contracts/Streaming/StreamClient.cs`
- `src/dotnet/Api.Contracts/Streaming/IStreamServer.cs`
- `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs` (new — or
  use existing if present)
- `src/dotnet/Streaming.Service/Services/StreamServer.cs` — gut audio
  + transcript bodies into proxy calls to `ILiveAudioStreams`.

### Naming collision

The existing `ILiveAudioStreams.GetStream(Session, ChatId, LiveStreamSettings)
→ RpcStream<LiveStreamItem>` is for the **ambient/radio LiveStream
feature**, unrelated to per-frame audio push/pull. The new audio read
method we want is `GetStream(Session, streamId, skipTo) → RpcStream<AudioFrame>`.

The existing method has `[LegacyName("GetLiveStream", "2.6.9999")]` —
which means it was previously named `GetLiveStream`. Two resolution
options:

1. **Rename existing back to `GetLiveStream`** (revert the rename) and
   make the new audio-frame method `GetStream`. Pros: short clean name
   for the new method; cons: breaking change to the live-stream feature.
2. **Use a different new name** like `GetAudioFrames` or keep the new
   one as `GetStream` and rename the existing to something like
   `GetMixStream`.

User indicated "yes for ILiveAudioStreams.GetStream" earlier — they
want the new method to be `GetStream`. **Recommended: option 1**
(rename existing back to `GetLiveStream`). Preserve `[LegacyName("GetStream", "...")]`
on it so RPC compat survives.

### New methods on ILiveAudioStreams

```csharp
// Audio frames push/pull (moved from IStreamServer)
[RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
Task PushStream(
    Session session,
    string chatId,
    string? repliedChatEntryId,
    double clientStartOffset,
    int preSkip,
    RpcStream<AudioFrame> frameStream,
    CancellationToken cancellationToken);

Task<RpcStream<AudioFrame>?> GetStream(
    Session session,
    string streamId,
    TimeSpan skipTo,
    CancellationToken cancellationToken);

// Transcript (moved from IStreamServer.GetTranscript)
Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
    Session session,
    string streamId,
    CancellationToken cancellationToken);

// Stays in current shape — audio quality control isn't being redesigned
Task<RpcNoWait> ReportAudioLatency(
    Session session,
    TimeSpan latency,
    CancellationToken cancellationToken);
```

### IStreamServer audio + transcript proxies

Stay in `IStreamServer` for legacy v2.6 clients. Each method body
forwards to the corresponding `ILiveAudioStreams` method, passing
`Session.Default` for the session arg. The proxy is a local in-process
call; the implementation today ignores the session value, so this works
even though `Session.Default` is not meaningfully populated through the
legacy WS path. Confirmed by user — "it's going to be a local call, but
it's fine for now, coz it's unused in the impl. anyway, we just want
to prepare for the future".

Update `StreamClient.PushAudio` to call `ILiveAudioStreams.PushStream`
instead of `IStreamServer.PushAudio`. Keep `IStreamClient.PushAudio` as
the local facade method name unless/until its public API is renamed
separately.

### IStreamServer marking

Mark `IStreamServer` `[Obsolete]` at the type level. Not just
deprecated in comments — actual `[Obsolete]` attribute.

## Step 8.2 — `ILiveVideoStreams` video write + IStreamServer video removal

### What lands on ILiveVideoStreams (additions only — keep all existing methods + their `[LegacyName]` attributes)

```csharp
[RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
Task PushStream(
    Session session,
    string chatId,
    double clientStartOffset,
    VideoFormat format,
    RpcStream<VideoFrame> frameStream,
    StreamKind streamKind,
    CancellationToken cancellationToken);

Task<RpcNoWait> RequestKeyFrame(
    Session session,
    string streamId,
    CancellationToken cancellationToken);

Task<RpcNoWait> ChangeRecordingQuality(
    Session session,
    RecordingQualityState? state,
    RecordingQualityInfo? info,
    CancellationToken cancellationToken);

Task<RpcNoWait> ChangePlaybackQuality(
    Session session,
    ApiMap<string, ReceiveQuality>? requestedQuality,
    PlaybackQualityInfo? info,
    CancellationToken cancellationToken);
```

### What to remove from ILiveVideoStreams

- The `[Obsolete]` attribute on `GetStream` — user explicitly authorised
  this removal.
- The `LegacyXxx` methods (`LegacyGetVideoStreamingAuthorIds`,
  `LegacyObserveSupportedDecoderCodecs`,
  `LegacyObserveStreamQualityRequests`) — these are video-only legacy
  shims and v2.6 clients don't speak video.
- Keep `GetQualityPreset` for now as the publisher-facing keyframe
  request signal. The old quality adaptation model is replaced by
  `ChangeRecordingQuality` / `ChangePlaybackQuality`, but
  `RequestKeyFrame` must still propagate immediately to the recorder by
  invalidating `GetQualityPreset` so the publisher observes
  `IsKeyFrameRequested = true`.

### What to keep on ILiveVideoStreams

Everything else with its `[LegacyName]` attribute intact:
- `List` / `[LegacyName("ListActiveStreams", "2.6.9999")]`
- `GetMemberCount` / `[LegacyName("GetVideoStreamMemberCount", ...)]`
- `GetSupportedCodecs` / `[LegacyName("GetSupportedDecoderCodecs", ...)]`
- `RegisterMember` / `[LegacyName("RegisterVideoStreamMember", ...)]`
- `UnregisterMember` / `[LegacyName("UnregisterVideoStreamMember", ...)]`

These are non-video-frame methods; v2.6 clients hit them.

Update `StreamClient.PushVideo` to call `ILiveVideoStreams.PushStream`
instead of `IStreamServer.PushVideo`. Keep `IStreamClient.PushVideo` as
the local facade method name unless/until its public API is renamed
separately.

### IStreamServer video removal

Remove these methods entirely from `IStreamServer.cs`:
- `Task<RpcStream<VideoFrame>?> GetVideo(...)`
- `Task PushVideo(...)`
- `Task RequestKeyFrame(...)`
- `Task<VideoLatencyReportResponse> ReportVideoLatency(...)`

Update `StreamServer.cs` to remove implementations of those methods.

### `RequestKeyFrame` return type

Change `Task → Task<RpcNoWait>` on the new
`ILiveVideoStreams.RequestKeyFrame`. The implementation already has
fire-and-forget semantics (just sets a flag); the type makes that
explicit at the wire level.

Implementation detail: `ILiveVideoStreams.RequestKeyFrame` should route
through the same immediate publisher signal path as today:

1. Store a pending keyframe request for the target stream, with the
   existing cooldown/collapse semantics.
2. Invalidate `GetQualityPreset(streamId)` immediately.
3. `GetQualityPreset` consumes the pending flag and returns the current
   preset with `IsKeyFrameRequested = true`.
4. `VideoRecorder` keeps its `GetQualityPreset` subscription for this
   signal and calls `forceKeyFrame` when the flag is observed.

This keeps keyframe recovery independent from playback quality control.
Later, if a dedicated publisher-control stream replaces
`GetQualityPreset`, move this path there in one deliberate change.

## Step 8.3 — `GetStream` impl using `GetVideoRaw` + `ReceiveQualityFilter`

### File changes

`src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`:

- New private field: per-session quality store. Use
  `ConcurrentDictionary<Session, ApiMap<string, ReceiveQuality>>` (or
  similar). Sticky routing keeps state local — confirmed by user.
- Helper: `ReceiveQuality GetReceiveQuality(Session, StreamId)` —
  returns the stored value, falling back to `ReceiveQuality.Default`
  (`(MaxSpatial=2, MaxTemporal=int.MaxValue)`) if the session has no
  stored map or the stream has no entry. Streams not requested at all
  also fall back to `Default` for now; in 10.3 the policy changes to
  "absent stream → Lowest".
- Rewrite `GetStream` body to:
  1. Call `VideoStreamingBackend.GetVideoRaw(streamId, ct)` (returns
     `RpcStream<VideoFrame>?`).
  2. Wrap with `ReceiveQualityFilter.Apply(rawStream, () => GetReceiveQuality(session, streamId), Log, ct)`.
  3. Return as `RpcStream<VideoFrame>` with `AckPeriod=5`, `BufferSize=10`.

The existing `LiveVideoStreams.GetStream` already does roughly this
shape but calls `VideoStreamingBackend.GetVideo` (with VideoStreamFilter)
instead of `GetVideoRaw` (no filter). Swap the call and add the filter
wrapper.

### Verification before moving on

After 8.3, every active video stream serves at the default cap
(`spatial=2, all temporal`). Coarser than today (no per-peer
adaptation) but predictable.

## Step 8.4 — TS streaming-api rebind

### TS files involved

- `src/nodejs/src/api/streaming-api.ts` — main facade, rebind shape.
- `src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts`
  line 393 — `streamServer.GetVideo(streamId, skipToTicks)` →
  `liveVideoStreams.GetStream('~', streamId)`.
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts`
  line 1371 — same swap.
- `src/dotnet/UI.Blazor.App/Services/Video/workers/video-streaming.ts`
  line 246 — `streamServer.PushVideo(...)` →
  `liveVideoStreams.PushStream('~', ...)`. The constant
  `RPC_SESSION_DEFAULT = '~'` is already defined at line 21 of that
  same file.
- All `streamServer.RequestKeyFrame(...)` call sites — find with
  `Grep` — swap to `liveVideoStreams.RequestKeyFrame('~', ...)`.
- All `streamServer.ReportVideoLatency(...)` call sites — **delete**.
  The metric is fully replaced by `ChangeRecordingQuality` /
  `ChangePlaybackQuality` info payloads (Steps 9 and 10).
- All audio + transcript callsites (`streamServer.PushAudio`,
  `GetAudio`, `GetTranscript`, `ReportAudioLatency`) — swap to
  `liveAudioStreams` equivalents. `PushAudio` becomes
  `liveAudioStreams.PushStream`; `GetAudio` becomes
  `liveAudioStreams.GetStream`. `GetTranscript` becomes
  `liveAudioStreams.GetTranscriptStream`.

### Pattern

JS passes `'~'` (= `RPC_SESSION_DEFAULT`) for the `session` parameter;
the server-side middleware resolves it from the WS `?session=` URL
parameter. This is the same pattern the existing stream push calls use
today. Both `decoder-worker.ts:21` and `audio-streamer.ts:21` already
define a local `RPC_SESSION_DEFAULT = '~'` constant; reuse them or
hoist to a shared module if convenient.

## Step 8.5 — Deletions

After 8.3 lands, the following code is dead and must go:

- `src/dotnet/Streaming.Service/Services/VideoStreamFilter.cs` — entire
  file. ~512 lines.
- `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`:
  - `GetVideo(...)` method (the version that wraps `VideoStreamFilter`).
    Keep `GetVideoRaw`.
  - Old quality adaptation inside `GetQualityPreset(...)`, but keep the
    method itself as the keyframe-request propagation path until a
    dedicated publisher-control channel exists.
  - Keep `RequestKeyFrame(...)` semantics (pending flag + cooldown +
    `GetQualityPreset` invalidation), exposed through the new
    `ILiveVideoStreams.RequestKeyFrame` method.
  - `ReportPeerLatency(...)` method.
  - The `LatencyStore.RecordFrameBytes(...)` call inside
    `PushVideoInternal.ProcessFrames` — drop the line.
  - `LatencyStore.UpdateMaxQuality(...)` keyframe-source-dim tracking —
    drop along with the rest of the LatencyStore deletions.
- `src/dotnet/Streaming.Service/Backend/StreamLatencyStore.cs`:
  - `RecordFrameBytes` and the throughput logic above it
    (`_totalBytesReceived`, `_bytesAtLastCheck`,
    `_consecutiveHighThroughputChecks`).
  - `EvaluateQuality` over-delivery branch.
  - Do not delete the whole `StreamLatencyStore` until the
    keyframe-request fields (`KeyFrameRequests`,
    `LastKeyFrameRequestTime`) are moved to a smaller store or otherwise
    preserved. Per-peer egress fallback, forwarded layer tracking, etc.
    all go.
- `src/dotnet/Streaming.Service/Backend/LiveVideoBackend.ChatState.cs`:
  - `EvaluatePriority` method.
  - `_pausedStreamIds` field and all reads.
  - `MaxWebcamStreamsPerChat`, `SilenceGracePeriod`,
    `PriorityActivationThreshold` constants.
  - `EvaluateStreamPriority`, `ShouldPause` plumbing.
  - **Keep**: codec-set tracking (`_currentSupportedDecoderCodecs`,
    `RecomputeCodecs`).
- `VideoQualityPreset.Paused` enum value (in `Api/Video/`).
- Publisher-side pause / old quality handling in
  `src/dotnet/UI.Blazor.App/Services/VideoRecorder.cs` — find the
  branch that responds to `Paused` preset and remove. Keep the
  `GetQualityPreset` subscription path that observes
  `IsKeyFrameRequested` and calls `forceKeyFrame`.
- `src/dotnet/Api/Video/VideoLatencyReport.cs` and
  `VideoLatencyReportResponse.cs` — likely entirely dead after `ReportVideoLatency`
  removal. Verify with Grep.
- All consumers of the deleted types — there are some, e.g. metrics
  modal might read `VideoQualityPreset` or `VideoLatencyReportResponse`.
  Grep, decide per-callsite.

After 8.5 the build should be clean and every video stream serves at
the fixed default cap from 8.3.

## Step 9 — Recording quality controller

### 9.1 — RpcStream API additions

There is no in-repo `src/dotnet/ActualLab.Rpc` project in this
workspace; the .NET side comes from the `ActualLab.Rpc` package. For
the browser video recording path, edit the local TypeScript RPC copy:

- `src/nodejs/src/actuallab-rpc/rpc-stream.ts`
- `src/nodejs/src/actuallab-rpc/rpc-stream-sender.ts`

Add sender-side metrics / controls:

```ts
readonly nextIndex: number;
readonly lastAckIndex: number;
readonly skipCount: number;
onAckProcessed?: () => void;               // post-ACK, post-compaction
```

These are item-agnostic — handler args carry no `T`. The recorder's
controller reads state via the properties on its own 1 Hz tick; `onAckProcessed`
just bumps a `lastAckAt = performance.now()` so the controller can tell
"stuck" (no ACK for > N seconds) from "throttled" (ACK flowing).

Compact-skip semantics: real-time `canSkipTo` compaction collapses the
already-buffered unsent suffix to the latest buffered restart point.

If this repository later vendors or exposes the .NET `ActualLab.Rpc`
sources, mirror the same counters there for non-browser senders, but do
not block the web recording controller on that package change.

### 9.2 — Worker-side `RecorderHealth` aggregation

`src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts`.
Add a 1 Hz aggregator that produces a `RecorderHealthSnapshot` (already
defined in `Api.Contracts/Streaming/Quality/RecordingQuality.cs`):

- `encodeRatio.avg/p90` — already partially tracked via `slotWindowMs`
  and the existing `slotReplacements/Arrivals` counters; sample per-frame
  encode time / `frameDurationMs` (where `frameDurationMs = 1000 / VIDEO.frameRate`).
- `slotReplacementRate` = `slotReplacements / framesProduced` over the
  last 1 s.
- `senderFrameDropRatio` = `Δ skipCount` over 1 s divided by `Constants.Video.FrameRate` (from 9.1).
- `lastAckAgeMs` = `Date.now() - lastAckAt`.

Posted to `.NET` once per second via a `DotNetObjectReference` callback
on `RecorderCallbacks` (see existing pattern in `VideoRecorder.cs`).

### 9.3 — `ChangeRecordingQuality` server stub

`src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`. New method:

```csharp
public Task<RpcNoWait> ChangeRecordingQuality(
    Session session,
    RecordingQualityState? state,
    RecordingQualityInfo? info,
    CancellationToken ct)
{
    Log.LogTrace("ChangeRecordingQuality: session={Session}, state={State}, info={Info}",
        session, state, info);
    return Task.FromResult(default(RpcNoWait));
}
```

Pure metrics. Will be wired to a meter later; for now Trace-log and
discard.

### 9.4 — `VideoQualityUI` service + recording branch

New file:
`src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs` (sibling of
`ChatVideoUI.cs`; matches the existing `*UI.cs` pattern in
`UI.Blazor.App/Services/`).

#### Outer service shape

- Holds per-`StreamKind` `RecordingQualityState` (keyed on `Webcam` /
  `Screencast`). Persisted across reconnects (in-memory; no DB).
- 1 Hz tick task (similar to `RecorderStateHub`'s tick patterns).
- `ConnectivityUI.IsConnected` gates the controller — read on each tick.
  When `false`, freeze; on `true → false → true` transition, wipe signal
  windows and apply 2 s cold-start grace.
- On every decision and on a 5 s heartbeat (decision resets the timer):
  - Apply the new state via `VideoRecorder.SetSimulcastLayers(...)` —
    the existing method takes a `IReadOnlyList<SpatialLayerSpec>?` and
    does the right thing on the JS side.
  - Push to server: `LiveVideoStreams.ChangeRecordingQuality(session, state?, info?)`.

#### Nested testable types (per user requirement)

Implement the pure-logic pieces as nested types under `VideoQualityUI`:

```csharp
public sealed class VideoQualityUI
{
    // Outer state machine, RPC plumbing, JS interop, ConnectivityUI gating

    public sealed record RecordingSignal(int Verdict);  // -1, 0, +1

    public static class RecordingClassifier
    {
        public static RecordingSignal Classify(RecorderHealthSnapshot h, RecordingThresholds t)
        {
            // pure function; no I/O
        }
    }

    public sealed class RecordingAggregator
    {
        // AIMD with K-window cooldown / K-consecutive-good rule
        // Internal state: int targetLayerCount, int consecutiveGood, int cooldownLeft
        // public methods: void Step(RecordingSignal s) -> RecordingDecision
        //                 RecordingQualityState Snapshot()
    }

    public sealed record RecordingDecision(
        int NewTargetLayerCount,
        bool Changed,
        RecordingQualityReason Reason);

    public sealed record RecordingThresholds(
        double EncodeRatioBadAbove,    // 1.0
        double EncodeRatioGoodBelow,   // 0.33
        double SenderFrameDropRatioBadAbove, // 0.20
        double LastAckBadMs,             // 2000
        double LastAckGoodMs,            // 500
        // …
        int K)                          // 5
    {
        public static RecordingThresholds Defaults => new(...);
    }
}
```

#### Tests

`tests/UI.Blazor.App.UnitTests/VideoQualityUI/RecordingClassifierTest.cs`
and `RecordingAggregatorTest.cs`. Use xUnit. Cover:

- All-good signal → climbs after K windows.
- Any-bad signal → instant step-down + cooldown.
- Cooldown blocks step-up.
- Floor sticky at `targetLayerCount=1` under sustained `-1`.
- Cold-start grace: first 1–2 windows after reconnect produce no
  decisions.
- Boundary conditions on each threshold.

## Step 10 — Playback quality controller

### 10.1 — `PlaybackHealth` per-stream sampling

DTO already defined as `PlaybackStreamInfo` (see Quality/PlaybackQuality.cs).
Add a worker-internal per-stream sampler in
`src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts`:

- `bufferDuration` — already tracked as `encodedBufferDepth` /
  `encodedBufferSpanMs` on `DecoderStats` (added in WIP Step E.4).
  Aggregate as p50 over 1 s.
- `incomingByteRate` — accumulate `encoded chunk byteLength` per
  stream over 1 s; reset each window.
- `keyframeSkipsInWindow` — increment on KF-aware evictions in
  `pushEncodedChunk` hard-cap path; reset each window.
- `decoderQueue.p90` — sample `decoder.getDecodeQueueSize()` per drain
  iteration; p90 over 1 s.

Posted per-stream via `DotNetObjectReference` callback to the .NET
client — `PlaybackQualityUI` (or `VideoQualityUI`'s playback branch)
collects the samples.

### 10.2 — DTOs (DONE)

Already on disk in
`src/dotnet/Api.Contracts/Streaming/Quality/PlaybackQuality.cs`:
`PlaybackStreamInfo`, `PlaybackQualityInfo`, `PlaybackQualityReason`,
`PlaybackStreamPriority`. No further work.

### 10.3 — `ChangePlaybackQuality` server impl

`src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`:

- Replace the per-session quality store seed from 8.3 (which returned
  `Default` for everything) with a real backing store updated by this
  method.
- Atomic-replace stored map per session on a non-null
  `requestedQuality` arg. Null → no change.
- Apply safety cap **before** storing:
  - Let `aboveLowest = entries where MaxSpatial > 0 OR MaxTemporal > 0`.
  - If `aboveLowest.Count > ServerCap = 9`, demote surplus to
    `(0, 0)` ordering: Secondary entries before Primary entries (the
    `Priority` is per-stream and lives in `info.Streams[streamId].Priority`
    — server cross-references). Then by request order.
- Streams not present in the map get `ReceiveQuality.Lowest` (`(0, 0)`)
  — equivalent to today's pause.

### 10.4 — `VideoQualityUI` playback branch

Same outer service, same `ConnectivityUI` gating, separate state.

#### Nested testable types

```csharp
public static class PlaybackVerdictClassifier
{
    public static int Classify(int bufferDurationMsP50, int keyframeSkips, PlaybackThresholds t);
    // returns -1, 0, +1
}

public static class AggregateHealth
{
    public static double Compute(IReadOnlyList<(long Rate, int Verdict)> streamSignals);
    // byte-weighted aggregate
}

public sealed class CapacityEstimator
{
    // state: long capacity
    // public methods: long Step(double aggregate, long sumIncomingRate) -> long newCapacity
    // √2 climb cap, 0.7 backoff
}

public sealed class Allocator
{
    // public method:
    //   IReadOnlyDictionary<StreamId, ReceiveQuality> Allocate(
    //       long budget,
    //       IReadOnlyList<StreamRequest> primaries,    // sorted by audio activity
    //       IReadOnlyList<StreamRequest> secondaries,
    //       Func<StreamId, int /* spatialLayer */, long /* predicted rate */> costModel);
}
```

#### Outer logic

- Cadence: `min(oldStreamCount, newStreamCount) ≤ 3` → 2 s ticks for
  10 s of stable active set; otherwise 5 s.
- Primary-promotion: when a stream's tile state changes from sidebar to
  focused, push a fresh request immediately (sub-cycle).
- Reconnect: re-push last-known map immediately on
  `ConnectivityUI.IsConnected → true`.
- `RenderQuality` rebind: today the per-canvas-width hint
  (`renderQualityLevelForWidth(width)` in video-player.ts:1232) maps to
  a 5-level enum and is sent in `VideoLatencyReport`. After this rewrite
  it becomes the Primary/Secondary classifier — focused tile width
  threshold (e.g. > 720px) = Primary, otherwise Secondary. The TS-side
  signal still fires from the `ResizeObserver`; .NET listens via
  callback and updates the per-stream priority in the controller.

#### Tests

`tests/UI.Blazor.App.UnitTests/VideoQualityUI/`:
- `PlaybackVerdictClassifierTest.cs`
- `AggregateHealthTest.cs` — including the user's two-stream insight
  (big healthy + small lagging → ≈ 0; small healthy + big lagging → ≈ -1).
- `CapacityEstimatorTest.cs` — √2 cap, 0.7 backoff, hold band.
- `AllocatorTest.cs` — primary-first, secondary defaults, floor.

## Build / test / commit cadence

After each task, run:

```bash
dotnet build ActualChat.CI.slnf
```

For TS changes:

```bash
npm run build:Verify
```

Commit at the end of each step (8, 9, 10) — not each sub-step. Keep
commit messages concise; reference the `video-pipeline-wip.md` step
number.

## Open issues / risks worth re-confirming with user

1. **`ILiveAudioStreams.GetStream` rename.** The existing GetStream
   returns `RpcStream<LiveStreamItem>` (ambient livestream). The new
   audio-frame `GetStream` collides. Recommended: rename existing back
   to `GetLiveStream` (its old name per `[LegacyName]`). Confirm before
   doing it.
2. **Whether `RecorderHealthSnapshot.SenderFrameDropRatio` should remain
   worker-owned.** The worker owns `InternalVideoStream` and can read
   `RpcStreamSender.skipCount`, so it computes the 1 Hz delta locally.
   If ownership moves, keep the metric as `dropped frames in last health
   window / Constants.Video.FrameRate`.
3. **Whether `StreamLatencyStore` becomes fully dead** after 8.5. Grep
   carefully — if there's any non-quality-related use (metrics, etc.)
   keep what's needed.
4. **`VideoQualityPreset` removal.** It's also in `Api/Video/` and may
   be referenced from diagnostics modals. The whole type might survive
   without the `Paused` value, or it might go entirely if nothing else
   references the preset concept.

## Coding-style reminders for the implementing agent

- File-scoped namespaces, 4-space indent, LF line endings, 120 char max.
- No `Async` suffix on async methods.
- No `///` XML docs on members. Inline `//` comment at top of method
  body only when the *why* is non-obvious.
- Type-level `///` summary required; max 5 lines, 3 ideal.
- Records and `init` accessors preferred for DTOs.
- MemoryPack + MessagePack annotation pattern (see existing
  `Quality/*.cs` files for the shape).
- Don't add backwards-compat shims unless the user explicitly asks —
  the user already authorised aggressive removal of obsolete video
  methods.
- For TS: 4-space indent, ESLint via `npm run build:Verify`.

## Concrete file inventory

For grep convenience, all files this plan references:

```
src/dotnet/Api.Contracts/Streaming/IStreamServer.cs
src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs
src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs
src/dotnet/Api.Contracts/Streaming/IStreamClient.cs           (update StreamClient facade as needed)
src/dotnet/Api.Contracts/Streaming/StreamClient.cs            (PushAudio/PushVideo move to live services)
src/dotnet/Api.Contracts/Streaming/Quality/ReceiveQuality.cs       (DONE)
src/dotnet/Api.Contracts/Streaming/Quality/RecordingQuality.cs     (DONE)
src/dotnet/Api.Contracts/Streaming/Quality/PlaybackQuality.cs      (DONE)
src/dotnet/Streaming.Service/Services/StreamServer.cs
src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs           (may not exist yet)
src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs
src/dotnet/Streaming.Service/Services/VideoStreamFilter.cs          (DELETE)
src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs       (DONE)
src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs       (gut quality methods)
src/dotnet/Streaming.Service/Backend/StreamLatencyStore.cs          (mostly DELETE)
src/dotnet/Streaming.Service/Backend/LiveVideoBackend.ChatState.cs  (strip pause logic)
src/dotnet/Streaming.Contracts/IVideoStreamingBackend.cs            (drop GetVideo, GetQualityPreset etc)
src/dotnet/Api/Video/VideoFrame.cs                                  (read for context)
src/dotnet/Api/Video/VideoQualityPreset.cs                          (drop Paused / maybe whole type)
src/dotnet/Api/Video/VideoLatencyReport.cs                          (DELETE — verify with grep)
src/dotnet/Api/Video/VideoLatencyReportResponse.cs                  (DELETE — verify with grep)
src/dotnet/UI.Blazor.App/Services/VideoRecorder.cs                  (drop GetQualityPreset subscription)
src/dotnet/UI.Blazor.App/Services/ChatVideoUI.cs                    (read for context)
src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs                 (NEW — Steps 9.4 + 10.4)
src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts (Step 9.2)
src/dotnet/UI.Blazor.App/Services/Video/workers/video-streaming.ts  (Step 8.4)
src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts   (Steps 8.4, 10.1)
src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts      (Steps 8.4, 10.4 RenderQuality rebind)
src/nodejs/src/api/streaming-api.ts                                 (Step 8.4)
tests/UI.Blazor.App.UnitTests/VideoQualityUI/                       (NEW directory; Steps 9.4, 10.4)
```

## Suggested execution order for next session

1. Re-create the TaskCreate list (13 tasks above).
2. Verify the four files marked `(DONE)` are intact on disk; spot-check
   contents.
3. **Step 8.2 first** — interface additions on `ILiveVideoStreams` and
   removal of video methods from `IStreamServer`. Build to surface
   compile errors before moving on. The build will be **broken** until
   8.4 swaps the TS callsites.
4. **Step 8.3** — rewrite `LiveVideoStreams.GetStream` to use
   `GetVideoRaw` + `ReceiveQualityFilter`. Add stub impls for
   `ChangeRecordingQuality` (logs, discards) and `ChangePlaybackQuality`
   (atomic store, safety cap). Add stub for `PushStream` and
   `RequestKeyFrame` that delegate to the backend and immediately
   invalidate `GetQualityPreset` so publishers receive the keyframe
   request. Build.
5. **Step 8.4** — rebind TS callsites. After this the .NET project
   builds with the old `IStreamServer` video methods removed AND the
   TS code still works.
6. **Step 8.1** — `ILiveAudioStreams` audio + transcript surface. Audio
   side mirrors video side. Resolve the `GetStream` naming collision
   (recommendation: rename existing to `GetLiveStream`).
7. **Step 8.5** — deletions. Build clean.
8. **Commit Step 8** before moving on.
9. **Step 9** — recording controller. Implement nested testable types
   first, write unit tests, then the outer service that drives them.
10. **Step 10** — playback controller. Same shape.
