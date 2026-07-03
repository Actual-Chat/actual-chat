// Idle keepalive. getDisplayMedia delivers frames only when the captured
// content changes, so a static screen starves the wire and the server's
// stream-silence watchdog (StreamSilenceCheckInterval × K = 10 s) tears the
// stream down mid-share. When no frame has passed for `periodMs`, this
// operator re-emits a clone of the last one at that cadence so bundles keep
// flowing. Sits between floodGate and stampCaptureTime: injected frames get
// a fresh monotonic capturedAt downstream, and temporalPace still drops them
// in the no-viewer fps=0 state.

import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

export interface KeepAliveOptions {
    // <= 0 disables the operator (camera path).
    periodMs: number;
    // FloodGate.isOpen — no injection while closed (backpressure implies
    // bundles are already in flight, so the wire isn't silent).
    isGateOpen?: () => boolean;
    // Test injectables.
    nowFn?: () => number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
    cloneFrame?: (src: VideoFrame, timestampUs: number) => VideoFrame;
}

interface Retained {
    frame: VideoFrame;
    timestampUs: number;
    realFrameAtMs: number;
    stats: CapturedFrame['stats'];
}

export function keepAlive(opts: KeepAliveOptions): PipeOperator<CapturedFrame, CapturedFrame> {
    const { periodMs, isGateOpen } = opts;
    if (periodMs <= 0)
        return source => from(source);

    const nowFn = opts.nowFn ?? (() => performance.now());
    const setTimeoutFn = opts.setTimeoutFn
        ?? ((cb: () => void, ms: number) => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn
        ?? ((handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>));
    const cloneFrame = opts.cloneFrame
        ?? ((src: VideoFrame, timestampUs: number) => new VideoFrame(src, { timestamp: timestampUs }));
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<CapturedFrame>): AsyncIterable<CapturedFrame> {
        const it = source[Symbol.asyncIterator]();
        let retained: Retained | null = null;
        let lastActivityAtMs = nowFn();
        let injectedCount = 0;
        let lastOutputIndex = -1;
        let pendingNext: Promise<IteratorResult<CapturedFrame>> | null = null;
        try {
            for (;;) {
                pendingNext ??= it.next();
                let result: IteratorResult<CapturedFrame>;
                if (retained === null) {
                    result = await pendingNext;
                } else {
                    const waitMs = Math.max(0, periodMs - (nowFn() - lastActivityAtMs));
                    let timerHandle: unknown = null;
                    const idleTick = new Promise<null>(resolve => {
                        timerHandle = setTimeoutFn(() => resolve(null), waitMs);
                    });
                    let raced: IteratorResult<CapturedFrame> | null;
                    try {
                        raced = await Promise.race([pendingNext, idleTick]);
                    } finally {
                        clearTimeoutFn(timerHandle);
                    }
                    if (raced === null) {
                        // Stale wake: a frame arrived after the timer was armed.
                        if (nowFn() - lastActivityAtMs < periodMs)
                            continue;

                        lastActivityAtMs = nowFn();
                        if (isGateOpen && !isGateOpen())
                            continue;

                        const timestampUs = retained.timestampUs
                            + Math.round((nowFn() - retained.realFrameAtMs) * 1000);
                        const injected: CapturedFrame = {
                            frame: cloneFrame(retained.frame, timestampUs),
                            capturedAt: { timeMs: 0, epoch: 0 },
                            // Unique, strictly-increasing index: the wire derives
                            // isKeyFrame from keyFrameIndex === index, so a reused
                            // index would mis-classify a later real delta as a keyframe.
                            index: ++lastOutputIndex,
                            dropTrace: [],
                            sourceWidth: 0,
                            sourceHeight: 0,
                            forceKeyframe: false,
                            rotation: 0,
                            stats: retained.stats,
                        };
                        injectedCount++;
                        retained.stats.keepAliveFramesInjected++;
                        yield injected;
                        continue;
                    }
                    result = raced;
                }
                pendingNext = null;
                if (result.done)
                    return;

                const envelope = result.value;
                let mustClose = true;
                try {
                    if (retained !== null)
                        try { retained.frame.close(); } catch { /* ignore */ }
                    retained = null;
                    const nowMs = nowFn();
                    try {
                        retained = {
                            frame: envelope.frame.clone(),
                            timestampUs: envelope.frame.timestamp,
                            realFrameAtMs: nowMs,
                            stats: envelope.stats,
                        };
                    } catch { /* no retained frame — no injection until the next one */ }
                    lastActivityAtMs = nowMs;
                    const output = injectedCount === 0
                        ? envelope
                        : { ...envelope, index: envelope.index + injectedCount };
                    lastOutputIndex = output.index;
                    mustClose = false;
                    yield output;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        } finally {
            if (retained !== null)
                try { retained.frame.close(); } catch { /* ignore */ }
            try { await it.return?.(); } catch { /* ignore */ }
        }
    }
}
