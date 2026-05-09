# 08 — Quality control

Quality control is two control loops connected through the API pod:

- A **sender** loop on the publisher's main thread that adjusts how many
  simulcast layers to encode based on encoder-health signals from its own
  worker.
- A **receiver** loop on every viewer's main thread that decides — for each
  stream that viewer subscribes to — what `MaxLayerId` / `MaxTemporalLayerId`
  to ask for, based on playback-health signals plus a render-size hint per
  stream.

The server doesn't make policy decisions; it carries the state and enforces
it via `ReceiveQualityFilter` and `RequestKeyFrame`.

## Files

- Controllers (both sides) — `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`
- Wire types — `src/dotnet/Api.Contracts/Streaming/Quality/{ReceiveQuality,RecordingQuality,PlaybackQuality}.cs`
- RPC endpoints — `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`
  (`ChangeRecordingQuality`, `ChangePlaybackQuality`, `RequestKeyFrame`)
- Server-side filter — `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`
- PLI plumbing — `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`
  (`RequestKeyFrame`, `LastKeyframeRequestAt`)

## The wire types

```csharp
public sealed partial record ReceiveQuality(int MaxLayerId, int MaxTemporalLayerId)
{
    public static readonly ReceiveQuality Lowest  = new(0, 0);            // base layer KFs only
    public static readonly ReceiveQuality Default = new(1, int.MaxValue);
    public bool IsLowest => MaxLayerId <= 0 && MaxTemporalLayerId <= 0;
}
```

`PlaybackQualityInfo` carries per-session aggregate signals plus per-stream
sub-records: `EstimatedCapacityBytesPerSec`, `AggregateHealth`,
`Streams[streamId] → { Priority, LatencyMsEma, KeyframeSkipsInWindow,
DecoderQueueDepthEma, … }`.

`RecordingQualityInfo` carries per-recorder encoder health:
`EncodeRatioEma`, `EncodeRatioP90`, `SenderFrameDropRatioEma`,
`SlotReplacementRateEma`, `LastAckAgeMs`, `IsConnected`, `IsPeerConnected`,
`SenderFramesDropped`, `SenderKeyframesDropped`.

## Sender side — `RecordingQuality` AIMD

Source: `VideoQualityUI.cs`.

Inputs come from `VideoRecordingStats` (mutated by the worker pipeline) at
~1 Hz via `Recorder.getStats()`. The classifier
(`RecordingClassifier.Classify`) gives a ternary verdict per tick using
`RecordingThresholds.Defaults`:

| Verdict | Conditions |
|---|---|
| **BAD (-1)** (any of) | `EncodeRatioEma > EncodeRatioBadAbove (1.333)` (encoder achieving < 22 fps for 30 fps source) |
|  | `LastAckAgeMs ≥ 0 && LastAckAgeMs > LastAckBadMs (≈ 2 s)` (peer feedback stale) |
|  | `SenderFrameDropRatioEma ≥ SenderFrameDropRatioBadAbove (0.20)` |
| **GOOD (+1)** (all of) | `EncodeRatioEma < EncodeRatioGoodBelow (0.333)` |
|  | `LastAckAgeMs < 0 || LastAckAgeMs < LastAckGoodMs (≈ 0.5 s)` |
|  | `SenderFrameDropRatioEma < SenderFrameDropRatioGoodBelow (0.10)` |
| **NEUTRAL (0)** | otherwise |

Aggregator (`RecordingAggregator`): AIMD over `effectiveLayerCount`
(1..maxTier):

- BAD verdict ⇒ instant decrement (and a cooldown before climbing again).
- GOOD verdict ⇒ counts a streak; after the configured streak length,
  increment.
- NEUTRAL ⇒ resets streak, no change.

When `effectiveLayerCount` changes, the controller calls
`recorder.SetTargetLayerCount(...)`, which restarts the encoder chain
(`Recorder.restart()`). The `EncoderPool` keeps parked instances during the
gap so the new chain warms up fast.

In parallel, every tick the controller sends
`ILiveVideoStreams.ChangeRecordingQuality(state, info)` — the server
records encoder-health histograms (`AppMeters.VideoSendEncodeRatio`,
`VideoSendDropRatio`, `VideoSendAckAgeMs`, `VideoSendLayerCount`) for
observability but does not act on it.

