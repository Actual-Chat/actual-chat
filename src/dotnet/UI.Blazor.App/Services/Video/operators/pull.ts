import { AsyncIterableX, exclusive, finalize, from } from 'ix-ext';
import { abortPromise } from 'promises';
import { MonotonicClock } from 'clocks';
import { closeEncodedChunk, type ArrivedChunk, type VideoPlaybackStats } from '../frame-envelopes';

// Mirror of the .NET wire DTO; PascalCase matches the MessagePack field names.
// Offset/Duration are 100-ns ticks; OffsetEpoch is the sender's MonotonicClock epoch.
export interface VideoFrameDto {
    Data: Uint8Array;
    Offset: number | bigint;
    OffsetEpoch?: number;
    Duration: number | bigint;
    IsKeyFrame: boolean;
    Width?: number;
    Height?: number;
    Description?: Uint8Array | null;
    Codec?: string | null;
    LayerId?: number;
    MaxLayerId?: number;
    TemporalLayerId?: number;
    SourceWidth?: number;
    SourceHeight?: number;
}

export interface PullSourceOptions {
    streamId: string;
    getStream: (
        streamId: string,
    ) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    arrivalClock?: MonotonicClock;
    stats: VideoPlaybackStats;
    abortSignal?: AbortSignal;
    // Graceful source-completion signal for local stop/restart.
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

// MessagePack may decode Description as a view onto a shared buffer;
// EncodedVideoChunk needs a standalone ArrayBuffer.
function copyToArrayBuffer(view: Uint8Array): ArrayBuffer {
    const out = new ArrayBuffer(view.byteLength);
    new Uint8Array(out).set(view);
    return out;
}

// VideoFrameDto -> ArrivedChunk. No pacing — downstream operators own that.
// `exclusive` rejects concurrent iteration; `finalize` runs `return()` on the
// upstream iterator so the in-flight RPC subscription unwinds on dispose.
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
    const segment = from(impl());
    return exclusive(finalize<ArrivedChunk>(release)(segment));

    async function* impl(): AsyncIterable<ArrivedChunk> {
        if (stopSignal?.aborted || abortSignal?.aborted)
            return;

        const iterableOrPromise = getStream(streamId);
        const iterableResult = await Promise.race([
            Promise.resolve(iterableOrPromise),
            abortAsDone<AsyncIterable<VideoFrameDto>>(stopSignal),
            abortAsDone<AsyncIterable<VideoFrameDto>>(abortSignal),
        ]);
        if ('done' in iterableResult) return;

        const iterable: AsyncIterable<VideoFrameDto> = iterableResult;
        const iterator = iterable[Symbol.asyncIterator]();
        activeIterator = iterator;
        try {
            while (!stopSignal?.aborted && !abortSignal?.aborted) {
                const result = await Promise.race([
                    iterator.next(),
                    abortAsDone<VideoFrameDto>(stopSignal),
                    abortAsDone<VideoFrameDto>(abortSignal),
                ]);
                if (result.done)
                    return;

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
                        layerId: dto.LayerId ?? 0,
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
