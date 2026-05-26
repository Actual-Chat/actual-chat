# Video QC Decision Log + Decoder Classifier Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **User workflow preference:** One task per turn, one commit per task, wait for user confirmation between tasks, do NOT run `dotnet build` — ask the user to run it after each .NET-touching task and report back. TypeScript `npm run build:Verify` is fine to run automatically.

**Goal:** Replace the BWE-only "Recent updates" history with a 10-row decision log that lists, per QC tick, the verdict + raw inputs for every signal and any cap change; remove the standalone verdict chips; loosen `ClassifyDecoder` (drop `recoveryStreak` and `presentSkipRatio` from the verdict combine); change the sticky decoder cap to demote once per Good→Bad edge instead of walking down on every Bad tick.

**Architecture:** `VideoQualityUI` gains two `Queue<QualityDecisionEntry>` ring buffers (one per direction). `RunOutboundTick` and `RecomputePlaybackQuality` append one entry per evaluation tick. `VideoDiagnosticsModal` renders the log via a new `AppendDecisionLog` helper that reuses the existing `.diag-chip-{good,marginal,bad,unknown}` CSS for in-row swatches and a new dim sub-line for raw values. `ReceiverHealthClassifier.ClassifyDecoder` keeps `recoveryStreak` / `presentSkipRatio` on the record but drops them from the combine. `VideoQualityUI.Playback.cs` tracks `_lastDecoderVerdict` and demotes the per-stream cap only on the Good→Bad edge.

**Tech Stack:** C# 12 / .NET 10, Razor, Tailwind via `@apply`, xUnit + FluentAssertions for tests. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-05-26-video-qc-decision-log-design.md`

---

## File map

- Modify: `src/dotnet/UI.Blazor.App/Services/ReceiverHealthClassifier.cs` — `ClassifyDecoder` combine
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs` — `QualityDecisionEntry` nested record + private ring buffers + public read-only accessors
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Recording.cs` — `RunOutboundTick` appends one entry per tick; uses pre/post cap snapshots already in place
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs` — adds `_lastDecoderVerdict`, edge-only sticky cap; `_prevDecoderCapByStream` + `_prevRequestedMap` for inbound cap-diff; appends one entry per `RecomputePlaybackQuality` call
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor` — adds `AppendDecisionLog` render helper (next to existing `AppendHistory` / `AppendRow`)
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Inbound.cs` — removes chip call; replaces `AppendHistory` with `AppendDecisionLog`
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Outbound.cs` — same
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-diagnostics-modal.css` — `.diag-log-row`, `.diag-log-row-action`, `.diag-log-swatches`, `.diag-log-swatch`, `.diag-log-subline` rules
- Modify: `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/ReceiverHealthClassifierTest.cs` — update `RecoveryStreak_AtThreshold_IsBad` (rename + invert assertion), add `PresentSkipHigh_NoOtherSignal_IsGood`, add `DecodeRatioBad_AloneIsBad`
- Create: `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/VideoQualityUIDecoderCapTest.cs` — sticky-cap edge-only behavior + stale pruning

`AppendHistory` is removed after Task 6; no other callers exist (verified by `grep -n "AppendHistory" src/dotnet/UI.Blazor.App`).

---

## Task 1: Classifier — drop noisy inputs from combine

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ReceiverHealthClassifier.cs:92`
- Modify: `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/ReceiverHealthClassifierTest.cs:79-89` (rename + invert)
- Add tests to: same file

- [ ] **Step 1.1: Replace the `RecoveryStreak_AtThreshold_IsBad` test (lines 78-89) with the inverted-expectation version**

In `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/ReceiverHealthClassifierTest.cs` replace the existing fact with:

```csharp
[Fact]
public void RecoveryStreak_AtThreshold_DoesNotDriveBad()
{
    // recoveryStreak is recorded on DecoderHealth but no longer drives the
    // combined verdict — it fires on decoder restart events, which are not
    // direct decoder fault.
    var c = new ReceiverHealthClassifier(T());
    var dec = c.ClassifyDecoder(
        decodeRatioEma: 0.5,
        hangRateIn60s: 0,
        recoveryStreak: ReceiverHealthThresholds.Defaults.RecoveryStreakBad,
        presentSkipRatio: 0,
        receiverDecodePathDropRatio: 0);
    dec.Verdict.Should().NotBe(HealthVerdict.Bad);
    dec.RecoveryStreak.Should().Be(ReceiverHealthThresholds.Defaults.RecoveryStreakBad);
}
```

- [ ] **Step 1.2: Add two new fact methods at the end of the class (before the closing `}`)**

```csharp
    [Fact]
    public void PresentSkipHigh_NoOtherSignal_DoesNotDriveBad()
    {
        // presentSkipRatio fires on MSTG catch-up skips, not decoder fault.
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            hangRateIn60s: 0,
            recoveryStreak: 0,
            presentSkipRatio: 0.9,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
        dec.PresentSkipRatio.Should().Be(0.9);
    }

    [Fact]
    public void DecodeRatioBad_AfterStreak_IsBad()
    {
        var c = new ReceiverHealthClassifier(T());
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 3; i++) {
            dec = c.ClassifyDecoder(
                decodeRatioEma: 2.0,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }
```

