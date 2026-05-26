# Video QC Diagnostics — Decision Log + Decoder Classifier Fix

Date: 2026-05-26
Owner: Alexey Kochetov

## Problem

The Video Diagnostics modal's "Quality Control" sections (inbound + outbound)
expose a `Recent updates` history that lists only `BandwidthEstimator` ticks.
When a quality demote happens, there is no per-tick record of *which* signal
drove it — Encoder vs Uplink (outbound) or Downlink vs Decoder (inbound).
The colored verdict chips above the history look like buttons and are
frequently misread as clickable controls.

Concrete trigger: during a recent dev-environment call, every peer's
`Decoder` chip went red and the inbound allocator demoted every stream to
L0. The current modal cannot show *which* decoder sub-signal exceeded its
threshold across all peers, so the suspected over-eager classification
cannot be confirmed from the UI.

## Goals

1. Show, per QC tick, the verdict and raw inputs for each signal that
   contributed to the decision, plus any cap change that resulted.
2. Remove the standalone verdict chips so the UI does not look clickable.
3. Loosen the decoder classifier so a single transient sub-signal cannot
   collapse all peers to L0, while keeping the verdict responsive to real
   decoder pressure (`decodeRatioEma`, `hangRateIn60s`).
4. Limit the sticky decoder cap to demote once per Good→Bad transition
   instead of walking down one layer per Bad tick.

## Non-goals

- No change to outbound encoder classifier, BWE algorithm, or allocator.
- No OpenTelemetry / server-side telemetry additions. Log is UI-local.
- No change to wire types or RPC contracts.

## Reuse

### Existing abstractions reused

- `HealthVerdict` (`Api.Contracts/Streaming/Quality/HealthVerdict.cs`),
  `BandwidthVerdict` (`Core/Bandwidth/`).
- `HealthVerdictExt.Combine` — keeps worst-of semantics over the reduced
  sub-signal set.
- `BandwidthEstimator.History` ring-buffer pattern (`Queue<T>` of capped
  size, exposed as `IReadOnlyCollection<T>`).
- `.diag-chip-{good,marginal,bad,unknown}` CSS rules
  (`video-diagnostics-modal.css`) — reused for in-row verdict swatches.
- `AppendRow` render helper in `VideoDiagnosticsModal.razor`.
- `SystemClock.Now` already available on `VideoQualityUI`.

### New components — placement

- `QualityDecisionEntry` record — feature-specific. Lives as a nested
  public record on `VideoQualityUI` in
  `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`. Carries
  video-QC-local verdict shape; no other consumer is anticipated, so not
  promoted to `Core`.
- `_outboundDecisionLog` / `_inboundDecisionLog` ring buffers
  (`Queue<QualityDecisionEntry>`, capacity 10) — private fields on
  `VideoQualityUI`, exposed as `IReadOnlyCollection<QualityDecisionEntry>`.
- `AppendDecisionLog` render helper — replaces `AppendHistory` in the
  modal. Lives in `VideoDiagnosticsModal.razor` next to existing
  `Append*` helpers.

## Design

### Decision-log entry

```csharp
public sealed record QualityDecisionEntry(
    Moment At,
    HealthVerdict SignalA,        // outbound: Encoder | inbound: Downlink
    HealthVerdict SignalB,        // outbound: Uplink  | inbound: Decoder
    BandwidthVerdict BweVerdict,
    string CapChange,             // "" or e.g. "camera 3→2 (enc)" / "<streamId> L2→L1 (decoder)"
    string Reason,                // one-line dominant trigger, e.g. "encDeficit 28%"
    string RawValuesA,            // free-form, e.g. "def=28% q=1.5 rs=0"
    string RawValuesB,
    string RawValuesBw);          // e.g. "↑850/cur 720 kbps"
```

Captured at the end of every QC tick (`RunOutboundTick`,
`RecomputePlaybackQuality`). FIFO drop when full. Strings are built at
the append site so the modal stays a pure renderer.

### Outbound append (`VideoQualityUI.Recording.cs`)

Inserted at end of `RunOutboundTick`, after caps are applied. Cap-change
string is non-empty when `preEncCam != postEncCam` or
`preBwCam != postBwCam`. Reason picker:

