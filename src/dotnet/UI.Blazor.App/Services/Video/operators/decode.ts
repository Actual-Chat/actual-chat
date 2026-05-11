import { from, type PipeOperator } from 'ix-ext';
import { abortPromise, PromiseSource } from 'promises';
import { closeEncodedChunk, type ArrivedChunk, type DecodedFrame } from '../frame-envelopes';

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
            const decodeTimeMs = decodedAtMs - meta.submitMs;
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
            stats.framesDecoded++;
            stats.decodeTimeMsSum += decodeTimeMs;
            stats.decodeTimeMsCount++;
            consecutiveRecoveries = 0;
            ready.push(envelope);
            if (!wakeup.isCompleted()) wakeup.resolve();
        },
        onError: (e: Error): void => {
            lastDecoderActivityMs = now();
            pendingError = e;
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
                // Synthesize an error so the recovery path takes over.
                pendingError ??= new Error(
                    `decode: hang watchdog (no frames in ${decoderHangTimeoutMs} ms, pending=${pending.length}, codec=${currentCodec})`);
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
                        arrived.stats.chunksDroppedDecoderError++;
                        continue;
                    }
                    consecutiveRecoveries++;
                    if (consecutiveRecoveries >= maxRecoveries) {
                        const codec = currentCodec;
                        pendingError = null;
                        onCodecExhausted?.(codec);
                        throw new Error(
                            `decode: recovery exhausted after ${consecutiveRecoveries} attempts (codec=${codec})`,
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
                        if (!description && !canConfigureWithoutDescription(currentCodec))
                            throw new Error(
                                `decode: codec ${currentCodec} requires description but none provided`);

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
                        dec.configure(config);
                        configured = true;
                        currentWidth = newWidth;
                        currentHeight = newHeight;
                    }
                }

                if (!configured) {
                    arrived.stats.chunksDroppedDecoderError++;
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
                    throw e;
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
