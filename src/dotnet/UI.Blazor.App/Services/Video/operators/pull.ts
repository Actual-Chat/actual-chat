import { AsyncIterableX, exclusive, finalize, from } from 'ix-ext';
import { abortPromise } from 'promises';
import { MonotonicClock } from 'clocks';
import { getLogs } from 'logging';
import { closeEncodedChunk, type ArrivedChunk, type VideoPlaybackStats } from '../frame-envelopes';

const { debugLog } = getLogs('VideoPipeline');

// Wire DTO mirror — kept independent of the streaming-api module so this
// operator stays test-isolatable. Field names are PascalCased to match
// the wire format produced by the .NET sender.
export interface VideoFrameDto {
    Data: Uint8Array;
    /** TimeSpan ticks (100ns) — sender's MonotonicClock-domain offset. */
    Offset: number | bigint;
    /** Sender's MonotonicClock epoch at capture. Missing → 0. */
    OffsetEpoch?: number;
    Duration: number | bigint;
    IsKeyFrame: boolean;
    Width?: number;
    Height?: number;
    Description?: Uint8Array | null;
    Codec?: string | null;
    SpatialLayerId?: number;
    MinSpatialLayerId?: number;
    MaxSpatialLayerId?: number;
    TemporalLayerId?: number;
    SourceWidth?: number;
    SourceHeight?: number;
}

export interface PullSourceOptions {
    streamId: string;
    /** Production: bound to `streamingApi.liveVideoStreams.GetStream`. */
    getStream: (
        streamId: string,
    ) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    /** Receiver-side wallclock. Default: a fresh `MonotonicClock`. */
    arrivalClock?: MonotonicClock;
    stats: VideoPlaybackStats;
    abortSignal?: AbortSignal;
    /** Graceful source-completion signal for local stop/restart. */
    stopSignal?: AbortSignal;
}

const TICKS_PER_MILLISECOND = 10000n;
const TICKS_PER_MICROSECOND = 10n;

function abortAsDone<T>(abortSignal: AbortSignal | undefined): Promise<IteratorReturnResult<T>> {
    if (!abortSignal)
        return new Promise(() => { /* never resolves */ });

    return abortPromise(abortSignal).catch(() => ({
        value: undefined as unknown as T,
        done: true,
    }));
}

function ticksToMs(ticks: number | bigint): number {
    const big = typeof ticks === 'bigint' ? ticks : BigInt(ticks);
    return Number(big / TICKS_PER_MILLISECOND);
}

function ticksToUs(ticks: number | bigint): number {
    const big = typeof ticks === 'bigint' ? ticks : BigInt(ticks);
    return Number(big / TICKS_PER_MICROSECOND);
}

// MessagePack may decode `Description` as a view onto a shared buffer;
// `EncodedVideoChunk` needs a standalone ArrayBuffer.
function copyToArrayBuffer(view: Uint8Array): ArrayBuffer {
    const out = new ArrayBuffer(view.byteLength);
    new Uint8Array(out).set(view);
    return out;
}

/**
 * `VideoFrameDto → ArrivedChunk`. Pulls from `getStream(streamId)`,
 * wraps each DTO into an envelope, paces nothing — downstream operators
 * own pacing.
 *
 * `exclusive` rejects concurrent iteration; `finalize` calls the upstream
 * iterator's `return()` so an in-flight RPC subscription unwinds.
 */
export function pullSource(opts: PullSourceOptions): AsyncIterableX<ArrivedChunk> {
    const { streamId, getStream, stats, abortSignal, stopSignal } = opts;
    const arrivalClock = opts.arrivalClock ?? new MonotonicClock();
    let activeIterator: AsyncIterator<VideoFrameDto> | null = null;
    const release = async (): Promise<void> => {
        const it = activeIterator;
        activeIterator = null;
        if (!it) return;

        try { await it.return?.(); } catch { /* ignore */ }
    };
    const tag = streamId.slice(-8);
    const segment = from(impl());
    return exclusive(finalize<ArrivedChunk>(release)(segment));

    async function* impl(): AsyncIterable<ArrivedChunk> {
        if (stopSignal?.aborted || abortSignal?.aborted)
            return;

        debugLog?.log(`pullSource[${tag}]: calling getStream`);
        const iterableOrPromise = getStream(streamId);
        const iterableResult = await Promise.race([
            Promise.resolve(iterableOrPromise),
            abortAsDone<AsyncIterable<VideoFrameDto>>(stopSignal),
            abortAsDone<AsyncIterable<VideoFrameDto>>(abortSignal),
        ]);
        if ('done' in iterableResult) return;

        const iterable: AsyncIterable<VideoFrameDto> = iterableResult;
        debugLog?.log(`pullSource[${tag}]: getStream resolved, awaiting first chunk`);
        const iterator = iterable[Symbol.asyncIterator]();
        activeIterator = iterator;
        let yielded = 0;
        try {
            while (!stopSignal?.aborted && !abortSignal?.aborted) {
                const result = await Promise.race([
                    iterator.next(),
                    abortAsDone<VideoFrameDto>(stopSignal),
                    abortAsDone<VideoFrameDto>(abortSignal),
                ]);
                if (result.done) {
                    debugLog?.log(`pullSource[${tag}]: iterator done after ${yielded} chunks`);
                    return;
                }
                if (yielded === 0)
                    debugLog?.log(`pullSource[${tag}]: first chunk arrived (key=${result.value.IsKeyFrame})`);

                yielded++;
                if (stopSignal?.aborted || abortSignal?.aborted)
                    return;

                const dto = result.value;
                const arrivedAt = arrivalClock.now();
                const data = dto.Data;
                const durationUs = ticksToUs(dto.Duration);
                const chunkInit: EncodedVideoChunkInit = {
                    type: dto.IsKeyFrame ? 'key' : 'delta',
                    timestamp: ticksToUs(dto.Offset),
                    data,
                };
                if (durationUs > 0)
                    chunkInit.duration = durationUs;

                const chunk = new EncodedVideoChunk(chunkInit);
                let mustClose = true;
                try {
                    const envelope: ArrivedChunk = {
                        chunk,
                        arrivedAt,
                        capturedAt: {
                            timeMs: ticksToMs(dto.Offset),
                            epoch: dto.OffsetEpoch ?? 0,
                        },
                        isKeyFrame: dto.IsKeyFrame,
                        spatialLayerId: dto.SpatialLayerId ?? 0,
                        width: dto.Width ?? 0,
                        height: dto.Height ?? 0,
                        rawByteLength: data.byteLength,
                        stats,
                    };
                    if (dto.Description && dto.Description.byteLength > 0)
                        envelope.description = copyToArrayBuffer(dto.Description);

                    stats.chunksArrived++;
                    stats.bytesReceived += data.byteLength;
                    mustClose = false;
                    yield envelope;
                } finally {
                    if (mustClose)
                        closeEncodedChunk(chunk);
                }
            }
        } finally {
            await release();
        }
    }
}
