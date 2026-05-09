import type { MonotonicTime } from 'clocks';
import { from, type PipeOperator } from 'ix-ext';
import { AsyncVideoEncoder, isAsyncVideoEncoderResetError } from '../adapters';
import {
    closeEncodedChunk,
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type EncodedFrame,
} from '../frame-envelopes';

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
     *  `bundle.frames.length`; mismatch is a hard error. */
    configs: readonly EncoderConfigPerLayer[];
    createEncoder: EncoderFactory;
}

/**
 * `CapturedBundle → EncodedBundle`. Per bundle: lazy-init one encoder
 * per layer on first iteration, submit all layers in parallel,
 * `Promise.allSettled` so one rejection doesn't leak the others'
 * already-produced chunks. Emits one `EncodedBundle` per source bundle
 * with `frames` bottom-first (L0, L1, …, LN-1).
 */
export function encode(opts: EncodeOptions): PipeOperator<CapturedBundle, EncodedBundle> {
    if (opts.configs.length === 0)
        throw new Error('encode: configs must contain at least one layer');

    const configs = opts.configs.slice();
    const createEncoder = opts.createEncoder;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            const encoders: AsyncVideoEncoder<EncodeInput, EncodedFrame>[] = [];
            // True on the very first bundle through this operator instance.
            // Forces keyFrame:true on the first encode per layer regardless
            // of policy — pool-reused encoders may otherwise emit a delta as
            // their first chunk after handleEncoderReset, which the server
            // drops as a pre-keyframe delta and the receiver waits for the
            // next natural keyframe (up to one full GOP, ~3 s).
            let forceKeyframeOnFirstEncode = true;
            let forceKeyframeNext = false;
            try {
                for await (const bundle of source) {
                    const layerCount = bundle.frames.length;
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
                    const keyFrame = bundle.frames[0].forceKeyframe
                        || forceKeyframeNext
                        || forceKeyframeOnFirstEncode;
                    forceKeyframeOnFirstEncode = false;
                    const layerFrames: readonly CapturedFrame[] = bundle.frames;
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
                    // Patch per-layer fields and assemble the bundle. On
                    // success we transfer ownership to downstream; on any
                    // failure between assembly and yield, close every chunk
                    // we produced.
                    const out: EncodedFrame[] = [];
                    let mustClose = true;
                    try {
                        const top = layerFrames[layerCount - 1];
                        for (let layerId = 0; layerId < results.length; layerId++) {
                            const partial = results[layerId];
                            const cfg = configs[layerId];
                            const completed: EncodedFrame = {
                                chunk: partial.chunk,
                                metadata: partial.metadata,
                                capturedAt: top.capturedAt,
                                index: top.index,
                                layerId: layerId,
                                sourceWidth: top.sourceWidth,
                                sourceHeight: top.sourceHeight,
                                encodedWidth: cfg.width,
                                encodedHeight: cfg.height,
                                stats: bundle.stats,
                            };
                            bundle.stats.chunksEncoded++;
                            bundle.stats.bytesEncoded += completed.chunk.byteLength;
                            if (completed.chunk.type === 'key')
                                bundle.stats.keyframesEncoded++;
                            out.push(completed);
                        }
                        forceKeyframeNext = false;
                        mustClose = false;
                        const encodedBundle: EncodedBundle = {
                            frames: out,
                            stats: bundle.stats,
                        };
                        yield encodedBundle;
                    } finally {
                        if (mustClose) {
                            // Either assembly threw or the consumer aborted
                            // before consuming the yield. Close everything
                            // we already wrapped, then anything left in
                            // `results` we hadn't yet wrapped.
                            for (const f of out)
                                closeEncodedFrame(f);
                            for (let i = out.length; i < results.length; i++)
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

function closeBundleFrames(bundle: CapturedBundle): void {
    closeCapturedFrames(bundle.frames);
}

function closeCapturedFrames(frames: readonly CapturedFrame[]): void {
    for (const frame of frames) {
        try { frame.frame.close(); } catch { /* ignore */ }
    }
}

function closeEncodedFrame(frame: EncodedFrame): void {
    closeEncodedChunk(frame.chunk);
}
