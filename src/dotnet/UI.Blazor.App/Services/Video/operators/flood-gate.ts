// Capture-side backpressure valve. The gate is driven by push-to-pull-buffer.ts:
// it closes when the bundle queue hits pushPullBufferSize/2 and reopens below
// pushPullBufferSize/4 (hysteresis). Skips bypass encoder/downscaler/wire.

import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame } from '../frame-envelopes';

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
            for await (const captured of source) {
                if (gate.isOpen) {
                    yield captured;
                    continue;
                }
                try { captured.frame.close(); } catch { /* ignore */ }
                gate.recordSkip();
            }
        }
    };
}
