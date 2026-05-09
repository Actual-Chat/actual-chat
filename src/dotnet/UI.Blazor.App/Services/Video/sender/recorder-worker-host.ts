// Recorder worker host. Mirrors `playback/player-worker-host.ts`:
// wires the new pipeline to the actuallab-rpc transport AND installs
// production deps (real WebCodecs `VideoEncoder`, real `WebGpuDownscaler`,
// real `streamingApi.liveVideoStreams.PushStream`).
//
// Imported lazily from `recorder-worker-bootstrap.ts` so worker logging
// is initialized before any of these modules run.

import { rpcClientServer } from 'rpc';
import { getLogs } from 'logging';
import { initAppConstants, type AppConstants } from 'app-constants';
import { SharedSettings } from 'shared-settings';
import { AsyncVideoEncoder } from '../adapters';
import {
    initRecorderWorker,
    recorderWorkerImpl,
    type RecorderWorkerDeps,
} from './recorder-worker';
import type {
    RecorderWorkerCallbacks,
} from './recorder-worker-contract';
import type { EncodedFrame } from '../frame-envelopes';
import type { EncoderConfigPerLayer, EncodeInput } from '../operators/encode';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';
import type { StreamSenderLike, VideoStreamFrameBundle } from '../operators/wire-send';
import { WebGpuDownscaler, type DownscaleTarget } from '../webgpu/downscaler';
import { WebGPUManager } from '../webgpu/manager';
import { CanvasDownscaler } from '../canvas/downscaler';
import {
    createWireSender,
    type DisposableStreamSender,
    type StreamingContext,
} from '../streaming/streaming-glue';
import type { PooledEncoder } from './encoder-pool';
import type { SenderSession } from './session';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// ---- Production hook factories ------------------------------------------

/**
 * Capture-source landing pad. The main thread transfers a
 * `ReadableStream<VideoFrame>` (built from
 * `MediaStreamTrackProcessor.readable`) via the `setSource` RPC method
 * before each `start()`. This module caches it for the host's
 * `createProcessor` to hand back to the `mstpSource` operator.
 *
 * Why a stream and not the track itself: Chromium doesn't honor
 * `postMessage(track, [track])` (only Safari 18+ does); the readable
 * stream is universally transferable.
 */
let pendingSource: ReadableStream<VideoFrame> | null = null;

/**
 * Install the readable stream the next `start()` will pick up. Called
 * by the RPC layer when a `setSource(...)` message lands.
 */
export function setPendingSource(source: ReadableStream<VideoFrame>): void {
    if (pendingSource && pendingSource !== source) {
        // Best-effort: cancel the prior stream so its underlying
        // producer (the previous `MediaStreamTrackProcessor`) tears
        // down its track-pull loop. Errors are benign — the stream
        // may already be locked or canceled.
        pendingSource.cancel().catch(() => { /* ignore */ });
    }
    pendingSource = source;
}

// ---- Frame-by-frame source (workaround for the MSTP-readable bug) ----
//
// Main side reads via `requestVideoFrameCallback` and `pushFrame`s each
// `VideoFrame` across individually. Architecture invariant: NO queue
// between rVFC and the recorder pipeline — only a single
// {@link ReplaceableSlot} (capacity = 1, drop-on-replace, with a
// dispose delegate that closes the evicted frame so its GPU buffer
// returns to the WebCodecs pool).
//
// Why no queue: a `VideoFrame` holds a GPU buffer reference and
// Chromium's pool is small (12–20 frames). Any build-up between rVFC
// and the consumer (e.g. during encoder warmup) exhausts the pool,
// future `new VideoFrame(<video>)` calls fail, and the camera freezes
// indefinitely. The pipe's downstream operators (encoder, etc.) get
// to consume at their own rate; for live video, the right behaviour
// when they fall behind is "drop the older frame, keep the newer
// one" — exactly what a single ReplaceableSlot does.
import { ReplaceableSlot } from 'buffers';

const pushedSlot = new ReplaceableSlot<VideoFrame>({
    dispose: f => { try { f.close(); } catch { /* ignore */ } },
});
let frameWaker: (() => void) | null = null;
let pushedSourceEnded = false;

function wakeFrameWaiter(): void {
    const w = frameWaker;
    if (w) { frameWaker = null; w(); }
}

export function pushFrameToHost(frame: VideoFrame): void {
    if (pushedSourceEnded) {
        // Late frame after end signal: just close it; the source
        // iterator has already returned.
        try { frame.close(); } catch { /* ignore */ }
        return;
    }
    // Slot replaces and disposes the prior frame in one atomic step.
    pushedSlot.push(frame);
    wakeFrameWaiter();
}

export function endPushedSource(): void {
    pushedSourceEnded = true;
    wakeFrameWaiter();
}