### Eval cadence

The same stream-age-tiered eval gate the receiver uses (below) applies on
the sender path: snapshots arrive at 1 Hz but only "due" ticks advance the
AIMD step. `IsEvaluationDue(startedAt, lastEvalAt, force=false)` reads:

- `QcStartupCooldown = 5 s` — no eval; classifier signal is recorded but
  the AIMD doesn't step.
- `QcSettlingInterval = 3 s` until total stream age `QcSettlingDuration = 10 s`.
- `QcSteadyInterval = 5 s` thereafter.

A `ColdStartTicks = 2` global counter further suppresses the very first
two ticks at process boot.

## Receiver side — `PlaybackQuality` AIMD + per-stream allocator

Source: `VideoQualityUI.cs`.

### Per-stream verdict

`PlaybackVerdictClassifier.Classify(bufferSpanMsEma, t, qualityReductionRequested)`:

| Verdict | Conditions |
|---|---|
| **BAD (-1)** | `qualityReductionRequested == true` (decoder backpressure) |
| **GOOD (+1)** | `bufferSpanMsEma > 0 && bufferSpanMsEma ≤ BufferDurationTooHighMs` |
| **NEUTRAL (0)** | otherwise (over-deep buffer or no signal yet) |

`KeyframeSkipsInWindow` and `DecoderQueueDepthEma` are not direct verdict
drivers anymore — they surface in metrics (`VideoReceiveKeyframeSkips`,
`VideoReceiveDecoderQueue`) and feed the diagnostics modal, but the
verdict itself is intentionally narrow: receiver-domain only, reacting to
local decoder/main-thread overload through `qualityReductionRequested` and
to buffer health through `bufferSpanMsEma`. Sender-side starvation
indicators (low buffer, KF skips, missing-segment counters) are owned by
the sender's QC.

### Capacity estimator

`CapacityEstimator.Step(aggregateHealth, sumIncomingBytesPerSec)`:

| Field | Default |
|---|---|
| Cold-start capacity | `ColdStartCapacityBytesPerSec = 1.5 Mbps` |
| Floor | `MinCapacityBytesPerSec = 50 kbps` |
| Climb cap | `ClimbCap = √2` |
| Backoff factor | `BackoffFactor = 0.7` |

- `aggregateHealth ≤ -0.5` ⇒ `_capacity *= BackoffFactor` (multiplicative
  decrease).
- `aggregateHealth ≥ +0.5` AND `sumIncomingBytesPerSec > 0` ⇒
  climb to `max(_capacity, sumIncomingBytesPerSec × ClimbCap)`.
- Otherwise hold.

Aggregate health (`AggregateHealth.Compute`) is byte-weighted across active
streams: a small lagging stream paired with a healthy big stream stays
near 0; a big lagging stream paired with a small healthy one trends to -1.

Per-stream peak rate (`ComputeDecayedPeak` with
`PeakDecayPerSecond = 0.97`) is tracked so the allocator doesn't
underestimate upper-tier cost when the receiver is currently subscribed to
a lower tier — peak halves in ~23 s, allowing periodic probes back up.

### Allocator

`Allocator.Allocate(budget, primaries, secondaries, maxLayerId?)`:

1. Sort streams by priority — primaries first, then secondaries.
2. For each `StreamRequest { StreamId, PredictedRatesByLayer, MaxLayerId? }`,
   pick the highest layer whose predicted rate fits the remaining budget AND
   whose layer index doesn't exceed `MaxLayerId`.
3. Streams that don't fit at the base layer are dropped from the result;
   the caller maps that to `ReceiveQuality.Lowest`.

Per-stream `MaxLayerId` is computed from `RenderVideoSize` — see below.

### `RenderVideoSize` and the force-eval gate

`PlaybackHealthSnapshot.RenderVideoSize` is a computed property derived
from the snapshot's `RenderCssLongSide` × `RenderDevicePixelRatio`. The
viewport fires `OnPlaybackViewportChanged(streamId, longSide, dpr)` from
JS into Razor, which updates the snapshot. `GetDesiredVideoSize` then
picks the layer index that best matches the rendered size — a 200-px tile
won't pull a 720p layer.

