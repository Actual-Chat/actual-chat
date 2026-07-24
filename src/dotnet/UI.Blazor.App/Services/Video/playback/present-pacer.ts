import { from, type PipeOperator } from 'ix-ext';
import { RunningEMA } from 'math';
import { abortPromise, delayAsync } from 'actuallab-core';
import { DeviceInfo } from 'device-info';
import { aggregateDropTrace, updatePlaybackRateEma, type DecodedFrame } from '../frame-envelopes';
import { FrameDropStage } from '../frame-drop-trace';
import { BufferSpanMeter, type BufferSpanMeterOptions } from './buffer-span-meter';

const PRESENT_SKIP_RATIO_EMA_ALPHA = 0.1;

// Mobile caps catch-up at 60 — 120 fps decode/present bursts burn power for
// an invisible latency win on a phone display.
const MAX_FPS = DeviceInfo.isMobile ? 60 : 120;
const MIN_FPS = 10;
const MIN_DURATION_MS = 1000 / MAX_FPS;
const MAX_DURATION_MS = 1000 / MIN_FPS;

// Land each write this many ms before its nominal slot. setTimeout fires late,
// never early, so without a lead the actual enqueue can slip past the display's
// vsync deadline — and the <video> (which recomposites at refresh and shows the
// latest enqueued frame) then holds the previous frame one extra refresh. The
// schedule anchor (lastWriteAt) stays at the nominal slot, so this is a one-time
// phase lead, not a rate change (no drift). ~quarter of a 60 Hz refresh.
export const PRESENT_LEAD_MS = 4;

// Beyond this backlog the bounded catch-up can't drain it; switch to skip-mode.
const CATCHUP_BUDGET_MS = 4_000;

// Catch-up is a bounded overspeed, not a burst: presents stay evenly spaced at
// natural/rate, with the rate ramping 1→MAX_CATCHUP_RATE as the backlog grows
// (rate = 1 + extra/GAIN). The old MAX_FPS collapse drained faster but landed
// several frames in one vsync and then waited — the display coalesced the burst,
// reading as ~12fps judder while the write counter said 30.
const MAX_CATCHUP_RATE = 2;
const CATCHUP_RATE_GAIN_MS = 500;

// One rendered frame. Returns true on success; may throw to abort the pipe
// (mstg write failure / canvas draw failure). The pacer bumps
// pendingPresenterDrops whenever present did not succeed, including the throw.
export interface PresentSink {
    present(frame: VideoFrame): Promise<boolean>;
    dispose?(): void;
}

export interface PresentPacerOptions {
    // Lazy: invoked once, on the first frame that reaches present (never during a
    // pure skip-storm), so the underlying sink is created at the same point the
    // old per-backend operators did `writer ??= getWriter()`.
    createSink: () => PresentSink;
    getBufferSpanMs: () => number;
    targetSpanMs: number;
    trackSkipRatio?: boolean;
    nowFn?: () => number;
    delayFn?: (ms: number) => Promise<void>;
    meter?: BufferSpanMeterOptions;
    // Skip-mode only engages once the overflow has persisted this long, and only
    // disengages after the overflow has cleared this long — damps flapping at the
    // CATCHUP_BUDGET_MS threshold. 0 reproduces the instantaneous legacy behavior.
    holdMs?: number;
    // Audio-master gate: current audio capture-point in this stream's capture-time
    // domain (same domain as DecodedFrame.capturedAt.timeMs), or null when there's
    // no paired audio. When set, video may sprint to catch up only while it's
    // behind this point; it never sprints past audio.
    getAudioCaptureOffsetMs?: () => number | null;
    // Bounds the present await and the pacing sleep so a wedged sink (dead MSTG
    // track, a stuck bitmap conversion) can't hang teardown forever.
    abortSignal?: AbortSignal;
}

const DEFAULT_HOLD_MS = 500;