- [ ] **Step 1.3: Modify the classifier combine (line 92)**

In `src/dotnet/UI.Blazor.App/Services/ReceiverHealthClassifier.cs:92` replace:

```csharp
        var combined = HealthVerdictExt.Combine([ratioVerdict, hangVerdict, recoveryVerdict, skipVerdict, dropVerdict]);
```

with:

```csharp
        // recoveryStreak and presentSkipRatio remain on the record for
        // diagnostics but no longer drive the combine — both fire on
        // transient pipeline events (decoder restart, MSTG catch-up skip)
        // that are not direct decoder fault.
        var combined = HealthVerdictExt.Combine([ratioVerdict, hangVerdict, dropVerdict]);
```

(Lines 76-86 that compute `ratioVerdict` / `hangVerdict` / `recoveryVerdict` / `skipVerdict` / `dropVerdict` stay unchanged — the record still carries `recoveryStreak` and `presentSkipRatio`.)

- [ ] **Step 1.4: Ask user to run .NET tests**

Ask user: "Please run the unit tests for `Chat.UI.Blazor.UnitTests`. Expected: all three tests (`RecoveryStreak_AtThreshold_DoesNotDriveBad`, `PresentSkipHigh_NoOtherSignal_DoesNotDriveBad`, `DecodeRatioBad_AfterStreak_IsBad`) pass alongside existing tests."

- [ ] **Step 1.5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ReceiverHealthClassifier.cs \
        tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/ReceiverHealthClassifierTest.cs
git commit -m "fix(video): drop recoveryStreak + presentSkip from decoder verdict combine"
```

- [ ] **Step 1.6: Wait for user confirmation before Task 2.**

---

## Task 2: Sticky decoder cap — demote on Good→Bad edge only

**Files:**
- Create: `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/VideoQualityUIDecoderCapTest.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs:42` (declarations), `:219-226` (logic), `:228-235` (stale pruning)

Note: `_decoderLayerCapByStream` is currently mutated *inside* `RecomputePlaybackQuality` from the per-tick `streamDecoder.Verdict`. Tests that exercise the edge logic must therefore drive `OnPlaybackStats` end-to-end. To avoid building a heavyweight test harness around `AppUIHub`, this task tests the new edge logic via an extracted pure helper that owns the per-stream verdict transition, then wires the helper into `RecomputePlaybackQuality`.

- [ ] **Step 2.1: Create the failing test file**

Create `tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/VideoQualityUIDecoderCapTest.cs`:

```csharp
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VideoQualityUIDecoderCapTest
{
    [Fact]
    public void DemoteOnEdge_BadAfterGood_SetsCap()
    {
        var s = new DecoderCapState();
        var cap = s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        cap.Should().BeNull();
        cap = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        cap.Should().Be(1); // requestedLayer=2 → cap=max(0, 2-1)=1
    }

    [Fact]
    public void DemoteOnEdge_RepeatedBad_DoesNotWalkDown()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        var cap1 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap2 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap3 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        cap1.Should().Be(1);
        cap2.Should().Be(1);
        cap3.Should().Be(1);
    }

    [Fact]
    public void GoodClearsCap()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap = s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        cap.Should().BeNull();
    }

    [Fact]
    public void ReDemoteAfterGoodBadCycle_PicksFreshLayer()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);   // cap=1
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 2);  // cleared
        var cap = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 2);
        cap.Should().Be(0); // requestedLayer=1 → cap=max(0, 1-1)=0
    }

    [Fact]
    public void MarginalHoldsExistingCap()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap = s.OnVerdict("stream-a", HealthVerdict.Marginal, requestedLayerCount: 3);
        cap.Should().Be(1);
    }

    [Fact]
    public void Prune_RemovesStaleStreamState()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        s.OnVerdict("stream-b", HealthVerdict.Bad, requestedLayerCount: 3);
        s.PruneStaleStreams(new HashSet<string> { "stream-a" });
        s.HasState("stream-a").Should().BeTrue();
        s.HasState("stream-b").Should().BeFalse();
    }
}
```

- [ ] **Step 2.2: Run test file — confirm compile failure on missing `DecoderCapState`**

Ask user to run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~VideoQualityUIDecoderCapTest"`.
Expected: compile error referencing `DecoderCapState`.

- [ ] **Step 2.3: Create the `DecoderCapState` helper**

Create `src/dotnet/UI.Blazor.App/Services/DecoderCapState.cs`:

