# Receiver Wedge Diagnostics + Self-Heal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a receiver video pipeline silently stops decoding/presenting while frames keep arriving (the 2026-07-24 "black tile for one viewer" incident), (a) capture WHY — per-stage liveness that reaches the server logs and the diag panel, and (b) self-heal by restarting the player.

**Architecture:** Add cheap liveness stamps (`Date.now()`) at every pipeline stage boundary to the existing per-stream `PlayerStats` (worker side). A main-thread 2 s poller reads them via the already-existing `worker.getStats()` RPC (which answers even when the pipeline loops are wedged, because worker RPC is message-driven), classifies the wedge with a pure `WedgeDetector`, and ships a compact diagnosis through the **existing** `OnPlaybackStalled` → `ChangePlaybackQuality` stall-note path, which already lands in server logs as a WARN. Teardown is made hang-proof (abortable present await + bounded worker stop), then the existing restart loop is reused for self-heal. A small server-side change damps the PLI flood caused by demand-map flapping.

**Tech Stack:** TypeScript (worker + main thread, vitest unit tests in `tests/ts/unit`), C# (Streaming.Service, xunit in `tests/Streaming.UnitTests`), Blazor diag panel.

## Background (incident recap — why these exact changes)

Dev incident 2026-07-24 05:15–05:32 UTC: Alex Yakunin's client received Dmitrii's
stream at 30 fps the whole time, but nothing decoded/presented — black tile.
Diag signature: throughput + growing `ReceiverSkipToLive Σ`, but no Demand row,
no DECODER section (both derive from `OnPlaybackStats`, which had gone silent).
No existing watchdog can fire in this state:

- `StreamStallTimer` resets on **arriving** chunks — they kept arriving.
- The decoder hang watchdog arms only when `pending > 0` — a wedge before/after
  the decoder keeps `pending == 0`.
- The present stage (`await writer.ready` / canvas draw) has no watchdog.
- `PlayerWorker.stop(streamId)` awaits `player.whenDone()` unboundedly; a present
  await that never resolves ignores the abort, so `whenDone` hangs, the stream
  stays in the worker `players` map, and every restart `start()` throws
  `PlayerWorker.start: stream … already running` — a silent forever-failing
  restart loop.
- Side effect: the C# QC pruned the stream as `stats-silent`, it flapped in/out
  of `requestedMap`, and `LiveVideoStreams.GetUpgradedStreams` treated every
  re-add as an upgrade → a 15-minute `RequestKeyFrame` (PLI) flood on the sender.

We have no access to user consoles, so every new signal must reach either the
**server logs** (stall-note path) or the **diag panel** (user can screenshot).

## Global Constraints

- Read `docs/CODING_STYLE.md` rules before writing code: no `Async` suffix, no XML docs on members, default to **no comments**, mixed brace style, 120-char lines.
- All new liveness timestamps use `Date.now()` (shared domain between worker and main thread; `performance.now()` origins differ per context).
- Stall-note strings are truncated server-side at 500 chars — keep a single wedge diagnosis ≤ ~200 chars.
- No new C#↔JS or client↔server wire contracts — reuse `OnPlaybackStalled`, `getStats`, `RemoteStreamDiagnostics`.
- TypeScript validation after every TS task: `npm run build:Verify` (or trigger the `/server-loop` rebuild when running).
- TS unit tests: `npm run test:unit` (vitest; scope with `npx vitest run tests/ts/unit/playback`).
- C# build: `dotnet build ActualChat.CI.slnf`.

## Reuse

**Existing abstractions reused (looked up in `docs/api-index-ts.md` / code):**
- `PlayerStats` (`Services/Video/frame-envelopes.ts`) — extended, not duplicated; already flows to worker `getStats`, the latency tap, and `RemoteStreamDiagnostics.decoderStats` (diag panel) for free.
- `IPlayerWorker.getStats(streamId)` RPC — already message-driven, answers during a wedge; no new RPC.
- `OnPlaybackStalled` (`VideoTrackPlayer.razor:222` → `VideoQualityUI.Playback.cs:147` → stall note → `LiveVideoStreams.ChangePlaybackQuality` WARN log) — the wedge diagnosis rides this existing path to server logs.
- The restart machinery in `video-player.ts` (`settleCurrentAttempt`, `runPlaybackLoop`, `fallbackFromMstgToCanvas`) — self-heal triggers it instead of introducing a parallel restart path.
- `RingBuffer<T>` from `actuallab-core` (`src/nodejs/src/actuallab-core/ring-buffer.ts`) — breadcrumb storage.
- `abortPromise`, `delayAsync` from `actuallab-core` — abort races / bounded waits.
- `VideoDiagnosticsModal.razor` + `collectRemoteStreamDiagnostics` — new rows only, no new panel plumbing.
- C#: `SystemClock`/`Moment`, existing `_qualityBySession` cleanup loop in `LiveVideoStreams`.

**Reusability of new components:**
- `WedgeDetector` (new, pure TS): reads the `PlayerStats` shape, so it is video-playback-specific. Options considered: (a) `Services/Video/playback/wedge-detector.ts` (local), (b) a generic "progress freeze detector" in `src/nodejs/src/actuallab-core`. **Recommendation: (a) local** — the generic core would be ~10 lines of delta tracking while all the value is in the PlayerStats-specific classification; promoting a near-empty abstraction is not worth it.
- Breadcrumb ring: reuses `RingBuffer<T>`; only a ~10-line `pushBreadcrumb` helper remains, local to `video-player.ts`. Nothing new to share.
- C# changes are edits inside `LiveVideoStreams` (one static helper + a dictionary); no new types.

## File Structure

