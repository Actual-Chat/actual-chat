// Capture-side backpressure valve for the recording pipeline.
//
// Concept: a single shared `FloodGate` flag lives between the capture
// source and its driver. The `floodGate(gate)` operator sits right
// after `mstpSource`. While `gate.isOpen` it passes captured frames
// through unchanged. While `gate.isClosed` (the gate is shut) it
// closes the captured `VideoFrame` and skips the yield — the encoder,
// downscaler, and wire stages stay idle.
//
// The gate is driven by `push-to-pull-buffer.ts`: when its bundle queue
// fills to `pushPullBufferSize / 2` slots, it calls `gate.close()`;
// when the queue drains below `pushPullBufferSize / 4`, it calls
// `gate.open()`. Hysteresis prevents flapping around the threshold.
//
// `gate.skipCount` is reported in stats (`floodGateSkipCount`) so QC
// can observe sustained backpressure — typically a sign the WebSocket
// has stalled and the publisher should consider tearing down or
// stepping down the encoder ladder.

import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

/**
 * Mutable open/closed flag shared by the `floodGate` operator and its
 * driver. Defaults to open (frames flow). The driver toggles state via
 * `open()` / `close()`; the operator polls `isOpen` per frame.
 */
export class FloodGate {
    private _isOpen = true;
    private _skipCount = 0;

    /** Total frames dropped at the gate since construction. */
    get skipCount(): number {
        return this._skipCount;
    }

    /** True when frames pass through. */
    get isOpen(): boolean {
        return this._isOpen;
    }

    /** Open the gate — captured frames are yielded again. */
    open(): void {
        this._isOpen = true;
    }

    /** Close the gate — captured frames are closed and skipped. */
    close(): void {
        this._isOpen = false;
    }

    /**
     * Increment the skip counter. Called by the operator only — the
     * driver should never invoke this directly.
     */
    recordSkip(): void {
        this._skipCount++;
    }
}

/**
 * `CapturedFrame → CapturedFrame`. Pass-through while `gate.isOpen`,
 * drop-and-close while closed. Drops increment `gate.skipCount`.
 *
 * Place this operator immediately after the capture source so closed
 * frames release the underlying GPU/CPU resource without traversing
 * any of the heavier downstream operators (downscale, encode, ...).
 */
export function floodGate(gate: FloodGate): PipeOperator<CapturedFrame, CapturedFrame> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            for await (const captured of source) {
                if (gate.isOpen) {
                    yield captured;
                    continue;
                }
                // Closed: release the underlying VideoFrame and skip.
                try { captured.frame.close(); } catch { /* ignore */ }
                gate.recordSkip();
            }
        }
    };
}
