import { from, type PipeOperator } from 'ix-ext';
import { abortPromise, PromiseSource } from 'promises';
import { closeEncodedChunk, type ArrivedChunk, type DecodedFrame } from '../frame-envelopes';
import { createCodecProofTracker, type CodecProofTracker } from '../codec-proof-tracker';

// Sentinel marker stamped on any error thrown by the decode operator
// once its internal recovery is exhausted. Consumers (player worker,
// main-thread VideoPlayer) test for it to decide whether to request
// codec exclusion vs. a generic pipeline restart. Encoded in the
// message because errors cross the worker boundary as strings.
export const CODEC_EXHAUSTED_PREFIX = '[CODEC_EXHAUSTED]';

export function isCodecExhaustedError(error: unknown): boolean {
    const message = error instanceof Error ? error.message : String(error);
    return message.startsWith(CODEC_EXHAUSTED_PREFIX);
}

// WebCodecs VideoDecoder surface. Tests inject a fake.
export interface DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed';
    decodeQueueSize: number;
    configure(config: VideoDecoderConfig): void;
    decode(chunk: EncodedVideoChunk): void;
    flush(): Promise<void>;
    close(): void;
}

export interface InitialDecoderConfig {
    codec: string;
    codedWidth?: number;
    codedHeight?: number;
    hardwareAcceleration?: 'prefer-hardware' | 'prefer-software' | 'no-preference';
    optimizeForLatency?: boolean;
}

export interface DecodeOptions {
    initialConfig: InitialDecoderConfig;
    createDecoder: (handlers: {
        onFrame: (frame: VideoFrame) => void;
        onError: (e: Error) => void;
    }) => DecoderLike;
    // Informational only — operator throws on the same tick.
    onCodecExhausted?: (codec: string) => void;
    // Fired once per stream when `framesUntilProven` decoded frames have
    // landed without resetting the counter (the counter resets on every
    // recovery). Signals the consumer that the codec actually works on this
    // device; the operator also switches to unbounded recovery from this
    // point on, so a single transient failure never tears down a working
    // codec.
    onCodecProven?: (codec: string) => void;
    framesUntilProven?: number;
    now?: () => number;
    maxRecoveries?: number;
    // Synthesises an error and drives recovery if the decoder sits on
    // submitted chunks without producing a frame or error. Default 2000 ms.
    decoderHangTimeoutMs?: number;
    setTimeoutFn?: (cb: () => void, ms: number) => unknown;
    clearTimeoutFn?: (handle: unknown) => void;
    abortSignal?: AbortSignal;
}

// HEVC (hev1/hvc1) needs a description; AVC and AV1 inline codec
// parameters in the bytestream and can configure without one.
function canConfigureWithoutDescription(codec: string): boolean {
    return codec.startsWith('avc1') || codec.startsWith('av01');
}

// FIFO mirroring decode() calls; output frames pair by shift order
// (WebCodecs guarantees in-order output per submitted chunk).
interface PendingDecode {
    capturedAt: { timeMs: number; epoch: number };
    arrivedAt: ArrivedChunk['arrivedAt'];
    layerId: number;
    submitMs: number;
    index: number;
    dropTrace: ArrivedChunk['dropTrace'];
}

