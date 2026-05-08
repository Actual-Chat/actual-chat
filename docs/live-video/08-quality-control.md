# 08 — Quality control

Quality control is two AIMD loops connected through the API pod:

- A **sender** loop on the publisher's main thread that adjusts how many
  simulcast layers to encode based on encoder-health signals from its own
  worker.
- A **receiver** loop on every viewer's main thread that decides — for each
  stream that viewer subscribes to — what `MaxLayerId` / `MaxTemporalLayerId`
  to ask for, based on playback-health signals from its own worker.

The server doesn't make policy decisions; it carries the state and enforces
it via `ReceiveQualityFilter` and `RequestKeyFrame`.

## Files

- Controllers (both sides) — `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`
- Wire types — `src/dotnet/Api.Contracts/Streaming/Quality/{ReceiveQuality,RecordingQuality,PlaybackQuality}.cs`
- RPC endpoints — `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs` (`ChangeRecordingQuality`, `ChangePlaybackQuality`)
- Server-side filter — `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`
- PLI plumbing — `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs` (`RequestKeyFrame`, `LastKeyframeRequestAt`)

## The wire types

```csharp
public sealed partial record ReceiveQuality(int MaxLayerId, int MaxTemporalLayerId)
{
    public static readonly ReceiveQuality Lowest = new(0, 0);            // base layer + KFs only
    public static readonly ReceiveQuality Default = new(1, int.MaxValue);
    public bool IsLowest => MaxLayerId <= 0 && MaxTemporalLayerId <= 0;
}
```

`PlaybackQualityInfo` carries per-session aggregate signals:

- `IncomingByteRate` (bytes/s sum across all streams)
- `BufferDurationMsEma`
- `KeyframeSkipsInWindow`
- `DecoderQueueDepthEma`
- `LatencyMsEma`
- `Priority`-tagged stream list (primary vs. secondary)
- Render-size hints per stream (`CssLongSide`, `DevicePixelRatio`)

`RecordingQualityInfo` carries per-recorder encoder health:

- `EncodeRatioEma`, `EncodeRatioP90` — encode time ÷ frame budget
- `SenderFrameDropRatioEma`
- `SlotReplacementRateEma`
- `LastAckAgeMs`, `IsConnected`, `IsPeerConnected`
- `SenderFramesDropped`, `SenderKeyframesDropped`

## Sender side — `RecordingQuality` AIMD

Source: `VideoQualityUI.cs`.

Inputs come from `VideoRecordingStats` (mutated by the worker pipeline) at
~1 Hz via `Recorder.getStats()`. The classifier gives a ternary verdict per
tick:

| Verdict | Conditions (any of) |
|---|---|
| **BAD (-1)** | `EncodeRatioP90 > 1.33` (encoder achieving < 22 fps for 30 fps source) |
|              | `LastAckAgeMs > LastAckBadMs` (~5 s — peer feedback stale) |
|              | `SenderFrameDropRatioEma >= 0.20` |
| **GOOD (+1)** | `EncodeRatioP90 < 0.33` AND `LastAckAgeMs < LastAckGoodMs` (~1 s) AND `SenderFrameDropRatioEma < 0.10` |
| **NEUTRAL (0)** | otherwise |

Aggregator: AIMD over `effectiveLayerCount` (1..3):

- BAD verdict ⇒ instant decrement, set 5-tick cooldown.
- GOOD verdict ⇒ counts a streak. After 5 consecutive GOODs, increment.
- NEUTRAL ⇒ resets streak, no change.

When `effectiveLayerCount` changes, the controller calls
`recorder.SetTargetLayerCount(...)`, which restarts the encoder chain
(`Recorder.restart()`: stop, rebuild ladder, start). The `EncoderPool` keeps
parked instances during the gap so the new chain warms up fast.

In parallel, every tick the controller sends
`ILiveVideoStreams.ChangeRecordingQuality(state, info)` — the server records
encoder-health histograms (`AppMeters.VideoSendEncodeRatio`,
`VideoSendDropRatio`, `VideoSendAckAgeMs`, `VideoSendLayerCount`) for
observability but does not act on it.

## Receiver side — `PlaybackQuality` AIMD + per-stream allocator

Source: `VideoQualityUI.cs`.

### Per-stream verdict

For each subscribed stream (`PlaybackHealthSnapshot`):

| Verdict | Conditions (any of) |
|---|---|
| **BAD (-1)** | `QualityReductionRequested == true` (decoder backpressure) |
|              | `KeyframeSkipsInWindow >= 1` |
|              | `DecoderQueueDepthEma > DecoderQueueDepthBadAbove` (~30) |
|              | `BufferDurationMsEma < BufferDurationTooLowMs` (~111 ms, after startup grace) |
| **GOOD (+1)** | buffer in healthy band [TooLow .. TooHigh] |
| **NEUTRAL (0)** | overfull buffer or otherwise unclear |

### Capacity estimator (per-session)

`CapacityEstimator` runs an AIMD over the session's available bitrate:

- Cold start: `1.5 Mbps`. Floor: `50 kbps`.
- Aggregate health (byte-weighted across streams) ≥ 0.5 ⇒ climb toward
  `√2 × observed_rate`.
- Aggregate health ≤ -0.5 ⇒ multiplicative backoff to `0.7 × current`.
- Peak rate decays at 0.97/s so a single bad period doesn't lock capacity
  low forever.

### Allocator

Greedy, runs every ~2 s (also on render-size change):

1. Sort streams by priority — primaries first, then secondaries.
2. For each stream, pick the highest layer whose bitrate fits the remaining
   budget AND whose dimensions don't exceed the render-size hint × DPR.
3. Streams that don't fit get `ReceiveQuality.Lowest` (base layer KFs only —
   essentially "tiny preview").