/** Build a synthetic `ReadableStream<VideoFrame>` backed by the
 *  push-slot. Returns the same shape `mstpSource` expects:
 *  `{ readable }`. */
function takePushedSourceProcessor(): { readable: ReadableStream<VideoFrame> } {
    // Reset state for the new run. Any leftover frame is disposed
    // by the slot's dispose delegate.
    pushedSlot.clear();
    pushedSourceEnded = false;

    const readable = new ReadableStream<VideoFrame>({
        async pull(controller) {
            for (;;) {
                const next = pushedSlot.take();
                if (next) {
                    controller.enqueue(next);
                    return;
                }
                if (pushedSourceEnded) {
                    controller.close();
                    return;
                }
                await new Promise<void>(resolve => { frameWaker = resolve; });
            }
        },
        cancel() {
            // Discard any pending frame via the slot's dispose, mark
            // the source ended so the pull loop exits cleanly.
            pushedSlot.clear();
            pushedSourceEnded = true;
            wakeFrameWaiter();
        },
    });
    return { readable };
}

/**
 * Stub `MediaStreamTrack`-shaped object the recorder pipeline accepts
 * but never actually reads. The real frame source is the readable
 * stream supplied via {@link createProcessor}; the operator's `track`
 * arg is only forwarded into the test factory's argument list.
 *
 * We hand back a single shared dummy across all `getTrack()` calls so
 * we don't allocate uselessly per run.
 */
const stubTrack = {} as unknown as MediaStreamTrack;

/**
 * Module-level streaming context. Populated on first `start()` (via the
 * sender factory closure) and reused across runs so `Api.hub` stays
 * warm — same pattern as the legacy worker.
 *
 * `chatId` / `sourceKind` / `apiUrl` are filled in per `start()` from
 * the RPC payload before any sender pump runs; the lazy
 * `rpcLiveVideoStreams` cache survives across runs.
 */
const streamingContext: StreamingContext = {
    chatId: '',
    serverClockOffsetMs: 0,
    sourceKind: 0,
    apiUrl: null,
    rpcLiveVideoStreams: null,
};

/**
 * Configure the streaming context from an init / start message. The
 * bootstrap calls this from the RPC handler that lands the chat / api
 * url before forwarding to {@link recorderWorkerImpl}.
 */
export function configureStreaming(opts: {
    chatId: string;
    apiUrl: string;
    sourceKind?: number;
    serverClockOffsetMs?: number;
}): void {
    streamingContext.chatId = opts.chatId;
    streamingContext.apiUrl = opts.apiUrl;
    if (opts.sourceKind !== undefined) streamingContext.sourceKind = opts.sourceKind;
    if (opts.serverClockOffsetMs !== undefined) streamingContext.serverClockOffsetMs = opts.serverClockOffsetMs;
}

/**
 * Adapter wrapping {@link WebGpuDownscaler} as a {@link DownscalerLike}.
 * The new operator wants `process(input, layers)` per-call; the legacy
 * downscaler wants a one-shot `configure(targets)` then bare
 * `process(input)`. The adapter calls `configure` lazily on the first
 * `process()` call (when the layer ladder is known) and short-circuits
 * subsequent calls.
 *
 * `dispose()` forwards to the wrapped instance; the operator calls it
 * in its `finally` so GPU slots are released between runs.
 */
class WebGpuDownscalerAdapter implements DownscalerLike {
    private configured = false;
    constructor(private readonly inner: WebGpuDownscaler) {}

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        if (!this.configured) {
            const targets: DownscaleTarget[] = layers.map(l => ({
                width: l.width,
                height: l.height,
                centerCrop: true,
            }));
            this.inner.configure(targets);
            this.configured = true;
        }
        const results = await this.inner.process(input);
        return results.map(r => r.frame);
    }

    dispose(): void {
        try { this.inner.dispose(); } catch { /* ignore */ }
    }
}

/**
 * Build a fresh `WebGpuDownscaler` against the worker's GPU device.
 * One device is shared across all runs (the device lives on
 * {@link WebGPUManager}); the adapter / per-run state lives in the
 * returned `DownscalerLike`.
 *
 * NOTE: Currently disabled in favour of {@link CanvasDownscaler} —
 * the WebGPU path has had recurring device-loss cascades under load.
 * Kept around so we can swap back when the underlying issues are
 * addressed (see `createDownscalerInstance` below).
 */
async function createWebGpuDownscalerInstance(): Promise<DownscalerLike> {
    const device = await WebGPUManager.init();
    return new WebGpuDownscalerAdapter(new WebGpuDownscaler(device));
}