The **force-eval gate** lives in `IsEvaluationDue(startedAt, lastEvalAt, force)`.
When called with `force = true` (e.g. on viewport change), the cadence
skip is bypassed but the startup cooldown still applies. Used so that a
viewer who just unhid a tile or the layout reflows gets a quality
re-evaluation immediately — without it, the viewer keeps the previous
allocator decision until the next periodic tick.

### Server-side stream-count cap

`LiveVideoStreams.ApplyStreamCountCap(qualityByStream, info)` enforces a
**server-side** ceiling of `serverCap = 9` streams above `Lowest` per
session: streams over the cap are demoted to `Lowest` by priority then
registration order. Per-stream `Priority` cross-referenced from
`info.Streams[streamId].Priority` (defaults to Secondary).

### Sending the result

`ILiveVideoStreams.ChangePlaybackQuality(session, qualityByStream, info)`:

```csharp
qualityByStream = ApplyStreamCountCap(qualityByStream, info);
_qualityBySession[session] = new ReceiveQualityState(qualityByStream, SystemClock.Now);

var upgradedStreams = GetUpgradedStreams(prevState?.QualityByStream, qualityByStream).ToArray();
if (upgradedStreams.Length != 0) {
    var keyFrameRequests = upgradedStreams
        .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), ct))
        .ToArray();
    await Task.WhenAll(keyFrameRequests);
}
```

For every stream whose desired spatial **or** temporal layer **increased**,
the server fires `RequestKeyFrame` (subject to the 1 s cooldown). The
publisher's worker forces the next bundle as a keyframe; the new layer's
keyframe arrives in `ReceiveQualityFilter` within milliseconds and the
filter switches over. Downgrades intentionally skip the request: the
receiver can keep showing the higher layer until the next periodic
keyframe, and burning the per-stream cooldown on a downgrade would block
the next upgrade's keyframe — exactly when the visible image just grew on
the client and we want a sharper picture immediately.

> **Removed.** An earlier `KeyFrameRequestDelay = 100 ms` between the
> upgrade and the PLI is gone. The motivation (let the new envelope
> propagate before the keyframe lands) didn't measurably help in practice
> and added latency to every quality upgrade.

### Throttling

The receiver controller throttles eval steps with the same gate as the
sender path:

- `QcStartupCooldown = 5 s` — covers the L2-keyframe wait (~3 s) plus
  EMA(10) ramp-up so the first eval lands on a settled buffer signal.
- `QcSettlingInterval = 3 s` until `QcSettlingDuration = 10 s` of stream age.
- `QcSteadyInterval = 5 s` thereafter.
- `PlaybackQualityKeepAlivePeriod = 1 min` — heartbeat call so the
  server's `_qualityBySession` doesn't go stale.

The session also records:
`AppMeters.VideoReceiveCapacityBps`, `VideoReceiveAggregateHealth`,
`VideoReceiveKeyframeSkips`, `VideoReceiveDecoderQueue`, `VideoLatency`
(per-stream, tagged primary/secondary).

## Server enforcement — `ReceiveQualityFilter`

File: `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`.

The filter is wrapped around every consumer's stream by
`LiveVideoStreams.GetStream`. It is an async iterator that calls
`getQuality()` **per frame** so changes take effect immediately.

State machine:

```
consumerMaxLayerId         ← from ReceiveQuality.MaxLayerId            (init -1)
consumerMaxTemporalLayerId ← from ReceiveQuality.MaxTemporalLayerId    (init int.MaxValue)
selectedLayer              ← -1
selectedMaxTemporalLayerId ← int.MaxValue
lastKeyFrameNumber         ← -1
skipping                   ← true
```

For each incoming frame:

```
producerMax  = frame.MaxLayerId
desiredLayer = clamp(consumerMaxLayerId, 0, producerMax)

if frame.IsKeyFrame:
    if frame.LayerId == desiredLayer:
        if frame.TemporalLayerId > consumerMaxTemporalLayerId:
            skipping = true; continue
        selectedLayer              = desiredLayer
        selectedMaxTemporalLayerId = consumerMaxTemporalLayerId
        lastKeyFrameNumber         = frame.KeyFrameNumber
        skipping = false
        yield frame
    else:
        continue                                  # other layers' KFs

else (delta frame):
    if skipping || selectedLayer < 0: continue
    if selectedLayer > producerMax:                # producer dropped layer mid-GOP
        skipping = true; continue
    if frame.LayerId != selectedLayer: continue
    if frame.KeyFrameNumber != lastKeyFrameNumber: # gap detected
        skipping = true; continue
    if frame.TemporalLayerId > selectedMaxTemporalLayerId: continue
    yield frame
```

