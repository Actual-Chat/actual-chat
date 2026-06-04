// Worker-side no-chunk-arrives stall timer. Aborts the playback pipeline
// when no chunks land in `timeoutMs` (caller passes 30 s today). The
// current pipeline self-heals on Rpc reconnect, stream-end events, and
// epoch resets — this is a belt-and-suspenders fallback for the case
// where chunks silently dry up. Flip IS_STREAM_STALL_TIMER_ENABLED to
// false to confirm the pipeline is healthy without the safety net.

export const IS_STREAM_STALL_TIMER_ENABLED = true;
export const isStreamStallTimerEnabled = (): boolean => IS_STREAM_STALL_TIMER_ENABLED;

export interface StreamStallTimerOptions {
    timeoutMs: number;
    abortController: AbortController;
    // QC may park a stream indefinitely (Float/Hide); while paused, the
    // server drops every frame, so the no-chunk heuristic would tear down
    // a perfectly healthy paused pipeline. Caller wires this to the same
    // "expectedPaused" flag the render backend uses.
    isPaused: () => boolean;
}

// `reset()` runs per arrived chunk, so it must stay O(1): it only stamps the
// last-activity time. A single low-frequency interval polls that stamp against
// `timeoutMs`, instead of re-arming a per-chunk setTimeout (which churned
// thousands of install/clear pairs a second on the playback worker).
export class StreamStallTimer {
    private intervalId: ReturnType<typeof setInterval> | null = null;
    private lastResetAt = 0;
    // Surfaced for callers that drain the pipeline and need to rethrow
    // the stall as the terminal error rather than the abort-controller
    // signal value.
    error: Error | null = null;

    constructor(private readonly opts: StreamStallTimerOptions) {}

    clear(): void {
        if (this.intervalId !== null)
            clearInterval(this.intervalId);
        this.intervalId = null;
    }

    reset(): void {
        if (!isStreamStallTimerEnabled())
            return;
        if (this.opts.timeoutMs <= 0)
            return;
        this.lastResetAt = performance.now();
        if (this.intervalId === null) {
            const checkMs = Math.min(1000, Math.max(50, this.opts.timeoutMs));
            this.intervalId = setInterval(() => this.tick(), checkMs);
        }
    }

    private tick(): void {
        const now = performance.now();
        if (this.opts.isPaused()) {
            // Treat paused as activity: the server drops every frame while
            // parked, so resume must get a full window rather than tripping.
            this.lastResetAt = now;
            return;
        }
        if (now - this.lastResetAt <= this.opts.timeoutMs)
            return;
        this.error = new Error(
            `Player stream stalled: no frames received for ${this.opts.timeoutMs}ms`);
        if (!this.opts.abortController.signal.aborted)
            this.opts.abortController.abort(this.error);
        this.clear();
    }
}