1. `encoderHealth.Verdict == Bad` → `"encDeficit {fusedEncodeDeficit*100:F0}%"`.
2. else `uplinkHealth.Verdict == Bad` →
   `"ackAge {fusedAckAgeMs:F0}ms / drop {fusedDropRatio:F2}"`.
3. else by `_outboundBwEstimator.LastVerdict`:
   `Good` → `"BW ↑ {ceiling_kbps}kbps"`, `Bad` → `"BW ↓ {ceiling_kbps}kbps"`,
   else `"stable"`.

Raw value strings (concise, units in-line):
- `RawValuesA` (encoder): `"def={pct}% q={depth:F1} rs={restarts}"`.
- `RawValuesB` (uplink): `"ack={ms}ms drop={ratio:F2} qd={depth:F1} flood={perSec:F1}"`.
- `RawValuesBw`: `"↑/↓ {ceiling}/cur {current} kbps"`.

### Inbound append (`VideoQualityUI.Playback.cs`)

Inserted at end of `RecomputePlaybackQuality`. Aggregate decoder verdict
= worst-of across streams (same rule the removed chip used). Cap-change
string captured against a new field `_prevDecoderCapByStream`
(`Dictionary<StreamId, int>`) plus a `_prevRequestedMap`
(`Dictionary<StreamId, ReceiveQuality>`) so an allocator-driven layer
shift is also visible. When multiple streams change in one tick, only
the first (by stream-id order) is shown in `CapChange`; the others are
implied by per-stream raw values.

