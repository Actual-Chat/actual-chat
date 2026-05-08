import type { MonotonicTime } from 'clocks';
import { from, type PipeOperator } from 'ix-ext';
import { AsyncVideoEncoder, isAsyncVideoEncoderResetError } from '../adapters';
import { closeEncodedChunk, type CapturedFrame, type EncodedFrame, type SimulcastBundle } from '../frame-envelopes';

// Subset of `VideoEncoderConfig` the operator threads into the encoded
// envelope; the full WebCodecs config lives inside `createEncoder`.
export interface EncoderConfigPerLayer {
    width: number;
    height: number;
    bitrate: number;
    framerate: number;
    codec: string;
}

// Per-layer encoder input. `index` and `capturedAt` ride the
// `AsyncVideoEncoder` boundary back into the produced `EncodedFrame`.
export interface EncodeInput {
    frame: VideoFrame;
    index: number;
    capturedAt: MonotonicTime;
}

// `buildOutput` (inside the factory) fills `chunk`, `metadata`,
// `capturedAt`, `index`, `encodedWidth/Height`. The operator patches
// `layerId`, `sourceWidth/Height`, `stats` from the bundle.
export type EncoderFactory = (
    config: EncoderConfigPerLayer,
    layerId: number,
) => AsyncVideoEncoder<EncodeInput, EncodedFrame>;

export interface EncodeOptions {
    /** One config per layer, bottom-first. Length MUST equal
     *  `bundle.extras.length + 1`; mismatch is a hard error. */
    configs: readonly EncoderConfigPerLayer[];
    createEncoder: EncoderFactory;
}

/**
 * `SimulcastBundle → EncodedFrame`. Per bundle: lazy-init one encoder
 * per layer on first iteration, submit all layers in parallel,
 * `Promise.allSettled` so one rejection doesn't leak the others'
 * already-produced chunks.
 *
 * Yield order is bottom-first (L0, L1, …, LN-1). Realtime RPC compaction
 * treats only L0 keyframes as restart points; emitting them first keeps
 * the rest of a simulcast keyframe burst in the suffix sent after a skip.
 */
export function encode(opts: EncodeOptions): PipeOperator<SimulcastBundle, EncodedFrame> {
    if (opts.configs.length === 0)
        throw new Error('encode: configs must contain at least one layer');

    const configs = opts.configs.slice();
    const createEncoder = opts.createEncoder;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedFrame> {
            const encoders: AsyncVideoEncoder<EncodeInput, EncodedFrame>[] = [];
            let forceKeyframeNext = false;
            try {
                for await (const bundle of source) {
                    const layerCount = bundle.extras.length + 1;
                    if (layerCount !== configs.length) {
                        closeBundleFrames(bundle);
                        throw new Error(
                            `encode: bundle has ${layerCount} layer(s), expected ${configs.length}`);
                    }
                    if (encoders.length === 0) {
                        try {
                            for (let layerId = 0; layerId < configs.length; layerId++) {
                                encoders.push(createEncoder(configs[layerId], layerId));
                            }
                        } catch (e) {
                            closeBundleFrames(bundle);
                            throw e;
                        }
                    }
                    const keyFrame = bundle.primary.forceKeyframe || forceKeyframeNext;
                    // Bottom-first: extras[0..N-2] then primary (top tier).
                    const layerFrames: CapturedFrame[] = [...bundle.extras, bundle.primary];
                    const promises: Promise<EncodedFrame>[] = [];
                    try {
                        for (let layerId = 0; layerId < layerCount; layerId++) {
                            const cf = layerFrames[layerId];
                            const enc = encoders[layerId];
                            const encInput: EncodeInput = {
                                frame: cf.frame,
                                index: cf.index,
                                capturedAt: cf.capturedAt,
                            };
                            promises.push(enc.encode(encInput, { keyFrame }));
                        }
                    } catch (e) {
                        closeCapturedFrames(layerFrames);
                        throw e;
                    }
                    const settled = await Promise.allSettled(promises);
                    const rejected = settled.filter((r): r is PromiseRejectedResult => r.status === 'rejected');
                    if (rejected.length > 0) {
                        for (const result of settled) {
                            if (result.status === 'fulfilled')
                                closeEncodedFrame(result.value);
                        }
                        if (rejected.every(r => isAsyncVideoEncoderResetError(r.reason))) {
                            forceKeyframeNext = true;
                            continue;
                        }
                        throw rejected.find(r => !isAsyncVideoEncoderResetError(r.reason))!.reason;
                    }
                    const results = settled.map((result): EncodedFrame => {
                        if (result.status !== 'fulfilled')
                            throw new Error('encode: unreachable rejected result after rejection check');

                        return result.value;
                    });
                    let closeFromIndex = 0;
                    try {
                        for (let layerId = 0; layerId < results.length; layerId++) {
                            const partial = results[layerId];
                            let mustClose = true;
                            try {
                                const cfg = configs[layerId];
                                const completed: EncodedFrame = {
                                    chunk: partial.chunk,
                                    metadata: partial.metadata,
                                    capturedAt: bundle.primary.capturedAt,
                                    index: bundle.primary.index,
                                    layerId: layerId,
                                    sourceWidth: bundle.primary.sourceWidth,
                                    sourceHeight: bundle.primary.sourceHeight,
                                    encodedWidth: cfg.width,
                                    encodedHeight: cfg.height,
                                    stats: bundle.stats,
                                };
                                bundle.stats.chunksEncoded++;
                                bundle.stats.bytesEncoded += completed.chunk.byteLength;
                                if (completed.chunk.type === 'key')
                                    bundle.stats.keyframesEncoded++;
                                forceKeyframeNext = false;

                                mustClose = false;
                                closeFromIndex = layerId + 1;
                                yield completed;
                            } finally {
                                if (mustClose) {
                                    closeEncodedFrame(partial);
                                    closeFromIndex = layerId + 1;
                                }
                            }
                        }
                    } finally {
                        for (let i = closeFromIndex; i < results.length; i++) {
                            closeEncodedFrame(results[i]);
                        }
                    }
                }
            } finally {
                for (const enc of encoders) {
                    try { enc.dispose(); } catch { /* ignore */ }
                }
            }
        }
    };
}

function closeBundleFrames(bundle: SimulcastBundle): void {
    closeCapturedFrames([...bundle.extras, bundle.primary]);
}

function closeCapturedFrames(frames: readonly CapturedFrame[]): void {
    for (const frame of frames) {
        try { frame.frame.close(); } catch { /* ignore */ }
    }
}

function closeEncodedFrame(frame: EncodedFrame): void {
    closeEncodedChunk(frame.chunk);
}