- `src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes.ts` — new `PlayerStats` fields.
- `src/dotnet/UI.Blazor.App/Services/Video/operators/downlink-tap.ts` — arrival stamp.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer.ts` — buffer-pull stamp.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/video-decoder-bridge.ts` — submit/decode stamps + ready count.
- `src/dotnet/UI.Blazor.App/Services/Video/operators/decode.ts` — feed-pump state.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/present-pacer.ts` — present states/stamps + abort support.
- `src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg.ts`, `present-canvas.ts` — sink-level await states, `stats` + `abortSignal` pass-through.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/player.ts` — plumb `stats`/`abortSignal` into present operators.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker.ts` — bounded `stop`.
- `src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector.ts` — NEW pure detector.
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts` — liveness poller, breadcrumbs, self-heal, diagnostics fields.
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor` — Liveness + Stall rows.
- `src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs` — PLI re-add damping.
- Tests: `tests/ts/unit/playback/*.test.ts`, `tests/ts/unit/operators/*.test.ts`, `tests/Streaming.UnitTests/LiveVideoStreamsTest.cs`.

---

### Task 1: Liveness stamps in PlayerStats (worker pipeline)

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes.ts` (PlayerStats ~line 106, createEmptyPlayerStats ~line 201)
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/operators/downlink-tap.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/video-decoder-bridge.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/operators/decode.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/present-pacer.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/operators/present-canvas.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/player.ts` (pass `stats` into present ops)
- Test: `tests/ts/unit/playback/encoded-frame-buffer.test.ts`, `tests/ts/unit/playback/video-decoder-bridge.test.ts`, `tests/ts/unit/playback/present-pacer.test.ts`

**Interfaces:**
- Produces (used by Tasks 2–4): new `PlayerStats` fields:
  `lastArrivalAtMs`, `lastBufferPullAtMs`, `lastSubmitAtMs`, `lastDecodeOutAtMs`, `lastPresentAttemptAtMs`, `lastPresentAtMs` (all `number`, `Date.now()` domain, `-1` = never), `decodedReadyCount: number`, `feedPumpState: string`, `presentState: string`.

- [ ] **Step 1: Extend PlayerStats**

In `frame-envelopes.ts`, append to `interface PlayerStats` (after `decoderQueueSize`):

```ts
    // Liveness stamps (Date.now() domain; -1 == never). Written at each stage
    // boundary so a wedge diagnosis can name the frozen stage without console
    // access. Read by the main-thread WedgeDetector via worker getStats.
    lastArrivalAtMs: number;
    lastBufferPullAtMs: number;
    lastSubmitAtMs: number;
    lastDecodeOutAtMs: number;
    lastPresentAttemptAtMs: number;
    lastPresentAtMs: number;
    decodedReadyCount: number;
    feedPumpState: string;
    presentState: string;
```

And to `createEmptyPlayerStats()`:

```ts
        lastArrivalAtMs: -1,
        lastBufferPullAtMs: -1,
        lastSubmitAtMs: -1,
        lastDecodeOutAtMs: -1,
        lastPresentAttemptAtMs: -1,
        lastPresentAtMs: -1,
        decodedReadyCount: 0,
        feedPumpState: 'idle',
        presentState: 'idle',
```

- [ ] **Step 2: Arrival stamp in downlink-tap**

In `downlink-tap.ts`, inside the `tap(...)` callback, right after `const wallNowMs = now();`:

```ts
        stats.lastArrivalAtMs = wallNowMs;
```

(`now` here is already `Date.now`-based — correct domain.)

- [ ] **Step 3: Buffer-pull stamp in EncodedFrameBuffer**

In `encoded-frame-buffer.ts`, replace `tryPull()`:

```ts
    tryPull(): ArrivedChunk | null {
        if (!this.isReady()) return null;
        const chunk = this.chunks.shift() ?? null;
        if (chunk && this.stats)
            this.stats.lastBufferPullAtMs = Date.now();
        return chunk;
    }
```

- [ ] **Step 4: Submit/decode stamps + ready count in VideoDecoderBridge**

In `video-decoder-bridge.ts`:
- In `submit()`, right after the successful `dec.decode(arrived.chunk);` (next to `arrived.stats.chunksReceived++;`):

```ts
                arrived.stats.lastSubmitAtMs = Date.now();
```

- In `onFrame()`, next to `stats.framesDecoded++;`:

```ts
        stats.lastDecodeOutAtMs = Date.now();
        stats.decodedReadyCount = this.ready.length + 1;
```

(`+1` because the envelope is pushed to `ready` a few lines below; alternatively place the assignment after `this.ready.push(envelope);` and drop the `+1` — do that, it's clearer.)

- In `tryPull()`, after a successful shift:

```ts
        if (frame && this.currentStats)
            this.currentStats.decodedReadyCount = this.ready.length;
```

- [ ] **Step 5: Feed-pump state in decode.ts**

In `runFeedPump()` in `decode.ts`, the pump owns a chunk between pull and submit; write states through `arrived.stats`:

```ts
                const arrived = result.value;
                arrived.stats.feedPumpState = 'blocked';
                for (;;) {
                    if (isStopped()) { closeEncodedChunk(arrived.chunk); return; }
                    if (bridge.error)
                        break;
                    if (canSubmit())
                        break;
                    const whenSpace = bridge.whenSpaceAvailable.wait();
                    if (canSubmit())
                        continue;
                    await Promise.race([whenSpace, abortWait]);
                }
                if (isStopped()) { closeEncodedChunk(arrived.chunk); return; }

                arrived.stats.feedPumpState = 'submitting';
                bridge.submit(arrived);
                arrived.stats.feedPumpState = 'awaiting-source';
```

- [ ] **Step 6: Present states in the pacer and sinks**

In `present-pacer.ts`:
- Add to `PresentPacerOptions`: nothing (stats ride each `DecodedFrame`).
- At the top of the `for await` body (after `const now = nowFn();`):

```ts
                    decoded.stats.presentState = 'pacing';
```

- Right before `presented = await sink.present(decoded.frame);`:

```ts
                    decoded.stats.lastPresentAttemptAtMs = Date.now();
                    decoded.stats.presentState = 'sink-await';
```

- In the success branch (next to `decoded.stats.presented++;`):

```ts
                            decoded.stats.presentState = 'presented';
                            decoded.stats.lastPresentAtMs = Date.now();
```

In `present-mstg.ts`: add `stats?: PlayerStats` to `MstgPresentOptions` (import the type from `../frame-envelopes`), and in the sink:

```ts
                async present(frame: VideoFrame): Promise<boolean> {
                    try {
                        if ((writer.desiredSize ?? 1) <= 0) {
                            if (opts.stats) opts.stats.presentState = 'mstg:awaiting-ready';
                            await writer.ready;
                        }
                        if (opts.stats) opts.stats.presentState = 'mstg:writing';
                        await writer.write(frame);
                        return true;
                    } catch (e: unknown) {
                        warnLog?.log('mstgPresent: write failed', e);
                        throw e;
                    }
                },
```

In `present-canvas.ts`: add `stats?: PlayerStats` to `CanvasPresentOptions`, and in the sink set `opts.stats.presentState = 'canvas:converting'` right before `await convertToBitmap(frame)` and `'canvas:drawing'` right before each `drawImage` call (both branches).

In `player.ts` (`Player.start`), pass `stats` into both present operators:

```ts
            present = mstgPresent({
                getWriter: () => writer,
                getBufferSpanMs,
                targetSpanMs,
                getAudioCaptureOffsetMs,
                stats,
            });
```

(same for `canvasPresent`).

- [ ] **Step 7: Write failing tests, then run them**

Add to `tests/ts/unit/playback/encoded-frame-buffer.test.ts`:

```ts
it('stamps lastBufferPullAtMs on successful pull', () => {
    const stats = createEmptyPlayerStats();
    const buffer = new EncodedFrameBuffer({ targetSpanMs: 0, stats });
    buffer.push(makeChunk({ isKeyFrame: true, timeMs: 0 }));
    expect(stats.lastBufferPullAtMs).toBe(-1);
    expect(buffer.tryPull()).not.toBeNull();
    expect(stats.lastBufferPullAtMs).toBeGreaterThan(0);
});
```

(reuse the file's existing chunk factory; adjust names to what it defines.)

Add to `tests/ts/unit/playback/video-decoder-bridge.test.ts` (reuse its fake decoder):

```ts
it('stamps submit/decode liveness and tracks decodedReadyCount', () => {
    // arrange a bridge with the file's fake decoder; submit one keyframe,
    // fire onFrame via the fake, then:
    expect(stats.lastSubmitAtMs).toBeGreaterThan(0);
    expect(stats.lastDecodeOutAtMs).toBeGreaterThan(0);
    expect(stats.decodedReadyCount).toBe(1);
    bridge.tryPull();
    expect(stats.decodedReadyCount).toBe(0);
});
```

Add to `tests/ts/unit/playback/present-pacer.test.ts`:

```ts
it('tracks presentState through a successful present', async () => {
    // drive one frame through a mock 'ok' sink (existing helpers), then:
    expect(stats.presentState).toBe('presented');
    expect(stats.lastPresentAtMs).toBeGreaterThan(0);
    expect(stats.lastPresentAttemptAtMs).toBeGreaterThan(0);
});
```

Run: `npx vitest run tests/ts/unit/playback` — expect the new tests FAIL before the implementation steps, PASS after (if you wrote tests last, at minimum verify they fail when the stamp lines are commented out once).

- [ ] **Step 8: Validate + commit**

Run: `npx vitest run tests/ts/unit` then `npm run build:Verify`. Expected: all green.

```bash
git add -A src/dotnet/UI.Blazor.App tests/ts
git commit -m "feat(video): per-stage liveness stamps in PlayerStats"
```

---

### Task 2: WedgeDetector (pure)

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector.ts`
- Test: `tests/ts/unit/playback/wedge-detector.test.ts`

**Interfaces:**
- Consumes: `PlayerStats` fields from Task 1.
- Produces (used by Task 3):

```ts
export type WedgeKind = 'decode-wedge' | 'present-wedge';
export interface WedgeDiagnosis { kind: WedgeKind; frozenMs: number; detail: string; }
export class WedgeDetector {
    constructor(wedgeAfterMs?: number);            // default 6_000
    onSample(stats: PlayerStats, nowMs: number): WedgeDiagnosis | null;
    get hasProgress(): boolean;                    // true when last sample advanced `presented`
    reset(): void;
}
```

- [ ] **Step 1: Write failing tests**

Create `tests/ts/unit/playback/wedge-detector.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { WedgeDetector } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector';
import { createEmptyPlayerStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

function statsAt(over: Partial<ReturnType<typeof createEmptyPlayerStats>>) {
    return { ...createEmptyPlayerStats(), ...over };
}

describe('WedgeDetector', () => {
    it('reports present-wedge when bytes+decoded advance but presented is frozen', () => {
        const d = new WedgeDetector(6_000);
        expect(d.onSample(statsAt({ bytesReceived: 100, framesDecoded: 10, presented: 5 }), 0)).toBeNull();
        expect(d.onSample(statsAt({ bytesReceived: 200, framesDecoded: 20, presented: 5 }), 3_000)).toBeNull();
        const diag = d.onSample(statsAt({
            bytesReceived: 300, framesDecoded: 30, presented: 5,
            presentState: 'mstg:awaiting-ready', feedPumpState: 'blocked',
        }), 7_000);
        expect(diag?.kind).toBe('present-wedge');
        expect(diag?.frozenMs).toBeGreaterThanOrEqual(6_000);
        expect(diag?.detail).toContain('mstg:awaiting-ready');
    });

    it('reports decode-wedge when bytes advance but decoded and presented are frozen', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, framesDecoded: 10, presented: 5 }), 0);
        const diag = d.onSample(statsAt({ bytesReceived: 900, framesDecoded: 10, presented: 5 }), 7_000);
        expect(diag?.kind).toBe('decode-wedge');
    });

    it('stays silent while presented advances', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        expect(d.onSample(statsAt({ bytesReceived: 200, presented: 6 }), 7_000)).toBeNull();
        expect(d.hasProgress).toBe(true);
    });

    it('stays silent when the source is starved (bytes frozen too)', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        expect(d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 20_000)).toBeNull();
    });

    it('progress resets the freeze window', () => {
        const d = new WedgeDetector(6_000);
        d.onSample(statsAt({ bytesReceived: 100, presented: 5 }), 0);
        d.onSample(statsAt({ bytesReceived: 200, presented: 6 }), 5_000);
        expect(d.onSample(statsAt({ bytesReceived: 300, presented: 6 }), 9_000)).toBeNull();
    });
});
```

Run: `npx vitest run tests/ts/unit/playback/wedge-detector.test.ts` — expect FAIL (module not found).

- [ ] **Step 2: Implement**

Create `src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector.ts`:

```ts
import type { PlayerStats } from '../frame-envelopes';

export type WedgeKind = 'decode-wedge' | 'present-wedge';

export interface WedgeDiagnosis {
    kind: WedgeKind;
    frozenMs: number;
    detail: string;
}

const DEFAULT_WEDGE_AFTER_MS = 6_000;

// Detects "frames keep arriving but decode/present froze" — the silent state no
// pipeline-internal watchdog covers (arrival resets the stream stall timer, the
// decoder hang watchdog needs in-flight chunks, present has no watchdog). Pure:
// feed it getStats() snapshots; deltas only, so clock domains don't matter.
export class WedgeDetector {
    private readonly wedgeAfterMs: number;
    private lastBytes = -1;
    private lastDecoded = -1;
    private lastPresented = -1;
    private presentFrozenSinceMs: number | null = null;
    private decodeFrozenSinceMs: number | null = null;
    private lastSampleHadProgress = false;

    constructor(wedgeAfterMs?: number) {
        this.wedgeAfterMs = wedgeAfterMs ?? DEFAULT_WEDGE_AFTER_MS;
    }

    get hasProgress(): boolean {
        return this.lastSampleHadProgress;
    }

    reset(): void {
        this.lastBytes = -1;
        this.lastDecoded = -1;
        this.lastPresented = -1;
        this.presentFrozenSinceMs = null;
        this.decodeFrozenSinceMs = null;
        this.lastSampleHadProgress = false;
    }

    onSample(stats: PlayerStats, nowMs: number): WedgeDiagnosis | null {
        const first = this.lastBytes < 0;
        const bytesAdvanced = stats.bytesReceived > this.lastBytes;
        const decodedAdvanced = stats.framesDecoded > this.lastDecoded;
        const presentedAdvanced = stats.presented > this.lastPresented;
        this.lastBytes = stats.bytesReceived;
        this.lastDecoded = stats.framesDecoded;
        this.lastPresented = stats.presented;
        this.lastSampleHadProgress = presentedAdvanced;
        if (first) {
            this.presentFrozenSinceMs = nowMs;
            this.decodeFrozenSinceMs = nowMs;
            return null;
        }
        if (presentedAdvanced)
            this.presentFrozenSinceMs = nowMs;
        if (decodedAdvanced)
            this.decodeFrozenSinceMs = nowMs;
        if (!bytesAdvanced)
            return null;

        const presentFrozenMs = nowMs - (this.presentFrozenSinceMs ?? nowMs);
        if (presentFrozenMs < this.wedgeAfterMs)
            return null;

        const decodeFrozenMs = nowMs - (this.decodeFrozenSinceMs ?? nowMs);
        const kind: WedgeKind = decodeFrozenMs >= this.wedgeAfterMs ? 'decode-wedge' : 'present-wedge';
        return { kind, frozenMs: presentFrozenMs, detail: formatDetail(stats, nowMs) };
    }
}

function age(nowMs: number, atMs: number): string {
    return atMs < 0 ? 'never' : `${((nowMs - atMs) / 1000).toFixed(1)}s`;
}

function formatDetail(s: PlayerStats, nowMs: number): string {
    return `present=${s.presentState} pump=${s.feedPumpState}`
        + ` ages[arr=${age(nowMs, s.lastArrivalAtMs)} pull=${age(nowMs, s.lastBufferPullAtMs)}`
        + ` sub=${age(nowMs, s.lastSubmitAtMs)} dec=${age(nowMs, s.lastDecodeOutAtMs)}`
        + ` presAtt=${age(nowMs, s.lastPresentAttemptAtMs)} pres=${age(nowMs, s.lastPresentAtMs)}]`
        + ` buf=${s.encodedQueueCount} inflight=${s.decoderQueueSize} ready=${s.decodedReadyCount}`
        + ` decoded=${s.framesDecoded} presented=${s.presented}`;
}
```

- [ ] **Step 3: Run tests**

Run: `npx vitest run tests/ts/unit/playback/wedge-detector.test.ts` — expect PASS.

- [ ] **Step 4: Validate + commit**

Run `npm run build:Verify`.

```bash
git add src/dotnet/UI.Blazor.App/Services/Video/playback/wedge-detector.ts tests/ts/unit/playback/wedge-detector.test.ts
git commit -m "feat(video): pure WedgeDetector for receiver pipeline freezes"
```

---

### Task 3: Main-thread liveness poller + breadcrumbs + wedge stall reports

This task delivers the user-facing goal: wedge diagnoses in **server logs** with zero console access.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts`

**Interfaces:**
- Consumes: `WedgeDetector` (Task 2), `IPlayerWorker.getStats`, existing `blazorRef.invokeMethodAsync('OnPlaybackStalled', …)`.
- Produces (used by Tasks 4 & 6): on the `VideoPlayer` class:
  `private pushBreadcrumb(note: string): void`,
  `private lastWedgeDiagnosis: string | null`, `private lastWedgeAtMs: number`,
  `private readonly breadcrumbs: RingBuffer<{ atMs: number; note: string }>`,
  `protected onWedgeDetected(diagnosis: WedgeDiagnosis): void` (Task 6 extends its body).

- [ ] **Step 1: Add fields and breadcrumb helper**

In `video-player.ts` imports add:

```ts
import { RingBuffer } from 'actuallab-core';
import { WedgeDetector, type WedgeDiagnosis } from '../../Services/Video/playback/wedge-detector';
```

(match the import specifier style used by the file's existing `actuallab-core` imports; if it imports via a different alias, follow that.)

Next to the `audioCaTimer` field (~line 266):

```ts
    private livenessTimer: ReturnType<typeof setInterval> | null = null;
    private readonly wedgeDetector = new WedgeDetector();
    private lastWedgeDiagnosis: string | null = null;
    private lastWedgeAtMs = -1;
    private lastWedgeReportAtMs = -1;
    private readonly breadcrumbs = new RingBuffer<{ atMs: number; note: string }>(24);
```

Private helper (place near the other private helpers):

```ts
    private pushBreadcrumb(note: string): void {
        this.breadcrumbs.pushTailAndMoveHeadIfFull({ atMs: Date.now(), note });
        debugLog?.log(`[${this.streamId}] ${note}`);
    }
```

- [ ] **Step 2: Start/stop the poller**

Where `audioCaTimer` is started (~line 351), add:

```ts
        this.livenessTimer = setInterval(() => void this.checkLiveness(), 2_000);
```

Where `audioCaTimer` is cleared (~line 1276), add:

```ts
        if (this.livenessTimer !== null) {
            clearInterval(this.livenessTimer);
            this.livenessTimer = null;
        }
```

- [ ] **Step 3: Implement checkLiveness**

```ts
    private async checkLiveness(): Promise<void> {
        if (!this.playerWorker || !this.isPlaying)
            return;
        let stats: PlayerStats;
        try {
            stats = await this.playerWorker.getStats(this.streamId);
        } catch {
            return;
        }
        const now = Date.now();
        const diag = this.wedgeDetector.onSample(stats, now);
        if (this.wedgeDetector.hasProgress) {
            this.lastWedgeDiagnosis = null;
            return;
        }
        if (!diag)
            return;

        this.lastWedgeDiagnosis = `${diag.kind}: frozen ${(diag.frozenMs / 1000).toFixed(1)}s; ${diag.detail}`;
        this.lastWedgeAtMs = now;
        this.onWedgeDetected(diag);
    }
```

- [ ] **Step 4: Implement onWedgeDetected (diagnostics-only for now)**

```ts
    // Self-heal lands in a follow-up task; for now: breadcrumb + server-side
    // stall report (the only channel that reaches dev logs without a console).
    protected onWedgeDetected(diag: WedgeDiagnosis): void {
        const note = this.lastWedgeDiagnosis!;
        this.pushBreadcrumb(note);
        const now = Date.now();
        if (this.lastWedgeReportAtMs > 0 && now - this.lastWedgeReportAtMs < 30_000)
            return;

        this.lastWedgeReportAtMs = now;
        void this.blazorRef.invokeMethodAsync('OnPlaybackStalled', `wedge: ${note}`)
            .catch((e: unknown) => warnLog?.log('OnPlaybackStalled error:', e));
    }
```

- [ ] **Step 5: Breadcrumb the existing lifecycle events**

Add `this.pushBreadcrumb(...)` calls at:
- `startPull` entry: `` this.pushBreadcrumb(`startPull backend=${this.renderBackend.kind}`); ``
- `runPlaybackLoop` catch block (next to the existing `warnLog` at ~line 912): `` this.pushBreadcrumb(`attempt ${this.restartAttempts} failed: ${e.message}`); ``
- `fallbackFromMstgToCanvas` (~line 1126): `` this.pushBreadcrumb(`mstg->canvas fallback: ${reason}`); ``
- the worker `onStreamEnded` callback (~line 500): `` this.pushBreadcrumb(`stream ended: ${reason}`); ``
- the worker `onError` callback (~line 508): `` this.pushBreadcrumb(`player-error: ${error}`); ``
- the codec-exclusion branch in `runPlaybackLoop` (~line 895): `` this.pushBreadcrumb(`codec exclusion requested: ${this.codecCategory}`); ``
- `runOneAttempt`'s finally, timing the worker stop:

```ts
            if (this.workerStreamActive) {
                const stopStartedMs = Date.now();
                try { await this.playerWorker.stop(streamId); }
                catch { /* ignore */ }
                const stopMs = Date.now() - stopStartedMs;
                if (stopMs > 1_000)
                    this.pushBreadcrumb(`worker.stop took ${stopMs}ms`);
                this.workerStreamActive = false;
            }
