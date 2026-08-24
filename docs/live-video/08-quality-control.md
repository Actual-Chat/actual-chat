# 08 — Quality control

Two control loops, one on each end:

- A **sender** loop on the publisher's main thread that adjusts how many
  simulcast layers to encode for camera and screencast, treating them as
  shares of one shared upstream pipe.
- A **receiver** loop on every viewer's main thread that decides — for
  each stream that viewer subscribes to — what `ReceiveQuality`
  (`LayerCount` + `TemporalLayerCount`) to ask for, anchored on one
  shared downstream pipe.

The server doesn't make policy decisions; it carries the state and
enforces it via `ReceiveQualityFilter` and `RequestKeyFrame`.

## Files

- Controllers (both sides) — `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`
- Shared adaptive controller — `src/dotnet/Core/Bandwidth/BandwidthEstimator.cs`
  (namespace `ActualChat.Bandwidth`)
- Per-direction caps, probe + allocator — same `Services/` folder:
  `LayerCap.cs`, `EncodingCap.cs`, `BandwidthCap.cs`, `ThermalCap.cs`,
  `SpeculativeProbe.cs`, `VideoQualityAllocator.cs`
- RPC connection epoch — `src/dotnet/Core/Rpc/RpcConnectionInfo.cs`
  (consumed by ConnectivityUI; ConnectionInfo nullable until first connect)
- Wire types — `src/dotnet/Api.Contracts/Streaming/Quality/{ReceiveQuality,RecordingQuality,PlaybackQuality}.cs`
- RPC endpoints — `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`
- Server-side filter — `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`
- PLI plumbing — `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`

## The wire types

```csharp
public sealed partial record ReceiveQuality(int LayerCount, int TemporalLayerCount)
{
    public static readonly ReceiveQuality Lowest  = new(1, 1);
    public static readonly ReceiveQuality Default = new(2, int.MaxValue);
}
```

`PlaybackQualityInfo` carries per-session aggregate signals plus per-stream
sub-records. `PlaybackStats` (client-local, not serialized) carries the
per-stream sample driving the receiver controller — including
`PlaybackRateEma` (source-clock ms drained per wall-clock ms at the
present stage, EMA-smoothed) and `AvailableTemporalLayerCount`.

`RecordingQualityInfo` carries per-recorder encoder health to the server
for telemetry only.

## Shared building blocks

### `BandwidthEstimator` (Core)

The same algorithm runs on both sides, one instance per direction. It
tracks a single number — `CeilingBps` — its best estimate of the
available bandwidth on the current RPC connection. Updated each tick
from a fused `signalLevel ∈ [0, 1]` plus the observed `currentBandwidthBps`:

| `signalLevel` | Meaning | Estimator response |
|---|---|---|
| `1.0` | perfect | raise `CeilingBps` if we're pushing the limit |
| `0.5` | "real cap is ~half of what I just used" | drift `CeilingBps` toward `currentBps × 0.5` |
| `0.0` | catastrophic | drift hard down |

Magnitude of each move scales with `(1 − signalLevel)`, the consecutive
negative streak, and an age-decay factor (older, more-stable connections
move in smaller steps).

**Probe back-off.** When a lift→drop cycle happens (we tried to push the
ceiling and got slapped down), `ProbeFailures` increments and the next
upward probe is suppressed for
`min(MaxProbeCooldownSec, BaseProbeCooldownSec × ProbeCooldownGrowth^ProbeFailures)`.
A sustained calm period (`CalmTicks ≥ ProbeFailureResetStreak`) resets the
backoff. The cycle exists by design — it's how we re-discover the cap
after the network changes — but spaces out over time so we don't burn
CPU on persistent up/down churn.

**Connection epoch.** Each new RPC connection
(`ConnectivityUI.ConnectionInfo.Value.Index` change) resets `CeilingBps`
to `config.InitialCeilingBps` (a per-direction default chosen to support
the baseline configuration without an early downgrade) and clears all
streaks / probe state / history. There's no cross-epoch carry — the
estimator re-learns the new connection's ceiling within seconds, while
the caller's `lastTarget` keeps the user-visible quality steady.

### `RpcConnectionInfo` (Core)

```csharp
public sealed record RpcConnectionInfo(int Index, Moment ConnectedAt);
```

`Index` is monotonic across the process lifetime; `null` on
`ConnectivityUI.ConnectionInfo` means "not currently connected".

## Sender side — outbound

Three things compose into the final per-stream layer count:

