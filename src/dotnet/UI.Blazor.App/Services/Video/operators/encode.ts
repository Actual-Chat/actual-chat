import type { MonotonicTime } from 'clocks';
import { getLogs } from 'logging';
import { from, type PipeOperator } from 'ix-ext';
import { RunningEMA } from 'math';
import { AsyncVideoEncoder, isAsyncVideoEncoderResetError } from '../adapters';
import { isCapturedBundleKeyFrame } from '../bundle-helpers';
import {
    closeEncodedChunk,
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type EncodedFrame,
} from '../frame-envelopes';

const ENCODE_QUEUE_DEPTH_EMA_ALPHA = 0.2;
const RESTART_STREAK_WINDOW_MS = 60_000;

// Message prefix stamped on a thrown error when the encode operator rejects
// without having produced any chunk — VideoRecorder reads it to drive
// sender-side codec exclusion. Mirrors CODEC_EXHAUSTED_PREFIX in decode.ts.
const { warnLog } = getLogs('VideoPipeline');

export const ENCODER_INIT_FAILED_PREFIX = '[ENCODER_INIT_FAILED]';

export function isEncoderInitFailedError(error: unknown): boolean {
    const message = error instanceof Error ? error.message : String(error);
    return message.startsWith(ENCODER_INIT_FAILED_PREFIX);
}

export function parseEncoderInitFailedCodec(error: unknown): string | null {
    const message = error instanceof Error ? error.message : String(error);
    const match = /\[ENCODER_INIT_FAILED\]\s+codec=([^\s:]+)/.exec(message);
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
    // Bundle-level hang watchdog. If `Promise.allSettled` on a bundle's
    // per-layer encode promises hasn't resolved within this budget, the
    // operator throws — VideoRecorder.scheduleRecovery picks up the rejection
    // and restarts the pipeline. One timer per bundle (not per layer); a
    // hung HW encoder that emits neither output nor error is the failure
    // mode this catches. Default 3000 ms covers steady-state (1 frame ~=
    // 33 ms at 30 fps) plus first-frame warm-up; pass 0 to disable.
    bundleTimeoutMs?: number;
}

