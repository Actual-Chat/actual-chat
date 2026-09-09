export interface CaptureStallSample {
    isRecording: boolean;
    isScreencast: boolean;
    isTabBackgrounded: boolean;
    isTrackLive: boolean;
    isRecoveryScheduled: boolean;
    framesCaptured: number;
}

const DEFAULT_STALL_AFTER_MS = 3_000;

// Recovers a reclaimed encoder / wedged source the frame-driven path can't
// see: a foreground capture flatline means no frame reaches the dead encoder
// to throw, so the recorder must be restarted. Screencast is exempt — a still
// screen legitimately captures nothing, and its keep-alive keeps feeding the
// encoder clones, so a dead encoder there does throw on the frame path.
// Pure: feed it health-monitor samples; deltas only.
export class CaptureStallDetector {
    private readonly stallAfterMs: number;
    private lastFramesCaptured = -1;
    private stallSinceMs = 0;

    constructor(stallAfterMs?: number) {
        this.stallAfterMs = stallAfterMs ?? DEFAULT_STALL_AFTER_MS;
    }

    reset(): void {
        this.lastFramesCaptured = -1;
        this.stallSinceMs = 0;
    }

    onSample(sample: CaptureStallSample, nowMs: number): boolean {
        const previous = this.lastFramesCaptured;
        this.lastFramesCaptured = sample.framesCaptured;
        const sourceShouldRun = sample.isRecording
            && !sample.isScreencast
            && !sample.isTabBackgrounded
            && sample.isTrackLive;
        if (!sourceShouldRun || previous < 0 || sample.isRecoveryScheduled
            || sample.framesCaptured > previous) {
            this.stallSinceMs = 0;
            return false;
        }
        if (this.stallSinceMs === 0) {
            this.stallSinceMs = nowMs;
            return false;
        }
        if (nowMs - this.stallSinceMs < this.stallAfterMs)
            return false;

        this.stallSinceMs = 0;
        return true;
    }
}