```csharp
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

internal sealed class DecoderCapState
{
    private readonly Dictionary<string, HealthVerdict> _lastVerdict = new();
    private readonly Dictionary<string, int> _caps = new();

    public IReadOnlyDictionary<string, int> Caps => _caps;

    public int? OnVerdict(string streamId, HealthVerdict verdict, int requestedLayerCount)
    {
        var prev = _lastVerdict.GetValueOrDefault(streamId, HealthVerdict.Unknown);
        if (verdict == HealthVerdict.Bad && prev != HealthVerdict.Bad) {
            var currentLayer = Math.Max(0, requestedLayerCount - 1);
            _caps[streamId] = Math.Max(0, currentLayer - 1);
        }
        else if (verdict == HealthVerdict.Good) {
            _caps.Remove(streamId);
        }
        _lastVerdict[streamId] = verdict;
        return _caps.TryGetValue(streamId, out var c) ? c : null;
    }

    public bool HasState(string streamId)
        => _lastVerdict.ContainsKey(streamId) || _caps.ContainsKey(streamId);

    public void PruneStaleStreams(IReadOnlyCollection<string> liveStreamIds)
    {
        var deadVerdictKeys = _lastVerdict.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray();
        foreach (var k in deadVerdictKeys)
            _lastVerdict.Remove(k);
        var deadCapKeys = _caps.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray();
        foreach (var k in deadCapKeys)
            _caps.Remove(k);
    }
}
```

- [ ] **Step 2.4: Ask user to run the new tests in isolation**

Ask user: "Please run `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter 'FullyQualifiedName~VideoQualityUIDecoderCapTest'`. Expected: all six tests pass."

- [ ] **Step 2.5: Wire `DecoderCapState` into `VideoQualityUI.Playback.cs`**

In `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs`:

(a) Replace the declarations at lines 38-43:

```csharp
    private readonly Dictionary<StreamId, DownlinkHealth> _lastDownlinkHealthByStream = new();
    private readonly Dictionary<StreamId, DecoderHealth> _lastDecoderHealthByStream = new();
    // Sticky decoder cap: set on DecoderHealth=Bad, cleared on =Good. Marginal
    // keeps the last value so the cap doesn't oscillate while the classifier
    // re-acquires Good.
    private readonly Dictionary<StreamId, int> _decoderLayerCapByStream = new();
```

with:

```csharp
    private readonly Dictionary<StreamId, DownlinkHealth> _lastDownlinkHealthByStream = new();
    private readonly Dictionary<StreamId, DecoderHealth> _lastDecoderHealthByStream = new();
    // Sticky decoder cap with edge-triggered demote. Set on Good→Bad
    // transition, cleared on =Good. Marginal holds the last value.
    private readonly DecoderCapState _decoderCapState = new();
```

(b) Replace the `InboundDecoderCapStreamCount` accessor (line 52):

```csharp
    public int InboundDecoderCapStreamCount => _decoderCapState.Caps.Count;
```

(c) Replace the per-stream sticky-cap block at lines 219-226 with:

```csharp
            _decoderCapState.OnVerdict(
                streamId.Value, streamDecoder.Verdict, state.RequestedLayerCount);
```

(d) Replace the stale-pruning at lines 228-235 with:

```csharp
        // Drop stale entries so per-stream classifiers don't leak.
        var liveStreamIds = entries.Select(x => x.Key).ToHashSet();
        var liveStreamIdStrings = liveStreamIds.Select(x => x.Value).ToHashSet();
        foreach (var sid in _receiverHealthByStream.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray()) {
            _receiverHealthByStream.Remove(sid);
            _lastDownlinkHealthByStream.Remove(sid);
            _lastDecoderHealthByStream.Remove(sid);
        }
        _decoderCapState.PruneStaleStreams(liveStreamIdStrings);
```

(e) Replace the allocator call at line 257-260 — the cap dictionary now comes from `_decoderCapState`:

```csharp
        var decoderLayerCapDict = _decoderCapState.Caps.Count == 0
            ? null
            : _decoderCapState.Caps;
        var requested = VideoQualityAllocator.Allocate(capacity, primaries, secondaries, decoderLayerCapDict);
```

(f) Update the log line at lines 293-298 — replace `_decoderLayerCapByStream.Count` with `_decoderCapState.Caps.Count`.

- [ ] **Step 2.6: Ask user to run all `Chat.UI.Blazor.UnitTests`**

Ask user: "Please run all tests in `Chat.UI.Blazor.UnitTests`. Expected: every test passes, including the new `VideoQualityUIDecoderCapTest`."

- [ ] **Step 2.7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/DecoderCapState.cs \
        src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs \
        tests/Chat.UI.Blazor.UnitTests/VideoQualityUI/VideoQualityUIDecoderCapTest.cs
git commit -m "fix(video): demote decoder cap once per Good→Bad edge"
```

- [ ] **Step 2.8: Wait for user confirmation before Task 3.**

---

## Task 3: `QualityDecisionEntry` record + ring buffers

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`

- [ ] **Step 3.1: Add the nested record + ring buffers**

In `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs`, inside the `public sealed partial class VideoQualityUI : UIWorkerBase<AppUIHub>` declaration, immediately before the closing `}` of the class, append:

