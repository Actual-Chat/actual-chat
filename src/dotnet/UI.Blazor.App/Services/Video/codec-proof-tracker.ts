// Once N successful frame decodes have landed at the highest spatial
// layer the decoder has produced, the codec is considered "proven" on
// this device. From that point on, transient decode failures no longer
// trip the codec-exclusion path — they get attributed to other issues
// (wire jitter, server hiccups, browser-side decoder quirks) and the
// pipeline restart-loop handles them in stride.
//
// Reset semantics: when a NEW (higher) spatial layer first appears,
// the streak resets — proven status follows the codec/layer pair the
// receiver actually intends to present. The previous (lower) top
// layer's proof status is irrelevant; HW that decodes 360p fine but
// stalls at 1080p is a codec problem we want to surface.
//
// Disable the whole proof mechanism via UseCodecProofTracker if we
// ever conclude that proven codecs still fail in the wild — a single
// flag-flip and the system reverts to the bounded-recovery behaviour.

export const UseCodecProofTracker = true;
export const FramesUntilCodecProven = 10;

export interface CodecProofTracker {
    noteFrameDecoded(layerId: number): void;
    noteDecoderError(): void;
    isProven(): boolean;
}

class TopLayerCodecProofTracker implements CodecProofTracker {
    private highestSeenLayerId = -1;
    private framesAtHighestSinceReset = 0;
    private proven = false;

    constructor(private readonly threshold: number) {}

    noteFrameDecoded(layerId: number): void {
        if (this.proven) return;
        if (layerId > this.highestSeenLayerId) {
            this.highestSeenLayerId = layerId;
            this.framesAtHighestSinceReset = 1;
        } else if (layerId === this.highestSeenLayerId) {
            this.framesAtHighestSinceReset++;
        }
        if (this.framesAtHighestSinceReset >= this.threshold)
            this.proven = true;
    }

    noteDecoderError(): void {
        if (this.proven) return;
        this.framesAtHighestSinceReset = 0;
    }

    isProven(): boolean {
        return this.proven;
    }
}

class AlwaysUnprovenCodecProofTracker implements CodecProofTracker {
    noteFrameDecoded(_layerId: number): void { /* identity */ }
    noteDecoderError(): void { /* identity */ }
    isProven(): boolean { return false; }
}

export function createCodecProofTracker(
    framesUntilProven: number = FramesUntilCodecProven,
): CodecProofTracker {
    if (!UseCodecProofTracker)
        return new AlwaysUnprovenCodecProofTracker();
    return new TopLayerCodecProofTracker(framesUntilProven);
}