Three behaviours fall out of this:

1. **Layer switches only on a keyframe.** Asking for a different
   `MaxLayerId` is the cheap part; the new layer locks in only when its
   next keyframe arrives. Combined with the server-issued PLI on quality
   change, this is typically < 50 ms.
2. **Temporal layer gating works on deltas.** A consumer asking for
   `MaxTemporalLayerId = 0` gets keyframes only — close to a "snapshot
   every 3 s" thumbnail. `MaxTemporalLayerId = int.MaxValue` (the default)
   is "full framerate".
3. **Gap detection via `KeyFrameNumber`.** If the memoizer evicted frames
   between two keyframes (rare under steady load, can happen during a
   long stall), `frame.KeyFrameNumber != lastKeyFrameNumber` puts the
   filter back into `skipping` mode and waits for the next matching
   keyframe. There's also a "producer dropped my layer" branch that flips
   to skipping when `selectedLayer > producerMax` — handles the case where
   the publisher's AIMD reduced its top tier mid-stream.

## End-to-end signal flow

```
                                                        ┌──────────────────┐
                                                        │ AppMeters        │
                                                        │  - SendEncodeRatio
                                                        │  - SendDropRatio │
            ChangeRecordingQuality (1 Hz)               │  - SendAckAgeMs  │
sender ─────────────────────────────────────────────▶  │  - SendLayerCount│
worker ──┐                                              └──────────────────┘
stats    │ ▲                                            ┌──────────────────┐
         ▼ │                                            │ _qualityBySession│
sender   │ │ SetTargetLayerCount                        └──────────────────┘
ctrl  ───┘ │                                                       ▲
   AIMD layer count                                                │ ChangePlaybackQuality
                                                                   │ (~2 s, 1 min keep-alive)
sender ────────────────────────────────────────────▶  ReceiveQualityFilter
RpcStream<VideoFrameBundle> ─▶ ProcessFrames ─▶ Memoizer            per-frame getQuality()
                                                                   │
                                                                   ▼
                                                       receiver ◀──┘
                                                       worker stats
                                                       buffer / decoder / latency
                                                                   │
                                                                   ▼
                                                       receiver ctrl
                                                       (per-stream verdict +
                                                        capacity AIMD +
                                                        allocator → ReceiveQuality)
                                                                   │
                                                                   ▼ ChangePlaybackQuality
                                                                   │   PLI on upgrade only
```

## Late joiners and PLI

A new viewer's `GetStream` call:

1. Always fires `RequestKeyFrame(streamId)` (rate-limited to 1 s globally).
2. Falls into the memoizer's `Replay` which starts from
   `min(latestKeyframeOffset[layer])` — so most of the time a usable
   keyframe for the desired layer is already in the prefix.
3. If not (cold-start, or the desired layer's KF was just evicted), the
   PLI forces a fresh keyframe ≤ 1 s away and the filter locks on.

Concurrent joiners' PLIs collapse — the cooldown ensures one PLI per burst.

## Known limits and trade-offs

- The capacity estimator is **derived from buffer/decoder signals plus
  incoming byte rate**. Not a fine-grained TCP-level controller, but the
  ACK cadence on the consumer-leg `RpcStream` already provides
  loss/latency feedback at a coarse grain.
- **Layer changes restart the sender encoder pipeline.** The pool keeps
  parked encoders so this is fast (sub-second), but it does drop a small
  number of frames around the transition. Within-layer bitrate-only
  reconfigs are in-place.
- **No client-side network probing.** Backpressure is observed from the
  publisher leg's RPC ring (compaction kicks in via `canSkipTo`) and from
  the receiver leg's buffer-span EMA.
- **Stream count cap is 9 above-Lowest.** Above that, the server demotes
  by priority then registration order. UI clients should set priority
  correctly on stream subscribe.