```csharp
    // ----- Decision log -----

    private const int DecisionLogCapacity = 10;
    private readonly Queue<QualityDecisionEntry> _outboundDecisionLog = new(DecisionLogCapacity);
    private readonly Queue<QualityDecisionEntry> _inboundDecisionLog = new(DecisionLogCapacity);

    public IReadOnlyCollection<QualityDecisionEntry> OutboundDecisionLog => _outboundDecisionLog;
    public IReadOnlyCollection<QualityDecisionEntry> InboundDecisionLog => _inboundDecisionLog;

    internal void AppendOutboundDecision(QualityDecisionEntry entry)
    {
        _outboundDecisionLog.Enqueue(entry);
        while (_outboundDecisionLog.Count > DecisionLogCapacity)
            _outboundDecisionLog.Dequeue();
    }

    internal void AppendInboundDecision(QualityDecisionEntry entry)
    {
        _inboundDecisionLog.Enqueue(entry);
        while (_inboundDecisionLog.Count > DecisionLogCapacity)
            _inboundDecisionLog.Dequeue();
    }

    public sealed record QualityDecisionEntry(
        Moment At,
        HealthVerdict SignalA,        // outbound: Encoder | inbound: Downlink
        HealthVerdict SignalB,        // outbound: Uplink  | inbound: Decoder
        BandwidthVerdict BweVerdict,
        string CapChange,             // "" when no cap moved
        string Reason,                // dominant trigger
        string RawValuesA,
        string RawValuesB,
        string RawValuesBw);
```

Add `using ActualChat.Streaming;` to the file's using list if not already present (it already imports `ActualChat.Streaming` via `VideoQualityUI.cs` line 3).

- [ ] **Step 3.2: Ask user to run .NET build**

Ask user: "Please build the solution. Expected: succeeds (only adds new types + properties; no callers yet)."

- [ ] **Step 3.3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs
git commit -m "feat(video): scaffold QualityDecisionEntry + ring buffers on VideoQualityUI"
```

- [ ] **Step 3.4: Wait for user confirmation before Task 4.**

---

## Task 4: Outbound decision-log append in `RunOutboundTick`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Recording.cs`

- [ ] **Step 4.1: Append the entry at the end of `RunOutboundTick`**

In `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Recording.cs`, locate `RunOutboundTick` (starts at line 157). The method currently ends at line 251 (the `foreach` over `_recordersByKind` that applies the target). After that `foreach` block but still inside the method, append:

```csharp
        // Decision-log entry. Cap-change string and reason are derived from
        // the pre/post snapshots already captured above.
        var capChange = "";
        if (preEncCam != postEncCam)
            capChange = $"camera {preEncCam}→{postEncCam} (enc)";
        else if (preBwCam != postBwCam)
            capChange = $"camera {preBwCam}→{postBwCam} (bw)";
        var ceilingKbps = _outboundBwEstimator.CeilingBps * 8 / 1000;
        var currentKbps = _outboundBwEstimator.LastCurrentBps * 8 / 1000;
        var reason = encoderHealth.Verdict == HealthVerdict.Bad
            ? $"encDeficit {(fusedEncodeDeficit * 100):F0}%"
            : uplinkHealth.Verdict == HealthVerdict.Bad
                ? $"ack {fusedAckAgeMs:F0}ms / drop {fusedDropRatio:F2}"
                : _outboundBwEstimator.LastVerdict switch {
                    BandwidthVerdict.Good => $"BW ↑ {ceilingKbps} kbps",
                    BandwidthVerdict.Bad => $"BW ↓ {ceilingKbps} kbps",
                    _ => "stable",
                };
        var rawA = $"def={(fusedEncodeDeficit * 100):F0}% q={fusedEncodeQueueDepth:F1} rs={maxRestartStreak}";
        var rawB = $"ack={(fusedAckAgeMs < 0 ? "n/a" : fusedAckAgeMs.ToString("F0") + "ms")} drop={fusedDropRatio:F2} qd={fusedWireQueueDepth:F1} flood={fusedFloodSkipPerSec:F1}";
        var rawBw = $"{(_outboundBwEstimator.LastVerdict == BandwidthVerdict.Good ? "↑" : _outboundBwEstimator.LastVerdict == BandwidthVerdict.Bad ? "↓" : "=")}{ceilingKbps}/cur {currentKbps} kbps";
        AppendOutboundDecision(new QualityDecisionEntry(
            SystemClock.Now,
            encoderHealth.Verdict,
            uplinkHealth.Verdict,
            _outboundBwEstimator.LastVerdict,
            capChange,
            reason,
            rawA,
            rawB,
            rawBw));
```

- [ ] **Step 4.2: Ask user to build**

Ask user: "Please build. Expected: succeeds. (No tests yet — verified visually in Task 7.)"

- [ ] **Step 4.3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Recording.cs
git commit -m "feat(video): emit outbound decision-log entry per QC tick"
```

- [ ] **Step 4.4: Wait for user confirmation before Task 5.**

---

## Task 5: Inbound decision-log append in `RecomputePlaybackQuality`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs`

