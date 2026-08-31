import type { MonotonicTime } from 'clocks';
import { getLogs } from 'logging';
import { from, type PipeOperator } from 'ix-ext';
import { AsyncSignal } from 'actuallab-core';
import { RunningEMA } from 'math';
import { AsyncVideoEncoder, isAsyncVideoEncoderResetError } from '../adapters';
import { getEncoderPipelineDepth } from '../codec-support';
import { isCapturedBundleKeyFrame } from '../bundle-helpers';
import {
    closeEncodedChunk,
    type CapturedBundle,
    type CapturedFrame,
    type EncodedBundle,
    type EncodedFrame,
} from '../frame-envelopes';
import type { LayerLadderController } from '../sender/layer-ladder-controller';

const ENCODE_QUEUE_DEPTH_EMA_ALPHA = 0.2;
const RESTART_STREAK_WINDOW_MS = 60_000;
// How many bundles the operator keeps in flight at the encoder before
// awaiting the oldest. Equal to `AsyncVideoEncoder.maxInflight` so the
// encoder pipeline fills exactly once. Higher = more memory + latency,
// lower = HW encoder pipeline stalls on per-frame variance. Browser-dependent:
// see getEncoderPipelineDepth.
const MAX_PIPELINE = getEncoderPipelineDepth();

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
    // Canonical ladder index (stable layer→resolution map; the wire LayerId).
    // Defaults to the slot index — only sparse demand-driven ladders set it.
    layerId?: number;
}

export interface EncodeInput {
    frame: VideoFrame;
    index: number;
    capturedAt: MonotonicTime;
    durationUs: number;
}

// Factory's buildOutput fills chunk/metadata/capturedAt/index/encodedWidth/Height;
// operator patches layerId, sourceWidth/Height, stats from the bundle.
export type EncoderFactory = (
    config: EncoderConfigPerLayer,
    layerId: number,
) => AsyncVideoEncoder<EncodeInput, EncodedFrame>;

export interface EncodeOptions {
    // The active layer set lives in the controller. Operator snapshots
    // `controller.current` on each bundle and grows/shrinks the encoder set
    // on every version bump. bundle.layers.length MUST equal
    // controller.current.configs.length (spatialize and encode read the same
    // controller so they stay in sync).
    controller: LayerLadderController;
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
    // Mutable local snapshot of the active ladder; replaced whenever the
    // encoder set is reshaped to match a bundle's layer count.
    let configs: readonly EncoderConfigPerLayer[] = opts.controller.current.configs;
    const createEncoder = opts.createEncoder;
    const bundleTimeoutMs = opts.bundleTimeoutMs ?? 3000;
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            const encoders: AsyncVideoEncoder<EncodeInput, EncodedFrame>[] = [];
            // Dims/codec each live encoder[i] was created with, so a reshape can
            // reuse a slot only when its config still matches configs[i] and
            // recreate it otherwise. Critical for the warmup→openGate flip: the
            // warmup ladder is a single TOP-res tier living at index 0, but the
            // expanded ladder is bottom-first, so index 0 becomes the BOTTOM
            // layer and its encoder MUST be reconfigured (else the bottom layer
            // encodes at the top res → oversized coded + conformance window →
            // Edge HEVC renders only the top-left corner).
            let encoderCfgs: EncoderConfigPerLayer[] = [];
            const sameEncDims = (a: EncoderConfigPerLayer, b: EncoderConfigPerLayer): boolean =>
                a.width === b.width && a.height === b.height && a.codec === b.codec;
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
            // Every promise settles on this path, so the bundle watchdog never fires —
            // without a cap, a codec that dies on every encode skips forever.
            let consecutiveRecoverySkips = 0;
            const maxRecoverySkips = 30;

            // Pipelined submission queue. Steady-state encoding submits
            // multiple bundles before awaiting any one's completion, so the
            // per-encoder HW queue stays primed and a single 60+ms encode
            // spike no longer pauses the whole pipeline. We drain back to
            // empty before keyframe verification, hot-apply, hang recovery,
            // and source end — those paths need a clean encoder state.
            interface PendingBundle {
                bundle: CapturedBundle;
                keyFrame: boolean;
                promises: Promise<EncodedFrame>[];
                layerDurations: number[];
            }
            const pending: PendingBundle[] = [];

