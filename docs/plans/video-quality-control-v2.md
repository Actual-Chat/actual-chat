# Video quality control — v2

Rework of inbound and outbound quality control around four ideas:

1. **Bandwidth is shared per direction**, not per stream.
2. **Estimator is connection-epoch anchored** — we carry a "max observed
   throughput that worked" across reconnects and never invent numbers
   above it.
3. **Encoding pressure and bandwidth pressure are two independent caps** on
   the same outbound knob (number of spatial layers).
4. **Temporal layers become fractional** on inbound (percent of framerate),
   honoured against each stream's actual `TemporalLayerCount`.

The protocol on the wire (`ReceiveQuality`, `RecordingQuality`) does **not**
change — the fractional logic lives in the controller and is rounded into
counts at the wire boundary.

---

## Reuse

### Existing abstractions

Searched `docs/api-index.md`, `docs/api-index-full.md`, and the live-video
sources for fits before designing the new pieces:

- **`Moment`** (`ActualLab.Time`) — used as-is for `ConnectedAt`,
  history timestamps. No new clock abstraction.
- **`MutableState<T>`** (`ActualLab.Fusion`) — used as-is for
  `ConnectivityUI.ConnectionInfo`.
- **Existing QC nested types in `VideoQualityUI.cs`** —
  `CapacityEstimator`, `RecordingClassifier`, `RecordingAggregator`,
  `PlaybackVerdictClassifier`. These are being **replaced**, not
  extended: they're tightly coupled to the old per-stream AIMD shape
  and the new pieces (`BandwidthEstimator`, `EncodingCap`,
  `BandwidthCap`) replace them wholesale.
- **`AppKindExt.IsMobile`** (`ActualChat.Hosting`) — reused for the
  device-class spatial-layer cap; no new helper.
- **`BrowserInfo.IsMobile`** (`ActualChat.UI.Blazor.Services`) — reused
  for the device-class cap.
- **`VideoLayerDef`** (`ActualChat.Core.Media`) — reused for the
  per-codec layer counts.
- **`Constants.Video`** — extended with new knobs; no parallel
  constants class.
- Live-video docs / `frame-envelopes.ts` / `PlaybackStats` /
  `RecorderStats` — extended in-place rather than parallel-typed.

Not found in the indexes: any existing **bandwidth-tracking** /
**probe-with-backoff** abstraction. `TokenEstimator` is the only match
on "Estimator" and it's unrelated. So `BandwidthEstimator` is genuinely
new.

### Placement of new components

| New component | Placement | Why |
|---|---|---|
| `RpcConnectionInfo` (record) | **`ActualChat.Core` → `Core/Rpc/`** | Pure data record (`int` + `Moment`), produced by `ConnectivityUI` in UI.Blazor but consumed by `BandwidthEstimator` in Core. Generic enough to be useful for any future code that needs connection-epoch state. |
| `BandwidthEstimator` + `BandwidthEstimatorConfig` + `BandwidthEstimateRecord` + `BandwidthVerdict` | **`ActualChat.Core` → `Core/Bandwidth/`** | Direction-agnostic adaptive controller. The video sender, video receiver, and (hypothetically) audio QC can all instantiate it with different configs. Lives in `ActualChat.Bandwidth` namespace. No UI dependencies. |
| `EncodingCap`, `BandwidthCap` | **`ActualChat.UI.Blazor.App`** (alongside `VideoQualityUI.cs`) | Video-specific layer-walker state machines; meaningful only for the simulcast pipeline. Local placement is correct. |
| New `Allocator` shape | **`ActualChat.UI.Blazor.App`** (in `VideoQualityUI.cs`) | Tied to per-stream priority / render-area semantics — not reusable. |
| UI rework on `VideoDiagnosticsModal.razor` | **`ActualChat.UI.Blazor.App`** | Razor component, single use. |

The two genuinely reusable abstractions are in Core; the rest are
correctly feature-specific.

## What's broken today

| Symptom | Root cause |
|---|---|
| Diagnostics shows "Estimated capacity 12 Mbps" while actual per-stream allocations are 2 Kbps / 156 Kbps | `CapacityEstimator` climbs to `sumIncomingBytesPerSec × ClimbCap` on any sustained GOOD verdict, and there's no anchor — it drifts well above what any stream is actually consuming. |
| New joiner ⇒ quality blip for existing viewers | No connection-level memory: every QC restart cold-starts from `ColdStartCapacityBytesPerSec`. |
| Outbound camera and screencast fight for the same uplink without coordinating | Each `Recorder.QualityUI` runs an independent AIMD. Two struggling streams both back off, but neither knows the other exists. |
| Temporal layers are an opaque integer knob | `MaxTemporalLayerId` doesn't map cleanly to "I want half-framerate". The allocator currently treats it as 0/1/2, not as a rate target. |
| Mobile devices encode the full layer set even when the top layer is wasted bandwidth | The device-class cap on camera spatial layers is implicit / scattered. |
| Screencast viewers almost always see L0, not L1 | **Resolved.** Root cause: `GetBestLayerFor` used "nearest by absolute width delta" which biases toward the lower layer when the desired size lands between two ladder rungs. For screencast with rungs at W960/W1920, any desired size in [W1280, W1920) rounded DOWN to L0 (W960). A common screencast tile (~500–700 CSS px @ DPR 2) hits exactly that range. Fixed by switching to the standard ABR rule: "smallest layer ≥ desired, falling back to the largest". |

---

## Shared infrastructure

### ConnectivityUI — new `ConnectionInfo` property

`src/dotnet/UI.Blazor/Services/ConnectivityUI/ConnectivityUI.cs`:

```csharp
public sealed record RpcConnectionInfo(int Index, Moment ConnectedAt);

private readonly MutableState<RpcConnectionInfo?> _connectionInfo;
public IState<RpcConnectionInfo?> ConnectionInfo => _connectionInfo;
```

Wired in `PushIsConnectedToJS`, on each `connectionState.WhenNext` step:

- `false → true`: `_connectionInfo.Set(new RpcConnectionInfo(prevIndex + 1, Now))`.
- `true → false`: `_connectionInfo.Set(null)`.

