// Once `threshold` successful frame decodes have landed at the highest spatial
// layer seen so far, the codec is considered "proven" on this device — later
// errors are treated as transient and don't trigger codec exclusion. The streak
// resets when a new (higher) top layer first appears. Toggle the whole
// mechanism off via `UseCodecProofTracker = false`.

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
        if (this.proven)
            return;

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
        if (this.proven)
            return;

        this.framesAtHighestSinceReset = 0;
    }

    isProven(): boolean {
        return this.proven;
    }
}

class AlwaysUnprovenCodecProofTracker implements CodecProofTracker {
    noteFrameDecoded(_layerId: number): void { void _layerId; }
    noteDecoderError(): void { return; }
    isProven(): boolean { return false; }
}

export function createCodecProofTracker(
    framesUntilProven: number = FramesUntilCodecProven,
): CodecProofTracker {
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- keep the exported kill-switch literal-editable.
    if (!UseCodecProofTracker)
        return new AlwaysUnprovenCodecProofTracker();

    return new TopLayerCodecProofTracker(framesUntilProven);
}
