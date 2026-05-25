// Recorder worker host — wires the pipeline to actuallab-rpc and installs
// production deps. Imported lazily from the bootstrap so worker logging is
// initialized first.

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
import type { FloodGate } from '../operators/flood-gate';
import type { StreamSenderLike, VideoStreamFrameBundle } from '../operators/wire-send';
import {
    createWireSender,
    type DisposableStreamSender,
    type StreamingContext,
} from '../streaming/push-to-pull-buffer';
import type { SenderSession } from './session';

const { infoLog, warnLog } = getLogs('VideoPipeline');

// ---- Production hook factories ------------------------------------------

let pendingSource: ReadableStream<VideoFrame> | null = null;

export function setPendingSource(source: ReadableStream<VideoFrame>): void {
    if (pendingSource && pendingSource !== source) {
        pendingSource.cancel().catch(() => { /* ignore */ });
    }
    pendingSource = source;
}

// ---- Frame-by-frame source (workaround for MSTP-readable cross-realm bug) ----
//
// Small bounded FIFO between rVFC and the consumer — capacity 3, drops the
// OLDEST on overflow and closes the evicted frame. A VideoFrame holds a GPU
// buffer reference and Chromium's pool is ~12–20; capacity 3 covers a
// ~50 ms drain hiccup while leaving plenty of pool headroom (encoder
// maxInflight × layers + slot + generators stay under 20).
import { BoundedQueue } from 'buffers';

const PUSHED_FRAME_QUEUE_CAPACITY = 3;

const pushedSlot = new BoundedQueue<VideoFrame>({
    capacity: PUSHED_FRAME_QUEUE_CAPACITY,
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
        try { frame.close(); } catch { /* ignore */ }
        return;
    }
    pushedSlot.push(frame);
    wakeFrameWaiter();
}

export function endPushedSource(): void {
    pushedSourceEnded = true;
    wakeFrameWaiter();
}

function takePushedSourceProcessor(): { readable: ReadableStream<VideoFrame> } {
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
            pushedSlot.clear();
            pushedSourceEnded = true;
            wakeFrameWaiter();
        },
    });
    return { readable };
}

// Pipeline accepts but never reads the track — real frames come from
// `createProcessor`. Shared singleton to avoid useless per-run allocation.
const stubTrack = {} as unknown as MediaStreamTrack;

// Reused across runs so `Api.hub` stays warm. Per-run fields (chatId etc.)
// are repopulated from the RPC payload before any sender pump runs.
const streamingContext: StreamingContext = {
    chatId: '',
    serverClockOffsetMs: 0,
    sourceKind: 0,
    apiUrl: null,
    rpcLiveVideoStreams: null,
};

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

function createEncoder(
    _session: SenderSession,
    _config: EncoderConfigPerLayer,
    _layerId: number,
): AsyncVideoEncoder<EncodeInput, EncodedFrame> {
    // `encodedWidth` / `encodedHeight` here are placeholders; the encode
    // operator patches them along with layerId/sourceWidth/sourceHeight using
    // the current per-layer config after each encode resolves. Anything
    // closure-captured from the construction-time `_config` would be stale
    // after EncoderPool reuses this encoder for a different layer.
    const buildOutput = (
        input: EncodeInput,
        chunk: EncodedVideoChunk,
        metadata: EncodedVideoChunkMetadata,
    ): EncodedFrame => ({
        chunk,
        metadata,
        capturedAt: input.capturedAt,
        index: input.index,
        dropTrace: [],
        layerId: 0,
        sourceWidth: 0,
        sourceHeight: 0,
        encodedWidth: 0,
        encodedHeight: 0,
        rotation: 0,
        stats: undefined as unknown as EncodedFrame['stats'],
    });

    // Forward-ref + `tag` lookup: avoids stale closure values after pool
    // reuse swaps this encoder onto a different layer. The owner
    // (recorder-worker) updates `enc.tag` on every acquire+configure.
    const onError = (e: unknown): void => {
        const msg = e instanceof Error ? e.message : String(e);
        const stack = e instanceof Error ? e.stack : undefined;
        const tag = enc.tag || 'pre-configure';
        warnLog?.log(`encoder error (${tag}): ${msg}`, stack ?? '');
    };

    // Per-encode setTimeout/clearTimeout churn was ~750ms / 30s on a 90
    // enc/sec pipeline (3 layers × 30 fps). Adapter-level timeouts disabled
    // here; the encode operator now applies a bundle-level watchdog (one
    // timer per bundle instead of per layer) so silent encoder hangs are
    // still detected without the per-layer timer churn.
    const enc = new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
        buildOutput,
        onError,
        { maxInflight: 5, firstTimeoutMs: 0, timeoutMs: 0 },
    );
    return enc;
}

