import { AsyncIterableX, exclusive, finalize, from } from 'ix-ext';
import { abortPromise } from 'promises';
import { MonotonicClock } from 'clocks';
import { closeEncodedChunk, type ArrivedChunk, type PlayerStats } from '../frame-envelopes';
import { quantize } from '../orientation/quantize';

// Mirror of the .NET wire DTO; PascalCase matches the MessagePack field names.
// Offset/Duration are 100-ns ticks; OffsetEpoch is the sender's MonotonicClock epoch.
// IsKeyFrame is NOT on the wire — derived as `KeyFrameIndex === Index`.
export interface VideoFrameDto {
    Data: Uint8Array;
    Offset: number | bigint;
    Duration: number | bigint;
    OffsetEpoch?: number;
    KeyFrameIndex?: number;
    Index?: number;
    Width?: number;
    Height?: number;
    LayerId?: number;
    LayerCount?: number;
    MaxLayerWidth?: number;
    MaxLayerHeight?: number;
    TemporalLayerId?: number;
    TemporalLayerCount?: number;
    Codec?: string | null;
    Description?: Uint8Array | null;
    DropTrace?: Uint8Array | null;
    Rotation?: number;
    // DateTime.UtcNow.Ticks (100-ns ticks since AD 0001-01-01) stamped by
    // VideoStreamingBackend.ProcessFrames. Missing/0 == legacy frame.
    ServerArrivedAtTicks?: number | bigint;
}

export interface PullSourceOptions {
    streamId: string;
    getStream: (
        streamId: string,
    ) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    arrivalClock?: MonotonicClock;
    stats: PlayerStats;
    abortSignal?: AbortSignal;
    // Graceful source-completion signal for local stop/restart.
    stopSignal?: AbortSignal;
}

const TICKS_PER_MILLISECOND = 10000n;
const TICKS_PER_MICROSECOND = 10n;
// DateTime ticks at Unix epoch (1970-01-01 UTC). Subtracted to convert
// .NET DateTime ticks into Unix-epoch milliseconds comparable with Date.now().
const UNIX_EPOCH_DOTNET_TICKS = 621355968000000000n;

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

function dateTimeTicksToUnixMs(ticks: number | bigint): number {
    const big = typeof ticks === 'bigint' ? ticks : BigInt(ticks);
    if (big === 0n) return 0;
    return Number((big - UNIX_EPOCH_DOTNET_TICKS) / TICKS_PER_MILLISECOND);
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
                // IsKeyFrame is derived: keyframe iff KeyFrameIndex equals Index.
                const index = dto.Index ?? 0;
                const keyFrameIndex = dto.KeyFrameIndex ?? 0;
                const isKeyFrame = keyFrameIndex === index;
                const chunkInit: EncodedVideoChunkInit = {
                    type: isKeyFrame ? 'key' : 'delta',
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
                        index,
                        // Copy on intake: MessagePack may hand us a view into
                        // a shared buffer, and downstream operators mutate.
                        dropTrace: dto.DropTrace && dto.DropTrace.byteLength > 0
                            ? Array.from(dto.DropTrace)
                            : [],
                        serverArrivedAtUnixMs: dto.ServerArrivedAtTicks != null
                            ? dateTimeTicksToUnixMs(dto.ServerArrivedAtTicks)
                            : 0,
                        isKeyFrame,
                        layerId: dto.LayerId ?? 0,
                        width: dto.Width ?? 0,
                        height: dto.Height ?? 0,
                        rawByteLength: data.byteLength,
                        rotation: quantize((dto.Rotation ?? 0) * 90),
                        stats,
                    };
                    if (dto.Description && dto.Description.byteLength > 0)
                        envelope.description = copyToArrayBuffer(dto.Description);

                    stats.bytesReceived += data.byteLength;
                    if (dto.TemporalLayerCount && dto.TemporalLayerCount > stats.producerTemporalLayerCount)
                        stats.producerTemporalLayerCount = dto.TemporalLayerCount;
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