- [ ] **Step 5.1: Add the prev-snapshot fields**

In `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs`, alongside `_decoderCapState` (the field added in Task 2.5.a), add:

```csharp
    // Snapshot of the previous tick's requested layer per stream, used to
    // detect allocator-driven cap moves for the decision log.
    private readonly Dictionary<string, int> _prevRequestedLayerByStream = new();
    private readonly Dictionary<string, int> _prevDecoderCapByStream = new();
```

- [ ] **Step 5.2: Append the decision-log entry at the end of `RecomputePlaybackQuality`**

In the same file, locate `RecomputePlaybackQuality` (starts at line 176). Just before the final `return;` (around line 331) — after the existing `LiveVideoStreams.ChangePlaybackQuality(...)` call and the `UpdateRequestedReceiveQualityRegistry` await — insert:

```csharp
        // Decision-log entry. Aggregate decoder verdict = worst across
        // streams (mirrors the removed Decoder chip semantics).
        var aggregateDecoderVerdict = HealthVerdict.Unknown;
        foreach (var (_, h) in _lastDecoderHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (aggregateDecoderVerdict == HealthVerdict.Unknown
                || (int)h.Verdict > (int)aggregateDecoderVerdict)
                aggregateDecoderVerdict = h.Verdict;
        }
        // Cap-change detection: compare requested layer + decoder cap maps
        // with the previous tick's snapshot. Pick the first changed stream
        // in lexical order (predictable, deterministic for the log).
        var capChange = "";
        foreach (var (sid, q) in requestedMap.OrderBy(x => x.Key)) {
            var prevLayer = _prevRequestedLayerByStream.GetValueOrDefault(sid, -1);
            if (prevLayer >= 0 && prevLayer != q.LayerId) {
                var capTag = _decoderCapState.Caps.ContainsKey(sid) ? "decoder" : "bw";
                capChange = $"{ShortStreamId(sid)} L{prevLayer}→L{q.LayerId} ({capTag})";
                break;
            }
        }
        // Refresh snapshot maps for the next tick.
        _prevRequestedLayerByStream.Clear();
        foreach (var (sid, q) in requestedMap)
            _prevRequestedLayerByStream[sid] = q.LayerId;
        _prevDecoderCapByStream.Clear();
        foreach (var (sid, c) in _decoderCapState.Caps)
            _prevDecoderCapByStream[sid] = c;

        // Pick the worst stream for the decoder raw-values line so the
        // operator sees the actual numbers behind the aggregate verdict.
        DecoderHealth? worstDecoder = null;
        foreach (var (_, h) in _lastDecoderHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (worstDecoder is null || (int)h.Verdict > (int)worstDecoder.Verdict)
                worstDecoder = h;
        }
        // Pick the worst stream for the downlink raw-values line similarly.
        DownlinkHealth? worstDownlink = null;
        foreach (var (_, h) in _lastDownlinkHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (worstDownlink is null || (int)h.Verdict > (int)worstDownlink.Verdict)
                worstDownlink = h;
        }
        var ceilingKbps = _inboundBwEstimator.CeilingBps * 8 / 1000;
        var currentKbps = _inboundBwEstimator.LastCurrentBps * 8 / 1000;
        var dlReason = aggregateDownlinkVerdict == HealthVerdict.Bad && worstDownlink is not null
            ? $"downlink lat={worstDownlink.ServerToReceiverLatencyEma:F0}ms drop={worstDownlink.ServerPathDropRatio:F2}"
            : "";
        var decReason = aggregateDecoderVerdict == HealthVerdict.Bad && worstDecoder is not null
            ? $"decode ratio={worstDecoder.DecodeRatioEma:F2} hang={worstDecoder.HangRateIn60s}"
            : "";
        var inboundReason = !string.IsNullOrEmpty(dlReason) ? dlReason
            : !string.IsNullOrEmpty(decReason) ? decReason
            : _inboundBwEstimator.LastVerdict switch {
                BandwidthVerdict.Good => $"BW ↑ {ceilingKbps} kbps",
                BandwidthVerdict.Bad => $"BW ↓ {ceilingKbps} kbps",
                _ => "stable",
            };
        var rawA = worstDownlink is not null
            ? $"lat={worstDownlink.ServerToReceiverLatencyEma:F0}ms drop={worstDownlink.ServerPathDropRatio:F2} und={worstDownlink.BufferUnderrunRatio:F2} pr={playbackRateEma:F2}"
            : $"pr={playbackRateEma:F2} drop={receiverDropRatio:F2}";
        var rawB = worstDecoder is not null
            ? $"ratio={worstDecoder.DecodeRatioEma:F2} hang={worstDecoder.HangRateIn60s} rec={worstDecoder.RecoveryStreak} skip={worstDecoder.PresentSkipRatio:F2}"
            : "";
        var rawBw = $"{(_inboundBwEstimator.LastVerdict == BandwidthVerdict.Good ? "↑" : _inboundBwEstimator.LastVerdict == BandwidthVerdict.Bad ? "↓" : "=")}{ceilingKbps}/cur {currentKbps} kbps";
        AppendInboundDecision(new QualityDecisionEntry(
            SystemClock.Now,
            aggregateDownlinkVerdict,
            aggregateDecoderVerdict,
            _inboundBwEstimator.LastVerdict,
            capChange,
            inboundReason,
            rawA,
            rawB,
            rawBw));
```

