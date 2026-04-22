# Video Throughput-Based Probing (medium-term)

## Goal

Enable **step-up on demonstrated bandwidth headroom** even when absolute latency is high (cross-continent peers, buffered links). Mirror the existing throughput-based step-down path with a symmetric step-up path, and add WebRTC-style capacity probing so we can discover headroom before committing to a higher quality.

Prerequisite: the short-term delta-from-baseline latency fix (already landed in `PeerLatencyState.IsNetworkSlow` / `IsNetworkFast`).

## Why

Today step-up requires every peer to be `IsNetworkFast`. Even with the delta-from-baseline fix this is reactive — we never *test* whether more bandwidth is available. On a long-haul link with 300 ms baseline latency the pipe may comfortably carry 8 Mbps 1080p HEVC but we stay on High-720p because no signal told us to try.

WebRTC GCC solves this by periodically **probing** — sending a short burst of padding bitrate above the current estimate and watching the delay gradient. If the gradient stays flat, the capacity is real; climb. If it rises, retreat immediately.

## Design

### 1. Throughput step-up (simple, ships first)

Mirror the existing throughput step-down in `StreamLatencyStore.EvaluateQuality`:

```csharp
// Symmetric to ThroughputStepDownRatio: require sustained near-target delivery
// as evidence that the sender is actually producing at cfg bitrate (not capped
// by upload saturation) and every peer is absorbing it without buffering.
if (targetBps > 0
    && measuredBps >= targetBps * Constants.Video.ThroughputStepUpRatio
    && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
    _consecutiveGoodThroughputChecks++;
    if (_consecutiveGoodThroughputChecks >= Constants.Video.ThroughputStepUpConsecutiveChecks) {
        var allHealthy = peers.All(p => !p.Value.IsNetworkSlow && !p.Value.IsReceiverBound);
        if (allHealthy) {
            var stepped = VideoQualityPreset.StepUp(currentQuality);
            if (stepped != null && stepped.Level >= _maxQuality) {
                _consecutiveGoodThroughputChecks = 0;
                _lastQualityChangeAt = CpuTimestamp.Now;
                QualityPreset.Value = stepped;
                Log.LogInformation(
                    "EvaluateQuality: THROUGHPUT STEP UP {OldLevel} -> {NewLevel}, measured={MeasuredKbps:F0}kbps (≥{Ratio:F2}×{TargetKbps:F0}kbps)",
                    currentQuality, stepped.Level, measuredBps / 1000, Constants.Video.ThroughputStepUpRatio, targetBps / 1000.0);
            }
        }
    }
} else {
    _consecutiveGoodThroughputChecks = 0;
}
```

New constants (`Constants.Video.cs`):
- `ThroughputStepUpRatio = 0.9f` — need ≥90% of target delivered.
- `ThroughputStepUpConsecutiveChecks = 3` — 3 × 2s = 6s of sustained healthy delivery.

Works because after step-up the new target is higher and if the link can't sustain it, the existing step-down path (throughput low OR delta-latency high) will reverse the decision within 2 windows. The hysteresis window prevents oscillation.

### 2. Capacity probing (larger change, ships second)

Instead of just observing steady-state delivery, actively **probe** by commanding the sender to emit a brief overshoot. Two variants:

**Variant A — encoder-level probe.** Server sends a preset whose bitrate is `1.2 × current target` for 1 second. Sender reconfigures encoder, VBR may or may not honor. Server watches delay gradient. If flat, commit step-up; else revert.

**Variant B — padding-packet probe.** Sender appends N padding bytes to RPC frames for 1-2 seconds, raising observed bitrate without inflating the video bitstream. Simpler than re-encoding but requires wire-format change and the SFU passthrough would need to strip padding.

Preferred: **A** — no wire changes, matches the existing reconfigure path, and encoder bitrate is the right knob. Bad case: HW encoder is slow to react to reconfigure → probe looks worse than reality. Mitigate by gating probes to sessions where `pureMedianEncodeTime < 5ms` (encoder has headroom).

Trigger cadence: probe once every 30 s if on a sub-max level AND all peers have been healthy for 15 s.

### 3. Delay-gradient signal (to replace/augment absolute baseline-delta)

Today `IsNetworkSlow` fires when `median > baseline + 200ms AND > baseline × 1.3`. Better: compute the **derivative** of median over a rolling 4s window. Positive slope → congestion building. Less sensitive to baseline drift.

Implementation in `PeerLatencyState`:
- Store the previous MedianLatencyMs each sample.
- Compute delta per sample → average over N samples.
- `IsNetworkSlow` = `recentDeltaPerSample > 75ms` (150 ms/s effective).

Matches WebRTC GCC's delay-gradient logic adapted to our 2s interval.

## Critical files

| File | Change |
|------|--------|
| `src/dotnet/Api/Constants.Video.cs` | Add `ThroughputStepUpRatio`, `ThroughputStepUpConsecutiveChecks`, probe cadence, delay-gradient threshold |
| `src/dotnet/Streaming.Service/Backend/StreamLatencyStore.cs` | Symmetric step-up block in `EvaluateQuality`; delay-gradient fields on `PeerLatencyState` |
| `src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs` (if probing) | Probe scheduler — issue temporary `VideoQualityPreset` at `1.2×` target, revert on observed delay rise |

## Verification

1. **Cross-continent step-up**: two peers, sender in US, peers in EU/AU. Start at High-720p HEVC. Delta-from-baseline keeps baseline around 300-400 ms. Throughput sustained at 90%+ of target. Expect: step up to Full-1080p within 6 s of sustained good delivery.

2. **Congestion rollback**: simulate a network limiter at 2 Mbps on one peer. Step-up fires → target becomes 3.25 Mbps → measured throughput drops below 50% → throughput step-down fires within 4 s. Verify step-down reverses the aspirational step-up.

3. **Oscillation test**: low-margin link that just barely sustains 90%. Expect at most one step-up / step-down cycle per `QualityHysteresisWindow × 2` (~10 s). More frequent than that means constants need tuning.

4. **Probe correctness** (variant A): inject artificial encoder reconfigure overhead. Confirm probe gate (`pureMedianEncodeTime < 5ms`) refuses the probe when the encoder is struggling.

5. **Logs to watch**:
   - `THROUGHPUT STEP UP {OldLevel} -> {NewLevel}` — new.
   - `THROUGHPUT STEP DOWN` immediately after → oscillation.

## Out of scope

- Multi-peer layer selection — deferred to simulcast (see `docs/plans/video-simulcast.md`).
- Loss-based CC — we run over TCP/WebSocket, no explicit loss signal. Latency spike is our proxy for loss.
- FEC — not meaningful without UDP.

## Risks

- **VBR encoder ignoring the bitrate cap** (HEVC, seen on iOS): probe commits a higher target but actual bitrate stays below, triggering false throughput step-down. Now that `VideoBitrateTable` is codec-aware this is less likely, but still worth watching.
- **Hysteresis window too short** causes oscillation under borderline conditions. If observed in testing, raise `QualityHysteresisWindow` from 5 s to 10 s during probing sessions.
- **Server-side throughput measurement granularity** — 2 s windows miss sub-second bursts. Keep as-is for step-down/step-up decisions; use finer granularity only for the probe's observation window.