            // One bundle-hang timer shared across all awaited bundles: it rides
            // until it fires instead of an install/clear pair per bundle (which
            // churned ~one timer per captured frame on the recorder worker).
            // Bundles are awaited FIFO, so only one deadline is live at a time; a
            // stale wake (timer armed for an earlier, shorter deadline) is a no-op
            // and the loop re-arms for the current bundle's deadline.
            const bundleWatchdogSignal = new AsyncSignal();
            let bundleWatchdogHandle: ReturnType<typeof setTimeout> | null = null;
            let bundleAwaitDeadline = 0;
            const ensureBundleWatchdog = (): void => {
                if (bundleWatchdogHandle !== null)
                    return;

                bundleWatchdogHandle = setTimeout(() => {
                    bundleWatchdogHandle = null;
                    bundleWatchdogSignal.notify();
                }, Math.max(0, bundleAwaitDeadline - performance.now()));
            };
            const disposeBundleWatchdog = (): void => {
                if (bundleWatchdogHandle === null)
                    return;

                clearTimeout(bundleWatchdogHandle);
                bundleWatchdogHandle = null;
            };

            const submitBundle = (bundle: CapturedBundle, keyFrame: boolean): PendingBundle => {
                const layerCount = bundle.layers.length;
                const promises: Promise<EncodedFrame>[] = [];
                const layerDurations = new Array<number>(layerCount).fill(0);
                for (let layerId = 0; layerId < layerCount; layerId++) {
                    const cf = bundle.layers[layerId];
                    const enc = encoders[layerId];
                    const encInput: EncodeInput = {
                        frame: cf.frame,
                        index: cf.index,
                        capturedAt: cf.capturedAt,
                        durationUs: cf.durationUs,
                    };
                    const id = layerId;
                    const startedAtMs = performance.now();
                    promises.push(enc.encode(encInput, { keyFrame }).then(
                        r => { layerDurations[id] = performance.now() - startedAtMs; return r; },
                        (e: unknown) => { layerDurations[id] = performance.now() - startedAtMs; throw e; },
                    ));
                }
                // The encoder has taken ownership of the layer frames via
                // AsyncVideoEncoder.submit (which closes them inline now);
                // nothing on the bundle side to release here.
                return { bundle, keyFrame, promises, layerDurations };
            };

            type AwaitOutcome =
                | { kind: 'yield'; bundle: EncodedBundle }
                | { kind: 'skip' }
                | { kind: 'throw'; error: unknown };

            const awaitPending = async (p: PendingBundle): Promise<AwaitOutcome> => {
                const layerCount = p.bundle.layers.length;
                let settled: PromiseSettledResult<EncodedFrame>[];
                try {
                    const all = Promise.allSettled(p.promises);
                    if (bundleTimeoutMs <= 0) {
                        settled = await all;
                    } else {
                        const tagged = all.then(r => ({ kind: 'done' as const, r }));
                        bundleAwaitDeadline = performance.now() + bundleTimeoutMs;
                        for (;;) {
                            ensureBundleWatchdog();
                            const wake = bundleWatchdogSignal.wait().then(() => ({ kind: 'wd' as const }));
                            const res = await Promise.race([tagged, wake]);
                            if (res.kind === 'done') {
                                settled = res.r;
                                break;
                            }

                            if (performance.now() >= bundleAwaitDeadline)
                                throw new Error(
                                    `encode: bundle ${p.bundle.index} hung — `
                                    + `Promise.allSettled exceeded ${bundleTimeoutMs}ms`);
                        }
                    }
                } catch (timeoutErr) {
                    bundleHangAttempts++;
                    recordRestart(p.bundle.stats);
                    if (bundleHangAttempts >= maxBundleHangAttempts)
                        return { kind: 'throw', error: timeoutErr };

                    warnLog?.log(
                        `bundle ${p.bundle.index} hung — resetting encoders in place `
                        + `(attempt ${bundleHangAttempts}/${maxBundleHangAttempts}); `
                        + `forcing keyframe on next bundle`);
                    // handleEncoderHang fails+closes all pending inflight inputs
                    // (frames) and issues encoder.reset()+configure(lastConfig).
                    // Any subsequent in-flight pending bundles will resolve with
                    // reset errors and get swept by the all-reset branch below.
                    for (const enc of encoders) {
                        try { enc.handleEncoderHang(); } catch { /* ignore */ }
                    }
                    forceKeyframeNext = true;
                    return { kind: 'skip' };
                }

                bundleHangAttempts = 0;
                let layerSumMs = 0;
                let layerMaxMs = 0;
                for (const d of p.layerDurations) {
                    layerSumMs += d;
                    if (d > layerMaxMs) layerMaxMs = d;
                }
                p.bundle.stats.encodeTimeMsSum += layerSumMs;
                p.bundle.stats.encodeTimeMsMaxSum += layerMaxMs;
                p.bundle.stats.encodeTimeMsCount++;
                const rejected = settled.filter(
                    (r): r is PromiseRejectedResult => r.status === 'rejected');
                for (const result of settled) {
                    if (result.status === 'fulfilled') {
                        anyEncodedOutput = true;
                        consecutiveRecoverySkips = 0;
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
                        if (++consecutiveRecoverySkips < maxRecoverySkips)
                            return { kind: 'skip' };

                        const skippedCodec = configs[configs.length - 1].codec;
                        return {
                            kind: 'throw',
                            error: new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${skippedCodec}: `
                                + `${consecutiveRecoverySkips} bundles recovered without output`),
                        };
                    }

                    const firstRealReason: unknown = rejected
                        .find(r => !isAsyncVideoEncoderResetError(r.reason))!.reason as unknown;
                    if (!anyEncodedOutput) {
                        const topCodec = configs[configs.length - 1].codec;
                        const message = firstRealReason instanceof Error
                            ? firstRealReason.message
                            : String(firstRealReason);
                        return {
                            kind: 'throw',
                            error: new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${topCodec}: ${message}`,
                                { cause: firstRealReason },
                            ),
                        };
                    }

                    return { kind: 'throw', error: firstRealReason };
                }