```

- [ ] **Step 6: Validate + commit**

Run `npm run build:Verify`. Expected: clean.

Manual check (optional if a dev server is running): open a two-user video call via `/debug-ui` helpers, confirm server log shows no new WARNs during normal playback (`gcloud`/local logs), i.e. no false wedge positives at steady state, including while a tile is paused/hidden (panel collapse pauses streams — `bytesReceived` freezes too, so the detector stays silent by design).

```bash
git add src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts
git commit -m "feat(video): main-thread wedge poller + lifecycle breadcrumbs, wedge diagnoses ride stall notes to server logs"
```

---

### Task 4: Diag panel — Liveness + Stall section

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts` (`RemoteStreamDiagnostics` ~line 74, `getDiagnosticsAsync` ~line 1047)
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoDiagnosticsModal.razor` (inbound stream card, after the Decoder group ~line 546)

**Interfaces:**
- Consumes: Task 3 fields (`lastWedgeDiagnosis`, `lastWedgeAtMs`, `breadcrumbs`), Task 1 `PlayerStats` liveness fields (already inside `decoderStats`).
- Produces: `RemoteStreamDiagnostics.stallDiagnosis: string | null`, `stallAgeMs: number`, `breadcrumbs: string[]` (formatted, oldest first).

- [ ] **Step 1: Extend RemoteStreamDiagnostics + getDiagnosticsAsync**

Interface additions:

```ts
    stallDiagnosis: string | null;
    stallAgeMs: number;
    breadcrumbs: string[];