1. **`EncodingCap`** — driven by `RecorderStats.EncodeRatioEma`. Walks
   camera then screencast layers down on sustained encode-ratio overrun
   (CPU/GPU can't keep up); back up on sustained underrun. Independent
   of bandwidth.
2. **`BandwidthCap`** — driven by `BandwidthEstimator`'s
   `NegativeStreak` / `PositiveStreak` (with the `ConfirmRatio` check
   for upward moves). Walks camera then screencast layers in the same
   priority order.
3. **Device-class cap** — at boot:
   ```csharp
   var isMobile = BrowserInfo.IsMobile || HostInfo.AppKind.IsMobile();
   deviceCameraCap = isMobile ? 2 : VideoLayerDef.CameraLayers.Length;
   screencastCap   = VideoLayerDef.ScreenCastLayers.Length;
   ```
   Mobile drops camera layer 2; screencast is always at full ladder.

The effective target per kind is `min(encCap, bwCap, deviceCap)`, then
clamped by `ThermalCap` (which also caps FPS). When it changes for either
kind, the controller calls `recorder.SetTargetLayerCount(target)`.

On top of that, `SpeculativeProbe` (`SpeculativeProbe.cs`) covers the case
the drain-rate measurement cannot: an idle wire offers nothing to measure.
When bandwidth is the binding cap, the link looks healthy (ack age at or
below `max(HealthyAckAgeMs, minRtt + HealthyAckSlackMs)`) and the wire queue
is shallow, it adds **one** camera layer for `WindowTicks` and watches ack
age. Flat ack age commits the climb by bumping `BandwidthCap`; an inflation
past `AckAgeInflationMs` reverts it and backs off (`BaseCooldownTicks ×
CooldownGrowth^failures`, capped at `MaxCooldownTicks`).

### Outbound `signalLevel`

Primary signal: **wire-send drop ratio** (`SenderFrameDropRatioEma`) —
the cleanest evidence we're producing more bytes than the link can
ship. ACK age and encode ratio are secondary penalties; the worst of
the three drives the result. Each penalty is a linear ramp from an
`Ok` threshold (penalty = 0) to a `Bad` threshold (penalty = 1):

```
dropPenalty = clamp((SenderFrameDropRatioEma − DropOkSender) / (DropBadSender − DropOkSender), 0, 1)
ackPenalty  = LastAckAgeMs < 0 ? 0
              : clamp((LastAckAgeMs − AckOkMs) / (AckBadMs − AckOkMs), 0, 1)
encPenalty  = clamp((EncodeRatioEma − EncOkRatio) / (EncBadRatio − EncOkRatio), 0, 1)
signalLevel = 1 − max(dropPenalty, ackPenalty, encPenalty)
```

Concrete thresholds (see `Constants.Video`): `DropOkSender = 0.20`,
`DropBadSender = 0.50`, `AckOkMs = 500`, `AckBadMs = 2000`,
`EncOkRatio = 1.0`, `EncBadRatio = 2.0`. Encode ratio is kept as a
fallback — `EncodingCap` is the primary response to encode overrun.

### Outbound per-tick (every 1 s, gated by `IsEvaluationDue`)

```
1. Update _lastRecorderStatsByKind[kind] with the latest snapshot
2. Fuse signals across all active kinds (max-penalty)
3. bwEstimator.Tick(connection, now, totalBytesPerSec, signalLevel)
4. encodingCap.Tick(fusedEncodeRatio)
5. bwCap.Tick(bwEstimator)
6. probeExtra = speculativeProbe.Tick(...)  // 0 or 1 camera layer
7. effCap = min(encCap, bwCap, deviceCap), camera + probeExtra
8. thermalCap.Tick(now, thermalLevel); effCap = min(effCap, thermalCap)
9. for each kind: recorder.SetTargetLayerCount(effCap[kind]) if changed,
   then recorder FPS ceiling = thermalCap.MaxFps
10. ChangeRecordingQuality(state, info)   // server-side telemetry only
```

## Receiver side — inbound

### Per-stream verdict (sub-signal)

`PlaybackVerdictClassifier.Classify(bufferSpanMsEma, t)` computes a
ternary verdict per stream for diagnostics and the server-side info
map (good if `0 < bufferSpanMsEma ≤ BufferDurationTooHighMs`, else
neutral). The pipeline does NOT drive layer choice from this verdict
directly — see `signalLevel` below.

### Inbound `signalLevel` — primary signal is playback rate

We don't have ACK age on the receive leg. Instead the primary signal
measures how much source time we're actually draining at the present
stage per unit of wall time. Once per second of wall clock the
`present` operator samples `offsetDelta / wallDelta` (where
`offsetDelta` is the change in `frame.offset` between the last and
current presented frame, `0` if no new frame was presented), clamps to
`[0, 1]`, and EMA-smooths into `playbackRateEma`. `1.0` = on time;
`0.8` = falling behind 200 ms every second. Catching-up runs are
floored at 1 (no credit for working off backlog).

Maintained in `frame-envelopes.ts → updatePlaybackRateEma()`, called
from the `present-mstg` and `present-canvas` operators after every
successful present. Resets on `frame.offset.epoch` change (an anchor
reset is not drift).

Drop ratio uses receiver-side stages only (FrameDropStage bytes 61–90:
`ReceiverPull`, `ReceiverEncodedBuffer`, `ReceiverDecode`,
`ReceiverPresent`). Sender + server drops in the trace are intentional
pacing and don't reflect downstream-bandwidth health.

```
playbackRateEma     = byteWeightedAggregate over streams (weight = IncomingByteRate)
receiverDropRatio   = Σ receiver-stage drops / (Σ receiver-stage drops + Σ presented)

driftPenalty = clamp((PlaybackRateOk − playbackRateEma) / (PlaybackRateOk − PlaybackRateBad), 0, 1)
dropPenalty  = clamp((receiverDropRatio − DropOkReceiver) / (DropBadReceiver − DropOkReceiver), 0, 1)
signalLevel  = 1 − max(driftPenalty, dropPenalty)
```

Concrete thresholds (see `Constants.Video`): `PlaybackRateOk = 0.90`,
`PlaybackRateBad = 0.00` (so `playbackRateEma ≥ 0.9` ⇒ 0 penalty,
`= 0` ⇒ full penalty), `DropOkReceiver = 0.20`,
`DropBadReceiver = 0.50` (so 20% drops still ride free, ramps linearly
to 1 at 50%).

### Priority allocator (`VideoQualityAllocator`)

Replaces the old greedy single-pass allocator. Per tick:

```
1. budget = bwEstimator.CeilingBps × debugBandwidthMultiplier
2. floorBudget = Σ over secondaries of predictedRate(layers=1, fraction=temporalFloor(s))
3. primaryBudget = max(0, budget − floorBudget)
4. for each primary: best-fit (spatialLayers, temporalFraction) ≤ primaryBudget
5. remaining = budget − primariesUsed
6. for each secondary:
       share = remaining × renderArea(s) / Σ renderArea(*)
       best-fit (layers, fraction) within share, never below floor
7. Translate (layers, fraction) → ReceiveQuality(LayerCount, TemporalLayerCount)
   at the wire boundary via:
       discreteTemporalCount = clamp(ceil(fraction × producerTemporalLayerCount),
                                     1, producerTemporalLayerCount)
```

`predictedRate(s, layers, fraction)` is `PredictedRatesByLayer[layers − 1] × fraction`.
Temporal fractions are dyadic: for K temporal layers, valid values are
`{1/2^(K−1), 1/2^(K−2), …, 1/2, 1}` (so for K=3: {0.25, 0.5, 1.0}).

If a stream's `AvailableTemporalLayerCount == 1`, the temporal request is
pinned to 1.0.

### `RenderVideoSize` → per-stream layer cap

`GetBestLayerFor(sourceKind, desiredSize)` uses the standard ABR rule:
**smallest ladder layer with `Width ≥ desiredWidth`**, falling back to
the largest available. The previous "nearest by absolute distance"
rule biased toward the lower layer when the desired size sat between
two ladder rungs (this is the cause of the historical
"screencast always presents L0" bug).

The viewport fires `OnPlaybackViewportChanged(streamId, longSide, dpr)`
from JS into Razor; `GetBestLayerFor` then picks the layer index that
best matches the rendered tile size.

### Cadence

Same as before:

- `QcStartupCooldown = 5 s` — no eval; signal is recorded but the
  controller doesn't step.
- `QcSettlingInterval = 3 s` until total stream age `QcSettlingDuration = 10 s`.
- `QcSteadyInterval = 5 s` thereafter.
- `PlaybackQualityKeepAlivePeriod = 1 min` — heartbeat so the server's
  `_qualityBySession` doesn't go stale.

A `ColdStartTicks = 2` global counter further suppresses the very first
two ticks at process boot.

### Inbound per-tick (full)

```
1. Distill receiver signals into signalLevel
2. bwEstimator.Tick(connection, now, sumIncomingBytesPerSec, signalLevel)
3. budget = bwEstimator.CeilingBps × debugBandwidthMultiplier
4. Build StreamAllocationRequests (with PredictedRatesByLayer, LayerCountCap,
   AvailableTemporalLayerCount, RenderArea)
5. VideoQualityAllocator.Allocate(budget, primaries, secondaries)
6. Build ReceiveQuality wire dict
7. ChangePlaybackQuality(session, qualityByStream, info)
8. UpdateRequestedReceiveQualityRegistry (for the keyframe-on-upgrade path)
```

## Server enforcement — `ReceiveQualityFilter`

Unchanged from the previous design. State machine per consumer:

```
consumerLayerCount         ← ReceiveQuality.LayerCount         (init 0)
consumerTemporalLayerCount ← ReceiveQuality.TemporalLayerCount (init int.MaxValue)
selectedLayer              ← -1
selectedTemporalLayerCount ← int.MaxValue
lastKeyFrameIndex          ← -1
skipping                   ← true
```

For each incoming frame:

```
producerLayerCount = frame.LayerCount
desiredLayer = clamp(consumerLayerCount − 1, 0, producerLayerCount − 1)

if frame.IsKeyFrame:
    if frame.LayerId == desiredLayer:
        if frame.TemporalLayerId >= consumerTemporalLayerCount:
            skipping = true; continue
        selectedLayer = desiredLayer
        selectedTemporalLayerCount = consumerTemporalLayerCount
        lastKeyFrameIndex = frame.KeyFrameIndex
        skipping = false
        yield frame
    else:
        continue                                  # other layers' KFs

else (delta frame):
    if skipping || selectedLayer < 0: continue
    if selectedLayer >= producerLayerCount:        # producer dropped layer mid-GOP
        skipping = true; continue
    if frame.LayerId != selectedLayer: continue
    if frame.KeyFrameIndex != lastKeyFrameIndex:   # gap detected
        skipping = true; continue
    if frame.TemporalLayerId >= selectedTemporalLayerCount: continue
    yield frame
```

Three behaviours fall out:

1. **Layer switches only on a keyframe.** Asking for a different
   `LayerCount` is cheap; the new layer locks in only when its next
   keyframe arrives. Combined with the server-issued PLI on quality
   upgrade, this is typically < 50 ms.
2. **Temporal layer gating works on deltas.** A consumer asking for
   `TemporalLayerCount = 1` gets every-Nth-frame service for an N-layer
   producer.
3. **Gap detection via `KeyFrameIndex`.** Memoizer eviction in the
   middle of a GOP flips the filter back to `skipping` until the next
   matching keyframe.

## Late joiners and PLI

A new viewer's `GetStream` call:

1. Always fires `RequestKeyFrame(streamId)` (rate-limited to 1 s globally).
2. Falls into the memoizer's `Replay`, which starts from
   `min(latestKeyframeOffset[layer])` — most of the time a usable
   keyframe for the desired layer is already in the prefix.
3. If not, the PLI forces a fresh keyframe ≤ 1 s away and the filter
   locks on.

Concurrent joiners' PLIs collapse — the cooldown ensures one PLI per
burst.

## End-to-end signal flow

```
                                                    ┌──────────────────┐
                                                    │ AppMeters        │
                                                    │  - SendDropRatio │
            ChangeRecordingQuality (1 Hz)           │  - SendAckAgeMs  │
sender ─────────────────────────────────────────▶  │  - SendLayerCount│
worker ──┐                                          └──────────────────┘
stats    │ ▲                                        ┌──────────────────┐
         ▼ │ ┌──────────────────┐                   │ _qualityBySession│
sender   │ │ │ Outbound pipeline│                   └──────────────────┘
ctrl  ───┘ │ │   - BandwidthEst.│                              ▲
   layer cnt│ │   - EncodingCap │                              │
            │ │   - BandwidthCap│                              │ ChangePlaybackQuality
            │ └──────────────────┘                             │
sender ────────────────────────────────────────▶  ReceiveQualityFilter
RpcStream<VideoFrameBundle> ─▶ ProcessFrames ─▶ Memoizer       per-frame getQuality()
                                                               │
                                                               ▼
                                                   receiver ◀──┘
                                                   worker stats
                                                   (playbackRateEma, presented,
                                                    bytesReceived, dropTrace)
                                                               │
                                                               ▼
                                                   ┌──────────────────┐
                                                   │ Inbound pipeline │
                                                   │   - BandwidthEst.│
                                                   │   - VideoQuality │
                                                   │     Allocator     │
                                                   └──────────────────┘
                                                               │
                                                               ▼ ChangePlaybackQuality
                                                               │   PLI on upgrade only
```

## Known limits and trade-offs

- **Probing is uplink-only, and only one layer wide.** `SpeculativeProbe`
  tests for camera headroom on the sender; there is no equivalent on the
  receiver leg, where backpressure is still observed rather than provoked —
  from the publisher leg's RPC ring (compaction via `canSkipTo`) and from
  `playbackRateEma`.
- **Layer changes restart the sender encoder pipeline.** A fresh
  `VideoEncoder` is constructed per layer (encoders are not pooled — see
  `02-sender.md`), so a few frames around the transition cost the
  cold-init delay. Within-layer bitrate-only reconfigs stay in-place.
- **Stream count cap is 9 above-Lowest.** Above that, the server demotes
  by priority then registration order. UI clients should set priority
  correctly on stream subscribe.
- **No cross-epoch carry on the estimator.** Each reconnect re-learns
  the ceiling from `InitialCeilingBps`. The user-visible quality stays
  steady because the caller's `lastTarget` resumes immediately — the
  estimator just needs a couple of seconds of good signal to walk back
  up to the prior level.