4. Server-side cap: at most 9 streams above `Lowest` per session
   (`ApplyStreamCountCap` in `LiveVideoStreams.cs`); excess are demoted by
   priority then registration order.

### Sending the result

`ILiveVideoStreams.ChangePlaybackQuality(session, qualityByStream, info)`:

```csharp
_qualityBySession[session] = new ReceiveQualityState(qualityByStream, SystemClock.Now);

var keyFrameRequests = GetChangedStreams(prevState?.QualityByStream, qualityByStream)
    .Select(x => VideoStreamingBackend.RequestKeyFrame(StreamId.Parse(x), ct))
    .ToArray();
if (keyFrameRequests.Length != 0)
    await Task.WhenAll(keyFrameRequests);
```

For every stream whose desired layer changed, the server fires
`RequestKeyFrame` (subject to the 1 s cooldown). The publisher's worker
forces the next bundle as a keyframe; the new layer's keyframe arrives in
`ReceiveQualityFilter` within milliseconds and the filter switches over (see
below).

The session also records:
`VideoReceiveCapacityBps`, `VideoReceiveAggregateHealth`,
`VideoReceiveKeyframeSkips`, `VideoReceiveDecoderQueue`.

### Throttling

The receiver controller throttles `ChangePlaybackQuality`:

- Startup grace: 5 s (no buffer-low backoff during this).
- Settling phase: 3 s minimum interval after any change.
- Steady state: 5 s minimum interval.
- Heartbeat: every 5 s the call goes out anyway, so the server's
  `_qualityBySession` doesn't go stale.

## Server enforcement — `ReceiveQualityFilter`

File: `src/dotnet/Streaming.Service/Services/ReceiveQualityFilter.cs`.

The filter is wrapped around every consumer's stream by
`LiveVideoStreams.GetStream`. It is an async iterator that calls
`getQuality()` **per frame** so changes take effect immediately.

State machine:

```
consumerMaxLayerId      ← from ReceiveQuality.MaxLayerId
consumerMaxTemporalLayerId ← from ReceiveQuality.MaxTemporalLayerId
selectedLayer            ← -1   (nothing locked yet)
selectedMaxTemporalLayerId
lastKeyFrameNumber       ← -1
skipping                 ← true
```

For each incoming frame (already from the per-layer-aware memoizer):

```
desiredLayer = clamp(consumerMaxLayerId, 0, frame.MaxLayerId ?? 0)

if frame.IsKeyFrame:
    if frame.LayerId == desiredLayer:
        if frame.TemporalLayerId > consumerMaxTemporalLayerId:
            skipping = true; continue
        selectedLayer = desiredLayer
        selectedMaxTemporalLayerId = consumerMaxTemporalLayerId
        lastKeyFrameNumber = frame.KeyFrameNumber
        skipping = false
        yield frame
    else:
        continue                                  # other layers' KFs

else (delta frame):
    if skipping or selectedLayer < 0: continue
    if selectedLayer > frame.MaxLayerId:          # producer dropped layer
        skipping = true; continue
    if frame.LayerId != selectedLayer: continue
    if frame.KeyFrameNumber != lastKeyFrameNumber:  # gap detected
        skipping = true; continue
    if frame.TemporalLayerId > selectedMaxTemporalLayerId: continue
    yield frame
```

Three behaviours fall out of this:

1. **Layer switches only on a keyframe.** Asking for a different `MaxLayerId`
   is the cheap part; the new layer locks in only when its next keyframe
   arrives. Combined with the server-issued PLI on quality change, this is
   typically < 50 ms.
2. **Temporal layer gating works on deltas.** A consumer asking for
   `MaxTemporalLayerId = 0` gets keyframes only — close to a "snapshot every
   3 s" thumbnail. `MaxTemporalLayerId = int.MaxValue` (the default) is "full
   framerate".
3. **Gap detection via `KeyFrameNumber`.** If the memoizer evicted frames
   between two keyframes (rare under steady load, can happen during a long
   stall), `frame.KeyFrameNumber != lastKeyFrameNumber` puts the filter back
   into `skipping` mode and waits for the next matching keyframe.

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
                                                                   │ (every ~2 s)
sender ────────────────────────────────────────────▶  ReceiveQualityFilter
RpcStream<VideoFrame> (via memoizer)                  per-frame getQuality()
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
```

## Late joiners and PLI

A new viewer's `GetStream` call:

1. Always fires `RequestKeyFrame(streamId)` (rate-limited to 1 s globally).
2. Falls into the memoizer's `Replay` which starts from
   `min(latestKeyframeOffset[layer])` — so most of the time a usable keyframe
   for the desired layer is already in the prefix.
3. If not (cold-start, or the desired layer's KF was just evicted), the PLI
   forces a fresh keyframe ≤ 1 s away and the filter locks on.

Concurrent joiners' PLIs collapse — the cooldown ensures one PLI per burst.

## Known limits and trade-offs

- The capacity estimator is **derived from buffer/decoder signals, not RTT**.
  This works because Fusion RPC's ACK cadence already provides loss/latency
  feedback at a coarse grain, but it's not a fine-grained TCP-level
  controller.
- **Layer changes restart the sender encoder pipeline.** The pool keeps
  parked encoders so this is fast (sub-second), but it does drop a small
  number of frames around the transition. Within-layer bitrate-only
  reconfigs are in-place.
- **No client-side network probing.** The server-side `RpcStreamBufferSize =
  10` and ACK cadence are the only flow-control signals; under heavy loss
  the publisher will see the buffer back up and the AIMD reduces layers.
- **Stream count cap is 9 above-Lowest.** Above that, the server demotes by
  priority then registration order. UI clients should set priority correctly
  on stream subscribe.
