import type { MonotonicTime } from 'clocks';
import { from, type PipeOperator } from 'ix-ext';
import { AsyncVideoEncoder, isAsyncVideoEncoderResetError } from '../adapters';
import { isCapturedBundleKeyFrame } from '../bundle-helpers';
import {
    closeEncodedChunk,
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type EncodedFrame,
} from '../frame-envelopes';

// Sentinel marker for an encoder-side codec init failure: thrown when
// the encode operator rejects without having produced any encoded
// output. Mirrors CODEC_EXHAUSTED_PREFIX on the receiver side — the
// recorder's restart loop uses it to drive sender-side codec
// exclusion. Encoded in the message because errors cross the worker
// boundary as strings.
export const ENCODER_INIT_FAILED_PREFIX = '[ENCODER_INIT_FAILED]';

export function isEncoderInitFailedError(error: unknown): boolean {
    const message = error instanceof Error ? error.message : String(error);
    return message.startsWith(ENCODER_INIT_FAILED_PREFIX);
}

export function parseEncoderInitFailedCodec(error: unknown): string | null {
    const message = error instanceof Error ? error.message : String(error);
    const match = /\[ENCODER_INIT_FAILED\]\s+codec=(\S+)/.exec(message);
    return match ? match[1] : null;
}

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
            // We always request a keyframe on the first encode. Encoders are
            // not pooled (createEncoder returns a fresh `new VideoEncoder()`),
            // so a fresh internal buffer guarantees the first encoded chunk
            // is an intra-coded keyframe.
            let forceKeyframeOnFirstEncode = true;
            let forceKeyframeNext = false;
            // Tracks whether ANY encoder has produced ANY chunk. False at
            // throw time signals an encoder-init failure (the WebCodecs
            // VideoEncoder rejected configure() or got an async error
            // before output) — the recorder loop maps that into a codec
            // exclusion via parseEncoderInitFailedCodec.
            let anyEncodedOutput = false;
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
                    // applyKeyframePolicy promotes forceKeyframe to all-or-none;
                    // use the bundle helper to keep the contract explicit.
                    const keyFrame = isCapturedBundleKeyFrame(bundle)
                        || forceKeyframeNext
                        || forceKeyframeOnFirstEncode;
                    forceKeyframeOnFirstEncode = false;
                    const layerFrames: readonly CapturedFrame[] = bundle.layers;
                    const promises: Promise<EncodedFrame>[] = [];
                    const layerDurations = new Array<number>(layerCount).fill(0);
                    try {
                        for (let layerId = 0; layerId < layerCount; layerId++) {
                            const cf = layerFrames[layerId];
                            const enc = encoders[layerId];
                            const encInput: EncodeInput = {
                                frame: cf.frame,
                                index: cf.index,
                                capturedAt: cf.capturedAt,
                            };
                            const id = layerId;
                            const startedAtMs = performance.now();
                            promises.push(enc.encode(encInput, { keyFrame }).then(
                                r => { layerDurations[id] = performance.now() - startedAtMs; return r; },
                                (e: unknown) => { layerDurations[id] = performance.now() - startedAtMs; throw e; },
                            ));
                        }
                    } catch (e) {
                        closeCapturedFrames(layerFrames);
                        throw e;
                    }
                    const settled = await Promise.allSettled(promises);
                    let layerSumMs = 0;
                    let layerMaxMs = 0;
                    for (const d of layerDurations) {
                        layerSumMs += d;
                        if (d > layerMaxMs) layerMaxMs = d;
                    }
                    bundle.stats.encodeTimeMsSum += layerSumMs;
                    bundle.stats.encodeTimeMsMaxSum += layerMaxMs;
                    bundle.stats.encodeTimeMsCount++;
                    const rejected = settled.filter((r): r is PromiseRejectedResult => r.status === 'rejected');
                    // Bump the proof flag for ANY successful settled result
                    // before deciding what to do with the rejections, so a
                    // mixed-outcome bundle (some layers OK, some failed)
                    // doesn't get re-classified as a codec init failure.
                    for (const result of settled) {
                        if (result.status === 'fulfilled') {
                            anyEncodedOutput = true;
                            break;
                        }
                    }
                    if (rejected.length > 0) {
                        for (const result of settled) {
                            if (result.status === 'fulfilled')
                                closeEncodedFrame(result.value);
                        }
                        if (rejected.every(r => isAsyncVideoEncoderResetError(r.reason))) {
                            forceKeyframeNext = true;
                            continue;
                        }
                        const firstRealReason = rejected
                            .find(r => !isAsyncVideoEncoderResetError(r.reason))!.reason;
                        // Encoder init failure: no encoder has ever
                        // produced a chunk and a non-reset error is
                        // surfacing. Tag with the codec string so the
                        // recorder can exclude the category and pick
                        // again, instead of restart-looping the same
                        // bad codec.
                        if (!anyEncodedOutput) {
                            const topCodec = configs[configs.length - 1].codec;
                            const message = firstRealReason instanceof Error
                                ? firstRealReason.message
                                : String(firstRealReason);
                            throw new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${topCodec}: ${message}`,
                                { cause: firstRealReason },
                            );
                        }
                        throw firstRealReason;
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
                                // Shared by reference with the bundle; per-layer
                                // wire DTOs carry the same trace bytes.
                                dropTrace: bundle.dropTrace,
                                layerId: layerId,
                                sourceWidth: top.sourceWidth,
                                sourceHeight: top.sourceHeight,
                                encodedWidth: cfg.width,
                                encodedHeight: cfg.height,
                                stats: bundle.stats,
                            };
                            bundle.stats.bytesEncoded += completed.chunk.byteLength;
                            out.push(completed);
                        }
                        forceKeyframeNext = false;
                        mustClose = false;
                        const encodedBundle: EncodedBundle = {
                            layers: out,
                            index: bundle.index,
                            dropTrace: bundle.dropTrace,
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