// CapturedBundle -> EncodedBundle. Lazy-init one encoder per layer on
// the first bundle, submit layers in parallel via allSettled so one
// rejection doesn't leak the others' chunks. Bundle layers bottom-first.
export function encode(opts: EncodeOptions): PipeOperator<CapturedBundle, EncodedBundle> {
    if (opts.configs.length === 0)
        throw new Error('encode: configs must contain at least one layer');

    const configs = opts.configs.slice();
    const createEncoder = opts.createEncoder;
    const bundleTimeoutMs = opts.bundleTimeoutMs ?? 3000;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            const encoders: AsyncVideoEncoder<EncodeInput, EncodedFrame>[] = [];
            const queueDepthEma = new RunningEMA(0, 1, ENCODE_QUEUE_DEPTH_EMA_ALPHA);
            const restartTimestamps: number[] = [];
            const recordRestart = (stats: EncodedBundle['stats'] | null): void => {
                const nowMs = performance.now();
                restartTimestamps.push(nowMs);
                while (restartTimestamps.length > 0
                    && nowMs - restartTimestamps[0] > RESTART_STREAK_WINDOW_MS)
                    restartTimestamps.shift();
                if (stats)
                    stats.encoderRestartStreakIn60s = restartTimestamps.length;
            };
            // We always request a keyframe on the first encode. Encoders may
            // come from `EncoderPool` (re-used across recording restarts to
            // avoid burning HW slots), so we cannot rely on a "fresh internal
            // buffer = guaranteed keyframe" assumption. Instead, after each
            // requested-keyframe bundle, we verify `chunk.type === 'key'` on
            // every layer's output and re-request if any layer produced a
            // delta — see verification block below.
            let forceKeyframeOnFirstEncode = true;
            let forceKeyframeNext = false;
            let anyEncodedOutput = false;
            // Consecutive bundle-watchdog timeouts. Reset on any successful
            // settle. Escalates to throw at 2 — first hang triggers in-place
            // VideoEncoder.reset()+reconfigure (WebCodecs spec recovery for
            // a wedged HW encoder); second consecutive hang means the reset
            // didn't help and we should rebuild the pipeline.
            let bundleHangAttempts = 0;
            const maxBundleHangAttempts = 2;
            try {
                for await (const bundle of source) {
                    // Time-decay restart timestamps so the count drops without
                    // needing a new event.
                    const bundleNowMs = performance.now();
                    while (restartTimestamps.length > 0
                        && bundleNowMs - restartTimestamps[0] > RESTART_STREAK_WINDOW_MS)
                        restartTimestamps.shift();
                    bundle.stats.encoderRestartStreakIn60s = restartTimestamps.length;
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
                            // The factory does synchronous WebCodecs configure(); a throw here
                            // means the codec failed init before any frame was encoded.
                            const topCodec = configs[configs.length - 1].codec;
                            const message = e instanceof Error ? e.message : String(e);
                            throw new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${topCodec}: ${message}`,
                                { cause: e },
                            );
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
                    // Bundle-level watchdog: race Promise.allSettled against
                    // a single timer. Detects a silently-hung HW encoder
                    // that emits neither output nor error (would otherwise
                    // deadlock the operator — upstream blocks on this
                    // await, no encode call is ever resubmitted so the
                    // adapter's queue-full backpressure never triggers).
                    // One timer per BUNDLE — 3× fewer set/clear ops than
                    // a per-layer setTimeout in the adapter.
                    // On first timeout per consecutive run we issue an
                    // in-place WebCodecs reset+reconfigure (handleEncoderHang)
                    // and retry on the next bundle — matches v2.8's per-encode
                    // self-heal that c24efe4f2 removed.
                    const bundleIndex: number = bundle.index;
                    // Sample queue depth BEFORE awaiting — captures the peak
                    // immediately after submitting every layer, which is the
                    // real backpressure signal. Sampling after the await would
                    // read zero in the steady state.
                    let maxQueueDepth = 0;
                    for (const enc of encoders) {
                        const q = enc.encoder.encodeQueueSize;
                        if (q > maxQueueDepth) maxQueueDepth = q;
                    }
                    queueDepthEma.appendSample(maxQueueDepth);
                    bundle.stats.encodeQueueDepthEma = queueDepthEma.value;
                    let settled: PromiseSettledResult<EncodedFrame>[];
                    try {
                        settled = await raceWithTimeout(
                            Promise.allSettled(promises),
                            bundleTimeoutMs,
                            bundleIndex,
                        );
                    } catch (timeoutErr) {
                        bundleHangAttempts++;
                        recordRestart(bundle.stats);
                        if (bundleHangAttempts >= maxBundleHangAttempts) {
                            // Encoders will be disposed by the outer finally; that
                            // closes any inflight frames via failAllPending.
                            throw timeoutErr;
                        }
                        warnLog?.log(
                            `bundle ${bundleIndex} hung — resetting encoders in place `
                            + `(attempt ${bundleHangAttempts}/${maxBundleHangAttempts}); `
                            + `forcing keyframe on next bundle`);
                        // handleEncoderHang fails+closes all pending inflight inputs
                        // (frames) and issues encoder.reset()+configure(lastConfig).
                        for (const enc of encoders) {
                            try { enc.handleEncoderHang(); } catch { /* ignore */ }
                        }
                        forceKeyframeNext = true;
                        continue;
                    }
                    bundleHangAttempts = 0;
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
                        const firstRealReason: unknown = rejected
                            .find(r => !isAsyncVideoEncoderResetError(r.reason))!.reason as unknown;
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
                    // Verify keyframe-ness when we asked for one. Pooled
                    // encoders may have lingering state where the first
                    // post-reset encode unexpectedly emerges as a delta;
                    // shipping deltas with no preceding key downstream
                    // would produce undecodable output at receivers. Drop
                    // this bundle's chunks and re-request a keyframe on
                    // the next iteration.
                    if (keyFrame) {
                        let allKey = true;
                        for (const r of results) {
                            if (r.chunk.type !== 'key') {
                                allKey = false;
                                break;
                            }
                        }
                        if (!allKey) {
                            warnLog?.log(
                                `requested keyframe but encoder produced delta(s) at index=${bundle.index}; re-requesting`);
                            for (const r of results)
                                closeEncodedFrame(r);
                            forceKeyframeNext = true;
                            continue;
                        }
                    }
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
                                rotation: bundle.rotation,
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
                            rotation: bundle.rotation,
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

async function raceWithTimeout<T>(p: Promise<T>, timeoutMs: number, bundleIndex: number): Promise<T> {
    if (timeoutMs <= 0)
        return p;
    let timer: ReturnType<typeof setTimeout> | null = null;
    return await new Promise<T>((resolve, reject) => {
        timer = setTimeout(() => {
            reject(new Error(`encode: bundle ${bundleIndex} hung — Promise.allSettled exceeded ${timeoutMs}ms`));
        }, timeoutMs);
        p.then(resolve, reject)
            .then(() => clearTimeout(timer!), () => clearTimeout(timer!));
    });
}
