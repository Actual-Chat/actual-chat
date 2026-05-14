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

export class StreamStallTimer {
    private timeoutId: ReturnType<typeof setTimeout> | null = null;
    // Surfaced for callers that drain the pipeline and need to rethrow
    // the stall as the terminal error rather than the abort-controller
    // signal value.
    error: Error | null = null;

    constructor(private readonly opts: StreamStallTimerOptions) {}

    clear(): void {
        if (this.timeoutId !== null)
            clearTimeout(this.timeoutId);
        this.timeoutId = null;
    }

    reset(): void {
        if (!isStreamStallTimerEnabled())
            return;
        if (this.opts.timeoutMs <= 0)
            return;
        if (this.opts.isPaused())
            return;
        this.clear();
        this.timeoutId = setTimeout(() => {
            this.error = new Error(
                `Player stream stalled: no frames received for ${this.opts.timeoutMs}ms`);
            if (!this.opts.abortController.signal.aborted)
                this.opts.abortController.abort(this.error);
        }, this.opts.timeoutMs);
    }
}
