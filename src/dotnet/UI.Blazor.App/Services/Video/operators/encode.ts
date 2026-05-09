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

// Subset of VideoEncoderConfig threaded into the encoded envelope;
// the full WebCodecs config lives inside createEncoder.
export interface EncoderConfigPerLayer {
    width: number;
    height: number;
    bitrate: number;
    framerate: number;
    codec: string;
}

export interface EncodeInput {
    frame: VideoFrame;
    index: number;
    capturedAt: MonotonicTime;
}

// Factory's buildOutput fills chunk/metadata/capturedAt/index/encodedWidth/Height;
// operator patches layerId, sourceWidth/Height, stats from the bundle.
export type EncoderFactory = (
    config: EncoderConfigPerLayer,
    layerId: number,
) => AsyncVideoEncoder<EncodeInput, EncodedFrame>;

export interface EncodeOptions {
    // Bottom-first; length MUST equal bundle.layers.length.
    configs: readonly EncoderConfigPerLayer[];
    createEncoder: EncoderFactory;
}

// CapturedBundle -> EncodedBundle. Lazy-init one encoder per layer on
// the first bundle, submit layers in parallel via allSettled so one
// rejection doesn't leak the others' chunks. Bundle layers bottom-first.
export function encode(opts: EncodeOptions): PipeOperator<CapturedBundle, EncodedBundle> {
    if (opts.configs.length === 0)
        throw new Error('encode: configs must contain at least one layer');

    const configs = opts.configs.slice();
    const createEncoder = opts.createEncoder;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            const encoders: AsyncVideoEncoder<EncodeInput, EncodedFrame>[] = [];
            // Pool-reused encoders may emit a delta as their first chunk after
            // handleEncoderReset; the server drops pre-keyframe deltas and the
            // receiver waits up to one full GOP (~3 s) for the next keyframe.
            let forceKeyframeOnFirstEncode = true;
            let forceKeyframeNext = false;
            try {
                for await (const bundle of source) {
                    const layerCount = bundle.layers.length;
                    if (layerCount !== configs.length) {
                        closeBundleLayers(bundle);
                        throw new Error(
                            `encode: bundle has ${layerCount} layer(s), expected ${configs.length}`);
                    }
                    if (encoders.length === 0) {
                        try {
                            for (let layerId = 0; layerId < configs.length; layerId++) {
                                encoders.push(createEncoder(configs[layerId], layerId));
                            }
                        } catch (e) {
                            closeBundleLayers(bundle);
                            throw e;
                        }
                    }
                    const keyFrame = bundle.layers[0].forceKeyframe
                        || forceKeyframeNext
                        || forceKeyframeOnFirstEncode;
                    forceKeyframeOnFirstEncode = false;
                    const layerFrames: readonly CapturedFrame[] = bundle.layers;
                    const promises: Promise<EncodedFrame>[] = [];
                    const encodeStartedAtMs = performance.now();
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
                    // Wall-clock per-bundle encode cost (parallel layers + dispatch
                    // overhead). Median needs a histogram; mean = sum/count is good
                    // enough for the diagnostics modal.
                    bundle.stats.encodeTimeMsSum += performance.now() - encodeStartedAtMs;
                    bundle.stats.encodeTimeMsCount++;
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
                            layers: out,
                            stats: bundle.stats,
                        };
                        yield encodedBundle;
                    } finally {
                        if (mustClose) {
                            // Assembly threw or consumer aborted pre-yield.
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

function closeBundleLayers(bundle: CapturedBundle): void {
    closeCapturedFrames(bundle.layers);
}

function closeCapturedFrames(frames: readonly CapturedFrame[]): void {
    for (const frame of frames) {
        try { frame.frame.close(); } catch { /* ignore */ }
    }
}

function closeEncodedFrame(frame: EncodedFrame): void {
    closeEncodedChunk(frame.chunk);
}