// Stop the MAX_FPS catch-up once video is within this margin of the audio
// capture-point, so video converges toward audio and stays slightly behind it
// (audio leads — the safe side) rather than sprinting past. Audio is the
// A/V-sync master: dropped video frames are imperceptible, audio rate changes
// are not.
const AUDIO_MASTER_LEAD_MS = 50;

// Per frame: extra = max(0, bufferSpan - targetSpan).
// Skip (extra > CATCHUP_BUDGET_MS && now - lastWriteAt < MIN_DURATION_MS):
//   leave lastWriteAt untouched so the next frame still hits its slot.
// Catch-up (extra ≥ one source frame): duration = natural / catchUpRate —
//   an evenly-spaced overspeed bounded by MAX_CATCHUP_RATE, never a burst.
// Steady (extra == 0): duration = clamp(naturalDelta, MIN, MAX) — schedule
//   advances by exactly the source delta, so it tracks capture time without
//   anchor drift across the run.
export function presentPacer(opts: PresentPacerOptions): PipeOperator<DecodedFrame, void> {
    const { createSink, getBufferSpanMs, targetSpanMs } = opts;
    const getAudioCaptureOffsetMs = opts.getAudioCaptureOffsetMs;
    const trackSkipRatio = opts.trackSkipRatio ?? true;
    const holdMs = opts.holdMs ?? DEFAULT_HOLD_MS;
    const nowFn = opts.nowFn ?? ((): number => performance.now());
    const delayFn = opts.delayFn ?? ((ms): Promise<void> => delayAsync(ms));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<DecodedFrame>): AsyncIterable<void> {
        const abortSignal = opts.abortSignal;
        const abortWait: Promise<'aborted'> | null = abortSignal
            ? abortPromise(abortSignal).catch((): 'aborted' => 'aborted')
            : null;
        let sink: PresentSink | null = null;
        let lastWriteAt: number | null = null;
        let prevCapturedAt: number | null = null;
        let prevCapturedEpoch: number | null = null;
        const presentSkipRatio = new RunningEMA(0, 1, PRESENT_SKIP_RATIO_EMA_ALPHA);
        const meter = new BufferSpanMeter(opts.meter);
        let skipEngaged = false;
        let skipCandidate = false;
        let skipCandidateSinceMs: number | null = null;
        try {
            for await (const decoded of source) {
                try {
                    const now = nowFn();
                    decoded.stats.presentState = 'pacing';
                    if (prevCapturedEpoch !== null && decoded.capturedAt.epoch !== prevCapturedEpoch) {
                        lastWriteAt = null;
                        prevCapturedAt = null;
                        meter.reset();
                        skipEngaged = false;
                        skipCandidate = false;
                        skipCandidateSinceMs = null;
                    }
                    const extraMs = Math.max(0, meter.sample(getBufferSpanMs(), now) - targetSpanMs);

                    // Last-resort overflow backstop: drop frames once the span runs
                    // CATCHUP_BUDGET_MS past target. Deliberately NOT audio-gated, unlike
                    // the time-compression sprint below — it sits well above the 600ms
                    // audio-anchored encoded-buffer skip, so the "no sprint past audio"
                    // invariant covers the sprint, not this drop.
                    const rawSkip = extraMs > CATCHUP_BUDGET_MS;
                    if (rawSkip !== skipCandidate) {
                        skipCandidate = rawSkip;
                        skipCandidateSinceMs = now;
                    } else if (skipCandidateSinceMs !== null && now - skipCandidateSinceMs >= holdMs) {
                        skipEngaged = skipCandidate;
                        skipCandidateSinceMs = null;
                    }

                    if (skipEngaged
                    && lastWriteAt !== null
                    && now - lastWriteAt < MIN_DURATION_MS) {
                        decoded.stats.pendingPresenterDrops++;
                        if (trackSkipRatio) {
                            presentSkipRatio.appendSample(1);
                            decoded.stats.presentSkipRatio = presentSkipRatio.value;
                        }
                        prevCapturedAt = decoded.capturedAt.timeMs;
                        continue;
                    }

                    if (trackSkipRatio) {
                        presentSkipRatio.appendSample(0);
                        decoded.stats.presentSkipRatio = presentSkipRatio.value;
                    }

                    // Audio-master gate: only sprint while video is behind audio.
                    const audioOffsetMs = getAudioCaptureOffsetMs?.() ?? null;
                    const mayCatchUp = audioOffsetMs === null
                        || decoded.capturedAt.timeMs < audioOffsetMs - AUDIO_MASTER_LEAD_MS;

                    let durationMs: number;
                    if (lastWriteAt === null || prevCapturedAt === null) {
                        durationMs = 0;
                    } else {
                        const natural = decoded.capturedAt.timeMs - prevCapturedAt;
                        // Catch up only when the backlog is at least one SOURCE frame
                        // beyond target. A sub-one-frame excess is the normal
                        // steady-state buffer; for a low-fps source (e.g. 10fps, 100ms
                        // frames) the 30fps-based targetSpanMs sits ~one frame below
                        // the natural buffer, so an `extraMs > 0` trigger would speed up
                        // every frame. Scaling the threshold to the actual frame
                        // interval keeps catch-up for genuine backlogs while letting a
                        // steady low-fps source play at 1x.
                        const catchUpThresholdMs = Math.max(MIN_DURATION_MS, natural);
                        if (extraMs >= catchUpThresholdMs && mayCatchUp) {
                            const catchUpRate = Math.min(MAX_CATCHUP_RATE, 1 + extraMs / CATCHUP_RATE_GAIN_MS);
                            durationMs = Math.max(MIN_DURATION_MS, natural / catchUpRate);
                        } else {
                            durationMs = Math.max(MIN_DURATION_MS, Math.min(MAX_DURATION_MS, natural));
                        }
                    }

                    const baseAt: number = lastWriteAt ?? now;
                    let nextWriteAt: number = baseAt + durationMs;
                    if (nextWriteAt - now > MAX_DURATION_MS)
                        nextWriteAt = now + MAX_DURATION_MS;

                    if (nextWriteAt > now) {
                        const sleepMs = nextWriteAt - now - PRESENT_LEAD_MS;
                        if (sleepMs > 0)
                            await (abortWait ? Promise.race([delayFn(sleepMs), abortWait]) : delayFn(sleepMs));
                        if (abortSignal?.aborted)
                            return;
                    }

                    sink ??= createSink();
                    let presented = false;
                    let aborted = false;
                    try {
                        decoded.stats.lastPresentAttemptAtMs = Date.now();
                        decoded.stats.presentState = 'sink-await';
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
                        if (!presented && !aborted) {
                            decoded.stats.pendingPresenterDrops++;
                        } else if (presented) {
                            // Carry-forward Option A: every successful present
                            // attributes the upstream trace AND any presenter
                            // drops accumulated since the previous accept to
                            // the cumulative histogram, then bumps `presented`.
                            aggregateDropTrace(decoded.stats, decoded.dropTrace);
                            if (decoded.stats.pendingPresenterDrops > 0) {
                                decoded.stats.dropTrace.set(
                                    FrameDropStage.ReceiverPresent,
                                    (decoded.stats.dropTrace.get(FrameDropStage.ReceiverPresent) ?? 0)
                                    + decoded.stats.pendingPresenterDrops);
                                decoded.stats.pendingPresenterDrops = 0;
                            }
                            decoded.stats.presented++;
                            decoded.stats.presentState = 'presented';
                            decoded.stats.lastPresentAtMs = Date.now();
                            updatePlaybackRateEma(decoded.stats, decoded.capturedAt, performance.now());
                        }
                    }
                    lastWriteAt = nextWriteAt;
                    prevCapturedAt = decoded.capturedAt.timeMs;
                    prevCapturedEpoch = decoded.capturedAt.epoch;
                } finally {
                    try { decoded.frame.close(); } catch { /* already closed */ }
                }
            }
        } finally {
            try { sink?.dispose?.(); } catch { /* ignore */ }
        }
    }
}