// ArrivedChunk -> DecodedFrame. Lazy decoder init on first keyframe,
// reconfigures on dim change. Per-layer description cache covers HEVC's
// "later keyframes may omit description" case. Decoder errors trigger a
// rebuild + reconfigure on the next keyframe; pre-keyframe deltas during
// recovery are dropped. After maxRecoveries consecutive recoveries the
// operator throws and onCodecExhausted fires.
export function decode(opts: DecodeOptions): PipeOperator<ArrivedChunk, DecodedFrame> {
    const initialConfig = opts.initialConfig;
    const createDecoder = opts.createDecoder;
    const onCodecExhausted = opts.onCodecExhausted;
    const onCodecProven = opts.onCodecProven;
    const framesUntilProven = Math.max(1, opts.framesUntilProven ?? 10);
    const now = opts.now ?? ((): number => performance.now());
    const maxRecoveries = opts.maxRecoveries ?? 4;
    const decoderHangTimeoutMs = opts.decoderHangTimeoutMs ?? 2_000;
    const setTimeoutFn = opts.setTimeoutFn ?? ((cb, ms): unknown => setTimeout(cb, ms));
    const clearTimeoutFn = opts.clearTimeoutFn ?? ((h: unknown): void => clearTimeout(h as ReturnType<typeof setTimeout>));
    const abortSignal = opts.abortSignal;

    return source => from(decodeAsync(
        source,
        initialConfig,
        createDecoder,
        onCodecExhausted,
        onCodecProven,
        framesUntilProven,
        now,
        maxRecoveries,
        decoderHangTimeoutMs,
        setTimeoutFn,
        clearTimeoutFn,
        abortSignal,
    ));
}