/**
 * Active downscaler factory — points at the simpler Canvas 2D
 * implementation by default. To swap back to the WebGPU one, replace
 * the body with `return createWebGpuDownscalerInstance()` and update
 * {@link LazyDownscaler}'s init path accordingly.
 */
function createDownscalerInstance(): DownscalerLike {
    return new CanvasDownscaler();
}

/**
 * Map a per-layer config + the operator's `EncodedFrame` envelope onto
 * a fresh `AsyncVideoEncoder<EncodeInput, EncodedFrame>`. This is the
 * production shape of `EncoderFactory` consumed by `EncoderPool.acquire`.
 *
 * The wrapper:
 *  - Constructs a `VideoEncoder` (via `AsyncVideoEncoder`'s constructor).
 *  - Leaves configuration to the worker's pooled acquire path so both
 *    fresh and reused encoders go through the wrapper's `configure(...)`
 *    method and retain the latest config for reset recovery.
 *  - Wires `buildOutput` so each encoded chunk is paired with the
 *    matching `EncodeInput` to yield an `EncodedFrame` envelope. The
 *    operator patches `layerId` / `sourceWidth` / `sourceHeight`
 *    / `stats` itself before yielding.
 */
function poolEncoderFactory(
    _session: SenderSession,
    config: EncoderConfigPerLayer,
    layerId: number,
): () => PooledEncoder {
    return () => {
        const buildOutput = (
            input: EncodeInput,
            chunk: EncodedVideoChunk,
            metadata: EncodedVideoChunkMetadata,
        ): EncodedFrame => ({
            chunk,
            metadata,
            capturedAt: input.capturedAt,
            index: input.index,
            // Patched by the encode operator using `bundle.primary` /
            // `bundle.extras[k]` after the per-layer encode resolves.
            layerId: 0,
            sourceWidth: 0,
            sourceHeight: 0,
            encodedWidth: config.width,
            encodedHeight: config.height,
            stats: undefined as unknown as EncodedFrame['stats'],
        });

        // Surface encoder errors loudly. Previously these were buried at
        // warn level; with simulcast on HW codecs (HEVC/AV1 in
        // particular) we can hit slot exhaustion and silent failures —
        // visibility here is the first signal that the pipe stalled.
        const onError = (e: unknown): void => {
            const msg = e instanceof Error ? e.message : String(e);
            const stack = e instanceof Error ? e.stack : undefined;
            warnLog?.log(
                `encoder error (layer=${layerId}, codec=${config.codec}, ${config.width}x${config.height}): ${msg}`,
                stack ?? '');
        };

        // First hardware-encoder output can be much slower than steady
        // state (driver warm-up + first keyframe). Give it a wider
        // window, then keep the tight realtime timeout after the first
        // successful output.
        const encoder = new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
            buildOutput,
            onError,
            { maxInflight: 2, firstTimeoutMs: 3_000, timeoutMs: 1_000 },
        );
        return encoder;
    };
}

/**
 * Production binding for the wire sender.
 *
 * `wireSend` calls `sender.init(format)` on the FIRST keyframe with the
 * real codec / dims / description metadata. The host defers actual
 * `PushStream` startup until then: we capture chatId + sourceStartedAtMs
 * here, but only construct the underlying `createWireSender` (and thus
 * issue `PushStream`) inside `init`.
 *
 * This avoids the legacy "recreate the stream on every keyframe dim
 * change" rebuild path: the receiver gets the correct format on the
 * first announce, and dim changes are absorbed by the per-frame DTO's
 * width/height fields.
 */
function createSender(chatId: string): StreamSenderLike {
    streamingContext.chatId = chatId;
    const sourceStartedAtMs = Date.now();
    let inner: DisposableStreamSender | null = null;
    let buffered: VideoStreamFrameBundle[] | null = null;
    // Surface the inner pump's terminal state; resolved synchronously
    // once `inner` exists, before that the outer never-completing
    // promise lets wireSend skip the check until init lands.
    let resolveWhenDisposed: (p: Promise<void>) => void = () => { /* set below */ };
    const whenDisposed = new Promise<void>((resolve, reject) => {
        resolveWhenDisposed = (p: Promise<void>) => {
            void p.then(resolve, reject);
        };
    });
    whenDisposed.catch(() => { /* observed via wireSend */ });
    return {
        whenDisposed,
        init(format) {
            if (inner) return;
            inner = createWireSender({
                chatId,
                format,
                sourceStartedAtMs,
                streamingContext,
            });
            resolveWhenDisposed(inner.whenDisposed);
            // Drain anything buffered before init landed (shouldn't
            // happen — `wireSend` calls `init()` BEFORE the first
            // `send()` on each keyframe path — but defensively).
            if (buffered) {
                for (const dto of buffered) void inner.send(dto);
                buffered = null;
            }
        },
        send(dto) {
            if (inner) {
                void inner.send(dto);
                return;
            }
            // No init yet — buffer until init lands. Cap to avoid
            // unbounded growth if init never fires.
            buffered ??= [];
            if (buffered.length < 32) buffered.push(dto);
        },
        dispose() {
            inner?.dispose();
        },
        getStats() {
            return inner?.getStats() ?? {
                addedFrameCount: buffered?.length ?? 0,
                queueDepth: buffered?.length ?? 0,
                maxQueueDepth: buffered?.length ?? 0,
                droppedAtSenderQueue: 0,
                droppedKeyframesAtSenderQueue: 0,
                rpcStreamSkipped: 0,
                lastAckAgeMs: -1,
                isPeerConnected: false,
            };
        },
    };
}