- [ ] **Step 5.3: Add the `ShortStreamId` helper at the end of the class (before the closing `}`)**

```csharp
    private static string ShortStreamId(string streamId)
    {
        if (string.IsNullOrEmpty(streamId)) return "";
        // StreamIds carry a long hash suffix; first 6 chars are enough to
        // disambiguate within a session's live set.
        return streamId.Length <= 6 ? streamId : streamId[..6];
    }
```

- [ ] **Step 5.4: Ask user to build**

Ask user: "Please build. Expected: succeeds."

- [ ] **Step 5.5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/VideoQualityUI.Playback.cs
git commit -m "feat(video): emit inbound decision-log entry per QC tick"
```

- [ ] **Step 5.6: Wait for user confirmation before Task 6.**

---

## Task 6: Modal — remove chips + replace `AppendHistory` with `AppendDecisionLog`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Inbound.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Outbound.cs`

- [ ] **Step 6.1: Add the `AppendDecisionLog` helper to the razor file**

In `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor`, locate the existing `AppendHistory` method (lines 604-627). Replace it with:

```csharp
    private static void AppendDecisionLog(
        RenderTreeBuilder builder, int seqBase,
        IReadOnlyCollection<VideoQualityUI.QualityDecisionEntry> entries,
        string labelA, string labelB)
    {
        builder.OpenElement(seqBase, "div");
        builder.AddAttribute(seqBase + 1, "class", "diag-group-header");
        builder.AddContent(seqBase + 2, "Decision log");
        builder.CloseElement();

        var i = 0;
        // Most recent first.
        foreach (var e in entries.Reverse()) {
            var rowClass = string.IsNullOrEmpty(e.CapChange)
                ? "diag-log-row"
                : "diag-log-row diag-log-row-action";
            builder.OpenElement(seqBase + 100 + i * 30, "div");
            builder.AddAttribute(seqBase + 101 + i * 30, "class", rowClass);

            // Line 1: time + swatches + cap change.
            builder.OpenElement(seqBase + 102 + i * 30, "div");
            builder.AddAttribute(seqBase + 103 + i * 30, "class", "diag-log-line");
            builder.OpenElement(seqBase + 104 + i * 30, "span");
            builder.AddAttribute(seqBase + 105 + i * 30, "class", "diag-log-time");
            builder.AddContent(seqBase + 106 + i * 30, e.At.ToDateTimeOffset().ToString("HH:mm:ss"));
            builder.CloseElement();

            builder.OpenElement(seqBase + 107 + i * 30, "span");
            builder.AddAttribute(seqBase + 108 + i * 30, "class", "diag-log-swatches");
            AppendSwatch(builder, seqBase + 109 + i * 30, labelA, e.SignalA);
            AppendSwatch(builder, seqBase + 113 + i * 30, labelB, e.SignalB);
            AppendBweSwatch(builder, seqBase + 117 + i * 30, "BW", e.BweVerdict);
            builder.CloseElement();

            builder.OpenElement(seqBase + 121 + i * 30, "span");
            builder.AddAttribute(seqBase + 122 + i * 30, "class",
                string.IsNullOrEmpty(e.CapChange) ? "diag-log-cap diag-dim" : "diag-log-cap");
            builder.AddContent(seqBase + 123 + i * 30,
                string.IsNullOrEmpty(e.CapChange) ? e.Reason : $"{e.CapChange} · {e.Reason}");
            builder.CloseElement();
            builder.CloseElement(); // diag-log-line

            // Line 2: raw values.
            builder.OpenElement(seqBase + 124 + i * 30, "div");
            builder.AddAttribute(seqBase + 125 + i * 30, "class", "diag-log-subline diag-dim");
            var subline = string.Join("   ",
                new[] {
                    string.IsNullOrEmpty(e.RawValuesA) ? "" : $"{labelA.ToLowerInvariant()}: {e.RawValuesA}",
                    string.IsNullOrEmpty(e.RawValuesB) ? "" : $"{labelB.ToLowerInvariant()}: {e.RawValuesB}",
                    string.IsNullOrEmpty(e.RawValuesBw) ? "" : $"bw: {e.RawValuesBw}",
                }.Where(s => !string.IsNullOrEmpty(s)));
            builder.AddContent(seqBase + 126 + i * 30, subline);
            builder.CloseElement();

            builder.CloseElement(); // diag-log-row
            i++;
        }
    }

    private static void AppendSwatch(
        RenderTreeBuilder builder, int seqBase, string label, HealthVerdict verdict)
    {
        var cls = verdict switch {
            HealthVerdict.Good => "diag-log-swatch diag-chip-good",
            HealthVerdict.Marginal => "diag-log-swatch diag-chip-marginal",
            HealthVerdict.Bad => "diag-log-swatch diag-chip-bad",
            _ => "diag-log-swatch diag-chip-unknown",
        };
        builder.OpenElement(seqBase, "span");
        builder.AddAttribute(seqBase + 1, "class", cls);
        builder.AddAttribute(seqBase + 2, "title", $"{label}: {verdict}");
        builder.AddContent(seqBase + 3, $"{label}:{VerdictLetter(verdict)}");
        builder.CloseElement();
    }

    private static void AppendBweSwatch(
        RenderTreeBuilder builder, int seqBase, string label, BandwidthVerdict verdict)
    {
        var cls = verdict switch {
            BandwidthVerdict.Good => "diag-log-swatch diag-chip-good",
            BandwidthVerdict.Bad => "diag-log-swatch diag-chip-bad",
            _ => "diag-log-swatch diag-chip-unknown",
        };
        var letter = verdict switch {
            BandwidthVerdict.Good => "G",
            BandwidthVerdict.Bad => "B",
            _ => "·",
        };
        builder.OpenElement(seqBase, "span");
        builder.AddAttribute(seqBase + 1, "class", cls);
        builder.AddAttribute(seqBase + 2, "title", $"{label}: {verdict}");
        builder.AddContent(seqBase + 3, $"{label}:{letter}");
        builder.CloseElement();
    }

    private static string VerdictLetter(HealthVerdict v) => v switch {
        HealthVerdict.Good => "G",
        HealthVerdict.Marginal => "M",
        HealthVerdict.Bad => "B",
        _ => "·",
    };
```