Raw value strings:
- `RawValuesA` (downlink): `"lat={ms}ms drop={ratio:F2} pr={playbackRate:F2} und={underrun:F2}"`.
- `RawValuesB` (decoder, worst stream's raw):
  `"ratio={decodeRatio:F2} hang={count} skip={ratio:F2} rec={streak}"`.
- `RawValuesBw`: same shape as outbound.

### Decoder classifier change (`ReceiverHealthClassifier.ClassifyDecoder`)

Drop `recoveryStreak` and `presentSkipRatio` from the verdict combine.
Both remain on the `DecoderHealth` record (still surfaced in the
diagnostics raw-values line) but no longer drive the verdict:

```csharp
// before
var combined = HealthVerdictExt.Combine(
    [ratioVerdict, hangVerdict, recoveryVerdict, skipVerdict, dropVerdict]);

// after
var combined = HealthVerdictExt.Combine([ratioVerdict, hangVerdict, dropVerdict]);
```

`dropVerdict` stays in the combine for symmetry — its input
(`receiverDecodePathDropRatio`) is still hardcoded to 0 at the call site
(existing TODO about splitting dropTrace stages 63-64). With the input
0, `dropVerdict` is always `Good`, so the effective decoder verdict is
worst-of(`ratioVerdict`, `hangVerdict`).

Rationale: `recoveryStreak` and `presentSkipRatio` fire on transient
pipeline events (decoder restart, MSTG catch-up skip) that are not
direct decoder fault. `decodeRatioEma` (decode time vs frame interval)
and `hangRateIn60s` are direct decoder-health signals.

### Sticky decoder cap (`VideoQualityUI.Playback.cs`)

Add `_lastDecoderVerdict` field (`Dictionary<StreamId, HealthVerdict>`).
Demote on Good→Bad transition only:

```csharp
var prev = _lastDecoderVerdict.GetValueOrDefault(streamId, HealthVerdict.Unknown);
if (streamDecoder.Verdict == HealthVerdict.Bad && prev != HealthVerdict.Bad) {
    var currentLayer = Math.Max(0, state.RequestedLayerCount - 1);
    _decoderLayerCapByStream[streamId] = Math.Max(0, currentLayer - 1);
}
else if (streamDecoder.Verdict == HealthVerdict.Good) {
    _decoderLayerCapByStream.Remove(streamId);
}
_lastDecoderVerdict[streamId] = streamDecoder.Verdict;
```

Cleanup `_lastDecoderVerdict` in the existing stale-streams pass next
to `_lastDecoderHealthByStream` and `_decoderLayerCapByStream`.

### Modal UI changes

- Remove `AppendVerdictChips` calls in
  `VideoDiagnosticsModal.Inbound.cs:25-27` and
  `VideoDiagnosticsModal.Outbound.cs:23-25`. Keep `.diag-chip*` CSS —
  swatches reuse it.
- Replace `AppendHistory` call (both sides) with `AppendDecisionLog`.
- Section order per side stays:
  1. `Quality Control` header.
  2. Bandwidth ceiling / current / signal (current tick).
  3. Inputs grid (raw current-tick values).
  4. Cap readout (outbound) / decoder-capped streams (inbound).
  5. Decision log — 10 rows, most recent on top.

### Decision-log row layout

Two visual lines per entry:

```
HH:mm:ss  [E:B][U:G][BW:M][Cap:B]  camera 3→2 (enc)
          enc: def=28% q=1.5 rs=0   up: ack=320ms drop=8%   bw: ↑850/cur 720 kbps
```

- Letter swatches reuse `.diag-chip-{good,marginal,bad,unknown}`; smaller
  (0.65rem), no border, ≈2-letter abbreviation (E/U/D/Dec/BW/Cap).
- Cap-change column dimmed (`.diag-dim`) when empty.
- Rows where `CapChange != ""` get class `.diag-log-row-action` —
  subtle background tint to draw the eye.
- Sub-line: `.diag-dim .font-mono` at 0.7rem. Empty when source verdict
  is `Unknown`. Sections separated by 3 spaces; modal copy-source picks
  this up naturally as plain text.

## Testing

### Unit tests

`tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/ReceiverHealthClassifierTest.cs`:

- Add: `presentSkipRatio = 0.9` + `recoveryStreak = 10` with healthy
  `decodeRatioEma` + `hangRateIn60s = 0` → verdict stays `Good` (proves
  the dropped inputs no longer drive the verdict).
- Add: `decodeRatioEma = 2.0` for ≥ `DecodeRatioBadStreak` ticks →
  verdict `Bad`.
- Add: `hangRateIn60s = 1` alone → verdict `Bad`.
- Update or split existing tests that used `recoveryStreak` /
  `presentSkipRatio` as Bad triggers — convert to record-shape
  assertions (the fields are still populated, just not in the combine).

New file: `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/VideoQualityUIDecoderCapTest.cs`:

- Sequence Good→Bad→Bad→Bad → `_decoderLayerCapByStream` set on the
  Good→Bad edge; no further mutation on subsequent Bad ticks.
- Sequence Good→Bad→Bad→Good→Bad → cap entered, cleared, re-entered.
  Cap value reflects the *current* `RequestedLayerCount` on the second
  Bad edge (not the value captured at the first one).
- Stale stream pruning removes the per-stream entries from
  `_lastDecoderVerdict`, `_lastDecoderHealthByStream`,
  `_decoderLayerCapByStream`, `_receiverHealthByStream`.

No new test infra needed — both test files use existing in-memory
classifier / fake-stats helpers.

### Manual repro at dev (after deploy)

1. Reproduce the all-peers-red-decoder scenario.
2. Open Video Diagnostics → Inbound tab.
3. Inspect decision log: confirm whether `decodeRatioEma` or
   `hangRateIn60s` actually exceed their thresholds, or whether the
   demote was previously driven by `recoveryStreak` /
   `presentSkipRatio` (now no longer in the combine).
4. If the verdict is still `Bad` for the right reasons, raw values
   point to the next threshold to tune (e.g. `DecodeRatioBad = 1.5`
   may need adjustment for the dev hardware).

### Visual smoke

Open the modal during a normal local call and verify:

- No standalone chips visible above the bandwidth section.
- Log fills as ticks happen (≈ 1 per 5s in steady state).
- Cap-change rows visually distinct.
- Two-line layout fits modal width without horizontal scroll.
- Copy-text source still produces useful plain-text dump.

## Open questions

None at design freeze. Threshold tuning (e.g. `DecodeRatioBad`,
`HangRateBad`) is deferred until the new log surfaces real values from
the dev environment.

## Risks

- **Verdict semantics drift.** Existing callers / tests that assumed
  worst-of(5) now see worst-of(3). The dropped sub-signals are still on
  the record; only the combine changes. Covered by the unit-test update.
- **Log row overflow.** Two-line layout with all raw values can wrap on
  narrow widths. CSS handles wrap; copy-text source remains usable.
- **Stale verdict map.** `_lastDecoderVerdict` must be pruned together
  with other per-stream maps — if missed, a long-lived process keeps
  dead entries. Covered by the stale-pruning test.