// ---- Wire-up ------------------------------------------------------------

const callbacks = rpcClientServer<RecorderWorkerCallbacks>(
    'RecorderWorker',
    self as unknown as Worker,
    recorderWorkerImpl,
);

function observeCallbackPromise(name: string, invoke: () => void): void {
    // The signature returns void, but RPC client bindings can return a
    // Promise — observe so async failures don't go silent.
    const result: unknown = (invoke as () => unknown)();
    if (
        result
        && typeof result === 'object'
        && 'catch' in result
        && typeof result.catch === 'function'
    ) {
        void (result as Promise<unknown>).catch((e: unknown) =>
            warnLog?.log(`Recorder callback ${name} failed:`, e));
    }
}

// Auth: read the session token from SharedSettings. The main thread
// pushes fresh tokens via `SharedSettingsWorkerSync.register(this.worker)`,
// which calls into our `updateSharedSettings` (mounted alongside
// `recorderWorkerImpl` via `sharedSettingsWorker`).
streamingContext.sessionTokenProvider = () =>
    Promise.resolve(SharedSettings.current.sessionToken ?? '');

const deps: RecorderWorkerDeps = {
    getTrack(): MediaStreamTrack {
        // Stub. The mstpSource operator forwards this to its
        // createProcessor(track) factory; production createProcessor
        // ignores `track` and returns the transferred readable.
        return stubTrack;
    },
    createProcessor(_track: MediaStreamTrack): { readable: ReadableStream<VideoFrame> } {
        // Frame-by-frame mode (default; main-side calls `pushFrame`):
        // build a fresh synthetic readable backed by the pushed queue.
        // Test/back-compat mode (`setSource(readable)`): use the
        // transferred readable directly.
        if (pendingSource) {
            const readable = pendingSource;
            pendingSource = null;
            return { readable };
        }
        return takePushedSourceProcessor();
    },
    setSource(source: ReadableStream<VideoFrame>): void {
        setPendingSource(source);
    },
    pushFrame(frame: VideoFrame): void {
        pushFrameToHost(frame);
    },
    endSource(): void {
        endPushedSource();
    },
    configureStreaming,
    initAppConstants(appConstants) {
        initAppConstants(appConstants as AppConstants);
    },
    reportError(error) {
        observeCallbackPromise('onError', () => callbacks.onError(error));
    },
    reportStreamEnded(reason) {
        observeCallbackPromise('onStreamEnded', () => callbacks.onStreamEnded(reason));
    },
    createSender,
    createDownscaler: () => createDownscalerInstance(),
    poolEncoderFactory,
};

/**
 * Sync `DownscalerLike` that defers actual GPU acquisition until the
 * first `process()` call. Used when the active downscaler is the
 * WebGPU implementation, whose construction is async (device init).
 * The new operator caches the downscaler for the run, so the lazy
 * init runs at most once per pipeline run.
 *
 * Currently unreferenced — kept as the wrapper to use when
 * {@link createDownscalerInstance} is switched back to WebGPU.
 */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
class WebGpuLazyDownscaler implements DownscalerLike {
    private inner: DownscalerLike | null = null;
    private initPromise: Promise<DownscalerLike> | null = null;

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        let inner = this.inner;
        if (!inner) {
            this.initPromise ??= createWebGpuDownscalerInstance();
            try {
                inner = await this.initPromise;
                this.inner = inner;
            } catch (e) {
                try { input.close(); } catch { /* already closed */ }
                throw e;
            }
        }
        return inner.process(input, layers);
    }

    dispose(): void {
        try { this.inner?.dispose?.(); } catch { /* ignore */ }
        this.inner = null;
        this.initPromise = null;
    }
}

initRecorderWorker(deps);

// `callbacks` reserved for future onError / onStreamCreated /
// onStreamEnded forwarding from the recorder; reference it here so
// eslint's no-unused-vars doesn't trip.
void callbacks;

infoLog?.log('Recorder worker host initialized');
