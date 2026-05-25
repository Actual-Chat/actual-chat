// Capture-side backpressure valve. The gate is driven by push-to-pull-buffer.ts:
// it closes when the bundle queue hits pushPullBufferSize/2 and reopens below
// pushPullBufferSize/4 (hysteresis). Skips bypass encoder/downscaler/wire.

import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

const SKIP_RATE_WINDOW_MS = 1_000;

export class FloodGate {
    private _isOpen = true;
    private _skipCount = 0;

    get skipCount(): number {
        return this._skipCount;
    }

    get isOpen(): boolean {
        return this._isOpen;
    }

    open(): void {
        this._isOpen = true;
    }

    close(): void {
        this._isOpen = false;
    }

    // Operator-only: never call from the driver.
    recordSkip(): void {
        this._skipCount++;
    }
}

// Place immediately after the capture source so dropped frames release
// the underlying GPU/CPU resource without traversing heavier downstream stages.
export function floodGate(gate: FloodGate): PipeOperator<CapturedFrame, CapturedFrame> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            const skipTimestamps: number[] = [];
            const updateRate = (stats: CapturedFrame['stats']): void => {
                const nowMs = performance.now();
                while (skipTimestamps.length > 0
                    && nowMs - skipTimestamps[0] > SKIP_RATE_WINDOW_MS)
                    skipTimestamps.shift();
                stats.floodGateSkipPerSec = skipTimestamps.length;
            };
            for await (const captured of source) {
                if (gate.isOpen) {
                    updateRate(captured.stats);
                    yield captured;
                    continue;
                }
                try { captured.frame.close(); } catch { /* ignore */ }
                gate.recordSkip();
                skipTimestamps.push(performance.now());
                updateRate(captured.stats);
            }
        }
    };
}