Add `using ActualChat.Streaming;` at the top of the razor file's `@code` imports if not already pulled in (the existing `@using ActualChat.Streaming` at line 4 is already present).

- [ ] **Step 6.2: Update `VideoDiagnosticsModal.Inbound.cs`**

Replace the whole file with:

```csharp
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public partial class VideoDiagnosticsModal
{
    private RenderFragment RenderInboundQualityControl() => builder => {
        var qualityUi = Hub.VideoQualityUI;
        var bw = qualityUi.InboundBandwidthEstimator;
        var snap = qualityUi.PlaybackSnapshot;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendCeilingAndSignal(builder, 100, bw);
        AppendInboundInputs(builder, 200, snap, qualityUi.InboundDecoderCapStreamCount);
        AppendDecisionLog(builder, 300, qualityUi.InboundDecisionLog, "Dl", "Dec");
        builder.CloseElement();
    };

    private static void AppendInboundInputs(
        RenderTreeBuilder builder, int seqBase,
        VideoQualityUI.PlaybackQualitySnapshot snap,
        int decoderCapStreamCount)
    {
        AppendRow(builder, seqBase + 0, "Allocation budget",
            (snap.EstimatedCapacityBytesPerSec * 8 / 1000).ToString("0") + " kbps");
        AppendRow(builder, seqBase + 1, "Playback rate", snap.PlaybackRateEma.ToString("F3"));
        AppendRow(builder, seqBase + 2, "Drop ratio", snap.DropRatio.ToString("F3"));
        AppendRow(builder, seqBase + 3, "Decoder-capped streams", decoderCapStreamCount.ToString());
    }
}
```

- [ ] **Step 6.3: Update `VideoDiagnosticsModal.Outbound.cs`**

Replace the file's `RenderOutboundQualityControl` body (lines 9-32) with:

```csharp
    private RenderFragment RenderOutboundQualityControl(ComputedModel m) => builder => {
        var qualityUi = Hub.VideoQualityUI;
        var bw = qualityUi.OutboundBandwidthEstimator;
        var encLayers = qualityUi.OutboundEncodingLayers;
        var bwLayers = qualityUi.OutboundBandwidthLayers;
        var activeKinds = m.OwnSourceKinds;
        var statsByKind = activeKinds
            .Select(k => (Kind: k, Stats: qualityUi.GetRecordingSnapshot(k).Health))
            .Where(x => x.Stats is not null)
            .ToDictionary(x => x.Kind, x => x.Stats!);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendCeilingAndSignal(builder, 100, bw);
        AppendOutboundInputs(builder, 200, activeKinds, statsByKind);
        AppendCapReadout(builder, 300, "Encoding cap", encLayers, activeKinds);
        AppendCapReadout(builder, 400, "Bandwidth cap", bwLayers, activeKinds);
        AppendDecisionLog(builder, 500, qualityUi.OutboundDecisionLog, "Enc", "Up");
        builder.CloseElement();
    };
```

The two `AppendOutboundInputs` / `AppendCapReadout` static helpers below stay unchanged. Also delete the now-unused `AppendVerdictChips` method body in the razor file (lines 644-666) since no caller remains.

- [ ] **Step 6.4: Delete `AppendVerdictChips` from the razor file**