```

In `getDiagnosticsAsync`, add to the returned object:

```ts
            stallDiagnosis: this.lastWedgeDiagnosis,
            stallAgeMs: this.lastWedgeAtMs > 0 ? Date.now() - this.lastWedgeAtMs : -1,
            breadcrumbs: this.breadcrumbs.toArray()
                .map(b => `[-${((Date.now() - b.atMs) / 1000).toFixed(0)}s] ${b.note}`),
```

- [ ] **Step 2: Render in the modal**

In `VideoDiagnosticsModal.razor`, in the inbound stream card after the Decoder group (`@if (Hub.VideoQualityUI.InboundDecoderHealthByStream…` block, ~line 546–560), add a Liveness row (from `decoderStats` inside `rdPeek` — follow the existing `GetNum(...)`/JSON access pattern the file uses for `rdPeek` fields) and a Stall group:

```razor
@if (hasDiag && TryGetString(rdPeek, "stallDiagnosis") is { Length: > 0 } stallDiagnosis) {
    <div class="diag-group-header">Stall</div>
    <div class="diag-row">
        <span class="diag-label">Last wedge</span>
        <span class="diag-value multiline">@stallDiagnosis (@FormatAgeSec(GetNum(rdPeek, "stallAgeMs")) ago)</span>
    </div>
}
@if (hasDiag && TryGetStringArray(rdPeek, "breadcrumbs") is { Count: > 0 } crumbs) {
    <div class="diag-row">
        <span class="diag-label">Lifecycle</span>
        <span class="diag-value multiline">@string.Join("\n", crumbs)</span>
    </div>
}
```

Add the private helpers to the modal's `@code` section (near the existing `GetNum` helper; `rdPeek` is a `JsonElement`):

```csharp
    private static string? TryGetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static List<string>? TryGetStringArray(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in p.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                result.Add(s);
        return result;
    }

    private static string FormatAgeSec(double ageMs)
        => ageMs < 0 ? "?" : $"{ageMs / 1000:0}s";
```

Also add a per-stream Liveness row using the `decoderStats` liveness fields (they arrive inside the same `rdPeek.decoderStats` JSON): show `arr/pull/dec/pres` ages the same way `formatDetail` does — this makes the frozen stage visible at a glance even before a wedge fires.

- [ ] **Step 3: Validate + commit**

Run `npm run build:Verify` and `dotnet build ActualChat.CI.slnf`. Expected: clean.

```bash
git add src/dotnet/UI.Blazor.App/Components/VideoPanel
git commit -m "feat(video-diag): stall diagnosis, lifecycle breadcrumbs, per-stage liveness in the inbound panel"
```

---

### Task 5: Hang-proof teardown (abortable present + bounded worker stop)

Without this, self-heal restarts can hang exactly like the incident did (`whenDone` never resolves → `PlayerWorker.start: stream already running` forever).

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/present-pacer.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/operators/present-mstg.ts`, `present-canvas.ts` (pass-through option)
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/player.ts` (pass `abortSignal`)
- Modify: `src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker.ts` (`stop`, ~line 282)
- Test: `tests/ts/unit/playback/present-pacer.test.ts`

**Interfaces:**
- Consumes: `abortPromise`, `delayAsync` from `actuallab-core` (already imported in these modules' neighbors).
- Produces: `PresentPacerOptions.abortSignal?: AbortSignal` (also on `MstgPresentOptions`/`CanvasPresentOptions`); `PlayerWorker.stop` resolves within `STOP_HANG_TIMEOUT_MS = 8_000` even for a wedged pipeline.

- [ ] **Step 1: Failing test — a sink that never resolves must not hang the pacer past abort**

Add to `tests/ts/unit/playback/present-pacer.test.ts`:

```ts
it('unwinds on abort while the sink is stuck', async () => {
    const abortController = new AbortController();
    const sink: PresentSink = {
        present: () => new Promise<boolean>(() => { /* never resolves */ }),
    };
    const op = presentPacer({
        createSink: () => sink,
        getBufferSpanMs: () => 0,
        targetSpanMs: 0,
        abortSignal: abortController.signal,
    });
    const done = count(pipe(sourceOf([frame(0)]), op));  // reuse the file's frame/source helpers
    setTimeout(() => abortController.abort(new Error('test abort')), 10);
    await expect(done).resolves.toBeDefined();
});
```

Run: `npx vitest run tests/ts/unit/playback/present-pacer.test.ts` — expect the new test to TIME OUT / FAIL.

- [ ] **Step 2: Implement abortable present in the pacer**

In `present-pacer.ts`:
- Add `abortSignal?: AbortSignal;` to `PresentPacerOptions`.
- Import `abortPromise` from `actuallab-core`.
- In `impl(...)`, before the loop:

```ts
        const abortSignal = opts.abortSignal;
        const abortWait: Promise<'aborted'> | null = abortSignal
            ? abortPromise(abortSignal).catch((): 'aborted' => 'aborted')
            : null;
```

- Replace the present await:

```ts
                    sink ??= createSink();
                    let presented = false;
                    let aborted = false;
                    try {
                        const presentP = sink.present(decoded.frame);
                        if (abortWait) {
                            presentP.catch(() => { /* late rejection of an abandoned present */ });
                            const winner = await Promise.race([presentP, abortWait]);
                            if (winner === 'aborted') {
                                aborted = true;
                                return;
                            }
                            presented = winner;
                        } else {
                            presented = await presentP;
                        }
                    } finally {
                        if (!presented && !aborted)
                            decoded.stats.pendingPresenterDrops++;
                        ...
```

(keep the existing success branch unchanged; the `return` exits through the outer `finally` which disposes the sink, and the per-frame `finally` closes the frame.)

- Also race the sleep: `await delayFn(sleepMs)` → `await (abortWait ? Promise.race([delayFn(sleepMs), abortWait]) : delayFn(sleepMs));` followed by `if (abortSignal?.aborted) return;` — bounded anyway, but this shortens stop latency.

- In `present-mstg.ts` and `present-canvas.ts`: add `abortSignal?: AbortSignal` to the options interfaces and forward to `presentPacer({ …, abortSignal: opts.abortSignal })`.

- In `player.ts` (`Player.start`), pass `abortSignal` (the existing `abortController.signal`) into both `mstgPresent` and `canvasPresent` options.

- [ ] **Step 3: Run the test**

Run: `npx vitest run tests/ts/unit/playback/present-pacer.test.ts` — expect PASS. Run the whole suite: `npx vitest run tests/ts/unit` — the operator tests (`present-mstg.test.ts`, `present-canvas.test.ts`, `encoded-buffer.test.ts`, `pipeline-integration.test.ts`) must stay green.

- [ ] **Step 4: Bounded worker stop**

In `player-worker.ts`, imports: add `delayAsync` (from `actuallab-core`, matching the file's import style). Replace the tail of `stop(streamId)`:

```ts
        const player = players.get(streamId);
        if (!player) return;
        locallyStopped.add(streamId);
        player.stop();
        const unwound = await Promise.race([
            player.whenDone().then(() => true, () => true),
            delayAsync(STOP_HANG_TIMEOUT_MS).then(() => false),
        ]);
        if (!unwound && players.get(streamId) === player) {
            // A pipeline that ignores abort must not block restarts forever:
            // abandon it (source already aborted) so start() can reuse the slot.
            warnLog?.log(`stop: pipeline ${streamId} did not unwind in ${STOP_HANG_TIMEOUT_MS}ms — abandoning`);
            players.delete(streamId);
        }
```

with module-level `const STOP_HANG_TIMEOUT_MS = 8_000;`. Apply the same bounded pattern to the `streamId === undefined` branch and `disposePlayerWorker` (`Promise.allSettled` → race with one shared `delayAsync(STOP_HANG_TIMEOUT_MS)`).

Check the `player.whenDone()` cleanup handler registered in `start()` (~line 182): it must tolerate the map entry already being gone (`players.get(opts.streamId) === player` guard before delete — add it if missing).

- [ ] **Step 5: Validate + commit**

Run `npx vitest run tests/ts/unit` and `npm run build:Verify`. Expected: clean.

```bash
git add -A src/dotnet/UI.Blazor.App tests/ts
git commit -m "fix(video): abortable present await + bounded worker stop so a wedged pipeline can't block restarts"
```

---

### Task 6: Self-heal — restart the player on a confirmed wedge

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts` (`onWedgeDetected` from Task 3; MSTG hook wiring ~line 574; `fallbackFromMstgToCanvas` ~line 1122)

**Interfaces:**
- Consumes: `settleCurrentAttempt`, `fallbackFromMstgToCanvas`, restart loop (existing); Task 5 guarantees the restart cannot hang.
- Produces: `private recreateMstgBackend(reason: string): void`, `private wireMstgBackendHooks(backend: OffThreadRenderBackend): void`.

Escalation ladder (rationale: a sender republish cured the incident because it forced a
**full receiver rebuild** — each rung rebuilds progressively more of the receiver):
1. Wedge #1 → attempt restart. `startWorkerForAttempt` already creates a fresh
   MSTG generator/track per attempt, so this is: fresh pipeline + fresh track,
   same `<video>` element.
2. Wedge #2 → recreate the `<video>` element + `OffThreadRenderBackend`, then
   attempt restart — the receiver-side equivalent of a republish; stays on MSTG.
3. Wedge #3+ → `fallbackFromMstgToCanvas` — last resort, for a machine whose
   MSTG/compositor path is broken for the whole session.

- [ ] **Step 1: Extract MSTG hook wiring**

In `initPlayerWorker` (~line 574) the MSTG backend's hooks are wired inline. Extract into a method so the recreate rung can re-wire them:

```ts
    private wireMstgBackendHooks(mstgBackend: OffThreadRenderBackend): void {
        mstgBackend.onFocusedChange = (focused: boolean) => { void focused; };
        mstgBackend.onPlaybackStalled = report => {
            const details =
                `watchdog:${report.reason}, readyState=${report.readyState}, ` +
                `videoWH=${report.videoWidth}x${report.videoHeight}, tracks=[${report.tracks}]`;
            void this.blazorRef.invokeMethodAsync('OnPlaybackStalled', details)
                .catch((e: unknown) => warnLog?.log('OnPlaybackStalled error:', e));
            this.fallbackFromMstgToCanvas(details);
        };
    }
```

Replace the inline wiring at ~line 574 with `this.wireMstgBackendHooks(this.renderBackend as OffThreadRenderBackend);` (keep the surrounding `if (this.renderBackend.kind === 'mstg')` guard).

- [ ] **Step 2: Add the element/backend recreate rung**

```ts
    private replaceVideoElement(): void {
        const replacement = this.videoEl.cloneNode(false) as HTMLVideoElement;
        this.videoEl.replaceWith(replacement);
        this.videoEl = replacement;
    }

    // Receiver-side equivalent of a sender republish for the render layer: a
    // fresh <video> + backend clears element/compositor state a per-attempt
    // fresh track can't reach.
    private recreateMstgBackend(reason: string): void {
        if (this.renderBackend.kind !== 'mstg')
            return;

        this.pushBreadcrumb(`mstg backend recreate: ${reason}`);
        this.renderBackend.dispose();
        this.replaceVideoElement();
        const backend = new OffThreadRenderBackend(this.videoEl);
        this.wireMstgBackendHooks(backend);
        this.renderBackend = backend;
        this.applyBackendVisibility(this.canvas, this.videoEl);
    }
```

Also update the stale invariant comment at ~line 312 ("Resolve off videoEl (never swapped, unlike canvas)") — `videoEl` can now be swapped by `recreateMstgBackend`; `placeholderEl` stays valid because it's resolved from the parent, which the swap preserves. Verify `OffThreadRenderBackend`'s constructor re-applies `muted`/`playsInline`/`autoplay` (it does) so the cloned element needs no extra setup.

- [ ] **Step 3: Extend onWedgeDetected with the ladder**

```ts
    protected onWedgeDetected(diag: WedgeDiagnosis): void {
        const note = this.lastWedgeDiagnosis!;
        this.pushBreadcrumb(note);
        const now = Date.now();
        if (this.lastWedgeReportAtMs < 0 || now - this.lastWedgeReportAtMs >= 30_000) {
            this.lastWedgeReportAtMs = now;
            void this.blazorRef.invokeMethodAsync('OnPlaybackStalled', `wedge: ${note}`)
                .catch((e: unknown) => warnLog?.log('OnPlaybackStalled error:', e));
        }
        this.wedgeRestartCount++;
        this.wedgeDetector.reset();
        this.pushBreadcrumb(`wedge restart #${this.wedgeRestartCount}`);
        if (this.renderBackend.kind === 'mstg') {
            if (this.wedgeRestartCount === 2)
                this.recreateMstgBackend(`wedge x${this.wedgeRestartCount}`);
            else if (this.wedgeRestartCount >= 3) {
                this.fallbackFromMstgToCanvas(`wedge x${this.wedgeRestartCount}`);
                return;
            }
        }

        this.settleCurrentAttempt({ kind: 'error', error: new Error(`wedge: ${note}`) });
    }
```

(`fallbackFromMstgToCanvas` settles the attempt itself, hence the early `return`; `recreateMstgBackend` does not, so the shared settle below it runs.)

Add the field next to the Task 3 fields:

```ts
    private wedgeRestartCount = 0;
```

Reset it where playback proves healthy — in `onWorkerLatencyReport`, next to the existing `if (this.restartAttempts > 0) this.restartAttempts = 0;` (~line 1199):

```ts
        if (this.wedgeRestartCount > 0)
            this.wedgeRestartCount = 0;
```

Notes for the implementer:
- `settleCurrentAttempt({kind:'error'})` rejects `runOneAttempt`'s `settled`, whose `finally` runs the (now bounded) `worker.stop`, and `runPlaybackLoop` retries with its existing exponential backoff (≤3 s). No new restart machinery.
- The detector's freeze window (6 s) + the 2 s poll cadence means a wedge triggers a restart in ≤ 8 s. There is deliberately **no give-up cap**: retries are already backoff-bounded, and the incident showed a full rebuild recovers — a periodic retry is strictly better than a permanent black tile.

- [ ] **Step 2: Validate + commit**

Run `npm run build:Verify`. Expected: clean.

Manual verification (dev server + `/debug-ui`, two users, one publishing): in the receiver's console inject a wedge — e.g. temporarily patch `mstgPresent` to `await new Promise(() => {})` after N frames via `video-trace-kill-control.ts` if a suitable kill hook exists, otherwise add a temporary debug hook and REMOVE it before commit. Confirm: black tile recovers within ~10 s, server log shows one `wedge: present-wedge: …` WARN, breadcrumbs show `wedge restart #1`.

```bash
git add src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts
git commit -m "feat(video): self-heal — restart the player on a confirmed receiver wedge, escalate to canvas backend"
```

---

### Task 7: Server-side PLI damping for demand-map re-add flaps

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs` (`ChangePlaybackQuality` ~line 372, `GetUpgradedStreams` ~line 492, session cleanup ~line 444)
- Test: `tests/Streaming.UnitTests/LiveVideoStreamsTest.cs`

**Interfaces:**
- Produces: `internal static IEnumerable<(string StreamId, bool WasAbsent)> GetUpgradedStreams(...)` (signature change; existing tests updated).

- [ ] **Step 1: Write failing tests**

In `tests/Streaming.UnitTests/LiveVideoStreamsTest.cs`, update the two existing `GetUpgradedStreams` tests for the tuple return, and add:

```csharp
[Fact]
public void GetUpgradedStreams_MarksReAddedStreamAsWasAbsent()
{
    var previous = new ApiMap<string, ReceiveQuality>();
    var current = new ApiMap<string, ReceiveQuality> { ["s1"] = new(1) };
    var result = LiveVideoStreams.GetUpgradedStreams(previous, current).ToArray();
    result.Should().ContainSingle(x => x.StreamId == "s1" && x.WasAbsent);
}

[Fact]
public void GetUpgradedStreams_MarksGenuineUpgradeAsPresent()
{
    var previous = new ApiMap<string, ReceiveQuality> { ["s1"] = new(0) };
    var current = new ApiMap<string, ReceiveQuality> { ["s1"] = new(2) };
    var result = LiveVideoStreams.GetUpgradedStreams(previous, current).ToArray();
    result.Should().ContainSingle(x => x.StreamId == "s1" && !x.WasAbsent);
}
```

(match the file's existing assertion library/style; adjust `new(1)` to the actual `ReceiveQuality` constructor shape used there.)

Run: `dotnet test tests/Streaming.UnitTests --filter LiveVideoStreams` — expect FAIL (compile error on tuple).

- [ ] **Step 2: Implement**

Change `GetUpgradedStreams`:

```csharp
    internal static IEnumerable<(string StreamId, bool WasAbsent)> GetUpgradedStreams(
        ApiMap<string, ReceiveQuality>? previous,
        ApiMap<string, ReceiveQuality> current)
    {
        foreach (var (streamId, quality) in current) {
            var wasAbsent = previous is null || !previous.TryGetValue(streamId, out var old);
            var oldQuality = wasAbsent ? ReceiveQuality.Lowest : previous![streamId];
            if (quality.LayerId > oldQuality.LayerId)
                yield return (streamId, wasAbsent);
        }
    }
```

Add near the other private fields (~line 25):

```csharp
    private static readonly TimeSpan ReAddPliCooldown = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(Session Session, string StreamId), Moment> _lastReAddPliAt = new();
```

In `ChangePlaybackQuality` (~line 372), replace the upgrade fan-out:

```csharp
        // A stream re-appearing in a viewer's map (stats-silent prune → re-add
        // flap) is indistinguishable from a fresh subscription here, and during
        // the 2026-07-24 receiver-wedge incident it PLI-flooded the sender for
        // 15 minutes. Genuine layer upgrades stay un-throttled; re-adds get a
        // per-(session, stream) cooldown.
        var now = SystemClock.Now;
        var upgradedStreams = GetUpgradedStreams(prevState?.QualityByStream, qualityByStream)
            .Where(x => !x.WasAbsent || TryStartReAddPli(session, x.StreamId, now))
            .Select(x => x.StreamId)
            .ToArray();
```

Add the private helper:

```csharp
    private bool TryStartReAddPli(Session session, string streamId, Moment now)
    {
        var key = (session, streamId);
        if (_lastReAddPliAt.TryGetValue(key, out var last) && now - last < ReAddPliCooldown)
            return false;

        _lastReAddPliAt[key] = now;
        return true;
    }
```

Extend the existing `_qualityBySession` cleanup pass (~line 444) to also prune `_lastReAddPliAt` entries older than 10 minutes:

```csharp
            foreach (var kv in _lastReAddPliAt)
                if (now - kv.Value > TimeSpan.FromMinutes(10))
                    _lastReAddPliAt.TryRemove(kv);
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Streaming.UnitTests --filter LiveVideoStreams` — expect PASS.

- [ ] **Step 4: Build + commit**

Run: `dotnet build ActualChat.CI.slnf` — expect clean.

```bash
git add src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs tests/Streaming.UnitTests/LiveVideoStreamsTest.cs
git commit -m "fix(streaming): cool down upgrade PLIs for demand-map re-adds so stats-silent flaps can't keyframe-flood the sender"
```

---

## Verification checklist (after all tasks)

1. `npx vitest run tests/ts/unit` — green.
2. `npm run build:Verify` — green.
3. `dotnet build ActualChat.CI.slnf` && `dotnet test tests/Streaming.UnitTests` — green.
4. Live smoke (dev, `/debug-ui`, 2 users): steady playback produces **no** wedge WARNs in server logs over ≥5 min, including panel collapse/expand and tab hide/show cycles.
5. Induced wedge (temporary debug hook): black tile self-heals ≤10 s; server log contains exactly one `wedge: …` WARN with `presentState`/ages naming the frozen stage; diag panel shows the Stall group + breadcrumbs.

## Explicitly out of scope (follow-ups)

- Worker-host recreate escalation (terminate + fresh `Worker`) if wedges survive the canvas fallback — add only if field data (the new WARNs) shows it's needed.
- The 05:30:09 simultaneous 4-viewer `CODEC_EXHAUSTED` on one sender's stream — separate sender-side investigation; the new liveness WARNs will capture receiver context if it recurs.
- Distinguishing `stats-silent` causes in C# — with Task 3 in place, a wedge WARN always precedes the prune, making the prune note self-explanatory.
