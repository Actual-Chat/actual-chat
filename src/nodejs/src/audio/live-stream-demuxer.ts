// TS port of LiveStreamDemuxer — mirror of
// src/dotnet/UI.Blazor.App/Services/Streaming/LiveStreamDemuxer.cs.
//
// Consumes an async iterable of multiplexed `LiveStreamItem`s coming off an
// RpcStream and emits one logical sub-stream per `StreamIndex`. Sub-streams are
// delivered to a callback the moment a `LiveStreamStart` arrives, exposing an
// AsyncIterable<Uint8Array> the caller can feed into the decoder pipeline.

import { getLogs } from 'logging';
import {
    LiveStreamTag,
    parseLiveStreamItem,
    type LiveStreamInfoDto,
} from '../api/live-audio-streams-api.js';

const { debugLog, warnLog } = getLogs('LiveStreamDemuxer');

export interface LiveStreamStartedInfo {
    /** Sub-stream index in the multiplexed wire stream (distinct from StreamId). */
    streamIndex: number;
    /** Stream metadata as published by the server. */
    streamInfo: LiveStreamInfoDto;
    /** Target playback moment (TimeSpan ticks — 100ns). */
    playsAtTicks: number;
    /** Async iterable of raw per-stream bytes (container-encoded, i.e. A_OPUS_S). */
    frames: AsyncIterable<Uint8Array>;
}

interface SubStream {
    queue: Uint8Array[];
    waiters: ((value: IteratorResult<Uint8Array>) => void)[];
    ended: boolean;
    error?: unknown;
}

/**
 * Run the demuxer over `source`. `onStreamStarted` is invoked every time a
 * `LiveStreamStart` arrives for a new `StreamIndex`; its `frames` iterable
 * completes when the matching `LiveStreamEnd` (or a `LiveStreamReset`) fires.
 */
export async function runLiveStreamDemuxer(
    source: AsyncIterable<unknown>,
    onStreamStarted: (info: LiveStreamStartedInfo) => void,
    signal?: AbortSignal,
): Promise<void> {
    const streams = new Map<number, SubStream>();

    const flush = (err?: unknown): void => {
        for (const [, sub] of streams) {
            if (err !== undefined) sub.error = err;
            sub.ended = true;
            // Wake any in-flight consumers.
            while (sub.waiters.length > 0) {
                const resolve = sub.waiters.shift()!;
                if (err !== undefined) resolve({ value: undefined, done: true });
                else resolve({ value: undefined, done: true });
            }
        }
        streams.clear();
    };

    if (signal?.aborted) return;
    const abortHandler = (): void => flush();
    signal?.addEventListener('abort', abortHandler, { once: true });

    let itemCount = 0;
    try {
        for await (const raw of source) {
            itemCount++;
            if (itemCount <= 3) {
                // Dump the first few raw items so we can verify the wire shape
                // matches `parseLiveStreamItem`'s expectations.
                warnLog?.log(`raw item #${itemCount}:`, raw);
            }
            if (signal?.aborted) break;
            const item = parseLiveStreamItem(raw);
            if (item === null) {
                warnLog?.log('unparseable LiveStreamItem:', raw);
                continue;
            }
            switch (item.tag) {
            case LiveStreamTag.Reset: {
                debugLog?.log(`Reset: flushing ${streams.size} in-flight streams`);
                flush();
                break;
            }
            case LiveStreamTag.Start: {
                const { StreamIndex, StreamInfo, PlaysAt } = item.payload;
                if (streams.has(StreamIndex)) {
                    warnLog?.log(`Start N${StreamIndex}: duplicate — ignoring`);
                    continue;
                }
                const sub: SubStream = { queue: [], waiters: [], ended: false };
                streams.set(StreamIndex, sub);
                const frames = subStreamToAsyncIterable(sub);
                debugLog?.log(`Start N${StreamIndex} stream=${StreamInfo.StreamId}`);
                try {
                    onStreamStarted({
                        streamIndex: StreamIndex,
                        streamInfo: StreamInfo,
                        playsAtTicks: PlaysAt,
                        frames,
                    });
                }
                catch (e) {
                    warnLog?.log('onStreamStarted handler threw:', e);
                }
                break;
            }
            case LiveStreamTag.Frame: {
                const { StreamIndex, Data } = item.payload;
                const sub = streams.get(StreamIndex);
                if (!sub || sub.ended) continue;
                if (sub.waiters.length > 0) {
                    const resolve = sub.waiters.shift()!;
                    resolve({ value: Data, done: false });
                } else {
                    sub.queue.push(Data);
                }
                break;
            }
            case LiveStreamTag.End: {
                const { StreamIndex } = item.payload;
                const sub = streams.get(StreamIndex);
                if (!sub) continue;
                streams.delete(StreamIndex);
                sub.ended = true;
                while (sub.waiters.length > 0) {
                    const resolve = sub.waiters.shift()!;
                    resolve({ value: undefined, done: true });
                }
                break;
            }
            }
        }
        debugLog?.log(`Stream ended normally after ${itemCount} items`);
    }
    catch (e) {
        warnLog?.log(`demuxer error after ${itemCount} items:`, e);
        flush(e);
        throw e;
    }
    finally {
        signal?.removeEventListener('abort', abortHandler);
        flush();
    }
}

function subStreamToAsyncIterable(sub: SubStream): AsyncIterable<Uint8Array> {
    return {
        [Symbol.asyncIterator](): AsyncIterator<Uint8Array> {
            return {
                next(): Promise<IteratorResult<Uint8Array>> {
                    if (sub.queue.length > 0) {
                        const value = sub.queue.shift()!;
                        return Promise.resolve({ value, done: false });
                    }
                    if (sub.ended) {
                        if (sub.error !== undefined) {
                            const raw: unknown = sub.error;
                            const err = raw instanceof Error
                                ? raw
                                : new Error(typeof raw === 'string' ? raw : JSON.stringify(raw));
                            return Promise.reject(err);
                        }
                        return Promise.resolve({ value: undefined, done: true });
                    }
                    return new Promise(resolve => sub.waiters.push(resolve));
                },
                return(): Promise<IteratorResult<Uint8Array>> {
                    sub.ended = true;
                    sub.queue.length = 0;
                    return Promise.resolve({ value: undefined, done: true });
                },
            };
        },
    };
}
