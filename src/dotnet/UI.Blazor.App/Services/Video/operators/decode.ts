import { from, type PipeOperator } from 'ix-ext';
import { abortPromise, PromiseSource } from 'promises';
import { closeEncodedChunk, type ArrivedChunk, type DecodedFrame } from '../frame-envelopes';

// WebCodecs `VideoDecoder` surface. Tests inject a fake.
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
    /** Notified when recovery is exhausted; the operator throws on the
     *  same tick, the callback is purely informational. The receiver
     *  decides whether to restart with the failing codec excluded. */
    onCodecExhausted?: (codec: string) => void;
    /** Test override for `performance.now`. */
    now?: () => number;
    /** Max consecutive recovery attempts before giving up. Default: 4. */
    maxRecoveries?: number;
    abortSignal?: AbortSignal;
}

// HEVC (`hev1`/`hvc1`) needs a description; AVC and AV1 inline their
// codec parameters in the bytestream and can configure without one.
function canConfigureWithoutDescription(codec: string): boolean {
    return codec.startsWith('avc1') || codec.startsWith('av01');
}

// FIFO entry mirroring `decode()` calls. Output frames are paired by
// shift order (WebCodecs guarantees in-order output per submitted chunk).
interface PendingDecode {
    capturedAt: { timeMs: number; epoch: number };
    arrivedAt: ArrivedChunk['arrivedAt'];
    spatialLayerId: number;
    submitMs: number;
}

/**
 * `ArrivedChunk → DecodedFrame`. Lazy decoder init on the first
 * keyframe; reconfigures on dim change. Per-spatial-layer description
 * cache covers HEVC's "later keyframes may omit description" case.
 *
 * Errors from the decoder's error callback are buffered and trigger
 * a rebuild + reconfigure on the next keyframe. After `maxRecoveries`
 * consecutive recoveries `onCodecExhausted` fires and the operator
 * throws; pre-keyframe deltas during recovery are dropped.
 */
export function decode(opts: DecodeOptions): PipeOperator<ArrivedChunk, DecodedFrame> {
    const initialConfig = opts.initialConfig;
    const createDecoder = opts.createDecoder;
    const onCodecExhausted = opts.onCodecExhausted;
    const now = opts.now ?? ((): number => performance.now());
    const maxRecoveries = opts.maxRecoveries ?? 4;
    const abortSignal = opts.abortSignal;

    return source => from(decodeAsync(
        source,
        initialConfig,
        createDecoder,
        onCodecExhausted,
        now,
        maxRecoveries,
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

    const handlers = {
        onFrame: (frame: VideoFrame): void => {
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
                spatialLayerId: meta.spatialLayerId,
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
        | { kind: 'abort' };

    try {
        for (;;) {
            if (abortSignal?.aborted) return;
            // Drain any ready envelopes synchronously first.
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
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition, @typescript-eslint/only-throw-error
            if (pendingError && sourceDone) throw pendingError;
            if (sourceDone && pending.length === 0) return;

            // Race source.next() against decoder activity (frame or error).
            const sourceP = sourceDone ? null : armSource();
            const wakeP = wakeup;

            let winner: WaitResult;
            if (sourceP) {
                winner = await Promise.race([
                    sourceP.then((r): WaitResult => ({ kind: 'src', result: r })),
                    wakeP.then((): WaitResult => ({ kind: 'wake' })),
                    abortWait,
                ]);
            } else {
                winner = await Promise.race([
                    wakeP.then((): WaitResult => ({ kind: 'wake' })),
                    abortWait,
                ]);
            }

            if (winner.kind === 'abort')
                return;

            if (winner.kind === 'wake') {
                // Re-arm; any pendingError or queued frame is handled at
                // the top of the loop on the next iteration.
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

                // Snapshot via local: TS can't see the async writes from
                // the decoder error callback so it narrows pendingError
                // to null.
                const errSnapshot = pendingError as Error | null;
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
                        descriptionByLayer.set(arrived.spatialLayerId, arrived.description);
                    const description = descriptionByLayer.get(arrived.spatialLayerId);

                    const dimChanged = configured
                        && (newWidth !== currentWidth || newHeight !== currentHeight);
                    if (!configured || dimChanged) {
                        if (!description && !canConfigureWithoutDescription(currentCodec))
                            throw new Error(
                                `decode: codec ${currentCodec} requires description but none provided`);

                        // Pin displayAspect=coded so the browser doesn't derive
                        // display dims from the bitstream SPS/HVCC. Without
                        // this, Edge HEVC HW returns swapped portrait display
                        // dims (1280×720 for a 720×1280 coded portrait stream)
                        // and Chrome Android delivers VideoFrames whose
                        // display dims confuse `<video srcObject>` rendering
                        // of an MSTG-fed track — track stays black until the
                        // watchdog falls back to canvas after ~8 s.
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
                    spatialLayerId: arrived.spatialLayerId,
                    submitMs,
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
