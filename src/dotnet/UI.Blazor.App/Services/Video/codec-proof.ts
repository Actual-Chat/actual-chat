import { getLogs } from 'logging';

const { infoLog } = getLogs('VideoPipeline');

export const IS_CODEC_PROOF_ENABLED = false;
export const FramesUntilCodecProven = 10;

export function isCodecProofEnabled(): boolean {
    return IS_CODEC_PROOF_ENABLED;
}

// Codec categories ('h264' | 'hevc' | 'av1' | 'vp9') we've already encoded or
// decoded enough frames of. When proof is enabled, failures for these codecs
// are treated as transient instead of excluding the codec for the session.
const provenEncoderCodecs = new Set<string>();
const provenDecoderCodecs = new Set<string>();

export function markEncoderCodecProven(category: string): void {
    if (!isCodecProofEnabled()) {
        infoLog?.log(`Encoder codec '${category}' proof ignored - codec-proof is disabled`);
        return;
    }
    if (provenEncoderCodecs.has(category))
        return;
    infoLog?.log(`Encoder codec '${category}' proven - exclusion suppressed for the rest of the session`);
    provenEncoderCodecs.add(category);
}

export function isEncoderCodecProven(category: string): boolean {
    return isCodecProofEnabled() && provenEncoderCodecs.has(category);
}

export function getProvenEncoderCodecs(): string[] {
    return isCodecProofEnabled() ? [...provenEncoderCodecs] : [];
}

export function markDecoderCodecProven(category: string): void {
    if (!isCodecProofEnabled()) {
        infoLog?.log(`Decoder codec '${category}' proof ignored - codec-proof is disabled`);
        return;
    }
    if (provenDecoderCodecs.has(category))
        return;
    infoLog?.log(`Decoder codec '${category}' proven - exclusion suppressed for the rest of the session`);
    provenDecoderCodecs.add(category);
}

export function isDecoderCodecProven(category: string): boolean {
    return isCodecProofEnabled() && provenDecoderCodecs.has(category);
}

export function getProvenDecoderCodecs(): string[] {
    return isCodecProofEnabled() ? [...provenDecoderCodecs] : [];
}

export interface CodecProofTracker {
    noteFrameDecoded(layerId: number): void;
    noteDecoderError(): void;
    isProven(): boolean;
}

// Once `threshold` successful frame decodes have landed at the highest spatial
// layer seen so far, the codec is considered proven on this device. The streak
// resets when a new higher top layer first appears.
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
    return isCodecProofEnabled()
        ? new TopLayerCodecProofTracker(framesUntilProven)
        : new AlwaysUnprovenCodecProofTracker();
}