                const results = settled.map((result): EncodedFrame => {
                    if (result.status !== 'fulfilled')
                        throw new Error('encode: unreachable rejected result after rejection check');

                    return result.value;
                });
                // Verify keyframe-ness when we asked for one. Pooled
                // encoders may have lingering state where the first
                // post-reset encode unexpectedly emerges as a delta;
                // shipping deltas with no preceding key downstream would
                // produce undecodable output at receivers. Drop this
                // bundle's chunks and re-request a keyframe on the next
                // bundle pulled from source. Pipelining guarantees we
                // drain before submitting more keyframe bundles, so the
                // re-request will see a clean encoder state.
                if (p.keyFrame) {
                    let allKey = true;
                    for (const r of results) {
                        if (r.chunk.type !== 'key') {
                            allKey = false;
                            break;
                        }
                    }

                    if (!allKey) {
                        warnLog?.log(
                            `requested keyframe but encoder produced delta(s) `
                            + `at index=${p.bundle.index}; re-requesting`);
                        for (const r of results)
                            closeEncodedFrame(r);
                        forceKeyframeNext = true;
                        return { kind: 'skip' };
                    }
                }

                const out: EncodedFrame[] = [];
                let mustClose = true;
                try {
                    const top = p.bundle.layers[layerCount - 1];
                    for (let layerId = 0; layerId < results.length; layerId++) {
                        const partial = results[layerId];
                        const cfg = configs[layerId];
                        const completed: EncodedFrame = {
                            chunk: partial.chunk,
                            metadata: partial.metadata,
                            durationUs: top.durationUs,
                            capturedAt: top.capturedAt,
                            index: top.index,
                            // Shared by reference with the bundle; per-layer
                            // wire DTOs carry the same trace bytes.
                            dropTrace: p.bundle.dropTrace,
                            layerId: cfg.layerId ?? layerId,
                            sourceWidth: top.sourceWidth,
                            sourceHeight: top.sourceHeight,
                            encodedWidth: cfg.width,
                            encodedHeight: cfg.height,
                            rotation: p.bundle.rotation,
                            stats: p.bundle.stats,
                        };
                        p.bundle.stats.bytesEncoded += completed.chunk.byteLength;
                        out.push(completed);
                    }
                    forceKeyframeNext = false;
                    mustClose = false;
                    p.bundle.stats.bundlesEncoded++;
                    return {
                        kind: 'yield',
                        bundle: {
                            layers: out,
                            index: p.bundle.index,
                            dropTrace: p.bundle.dropTrace,
                            rotation: p.bundle.rotation,
                            stats: p.bundle.stats,
                        },
                    };
                } finally {
                    if (mustClose) {
                        for (const f of out)
                            closeEncodedFrame(f);
                        for (let i = out.length; i < results.length; i++)
                            closeEncodedFrame(results[i]);
                    }
                }
            };

            async function* drainPending(): AsyncIterable<EncodedBundle> {
                while (pending.length > 0) {
                    const p = pending.shift()!;
                    const outcome = await awaitPending(p);
                    if (outcome.kind === 'yield')
                        yield outcome.bundle;
                    else if (outcome.kind === 'throw')
                        throw outcome.error;
                }
            }

            try {
                for await (const bundle of source) {
                    // Time-decay restart timestamps so the count drops without
                    // needing a new event.
                    const bundleNowMs = performance.now();
                    while (restartTimestamps.length > 0
                        && bundleNowMs - restartTimestamps[0] > RESTART_STREAK_WINDOW_MS)
                        restartTimestamps.shift();
                    bundle.stats.encoderRestartStreakIn60s = restartTimestamps.length;
                    // Hot-apply: align encoders to the BUNDLE's layer count,
                    // not to the controller's current version. spatialize emits
                    // bundles at the controller version it saw; if controller
                    // mutated between spatialize-emit and encode-receive, the
                    // bundle is at the OLD version while controller is at NEW.
                    // Using the bundle as the source of truth avoids that race.
                    // Same-count set changes (e.g. sparse demand {L1}→{L2})
                    // reshape too — compared by canonical id + dims/codec.
                    const layerCount = bundle.layers.length;
                    const curConfigs = opts.controller.current.configs;
                    const needsReshape = encoders.length !== layerCount
                        || (curConfigs.length === layerCount
                            && curConfigs.some((c, i) => !encoderCfgs[i]
                                || (encoderCfgs[i].layerId ?? i) !== (c.layerId ?? i)
                                || !sameEncDims(encoderCfgs[i], c)));
                    if (needsReshape) {
                        // Drain pending first so encoder reconfig doesn't race
                        // in-flight encodes against a different layer set. The
                        // await also lets a just-issued controller reshape land
                        // before we snapshot it below.
                        try {
                            for await (const r of drainPending())
                                yield r;
                        } catch (e) {
                            closeBundleLayers(bundle);
                            throw e;
                        }

                        const oldN = encoders.length;
                        const cur = opts.controller.current;
                        // The bundle's layer set belongs to the ladder version downscale
                        // stamped on it. Reshaping against a newer one would build encoders
                        // from one generation's configs and pair them with another's layers
                        // — cur.configs[i] is not even guaranteed to exist for every i <
                        // layerCount. Drop the straggler instead; downscale reads the
                        // controller per frame, so the bundles behind it are already current.
                        if (bundle.ladderVersion !== cur.version) {
                            closeBundleLayers(bundle);
                            continue;
                        }

                        // Reconcile by CANONICAL layer id: a slot's encoder is
                        // reused only when a config with the same canonical id
                        // and dims/codec still exists, wherever it now sits in
                        // the (possibly sparse) ladder — so dropping/re-adding a
                        // tier never restarts the surviving tiers' encoders or
                        // breaks their keyframe chains. Dim mismatches recreate
                        // (this is what makes the warmup→openGate flip correct:
                        // the warmup ladder is a single TOP-res tier at id 0,
                        // and the expanded ladder's id 0 is the BOTTOM res).
                        const reused = new Array<AsyncVideoEncoder<EncodeInput, EncodedFrame> | undefined>(layerCount);
                        const reusedOldSlots = new Set<number>();
                        try {
                            const oldSlotByLayerId = new Map<number, number>();
                            for (let i = 0; i < encoders.length; i++)
                                if (encoderCfgs[i])
                                    oldSlotByLayerId.set(encoderCfgs[i].layerId ?? i, i);
                            for (let i = 0; i < layerCount; i++) {
                                const want = cur.configs[i];
                                const wantId = want.layerId ?? i;
                                const oldSlot = oldSlotByLayerId.get(wantId);
                                if (oldSlot !== undefined && sameEncDims(encoderCfgs[oldSlot], want)) {
                                    reused[i] = encoders[oldSlot];
                                    reusedOldSlots.add(oldSlot);
                                } else {
                                    reused[i] = createEncoder(want, wantId);
                                }
                            }
                        } catch (e) {
                            for (const enc of reused)
                                if (enc) try { enc.dispose(); } catch { /* ignore */ }
                            for (const enc of encoders)
                                try { enc.dispose(); } catch { /* ignore */ }
                            encoders.length = 0;
                            encoderCfgs = [];
                            closeBundleLayers(bundle);
                            const topCodec = cur.configs[layerCount - 1]?.codec ?? 'unknown';
                            const message = e instanceof Error ? e.message : String(e);
                            throw new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${topCodec}: ${message}`,
                                { cause: e },
                            );
                        }

                        // Dispose old encoders that no config reuses.
                        for (let i = 0; i < oldN; i++)
                            if (!reusedOldSlots.has(i))
                                try { encoders[i].dispose(); } catch { /* ignore */ }
                        encoders.length = 0;
                        encoders.push(...(reused as AsyncVideoEncoder<EncodeInput, EncodedFrame>[]));
                        encoderCfgs = cur.configs.slice(0, layerCount).map(c => ({ ...c }));
                        // Force a keyframe so every (re)created encoder re-anchors —
                        // a receiver only switches layers on a keyframe of the
                        // desired layer. Skip only the first-ever init (oldN === 0),
                        // which `forceKeyframeOnFirstEncode` already covers.
                        if (oldN > 0) forceKeyframeNext = true;
                        configs = cur.configs;
                    }
                    // A fatal WebCodecs error closes an encoder permanently — reset()
                    // cannot revive it — so rebuild any slot that is no longer
                    // configured before it can reject the whole bundle.
                    const configuredSlots = Math.min(encoders.length, encoderCfgs.length);
                    for (let i = 0; i < configuredSlots; i++) {
                        const deadState = encoders[i].state;
                        if (deadState !== 'closed' && deadState !== 'unconfigured')
                            continue;

                        const cfg = encoderCfgs[i];
                        try { encoders[i].dispose(); } catch { /* ignore */ }
                        try {
                            encoders[i] = createEncoder(cfg, cfg.layerId ?? i);
                        } catch (e) {
                            closeBundleLayers(bundle);
                            const message = e instanceof Error ? e.message : String(e);
                            throw new Error(
                                `${ENCODER_INIT_FAILED_PREFIX} codec=${cfg.codec}: ${message}`,
                                { cause: e },
                            );
                        }

                        recordRestart(bundle.stats);
                        forceKeyframeNext = true;
                        warnLog?.log(`encoder ${i} was '${deadState}' — recreated`);
                    }

                    // applyKeyframePolicy promotes forceKeyframe to all-or-none;
                    // use the bundle helper to keep the contract explicit.
                    const keyFrame = isCapturedBundleKeyFrame(bundle)
                        || forceKeyframeNext
                        || forceKeyframeOnFirstEncode;
                    forceKeyframeOnFirstEncode = false;
                    // Keyframe bundles drain the pipeline first so we can
                    // verify the encoder honored the request before pulling
                    // more bundles from source. Delta bundles pipeline freely.
                    if (keyFrame && pending.length > 0) {
                        try {
                            for await (const r of drainPending())
                                yield r;
                        } catch (e) {
                            closeBundleLayers(bundle);
                            throw e;
                        }
                    }

                    pending.push(submitBundle(bundle, keyFrame));

                    // Sample queue depth right after submit — under
                    // pipelining this captures the true encoder
                    // saturation (multiple bundles can be inflight).
                    let maxQueueDepth = 0;
                    for (const enc of encoders) {
                        const q = enc.encoder.encodeQueueSize;
                        if (q > maxQueueDepth) maxQueueDepth = q;
                    }
                    queueDepthEma.appendSample(maxQueueDepth);
                    bundle.stats.encodeQueueDepthEma = queueDepthEma.value;

                    // Hold the pipeline at `MAX_PIPELINE` deep — drain the
                    // oldest before submitting more on the next iteration.
                    while (pending.length >= MAX_PIPELINE) {
                        const p = pending.shift()!;
                        const outcome = await awaitPending(p);
                        if (outcome.kind === 'yield')
                            yield outcome.bundle;
                        else if (outcome.kind === 'throw')
                            throw outcome.error;
                    }
                }

                // Source ended — drain anything still in flight.
                for await (const r of drainPending())
                    yield r;
            } finally {
                disposeBundleWatchdog();
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