`prevIndex` is the last non-null `_connectionInfo.Value.Index` (0 if there
hasn't been one yet). The first connect gives
`ConnectionInfo = { Index = 1, ConnectedAt = Now }`. Index is monotonic
across the lifetime of the process; `null` means "not currently
connected".

**Not propagated to JS.** This is server-side state only; the JS bridge
keeps its existing `setConnected(bool)` shape.

### `BandwidthEstimator` — one shared class, two instances

`src/dotnet/UI.Blazor.App/Services/VideoQualityUI/BandwidthEstimator.cs`
*(new file)*. One implementation, used twice: once by sender QC for the
upstream pipe, once by receiver QC for the downstream pipe. The two
contexts agree on a tiny pre-fused input shape — the caller is
responsible for distilling its direction-specific signals into one
number; the estimator stays direction-agnostic.

```csharp
public sealed record BandwidthEstimatorConfig(
    long   InitialCeilingBps,           // direction-specific, see below
    double BaseStep                = 0.20,
    double MaxStep                 = 0.50,
    double MaxStepUp               = 0.20,
    double AsymRatio               = 0.30,
    double NegStreakStep           = 0.15,
    double PosStreakStep           = 0.05,
    double GoodThreshold           = 0.95,
    double BadThreshold            = 0.85,
    double ConfirmRatio            = 0.85,
    double TauSec                  = 30,
    double MinAgeFactor            = 0.30,
    long   FloorBps                = 50_000,
    int    HistorySize             = 10,
    // Probe back-off — controls "explore the ceiling, but less often each retry"
    double BaseProbeCooldownSec    = 5,
    double ProbeCooldownGrowth     = 1.7,
    double MaxProbeCooldownSec     = 120,
    int    ProbeFailureResetStreak = 20)
{
    public Func<double, BandwidthVerdict>? Classify { get; init; }
}

public readonly record struct BandwidthEstimateRecord(
    Moment At,
    long   TriedBps,        // currentBandwidthBps the caller reported
    double SignalLevel,
    long   CeilingBefore,
    long   CeilingAfter,
    BandwidthVerdict Verdict);

public enum BandwidthVerdict { Bad, Neutral, Good }

public sealed class BandwidthEstimator(BandwidthEstimatorConfig config)
{
    public int     EpochIndex          { get; private set; }
    public long    CeilingBps          { get; private set; } // best estimate of the cap
    public long    LastCurrentBps      { get; private set; } // for diagnostics
    public double  LastSignalLevel     { get; private set; } // for diagnostics
    public int     NegativeStreak      { get; private set; }
    public int     PositiveStreak      { get; private set; }
    public int     CalmTicks           { get; private set; } // non-Bad ticks in a row
    public int     ProbeFailures       { get; private set; } // consecutive "tried up, got bad"
    public Moment? LastCeilingDownAt   { get; private set; }

    // Ring buffer of the last config.HistorySize Tick outcomes.
    // Surfaced in the diagnostics UI (Quality Control section).
    public IReadOnlyCollection<BandwidthEstimateRecord> History { get; }

    /// <param name="currentBandwidthBps">
    /// Bytes/s the caller actually used (or attempted to use) in the
    /// window ending now. NOT a target — just an observation.
    /// </param>
    /// <param name="signalLevel">
    /// Caller-computed health number in [0, 1].
    ///   1.0  = perfectly healthy
    ///   0.5  = "I suspect the real bandwidth is ~half of currentBandwidthBps"
    ///   0.0  = catastrophically bad (nothing is getting through)
    /// </param>
    public void Tick(
        RpcConnectionInfo? connection,
        Moment now,
        long currentBandwidthBps,
        double signalLevel);
}
```

The estimator is pluggable in two places, both per-direction:

1. **`Classify`** — the policy that turns a `signalLevel` into a
   `BandwidthVerdict`. The default classifier uses the
   `GoodThreshold` / `BadThreshold` knobs:
   ```csharp
   v => v < config.BadThreshold  ? BandwidthVerdict.Bad
      : v >= config.GoodThreshold ? BandwidthVerdict.Good
      :                             BandwidthVerdict.Neutral
   ```
   A caller can override this with a stateful classifier (e.g.
   hysteresis, dwell time, debouncing) without touching the estimator
   itself.

2. **The full config record** — all magnitude/streak/decay knobs are
   per-instance. Sender and receiver can have different tunings.

#### Mental model

- The estimator tracks **one number**: `CeilingBps` — its best guess at
  the actual available bandwidth on the current connection.
- Each new connection epoch resets `CeilingBps` to
  `config.InitialCeilingBps` — a deliberately optimistic per-direction
  default that's roughly enough to run baseline streams. We never start
  at `Floor`; the goal is for the first connect (or reconnect) not to
  cause a visible quality blip.
- `currentBandwidthBps` is informational — the caller's observed usage in
  the window. It is *not* a target the estimator stores; we just use it
  as a reference point to lift or lower the ceiling.
- `signalLevel` collapses everything else (drop ratio, ACK staleness,
  decoder backpressure, buffer health) into one continuous health
  number. Caller-specific (see "Computing signalLevel" below).
- A short ring-buffer `History` of recent `Tick` outcomes is kept on
  the estimator for the diagnostics UI to surface — exactly the
  "I tried X, signal was Y, ceiling became Z" loop the user described.

Update rules in plain English:

- **Healthy and pushing the ceiling.** When signal is good and the
  caller is actually using close to (or above) the current ceiling, the
  ceiling rises — we just demonstrated more headroom exists.
- **Healthy with lots of headroom.** When signal is good but the caller
  is well below the ceiling, hold — there's no new evidence either way.
- **Bad.** Lower the ceiling toward `currentBandwidthBps × signalLevel`
  (the caller's signalLevel implicitly says "the real cap is ~this
  fraction of what I just used"). Magnitude scaled by streak / age.

The ceiling is **not** a strict high-water — within an epoch it goes up
on demonstrated good throughput and down on bad signal. Within-epoch
transient throughput dips (e.g. a stream leaving) under a healthy signal
hold the ceiling.

#### Per-tick algorithm

```
0.  If connection is null:
        // Not currently connected — freeze state, do nothing.
        return

1.  If connection.Index != EpochIndex:
        // New epoch — seed CeilingBps to a generous per-direction default
        // (config.InitialCeilingBps). On the first real Tick the
        // signal will refine it.
        CeilingBps          = config.InitialCeilingBps
        LastCurrentBps      = 0
        NegativeStreak      = 0
        PositiveStreak      = 0
        ProbeFailures       = 0
        LastCeilingDownAt   = null
        EpochIndex          = connection.Index
        History.Clear()
        // Fall through into the regular path.

2.  Compute per-tick adjustment scale base:
        ageSec    = (now - connection.ConnectedAt).TotalSeconds
        ageFactor = clamp(1.0 - log10(1 + ageSec / config.TauSec),
                          config.MinAgeFactor, 1.0)

3.  Ask the pluggable classifier (defaults to threshold-based):
        verdict = config.Classify(signalLevel)
                  ?? defaultClassify(signalLevel, config)
        // verdict ∈ { Bad, Neutral, Good }

4.  Snapshot ceiling-before for History:
        ceilingBefore = CeilingBps
        LastCurrentBps  = currentBandwidthBps
        LastSignalLevel = signalLevel

5a. If verdict == Bad:
        suggestedCap = max(config.FloorBps, round(currentBandwidthBps * signalLevel))
        // Drift CeilingBps toward suggestedCap by α.
        // (signalLevel low ⇒ suggestedCap far below current ⇒ big move.)
        α = clamp(config.BaseStep
                  * ageFactor
                  * (1.0 - signalLevel)        // severity
                  * (1.0 + NegativeStreak * config.NegStreakStep),
                  0, config.MaxStep)
        var newCeiling = max(config.FloorBps,
                             round(CeilingBps + α * (suggestedCap - CeilingBps)))
        if newCeiling < CeilingBps:
            // We just lowered the ceiling. If this comes on the heels
            // of a recent probe-up, count it as a probe failure so the
            // next probe waits longer.
            if PositiveStreak > 0 OR (LastCeilingDownAt == null AND PositiveStreak == 0 AND ProbeFailures == 0):
                ProbeFailures++         // bump backoff
            LastCeilingDownAt = now
        CeilingBps = newCeiling
        NegativeStreak++; PositiveStreak = 0
        return

5b. If verdict == Good AND currentBandwidthBps >= CeilingBps * config.ConfirmRatio:
        // We're actively pushing the ceiling and it's holding — but
        // only lift if we're past the probe-cooldown for this epoch.
        cooldownSec = min(config.MaxProbeCooldownSec,
                          config.BaseProbeCooldownSec
                          * pow(config.ProbeCooldownGrowth, ProbeFailures))
        if LastCeilingDownAt != null
           AND (now - LastCeilingDownAt).TotalSeconds < cooldownSec:
            // In cooldown — decay streaks, hold ceiling, return.
            PositiveStreak = max(0, PositiveStreak - 1)
            return

        α = clamp(config.BaseStep
                  * ageFactor
                  * config.AsymRatio
                  * (1.0 + PositiveStreak * config.PosStreakStep),
                  0, config.MaxStepUp)
        var lift = max(currentBandwidthBps, round(CeilingBps * (1.0 + α)))
        CeilingBps = lift
        PositiveStreak++; NegativeStreak = 0
        return

5c. Otherwise (Neutral, OR Good with plenty of headroom):
        // No new information; decay streaks; hold ceiling.
        NegativeStreak = max(0, NegativeStreak - 1)
        PositiveStreak = max(0, PositiveStreak - 1)
        return

6.  Update CalmTicks and apply backoff reset:
        if verdict == Bad: CalmTicks = 0
        else:              CalmTicks++
        if CalmTicks >= config.ProbeFailureResetStreak:
            ProbeFailures    = 0
            LastCeilingDownAt = null     // also clears the cooldown anchor

7.  At end of every tick:
        History.Append(new BandwidthEstimateRecord(
            now, currentBandwidthBps, signalLevel,
            ceilingBefore, CeilingBps, verdict));
        // Bounded ring buffer — drops the oldest when len > config.HistorySize.
```

#### Probe back-off — explore the ceiling, less often each retry

Without back-off the algorithm naturally cycles at the bandwidth limit:
lift the ceiling → cap walker bumps a layer → bytes climb → signal goes
bad → ceiling drops → repeat. We *want* those cycles to exist (they're
how we re-discover the cap after it changes), but we don't want them to
run continuously at high frequency.

`ProbeFailures` counts how many lift→drop cycles we've seen recently. The
cooldown between lifts is
`min(MaxProbeCooldownSec, BaseProbeCooldownSec × ProbeCooldownGrowth^ProbeFailures)`.
With the defaults that's roughly:

| Failure # | Cooldown |
|---|---|
| 0 | 5 s |
| 1 | 8.5 s |
| 2 | 14 s |
| 3 | 24 s |
| 4 | 41 s |
| 5 | 70 s |
| 6+ | 120 s (capped) |

A sustained calm period — `CalmTicks ≥ ProbeFailureResetStreak`
non-Bad ticks in a row, ≈ 20 s at 1 Hz — resets `ProbeFailures` to 0
and clears `LastCeilingDownAt`, so when the network *does* genuinely
improve, we resume aggressive probing immediately rather than dragging
the 2-minute cooldown forever. `CalmTicks` is reset to 0 on any Bad
verdict.

#### Computing `signalLevel`

The caller maps its direction-specific raw signals into a single
`signalLevel ∈ [0, 1]`. Each side has one **primary** signal that
quantifies the bandwidth shortfall directly, plus secondary signals
that contribute only when worse than the primary.

**Sender (outbound) — primary: wire-send drop ratio.**

Wire-send drops are the cleanest evidence we're over-budget. They map
straight to "we're producing more bytes than the link can ship", which
is exactly what the ceiling should reflect.

Each penalty is a linear ramp from an `Ok` threshold (penalty = 0) to
a `Bad` threshold (penalty = 1):

```
// Primary
dropPenalty = clamp((SenderFrameDropRatioEma - DropOkSender) / (DropBadSender - DropOkSender), 0, 1)

// Secondary (only contribute when worse than primary)
ackPenalty  = LastAckAgeMs < 0
              ? 0
              : clamp((LastAckAgeMs - AckOkMs) / (AckBadMs - AckOkMs), 0, 1)
encPenalty  = clamp((EncodeRatioEma - EncOkRatio) / (EncBadRatio - EncOkRatio), 0, 1)
              // NOTE: encPenalty stays in the formula as a fallback, but the
              // encoding-pressure cap (EncodingCap) is the *primary* response
              // to encode overrun — bandwidth shouldn't double-count it.

signalLevel = 1.0 - max(dropPenalty, ackPenalty, encPenalty)
```

Concrete thresholds (`Constants.Video`): `DropOkSender = 0.20` (20%
wire-send drops still ride free), `DropBadSender = 0.50`,
`AckOkMs = 500`, `AckBadMs = 2000`, `EncOkRatio = 1.0`,
`EncBadRatio = 2.0`.

**Receiver (inbound) — primary: playback rate.**

We can't ACK on the receive leg. Instead we measure how much source
time we're actually draining at the present stage per unit of wall
time. Every successful `present` (in `present-mstg` and
`present-canvas`) samples the source-clock progress against
wall-clock:

```
offsetDelta(t)        = frame.offset.timeMs(t) - frame.offset.timeMs(prevPresented)
                        // 0 if no new frame has been presented since last sample
wallDelta(t)          = wallNow(t) - wallNow(prevSample)
playbackRate(t)       = min(1, offsetDelta / wallDelta)
                        // ∈ [0, 1]; 1 = on time; 0.8 = falling 200 ms/s behind
playbackRateEma       = EMA over 1-Hz samples (α = 0.3, ~3 s half-life)
```

`min(1, …)` floors catching-up at 1 — working off backlog doesn't earn
credit. Reset on `frame.offset.epoch` change (anchor reset is not
drift). Initial value is `1` (perfect). Lives in `PlayerStats`
alongside `presented` / `bytesReceived`, byte-weighted across active
streams the same way throughput is.

Each receiver-side penalty is a linear ramp from `Ok` (penalty = 0)
to `Bad` (penalty = 1):

```
// Primary — gap between actual playback rate and the "perfect" floor
driftPenalty = clamp((PlaybackRateOk − playbackRateEma) / (PlaybackRateOk − PlaybackRateBad), 0, 1)

// Secondary — receiver-side drops only (FrameDropStage bytes 61–90).
//   Sender + server drops in the trace are intentional pacing and don't
//   reflect downstream-bandwidth health.
dropPenalty  = clamp((aggregateReceiveDropRatio − DropOkReceiver) / (DropBadReceiver − DropOkReceiver), 0, 1)

signalLevel  = 1.0 - max(driftPenalty, dropPenalty)
```

Concrete thresholds (`Constants.Video`): `PlaybackRateOk = 0.90`
(so `playbackRateEma ≥ 0.9` ⇒ 0 penalty — absorbs scheduler jitter),
`PlaybackRateBad = 0.00` (penalty = 1 only at full stall),
`DropOkReceiver = 0.20`, `DropBadReceiver = 0.50`.

The shape — "what fraction of bandwidth do I trust right now?" — is the
same on both sides. The thresholds aren't.

**Implementation notes for the playback-rate signal:**

- `DecodedFrame` already carries `capturedAt: { timeMs, epoch }` (see
  `frame-envelopes.ts`). Per-stream `playbackRateEma` field on
  `PlayerStats` is updated in the `present` operators after every
  successful present, anchored by `capturedAt.epoch`.
- Update rule per present, sampled at fixed 1 s wall-clock intervals:
  ```
  if presentedOffset.epoch != driftAnchorEpoch:
      driftAnchorEpoch         = presentedOffset.epoch
      driftLastSampleOffsetMs  = presentedOffset.timeMs
      driftLastSampleWallMs    = wallNow
      playbackRateEma          = 1
      return
  wallDelta = wallNow - driftLastSampleWallMs
  if wallDelta < DRIFT_SAMPLE_INTERVAL_MS: return        // 1 s
  offsetDelta = max(0, presentedOffset.timeMs - driftLastSampleOffsetMs)
  playbackRate = min(1, offsetDelta / wallDelta)
  playbackRateEma = (1 - α) * playbackRateEma + α * playbackRate    // α = 0.3
  driftLastSampleOffsetMs = presentedOffset.timeMs
  driftLastSampleWallMs   = wallNow
  ```
  At 1 Hz, `α = 0.3` gives a half-life of ~2 s.
- Aggregation across streams for the inbound signal: byte-weighted mean
  of per-stream `playbackRateEma`.

#### Knobs

Defaults live on `BandwidthEstimatorConfig` (see record definition
above). Direction-specific overrides are passed per instance — e.g. the
receiver may want a more conservative `BadThreshold` than the sender, or
a different `BaseStep`. No global static knob table — that was the
source of the per-direction confusion in the old design.

**Concrete `InitialCeilingBps` defaults** — picked to be generous
enough that the first connect/reconnect doesn't visibly downgrade
quality. The estimator pulls these down quickly on the first bad
signal, so erring high is safe.

| Direction | Rationale | Default |
|---|---|---|
| Outbound | Enough for camera + screencast on mobile (2-layer camera + full screencast) | **3 Mbps** (`3_000_000 / 8 = 375 kB/s` → `3_000_000` if we keep units in bits, or `375_000` in bytes — match the unit the codebase uses; see note below) |
| Inbound | Enough for several L1 webcams + one screencast | **8 Mbps** |

> **Unit note.** The existing controller measures
> `IncomingBytesPerSec` in *bytes*/s. The estimator's public type is
> `long` representing the same unit. The "Mbps" numbers above are
> human-readable shorthand; the actual constants are in B/s
> (`InitialOutboundBps = 375_000`, `InitialInboundBps = 1_000_000`).

#### Invariants

- `CeilingBps` is always defined post-construction:
  initially `InitialCeilingBps`; clamped to `>= FloorBps` after every
  tick.
- Within an epoch, no carry across reconnects — each new
  `connection.Index` resets `CeilingBps` back to `InitialCeilingBps`
  and clears `History`.
- `currentBandwidthBps` is not stored as a target; the estimator only
  uses it transiently within `Tick`.
- `History` contains at most `config.HistorySize` records, newest last.

#### What this fixes

- **The "12 Mbps phantom":** the estimator no longer tracks an
  unbounded climbing number. Ceiling rises only on *demonstrated*
  throughput at-or-above current level and gentle scaling; it can't
  decouple from reality.
- **Connection epochs are clean:** disconnect/reconnect resets the
  ceiling to unknown. The caller starts probing from whatever it likes
  (typically its prior `lastTarget`, or a profile default).
- **One concept of "how bad is it":** drop ratio, ACK staleness, decoder
  backpressure — every fused into a single `signalLevel`. No
  duplicated thresholds across the algorithm.
- **Asymmetric and severity-aware:** a tick with `signalLevel = 0.3`
  drops the ceiling much harder than a tick with `signalLevel = 0.8`,
  even before streak amplification.

#### Carry across epochs — *not* the estimator's job

We deliberately don't carry `CeilingBps` across reconnects. The caller
*may* remember its `lastTarget` and resume there on a new epoch (the
estimator will quickly raise `CeilingBps` if the network supports it,
or quickly lower it if not). This keeps the estimator simple and
auditable.

---

## Outbound rework

Two caps, both expressed as **per-stream spatial-layer counts**, combined
with `min` to produce the actual encoder target.

### Cap 1 — Encoding pressure (sender-only)

Independent of bandwidth. Driven by cumulative `EncodeRatioEma`.

```
encCap.cameraLayers       = deviceCameraCap            // initial
encCap.screencastLayers   = screencastCap              // initial

every tick:
    if EncodeRatioEma > EncodeRatioBad for >= EncBadStreak ticks:
        if encCap.cameraLayers > 1:
            encCap.cameraLayers--
        else if encCap.screencastLayers > 1:
            encCap.screencastLayers--
        reset EncBadStreak counter
    else if EncodeRatioEma < EncodeRatioGood for >= EncGoodStreak ticks:
        if encCap.screencastLayers < screencastCap:
            encCap.screencastLayers++
        else if encCap.cameraLayers < deviceCameraCap:
            encCap.cameraLayers++
        reset EncGoodStreak counter
```

Knobs:
```
EncodeRatioBad     = 2.0   // "encoder running at half framerate"
EncodeRatioGood    = 1.2
EncBadStreak       = 2     // ~2s of sustained bad
EncGoodStreak      = 5     // ~5s of sustained good before climbing back
```

### Cap 2 — Bandwidth pressure (sender-only)

Driven by `BandwidthEstimator` over outbound `(currentBandwidthBps,
signalLevel)`:

- `currentBandwidthBps = bytesEncodedPerSecAcrossOwnStreams`
- `signalLevel` from the fused outbound formula (see "Computing
  signalLevel" — wire-send `SenderFrameDropRatioEma` is the primary
  signal; ACK age and encode ratio are secondary).

The estimator yields `CeilingBps`, but we don't use the *number*
directly — we use its `NegativeStreak` / `PositiveStreak` to walk a
discrete cap, in the same order as encoding pressure:

```
bwCap.cameraLayers       = deviceCameraCap            // initial
bwCap.screencastLayers   = screencastCap              // initial

every tick:
    if NegativeStreak >= BwBadStreak:
        if bwCap.cameraLayers > 1:
            bwCap.cameraLayers--
        else if bwCap.screencastLayers > 1:
            bwCap.screencastLayers--
        consume one bad event (reset counter to BwBadStreak/2)

    else if PositiveStreak >= BwGoodStreak
            AND ObservedThroughputBps >= CurrentEstimateBps * ConfirmRatio:
        if bwCap.screencastLayers < screencastCap:
            bwCap.screencastLayers++
        else if bwCap.cameraLayers < deviceCameraCap:
            bwCap.cameraLayers++
        consume one good event
```

Knobs:
```
BwBadStreak        = 2
BwGoodStreak       = 5
```

### Device-class spatial-layer caps

```csharp
var isMobile = BrowserInfo.IsMobile || HostInfo.AppKind.IsMobile();
deviceCameraCap = isMobile ? 2 : VideoLayerDef.MaxCameraLayers;
screencastCap   = VideoLayerDef.MaxScreencastLayers;
```

We OR two signals:

- `BrowserInfo.IsMobile` — JS-detected, set from `DeviceInfo.isMobile`
  in `browser-info.ts`. Correctly identifies a mobile browser on mobile
  hardware regardless of how the app shell is packaged.
- `HostInfo.AppKind.IsMobile()` — extension on `AppKind` in
  `AppKindExt.cs`, returns true exactly for `Android` / `Ios`. This is
  the fallback for Maui apps where the JS-side detection might not
  apply.

(`MaxCameraLayers` / `MaxScreencastLayers` come from the existing
`VideoLayerDef` table; mobile drops layer 2.)

### Combination

```
effCap.cameraLayers     = min(encCap.cameraLayers,     bwCap.cameraLayers,     deviceCameraCap)
effCap.screencastLayers = min(encCap.screencastLayers, bwCap.screencastLayers, screencastCap)
```

### Priority allocation (screencast first)

```
allocate(effCap, ownStreams):
    if ScreenCast in ownStreams:
        targetLayers[ScreenCast] = effCap.screencastLayers
    if Camera in ownStreams:
        targetLayers[Camera] = effCap.cameraLayers
```

Screencast going first matters for one thing only: when *both* caps are
hard-constrained, the order in which they were lowered (camera first)
already gave screencast its full share. Allocation here is "publish the
targets" — no fighting for budget.

For each stream whose `targetLayers` changed since the previous tick:

```csharp
recorder[streamId].SetTargetLayerCount(targetLayers[streamId]);
```

(Existing call. The encoder pool warm path is unchanged.)

### Per-tick outbound algorithm (full)

```
For each session, every Tick (subject to existing IsEvaluationDue):

  // 1. Distill signals into one number (wire-send drops are primary)
  dropPenalty = clamp((SenderFrameDropRatioEma - DropOkSender)
                      / (DropBadSender - DropOkSender), 0, 1)
  ackPenalty  = LastAckAgeMs < 0
                ? 0
                : clamp((LastAckAgeMs - AckOkMs) / (AckBadMs - AckOkMs), 0, 1)
  encPenalty  = clamp((EncodeRatioEma - EncOkRatio)
                      / (EncBadRatio - EncOkRatio), 0, 1)
  signalLevel = 1.0 - max(dropPenalty, ackPenalty, encPenalty)

  // 2. Update estimator + caps
  bwEstimator.Tick(
      ConnectivityUI.ConnectionInfo.Value,
      Now,
      currentBandwidthBps: bytesEncodedPerSecAcrossOwnStreams,
      signalLevel)

  encodingCap.Tick(EncodeRatioEma)        // updates encCap.{camera,screencast}Layers
  bwCap.Tick(bwEstimator.Snapshot)        // updates bwCap.{camera,screencast}Layers

  effCap = min(encCap, bwCap, deviceCap)

  // 3. Publish targets
  for kind in ownStreams:
      target = effCap[kind]
      if target != lastTarget[kind]:
          recorder[kind].SetTargetLayerCount(target)
          lastTarget[kind] = target

  // 4. Wire-level send-quality reporting (unchanged)
  ChangeRecordingQuality(state, RecordingQualityInfo { ... })
```

### What this fixes

- Camera and screencast caps come from one place, walked in a single
  order — no more two AIMDs fighting each other.
- The encoding cap is **independent** of bandwidth — if the CPU can't
  encode, we cut layers regardless of network. If the network is bad, we
  cut layers regardless of CPU. Both are layer-shaped, so the
  combination is just `min`.
- Mobile clients can't blow their pixel budget on layer 2 they'll never
  send.

---

## Inbound rework

### Per-stream capability discovery

`PlaybackStats` (Api.Contracts/Streaming/Quality/PlaybackQuality.cs)
already exposes per-stream info. Add:

```csharp
public int    AvailableTemporalLayerCount { get; init; } = 1;
public double PlaybackRateEma             { get; init; } = 1;
```

- `AvailableTemporalLayerCount` — populated from the latest
  `VideoFrame.TemporalLayerCount`. The per-stream cap on the temporal
  fraction the allocator can request.
- `PlaybackRateEma` — the per-stream playback-rate signal described
  under "Computing signalLevel". Source on the TS side:
  `PlayerStats.playbackRateEma: number` on `frame-envelopes.ts`,
  updated in the `present-mstg` / `present-canvas` operators after
  every successful present, sampled at 1 Hz with α = 0.3.

### Fractional temporal target

Replace the per-stream discrete `MaxTemporalLayerId` knob with a
fractional **temporal fraction** in `[temporalFloor(s), 1.0]`, where:

| `producerTemporalLayerCount` | `temporalFloor` |
|---|---|
| 1 | 1.00 |
| 2 | 0.50 |
| 3 | 0.25 |

Round-trip to the wire at the boundary:

```csharp
int temporalLayerCount = Math.Clamp(
    (int)Math.Ceiling(temporalFraction * producerTemporalLayerCount),
    1,
    producerTemporalLayerCount);
```

(So fraction `0.5` on a 2-temporal-layer stream sends `TemporalLayerCount = 1`
which the server interprets as "every other frame".)

### Capacity — drop `CapacityEstimator`, use `BandwidthEstimator`

Inbound feed for `signalLevel` (see "Computing signalLevel" above for the
primary playback-rate signal and the secondary drop signal; the
estimator itself takes only `(currentBandwidthBps, signalLevel)`):

- `currentBandwidthBps = sumIncomingBytesPerSec` across all active
  streams.
- `signalLevel` = `1.0 - max(driftPenalty, dropPenalty)`
  where `driftPenalty` derives from the byte-weighted aggregate of
  per-stream `playbackRateEma`.

Output `CeilingBps` is the **total downstream budget**.

### Priority allocator (replaces single-pass allocator)

Per tick:

```
1.  budget = bwEstimator.CeilingBps

2.  Classify streams by priority:
        primary    = the single primary stream (if any)
        secondary  = all others

3.  Compute secondary floor:
        floorBudget = Σ over secondary streams:
            predictedRate(s, layers=1, fraction=temporalFloor(s))

    (Lowest sensible config — bottom spatial layer at the lowest
    temporal fraction the stream supports.)

4.  primaryBudget = max(0, budget - floorBudget)

5.  Allocate primary:
        Among (spatialLayerCount, temporalFraction) candidates whose
        predicted rate ≤ primaryBudget:
            pick the one that maximises (spatialLayerCount,
            temporalFraction) in lexicographic order.
        (Tie-break: prefer higher spatial first, then higher temporal.)

6.  remaining = budget - actualPrimaryRate

7.  Distribute remaining across secondaries proportional to render-size
    area (renderCssLongSide² × dpr²):
        for s in secondary:
            share[s] = remaining * area[s] / Σ area[*]

    Then for each secondary independently:
        pick (layers, fraction) whose predictedRate ≤ share[s]
        but never below (layers=1, fraction=temporalFloor(s)).

8.  Honour producer caps:
        if producerTemporalLayerCount[s] == 1:
            fraction[s] = 1.0

9.  Translate (layers, fraction) → ReceiveQuality(layerCount,
    temporalLayerCount) per stream and ship via ChangePlaybackQuality.
```

`predictedRate(s, layers, fraction)` keeps the existing `PredictedRatesByLayer`
prefix-sums, but multiplied by `fraction` for the temporal scaling.

### Cadence

Unchanged: `QcStartupCooldown = 5 s`; `QcSettlingInterval = 3 s` until
stream age 10 s; `QcSteadyInterval = 5 s` thereafter. `IsEvaluationDue`
still force-fires on viewport changes.

### Per-tick inbound algorithm (full)

```
For each session, every Tick (subject to existing IsEvaluationDue):

  // 1. Distill signals into one number
  playbackRateEma   = byte-weighted aggregate of per-stream playbackRateEma
  receiverDropRatio = Σ receiver-stage drops / (Σ receiver-stage drops + Σ presented)
  driftPenalty = clamp((PlaybackRateOk - playbackRateEma)
                       / (PlaybackRateOk - PlaybackRateBad), 0, 1)
  dropPenalty  = clamp((receiverDropRatio - DropOkReceiver)
                       / (DropBadReceiver - DropOkReceiver), 0, 1)
  signalLevel  = 1.0 - max(driftPenalty, dropPenalty)

  // 2. Update estimator
  bwEstimator.Tick(
      ConnectivityUI.ConnectionInfo.Value,
      Now,
      currentBandwidthBps: sumIncomingBytesPerSec,
      signalLevel)

  // 3. Allocator (CeilingBps is always defined — seeded to
  //    config.InitialCeilingBps on epoch reset)
  budget     = bwEstimator.CeilingBps
  primary    = streams.first(s => s.Priority == Primary)
  secondary  = streams.where(s => s != primary)

  floorBudget    = sum_secondary predictedRate(s, 1, temporalFloor(s))
  primaryBudget  = max(0, budget - floorBudget)

  primaryAlloc   = pickBestFit(primary, primaryBudget)
  remaining      = budget - primaryAlloc.rate

  for s in secondary, sorted by area desc:
      share = remaining * area(s) / Σ area(*)
      s.alloc = pickBestFit(s, share, lowerBound = (1, temporalFloor(s)))

  // 5. Wire-translate and ship
  qualityByStream = build ReceiveQuality dict via (layers, fraction) → (count, count)
  ChangePlaybackQuality(session, qualityByStream, info)
```

### What this fixes

- Inbound capacity is now grounded in actual throughput plus
  cross-connection memory — the "12 Mbps phantom" goes away.
- Temporal layers are first-class fractions, so requesting "half
  framerate" doesn't depend on the consumer knowing the producer's
  layer count internally.
- Secondary streams are guaranteed at least their floor, even when the
  primary is greedy — the floor is reserved before primary allocation.
- The remaining-budget split is **proportional to displayed area**, so a
  tiny avatar tile doesn't get the same chunk as a fullscreen viewer.

---

## Diagnostics UI — unify into the video diagnostics modal

The current model has the video diagnostics modal showing inbound and
outbound stream details, and a *separate* settings modal for QC knobs.
Both move into one modal with two tabs.

### Tab layout

The modal header gets a two-button tab strip: **Inbound** | **Outbound**.
Each tab is self-contained — no shared body below it. Layout per tab:

```
┌─ Inbound ─┐ Outbound

  Streams
  ───────
  (one .diag-stream per inbound stream — same as today,
   minus the per-stream "Quality Control" sub-section
   which is moving down)

  Quality Control
  ───────────────
  Current ceiling: 4.12 Mbps         Signal: 0.96 (Good)
  Probe failures: 1                  Cooldown remaining: 4.3 s
  Connection: epoch #3, connected for 2m 17s

  Recent updates (latest first)
  ─────────────────────────────────────────────────────────
  +0.0s  tried 3.81 Mbps  signal 0.96  ceiling 3.91 → 4.12  Good
  -1.0s  tried 3.74 Mbps  signal 0.97  ceiling 3.85 → 3.91  Good
  -2.0s  tried 3.62 Mbps  signal 0.89  ceiling 3.92 → 3.85  Neutral
  -3.0s  tried 3.91 Mbps  signal 0.61  ceiling 4.20 → 3.92  Bad
  ...

  Knobs
  ─────
  [BaseStep                ] 0.20   [reset]
  [BadThreshold            ] 0.85   [reset]
  [GoodThreshold           ] 0.95   [reset]
  ... (one row per BandwidthEstimatorConfig field)
```

The **Outbound** tab mirrors this: own streams (camera, screencast),
then outbound QC (with its own estimator's ceiling/signal/history),
then outbound knobs.

### Where the QC section comes from

Each tab's QC section reads from the direction-appropriate
`BandwidthEstimator` instance:

- `Ceiling: bwEstimator.CeilingBps`
- `Signal: bwEstimator.LastSignalLevel` plus its verdict
- `Probe failures + cooldown`: `bwEstimator.ProbeFailures` and
  `BaseProbeCooldownSec × ProbeCooldownGrowth^ProbeFailures` minus
  `(Now - bwEstimator.LastCeilingDownAt)`
- `Connection`: `ConnectivityUI.ConnectionInfo`
- `Recent updates`: `bwEstimator.History`, formatted

These all live on the C# side already (in `VideoQualityUI`) — surface
them through the existing `ComputedModel` in
`VideoDiagnosticsModal.razor`.

### Knobs section (replaces the separate settings modal)

Whichever fields of `BandwidthEstimatorConfig` (per direction) are
intended to be user-tunable get rendered as numeric inputs. Editing a
field rebuilds the per-direction `BandwidthEstimator` with the new
config (or, if we make config a mutable record, just swaps it in —
cleaner since `History` and current ceiling shouldn't be discarded on
each knob tweak).

The old separate "Settings" modal trigger goes away — there's nowhere
left it can show that isn't already in the diagnostics modal.

### What's *removed* from streams

The per-stream `Quality Control` sub-section currently rendered inside
each inbound stream tile goes away — that information moves to the
**Inbound → Quality Control** section at the bottom of that tab.
Per-stream values that are *not* about the session-wide allocator
(buffer span, drop rate, etc.) stay in the stream tile.

### Source files

| File | Change |
|---|---|
| `VideoDiagnosticsModal.razor` | Add `_activeTab` field; render tab strip; conditionally render inbound vs outbound content. Wire `_ownDiagByKind` to its tab only. |
| `video-diagnostics-modal.css` | Add `.diag-tabs`, `.diag-tab`, `.diag-tab--active`. Add `.diag-qc-section`, `.diag-qc-history`, `.diag-qc-knobs`. |
| (separate settings modal, wherever it currently lives) | Deleted; its launcher button removed from the QC modal header. |

### .NET

| File | Change |
|---|---|
| `Core/Rpc/RpcConnectionInfo.cs` *(new)* | Pure data record `(int Index, Moment ConnectedAt)`. |
| `Core/Bandwidth/BandwidthEstimator.cs` *(new)* | Shared adaptive controller (Core, namespace `ActualChat.Bandwidth`). Sender + receiver instantiate with different configs. Used by future audio QC too. |
| `ConnectivityUI.cs` | Add `ConnectionInfo: IState<RpcConnectionInfo?>`; update on connect transitions. |
| `VideoQualityUI.cs` | Replace `CapacityEstimator` + `Allocator` with `BandwidthEstimator` + priority allocator; drop `RecordingClassifier`/`RecordingAggregator` in favour of `EncodingCap` + `BwCap`. |
| `VideoQualityUI/EncodingCap.cs` *(new, UI.Blazor.App)* | Video-specific sender encoding-pressure state machine. |
| `VideoQualityUI/BandwidthCap.cs` *(new, UI.Blazor.App)* | Video-specific layer-cap walker driven off `BandwidthEstimator`. |
| `VideoQualityUI/Allocator.cs` | Reshaped: floor reserve → primary best-fit → area-proportional secondary distribution. |
| `Api.Contracts/Streaming/.../PlaybackQuality.cs` | Add `AvailableTemporalLayerCount` to per-stream sub-record. |
| `Constants.Video.cs` | All knobs above. |
| `ReceiveQualityFilter.cs` | No change. |
| Wire types (`ReceiveQuality`, `RecordingQuality`) | No change. |

### TS

- `frame-envelopes.ts` — extend `PlayerStats` with `playbackRateEma:
  number` (default `1`). Update the `present-mstg` / `present-canvas`
  operators to call `updatePlaybackRateEma(stats, presentedOffset,
  wallNow)` after every successful present per the rule in "Computing
  signalLevel". Reset on `frame.offset.epoch` change.
- `video-player.ts` — pass `stats.playbackRateEma` in the JSInvokable
  payload to Blazor (same channel as the other pre-computed rates).
- No structural changes beyond those two adds.

### Tests

- `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/` — add:
  - `BandwidthEstimatorTest.cs` — drives the estimator with synthetic
    `(currentBps, signalLevel)` sequences against a manually-stepped
    clock. Required cases:
    - Fresh epoch: `CeilingBps == config.InitialCeilingBps`,
      `History` empty, streaks/probe state zero.
    - Epoch transition: `ConnectionInfo.Index` change resets
      `CeilingBps` back to `InitialCeilingBps` and clears `History`,
      `ProbeFailures`, `LastCeilingDownAt`.
    - History ring buffer: after `> HistorySize` ticks, only the most
      recent `HistorySize` records are present, newest last.
    - Bad tick lowers ceiling toward `currentBps × signalLevel`,
      magnitude scales with `(1 - signalLevel)` and `NegativeStreak`.
    - Good tick at-or-above `ConfirmRatio × Ceiling` lifts ceiling.
    - Good tick well below ceiling holds.
    - Pluggable classifier override is respected.
    - **Probe back-off (the cycle-widening test):**
      drive a 1 Hz tick sequence that pushes (good ⇒ lift, then
      bad ⇒ drop) repeatedly. Assert that:
      1. After each bad-after-good event, `ProbeFailures` increments.
      2. The next lift is suppressed for ≥ `BaseProbeCooldownSec × ProbeCooldownGrowth^ProbeFailures` seconds.
      3. The cooldown is capped at `MaxProbeCooldownSec`.
      4. After ≥ `ProbeFailureResetStreak` ticks of sustained
         neutral/good-with-headroom, `ProbeFailures` resets to 0 and
         the next probe lifts immediately.
  - `EncodingCapTest.cs` — `EncCap` walks down on sustained
    `EncodeRatioEma > Bad` and back up on sustained
    `EncodeRatioEma < Good`, camera-first reduction order.
  - `BandwidthCapTest.cs` — `BwCap` walks layers off the estimator's
    streak signals in the same camera-first order.
  - `AllocatorPriorityTest.cs` — floor-reserve → primary best-fit →
    area-proportional secondary distribution. Includes
    `producerTemporalLayerCount = 1` ⇒ fraction pinned to 1.0.
- Repurpose `PlaybackVerdictClassifierTest.cs` as a sanity test for the
  default verdict thresholds.

---

## Migration / shippability

- **Wire protocol unchanged.** All changes are internal to QC controllers
  and the modal.
- **One branch, one rollout.** Validate via `/server-loop` + `/debug-ui`:
  - Inbound: `CeilingBps` should stay within 1.5× of the steady-state
    `sumIncomingBytesPerSec` once a stream settles. `playbackRateEma`
    should sit near 1 in the healthy case.
  - Outbound: with both camera and screencast active, dropping the
    upstream link to (say) 1 Mbps should walk camera layers down first,
    only touching screencast when camera hits 1 layer.
  - Reconnect: dropping and restoring the network resets
    `CeilingBps` back to `config.InitialCeilingBps`. The transition
    should be invisible to the user in terms of layer count — the
    initial value is generous enough for the baseline configuration.

---

## Open questions

1. **Inbound severity signal — resolved.** Primary inbound signal is
   `playbackRateEma` (source-clock ms drained per wall-clock ms at the
   present stage, sampled at 1 Hz with α = 0.3 EMA). `1.0` = on time;
   penalty kicks in below `PlaybackRateOk = 0.9`. Receiver-side drop
   ratio (FrameDropStage 61–90) is the secondary signal.
   `decoderQueueDepthEma` is **not** added — it tracks GPU/decoder
   pressure, which is orthogonal to bandwidth.
2. **Mobile detection — resolved.** Use
   `BrowserInfo.IsMobile || HostInfo.AppKind.IsMobile()`. Both already
   exist (`AppKindExt.IsMobile` returns true for `Android` / `Ios`).
   No new plumbing.
3. **Persistence of `lastTarget` across app launches.** Out of scope.
   Each app launch starts cold; the estimator quickly walks up to the
   real ceiling within seconds.
4. **Probing.** Today's `docs/plans/video-throughput-probing.md` is
   compatible — once the estimator is in place, the probing logic
   plugs in by feeding `currentBandwidthBps` above the current ceiling
   for K seconds, then watching how `signalLevel` reacts. Defer to a
   follow-up.
5. **Cross-epoch retention.** Should the estimator remember anything
   across reconnects? Default: no — each epoch is clean. The caller
   may remember its `lastTarget` and resume there; the estimator
   re-learns the ceiling within seconds.

---

## Roll-out order (implementation steps)

**Phase 1 — `BandwidthEstimator` standalone.** Implement and finalize the
estimator end-to-end *before touching anything else*. It has no external
dependencies once `RpcConnectionInfo` exists.

1. `ConnectivityUI.ConnectionInfo` (+ `RpcConnectionInfo` record) — pure
   addition, no callers yet.
2. `BandwidthEstimator` class + `BandwidthEstimatorConfig` record +
   `BandwidthEstimateRecord`.
3. Full unit-test coverage of `BandwidthEstimator`:
   - Fresh-epoch defaults, epoch transitions.
   - Bad/good/neutral verdict behaviour at varying `signalLevel`.
   - Probe back-off (the cycle-widening test).
   - History ring-buffer semantics.
   - Pluggable classifier.
   - Age-decay scaling.
4. **Iterate until tests pass and the algorithm feels right** —
   tweak knobs, refine clauses. This is the only phase where the rest
   of the codebase isn't waiting on the estimator's behaviour, so it's
   the cheapest place to get the algorithm right.

**Phase 2 — Plug the estimator into QC.** Only after Phase 1 is solid.

5. `EncodingCap`, `BandwidthCap` + unit tests.
6. Reshape `Allocator` to (floor reserve, primary best-fit,
   area-proportional secondaries) + unit tests.
7. Wire `PlaybackStats.AvailableTemporalLayerCount` +
   `PlaybackStats.PlaybackRateEma` field through .NET and TS;
   maintain `playbackRateEma` in the TS present operators.
8. Replace `CapacityEstimator` + `RecordingAggregator` call sites in
   `VideoQualityUI.cs` with the new pieces. Wire signalLevel formula
   for both directions.

**Phase 3 — Investigate, validate, document.**

9. ~~Investigate "screencast presented at L0" bug~~ ✅ **Done.** Root cause
   was `GetBestLayerFor`'s nearest-neighbor rule biasing toward smaller
   layers when the desired size sat between two ladder rungs. Switched
   to "smallest ≥ desired, fall back to largest". Regression covered
   in `GetBestLayerForTest.cs`.
10. End-to-end validation via `/server-loop` + `/debug-ui`. Knobs are
    still tunable via code at this stage; the UI rework hasn't landed
    yet.
11. Update `docs/live-video/08-quality-control.md` to reflect the new
    model.

**Phase 4 — UI rework (last).** Only after Phase 3 closes out and the
controller behaviour is stable. Touching the UI early risks chasing
visual artefacts that are really algorithm bugs.

12. `VideoDiagnosticsModal.razor` tabs (Inbound / Outbound). QC section
    per tab reading from the per-direction `BandwidthEstimator`
    (ceiling, signal, probe failures, history). Knobs section per tab
    bound to mutable `BandwidthEstimatorConfig`. Delete the separate
    settings modal.