// Defers `PushStream` startup until the first keyframe lands `init(format)`,
// so the receiver gets the correct codec/dims on the first announce and dim
// changes after that are absorbed by per-frame DTO width/height — avoiding
// the legacy "recreate stream on every dim change" rebuild path.
function createSender(chatId: string, floodGate: FloodGate): StreamSenderLike {
    streamingContext.chatId = chatId;
    const sourceStartedAtMs = Date.now();
    let inner: DisposableStreamSender | null = null;
    let buffered: VideoStreamFrameBundle[] | null = null;
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
                floodGate,
            });
            resolveWhenDisposed(inner.whenDisposed);
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
            // Defensive: wireSend should always init() before send(). Cap
            // buffer to avoid unbounded growth if init never fires.
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
                rpcStreamSkipped: 0,
                floodGateSkipCount: 0,
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

function observeCallbackPromise(name: string, invoke: () => void | Promise<void>): void | Promise<void> {
    // RPC client bindings may return a Promise despite the void signature;
    // observe so async failures don't go silent.
    const result: unknown = (invoke as () => unknown)();
    if (
        result
        && typeof result === 'object'
        && 'catch' in result
        && typeof result.catch === 'function'
    ) {
        return (result as Promise<unknown>)
            .catch((e: unknown) => {
                warnLog?.log(`Recorder callback ${name} failed:`, e);
            })
            .then(() => undefined);
    }
    return undefined;
}

// Main thread pushes fresh tokens via `SharedSettingsWorkerSync.register`.
streamingContext.sessionTokenProvider = () =>
    Promise.resolve(SharedSettings.current.sessionToken ?? '');

const deps: RecorderWorkerDeps = {
    // -- init / configuration --
    initAppConstants(appConstants) {
        initAppConstants(appConstants as AppConstants);
    },
    configureStreaming,

    // -- source --
    getTrack(): MediaStreamTrack {
        return stubTrack;
    },
    createProcessor(_track: MediaStreamTrack): { readable: ReadableStream<VideoFrame> } {
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

    // -- pipeline-stage factories --
    createEncoder,
    createSender,

    // -- lifecycle callbacks --
    reportError(error) {
        void observeCallbackPromise('onError', () => callbacks.onError(error));
    },
    reportStreamEnded(reason) {
        void observeCallbackPromise('onStreamEnded', () => callbacks.onStreamEnded(reason));
    },
    reportTraceKillInjected() {
        void observeCallbackPromise('onTraceKillInjected', () => callbacks.onTraceKillInjected());
    },
    reportPreviewFrame(frame) {
        return observeCallbackPromise(
            'onPreviewFrame',
            () => callbacks.onPreviewFrame(frame));
    },
    reportPreviewFramePresentation(presentation) {
        void observeCallbackPromise(
            'onPreviewFramePresentation',
            () => callbacks.onPreviewFramePresentation(presentation));
    },
};

initRecorderWorker(deps);

// Kept referenced for eslint while callback forwarding is wired up.
void callbacks;

infoLog?.log('Recorder worker host initialized');
