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
// NO QUEUE between rVFC and the consumer — only a capacity-1 ReplaceableSlot
// that closes the evicted frame. A VideoFrame holds a GPU buffer reference and
// Chromium's pool is ~12–20; any build-up exhausts it, future
// `new VideoFrame(<video>)` calls fail, and the camera freezes.
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
    config: EncoderConfigPerLayer,
    layerId: number,
): AsyncVideoEncoder<EncodeInput, EncodedFrame> {
    const buildOutput = (
        input: EncodeInput,
        chunk: EncodedVideoChunk,
        metadata: EncodedVideoChunkMetadata,
    ): EncodedFrame => ({
        chunk,
        metadata,
        capturedAt: input.capturedAt,
        index: input.index,
        // Patched by the encode operator after the per-layer encode resolves.
        dropTrace: [],
        layerId: 0,
        sourceWidth: 0,
        sourceHeight: 0,
        encodedWidth: config.width,
        encodedHeight: config.height,
        rotation: 0,
        stats: undefined as unknown as EncodedFrame['stats'],
    });

    const onError = (e: unknown): void => {
        const msg = e instanceof Error ? e.message : String(e);
        const stack = e instanceof Error ? e.stack : undefined;
        warnLog?.log(
            `encoder error (layer=${layerId}, codec=${config.codec}, ${config.width}x${config.height}): ${msg}`,
            stack ?? '');
    };

    // First HW-encoder output can be much slower than steady state
    // (driver warm-up + first keyframe); use a wider window for it.
    return new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
        buildOutput,
        onError,
        { maxInflight: 2, firstTimeoutMs: 3_000, timeoutMs: 1_000 },
    );
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

function observeCallbackPromise(name: string, invoke: () => void): void {
    // RPC client bindings may return a Promise despite the void signature;
    // observe so async failures don't go silent.
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
        observeCallbackPromise('onError', () => callbacks.onError(error));
    },
    reportStreamEnded(reason) {
        observeCallbackPromise('onStreamEnded', () => callbacks.onStreamEnded(reason));
    },
    reportTraceKillInjected() {
        observeCallbackPromise('onTraceKillInjected', () => callbacks.onTraceKillInjected());
    },
    reportPreviewFramePresentation(presentation) {
        observeCallbackPromise(
            'onPreviewFramePresentation',
            () => callbacks.onPreviewFramePresentation(presentation));
    },
};

initRecorderWorker(deps);

// Kept referenced for eslint while callback forwarding is wired up.
void callbacks;

infoLog?.log('Recorder worker host initialized');