async function* decodeAsync(
    source: AsyncIterable<ArrivedChunk>,
    initialConfig: InitialDecoderConfig,
    createDecoder: DecodeOptions['createDecoder'],
    onCodecExhausted: ((codec: string) => void) | undefined,
    onCodecProven: ((codec: string) => void) | undefined,
    framesUntilProven: number,
    now: () => number,
    maxRecoveries: number,
    decoderHangTimeoutMs: number,
    setTimeoutFn: (cb: () => void, ms: number) => unknown,
    clearTimeoutFn: (handle: unknown) => void,
    abortSignal: AbortSignal | undefined,
): AsyncIterable<DecodedFrame> {
    const currentCodec = initialConfig.codec;
    let currentWidth = initialConfig.codedWidth ?? 0;
    let currentHeight = initialConfig.codedHeight ?? 0;
    let configured = false;
    const descriptionByLayer = new Map<number, ArrayBuffer>();
    const pending: PendingDecode[] = [];
    const ready: DecodedFrame[] = [];
    let wakeup = new PromiseSource<void>();
    let pendingError: Error | null = null;
    let consecutiveRecoveries = 0;
    // See codec-proof-tracker.ts. Tracks the highest spatial layer the
    // decoder has produced; once enough frames decode at that layer the
    // codec is "proven" on this device and the exhaustion check stops
    // firing (transient failures still trigger the decoder rebuild +
    // drop-deltas-until-keyframe recovery loop below). Toggleable via
    // the UseCodecProofTracker constant.
    const codecProofTracker: CodecProofTracker = createCodecProofTracker(framesUntilProven);
    let codecProvenFired = false;
    const noteFrameDecoded = (layerId: number): void => {
        const wasProven = codecProofTracker.isProven();
        codecProofTracker.noteFrameDecoded(layerId);
        if (!codecProvenFired && !wasProven && codecProofTracker.isProven()) {
            codecProvenFired = true;
            onCodecProven?.(currentCodec);
        }
    };
    // Watchdog: detects a hung decoder while chunks sit in the pending FIFO.
    let lastDecoderActivityMs = now();

    const handlers = {
        onFrame: (frame: VideoFrame): void => {
            lastDecoderActivityMs = now();
            const meta = pending.shift();
            if (!meta) {
                try { frame.close(); } catch { /* ignore */ }
                return;
            }
            const decodedAtMs = now();
            const stats = currentStats!;
            const envelope: DecodedFrame = {
                frame,
                capturedAt: meta.capturedAt,
                arrivedAt: meta.arrivedAt,
                decodedAt: { timeMs: decodedAtMs, epoch: 0 },
                index: meta.index,
                dropTrace: meta.dropTrace,
                layerId: meta.layerId,
                stats,
            };
            consecutiveRecoveries = 0;
            noteFrameDecoded(meta.layerId);
            ready.push(envelope);
            if (!wakeup.isCompleted()) wakeup.resolve();
        },
        onError: (e: Error): void => {
            lastDecoderActivityMs = now();
            pendingError = e;
            codecProofTracker.noteDecoderError();
            if (!wakeup.isCompleted()) wakeup.resolve();
        },
    };

    let currentStats: ArrivedChunk['stats'] | null = null;

    let decoder: DecoderLike = createDecoder(handlers);
    const abortWait: Promise<WaitResult> = abortSignal
        ? abortPromise(abortSignal).catch((): WaitResult => ({ kind: 'abort' }))
        : new Promise<WaitResult>(() => { /* never resolves */ });

    // Manual iterator so we can race source.next() against the wakeup.
    const sourceIter = source[Symbol.asyncIterator]();
    let nextSourcePromise: Promise<IteratorResult<ArrivedChunk>> | null = null;
    let sourceDone = false;

    const armSource = (): Promise<IteratorResult<ArrivedChunk>> => {
        if (!nextSourcePromise && !sourceDone) {
            nextSourcePromise = sourceIter.next();
        }
        return nextSourcePromise!;
    };

    type WaitResult =
        | { kind: 'src'; result: IteratorResult<ArrivedChunk> }
        | { kind: 'wake' }
        | { kind: 'abort' }
        | { kind: 'watchdog' };

    try {
        for (;;) {
            if (abortSignal?.aborted) return;
            while (ready.length > 0) {
                const envelope = ready.shift()!;
                let mustClose = true;
                try {
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
            if (pendingError && sourceDone) throw pendingError;
            if (sourceDone && pending.length === 0) return;

            const sourceP = sourceDone ? null : armSource();
            const wakeP = wakeup;

            // Watchdog arms only when the decoder owes us a frame —
            // otherwise a quiet stream would synthesise spurious timeouts.
            const racers: Promise<WaitResult>[] = [
                wakeP.then((): WaitResult => ({ kind: 'wake' })),
                abortWait,
            ];
            if (sourceP)
                racers.push(sourceP.then((r): WaitResult => ({ kind: 'src', result: r })));
            let watchdogHandle: unknown = null;
            if (pending.length > 0) {
                const elapsed = now() - lastDecoderActivityMs;
                const remaining = Math.max(0, decoderHangTimeoutMs - elapsed);
                racers.push(new Promise<WaitResult>(resolve => {
                    watchdogHandle = setTimeoutFn(() => resolve({ kind: 'watchdog' }), remaining);
                }));
            }
            let winner: WaitResult;
            try {
                winner = await Promise.race(racers);
            } finally {
                if (watchdogHandle !== null) {
                    try { clearTimeoutFn(watchdogHandle); } catch { /* ignore */ }
                }
            }

            if (winner.kind === 'abort')
                return;

            if (winner.kind === 'watchdog') {
                // Synthesize a decoder error so the recovery path takes
                // over (rebuild decoder, drop-deltas-until-keyframe).
                pendingError ??= new Error(
                    `decode: hang watchdog (no frames in ${decoderHangTimeoutMs} ms, pending=${pending.length}, codec=${currentCodec})`);
                codecProofTracker.noteDecoderError();
                lastDecoderActivityMs = now();
                wakeup = new PromiseSource<void>();
                continue;
            }

            if (winner.kind === 'wake') {
                wakeup = new PromiseSource<void>();
                continue;
            }

            const result = winner.result;
            nextSourcePromise = null;
            if (result.done) {
                sourceDone = true;
                continue;
            }
            const arrived = result.value;
            try {
                currentStats = arrived.stats;

                // Local snapshot: TS narrows pendingError to null otherwise
                // (it can't see the async writes from the error callback).
                const errSnapshot = pendingError;
                if (errSnapshot) {
                    if (!arrived.isKeyFrame) {
                        continue;
                    }
                    consecutiveRecoveries++;
                    if (!codecProofTracker.isProven() && consecutiveRecoveries >= maxRecoveries) {
                        const codec = currentCodec;
                        pendingError = null;
                        onCodecExhausted?.(codec);
                        throw new Error(
                            `${CODEC_EXHAUSTED_PREFIX} decode: recovery exhausted after ${consecutiveRecoveries} attempts (codec=${codec})`,
                            { cause: errSnapshot },
                        );
                    }
                    pendingError = null;
                    try { decoder.close(); } catch { /* ignore */ }
                    decoder = createDecoder(handlers);
                    configured = false;
                    pending.length = 0;
                }

                const dec = decoder;
                if (arrived.isKeyFrame) {
                    const newWidth = arrived.width || currentWidth;
                    const newHeight = arrived.height || currentHeight;
                    if (arrived.description && arrived.description.byteLength > 0)
                        descriptionByLayer.set(arrived.layerId, arrived.description);
                    const description = descriptionByLayer.get(arrived.layerId);

                    const dimChanged = configured
                        && (newWidth !== currentWidth || newHeight !== currentHeight);
                    if (!configured || dimChanged) {
                        if (!description && !canConfigureWithoutDescription(currentCodec)) {
                            // Treat "needs description" as a codec-side
                            // problem — feed into pendingError so the same
                            // recovery loop (rebuild → wait keyframe → maybe
                            // exhaust) handles it.
                            pendingError ??= new Error(
                                `decode: codec ${currentCodec} requires description but none provided`);
                            codecProofTracker.noteDecoderError();
                            continue;
                        }

                        // Pin displayAspect=coded so the browser doesn't
                        // derive display dims from the bitstream SPS/HVCC.
                        // Without this, Edge HEVC HW returns swapped portrait
                        // display dims (1280x720 for a 720x1280 coded portrait
                        // stream) and Chrome Android delivers VideoFrames
                        // whose display dims confuse <video srcObject>
                        // rendering of an MSTG-fed track — track stays black
                        // until the watchdog falls back to canvas after ~8 s.
                        const config: VideoDecoderConfig = {
                            codec: currentCodec,
                            codedWidth: newWidth || undefined,
                            codedHeight: newHeight || undefined,
                            hardwareAcceleration: initialConfig.hardwareAcceleration,
                            optimizeForLatency: initialConfig.optimizeForLatency,
                        };
                        if (description) config.description = description;
                        if (newWidth > 0 && newHeight > 0) {
                            config.displayAspectWidth = newWidth;
                            config.displayAspectHeight = newHeight;
                        }
                        try {
                            dec.configure(config);
                            configured = true;
                            currentWidth = newWidth;
                            currentHeight = newHeight;
                        } catch (e) {
                            // configure() throws on unsupported configs
                            // (bad SPS, codec/dim mismatch). Funnel through
                            // pendingError so it counts as a recovery
                            // attempt and feeds the codec-exhausted path.
                            pendingError ??= e instanceof Error ? e : new Error(String(e));
                            codecProofTracker.noteDecoderError();
                            continue;
                        }
                    }
                }

                if (!configured) {
                    continue;
                }

                const submitMs = now();
                pending.push({
                    capturedAt: arrived.capturedAt,
                    arrivedAt: arrived.arrivedAt,
                    layerId: arrived.layerId,
                    submitMs,
                    index: arrived.index,
                    dropTrace: arrived.dropTrace,
                });
                try {
                    dec.decode(arrived.chunk);
                } catch (e) {
                    pending.pop();
                    // Decode() throws on malformed chunks / decoder in
                    // bad state. Same funnel as configure() above —
                    // pendingError → next-keyframe rebuild → maybe
                    // codec exhaustion. Pre-tracker behaviour rethrew
                    // here, which short-circuited recovery and forced
                    // a hard pipeline failure.
                    pendingError ??= e instanceof Error ? e : new Error(String(e));
                    codecProofTracker.noteDecoderError();
                    if (!wakeup.isCompleted()) wakeup.resolve();
                }
            } finally {
                closeEncodedChunk(arrived.chunk);
            }
        }
    } finally {
        while (ready.length > 0) {
            const envelope = ready.shift()!;
            try { envelope.frame.close(); } catch { /* ignore */ }
        }
        try { decoder.close(); } catch { /* ignore */ }
        try { await sourceIter.return?.(); } catch { /* ignore */ }
    }
}
