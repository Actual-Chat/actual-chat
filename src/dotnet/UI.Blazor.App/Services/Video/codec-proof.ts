import { getLogs } from 'logging';

const { infoLog } = getLogs('VideoPipeline');

// Encoder proof is on: with h264 runtime-excludable (Firefox, bugzil.la/1918769)
// it's the guard that keeps a transient failure of an already-shipping codec
// from permanently excluding it mid-session. Decoder proof stays off.
export const IS_ENCODER_CODEC_PROOF_ENABLED = true;
export const IS_DECODER_CODEC_PROOF_ENABLED = false;
export const FramesUntilCodecProven = 10;

export function isEncoderCodecProofEnabled(): boolean {
    return IS_ENCODER_CODEC_PROOF_ENABLED;
}

export function isDecoderCodecProofEnabled(): boolean {
    return IS_DECODER_CODEC_PROOF_ENABLED;
}

// Codec categories ('h264' | 'hevc' | 'av1' | 'vp9') we've already encoded or
// decoded enough frames of. When proof is enabled, failures for these codecs
// are treated as transient instead of excluding the codec for the session.
const provenEncoderCodecs = new Set<string>();
const provenDecoderCodecs = new Set<string>();
const disabledEncoderProofLogged = new Set<string>();
const disabledDecoderProofLogged = new Set<string>();

export function markEncoderCodecProven(category: string): void {
    if (!isEncoderCodecProofEnabled()) {
        if (!disabledEncoderProofLogged.has(category)) {
            disabledEncoderProofLogged.add(category);
            infoLog?.log(`Encoder codec '${category}' proof ignored - codec-proof is disabled`);
        }
        return;
    }

    if (provenEncoderCodecs.has(category))
        return;

    infoLog?.log(`Encoder codec '${category}' proven - exclusion suppressed for the rest of the session`);
    provenEncoderCodecs.add(category);
}

export function isEncoderCodecProven(category: string): boolean {
    return isEncoderCodecProofEnabled() && provenEncoderCodecs.has(category);
}

export function getProvenEncoderCodecs(): string[] {
    return isEncoderCodecProofEnabled() ? [...provenEncoderCodecs] : [];
}

export function markDecoderCodecProven(category: string): void {
    if (!isDecoderCodecProofEnabled()) {
        if (!disabledDecoderProofLogged.has(category)) {
            disabledDecoderProofLogged.add(category);
            infoLog?.log(`Decoder codec '${category}' proof ignored - codec-proof is disabled`);
        }
        return;
    }

    if (provenDecoderCodecs.has(category))
        return;

    infoLog?.log(`Decoder codec '${category}' proven - exclusion suppressed for the rest of the session`);
    provenDecoderCodecs.add(category);
}

export function isDecoderCodecProven(category: string): boolean {
    return isDecoderCodecProofEnabled() && provenDecoderCodecs.has(category);
}

export function getProvenDecoderCodecs(): string[] {
    return isDecoderCodecProofEnabled() ? [...provenDecoderCodecs] : [];
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
    return isDecoderCodecProofEnabled()
        ? new TopLayerCodecProofTracker(framesUntilProven)
        : new AlwaysUnprovenCodecProofTracker();
}