In `VideoDiagnosticsModal.razor`, delete the entire `AppendVerdictChips` method (lines 644-666).

- [ ] **Step 6.5: Ask user to build**

Ask user: "Please build. Expected: succeeds. We will visually verify in Task 8."

- [ ] **Step 6.6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor \
        src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Inbound.cs \
        src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.Outbound.cs
git commit -m "feat(video): replace verdict chips + BWE history with decision log"
```

- [ ] **Step 6.7: Wait for user confirmation before Task 7.**

---

## Task 7: CSS — log row + sub-line styles

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-diagnostics-modal.css`

- [ ] **Step 7.1: Append the new rules**

At the end of `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-diagnostics-modal.css`, append:

```css
.diag-log-row {
    @apply flex flex-col gap-0.5 mb-1 px-1 py-0.5 rounded;
}

.diag-log-row-action {
    background: rgba(99, 102, 241, 0.10);
    border: 1px solid rgba(99, 102, 241, 0.35);
}

.diag-log-line {
    @apply flex flex-row items-center gap-2 font-mono;
    font-size: 0.75rem;
}

.diag-log-time {
    @apply text-04;
}

.diag-log-swatches {
    @apply flex flex-row gap-1;
}

.diag-log-swatch {
    @apply px-1 py-0 rounded font-mono;
    font-size: 0.65rem;
    border: none;
}

.diag-log-cap {
    @apply flex-1 text-01;
}

.diag-log-subline {
    @apply font-mono;
    font-size: 0.7rem;
    padding-left: 0.5rem;
    overflow-wrap: anywhere;
}
```

- [ ] **Step 7.2: Confirm CSS file already imported (no action needed)**

`video-diagnostics-modal.css` is imported via `src/dotnet/UI.Blazor.App/styles.css` (already in place — verify with `grep video-diagnostics-modal src/dotnet/UI.Blazor.App/styles.css`).

- [ ] **Step 7.3: Run TypeScript verify (also rebuilds CSS bundle)**

```bash
npm run build:Verify
```

Expected: passes.

- [ ] **Step 7.4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/VideoPanel/video-diagnostics-modal.css
git commit -m "style(video): decision-log row + subline + action-row highlight"
```

- [ ] **Step 7.5: Wait for user confirmation before Task 8.**

---

## Task 8: Visual smoke + reproduction at dev

This task is the only one that exercises the full UI loop. No code changes.

- [ ] **Step 8.1: Local smoke test**

Ask user: "Please open a local call, then open Video Diagnostics modal. Verify:
1. Inbound tab: no chips above the bandwidth section; `Decision log` section shows 10 rows max, most recent on top.
2. Each row shows time, three swatches (`Dl`, `Dec`, `BW`), a cap-change / reason column, and a dim sub-line of raw values.
3. Outbound tab: same shape, with `Enc` / `Up` / `BW` swatches.
4. When a cap moves (e.g. forced via the Outbound layer cap dropdown), the row gets a subtle highlight.
5. Copy button still produces useful plain text including the log rows."

- [ ] **Step 8.2: Dev environment repro**

Ask user: "Please reproduce the all-peers-red-decoder scenario at the dev environment. Open Inbound diagnostics. Capture the decision log content (Copy button output) and share it. We will look at the per-tick raw values (`ratio=…`, `hang=…`) and decide whether to tune `DecodeRatioBad` / `HangRateBad` next."

- [ ] **Step 8.3: Done.**

---

## Self-review

### Spec coverage

- ✅ Decision log per tick (inbound + outbound) — Tasks 3, 4, 5
- ✅ 10-row ring buffer — Task 3
- ✅ Per-signal verdict swatches + reason text + raw values — Tasks 4, 5, 6, 7
- ✅ Cap-change highlighting — Tasks 4, 5, 6, 7
- ✅ Standalone chips removed — Task 6
- ✅ `ClassifyDecoder` loosened (drop noisy inputs) — Task 1
- ✅ Sticky cap demote-once-per-edge — Task 2
- ✅ Stale-stream pruning — Task 2 (via `DecoderCapState.PruneStaleStreams`)
- ✅ Unit tests for both classifier + sticky cap — Tasks 1, 2
- ✅ Manual repro plan — Task 8

### Placeholders

None found. Every step shows the exact code to write.

### Type consistency

- `QualityDecisionEntry` field order + types match between Task 3 (definition), Task 4 (outbound emit), Task 5 (inbound emit), Task 6 (renderer).
- `DecoderCapState.OnVerdict(string, HealthVerdict, int)` signature used identically in Tasks 2.1 (tests) and 2.5 (caller).
- `_decoderCapState.Caps` is `IReadOnlyDictionary<string, int>` — same type expected by `VideoQualityAllocator.Allocate` (verified in Task 2.5.e).
- `ShortStreamId` is defined once in Task 5.3.
- `AppendDecisionLog(builder, seqBase, entries, labelA, labelB)` signature consistent between Task 6.1 (definition) and Tasks 6.2, 6.3 (callers).
