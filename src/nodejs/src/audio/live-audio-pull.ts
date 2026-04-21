// Live audio pull — TS-side alternative to the .NET AudioTrackPlayer frame path.
//
// Instead of having the .NET `AudioTrackPlayer` iterate the Fusion RPC stream
// and shove every Opus frame through Blazor JS interop, the TS side subscribes
// to `ILiveAudioStreams.GetStream/GetReplayStream` directly, demultiplexes it,
// parses the A_OPUS_S container, and hands raw Opus packets to a consumer.
//
// The consumer is what stitches the result into playback (e.g. hands packets
// to an OpusDecoderWorker). This module itself is transport + parsing only,
// so it's easy to drop into either a full replacement for AudioTrackPlayer or
// a feature-flagged coexistence path.

import type { RpcStream } from 'actuallab-rpc';
import { getLogs } from 'logging';
import { Api } from '../api/api.js';
import {
    liveAudioStreamsApi,
    LiveStreamKind,
    type LiveStreamInfoDto,
    type LiveStreamSettingsDto,
} from '../api/live-audio-streams-api.js';
import {
    isActualOpusStreamHeader,
    parseActualOpusStreamHeader,
    type ActualOpusStreamHeader,
    type ActualOpusPacket,
} from './actual-opus-stream-parser.js';
import { runLiveStreamDemuxer, type LiveStreamStartedInfo } from './live-stream-demuxer.js';

const { debugLog, warnLog, errorLog } = getLogs('LiveAudioPull');

export const LiveAudioPullMode = {
    Live: 'live',
    Replay: 'replay',
} as const;
export type LiveAudioPullMode = (typeof LiveAudioPullMode)[keyof typeof LiveAudioPullMode];

export interface LiveAudioPullOptions {
    session: string;
    chatId: string;
    mode: LiveAudioPullMode;
    /** Live: optional filter (defaults to Audio). */
    settings?: LiveStreamSettingsDto;
    /** Replay: Moment ticks (100ns units). Use BigInt when the value exceeds
     *  Number.MAX_SAFE_INTEGER (~9e15) — anything from year 2000 onward does. */
    startAtTicks?: bigint | number;
    /** Replay: TimeSpan ticks (100ns units). */
    rewindOffsetTicks?: bigint | number;
    /** Replay: playback rate. Default 1.0. */
    speed?: number;
    /** If set, sub-streams authored by this user are dropped — prevents hearing
     *  your own voice echoed back while listening. Matches .NET
     *  `ChatListener.OnStreamStarted`'s `Authors.GetOwn` filter. */
    ownAuthorId?: string | null;
}

/**
 * Consumer of demuxed + parsed Opus packets for a single sub-stream.
 * `start` returns an `unknown` token that is later passed back to every
 * `onPacket` / `onEnd` call — use it to key per-stream state on the
 * consumer side.
 */
export interface LiveAudioPullConsumer {
    onStreamStarted(
        streamInfo: LiveStreamInfoDto,
        playsAtTicks: number,
        header: ActualOpusStreamHeader,
    ): Promise<unknown>;
    onOpusPacket(streamToken: unknown, packet: ActualOpusPacket): void;
    onStreamEnded(streamToken: unknown, error?: unknown): void;
}

export interface LiveAudioPullDriver {
    /** Await the outer RPC stream's completion (normal end or error). */
    readonly whenStopped: Promise<void>;
    stop(): Promise<void>;
}

/**
 * Start a pull. Returns a driver whose `whenStopped` resolves when the outer
 * stream ends naturally or when `stop()` is called. The outer stream's
 * failures are logged and surfaced via `whenStopped.reject`.
 */
export function startLiveAudioPull(
    options: LiveAudioPullOptions,
    consumer: LiveAudioPullConsumer,
): LiveAudioPullDriver {
    const abort = new AbortController();
    let stopped = false;

    // Unique scope per driver so concurrent drivers don't fight over
    // requireConnection/releaseConnection refcounts. Same pattern as
    // video-player.ts (`VideoPlayer:${streamId}`). Without this, Api.peer
    // never opens the WebSocket and the RPC call queues forever in
    // `_pendingSends` with no visible error.
    const connectionScope = `LiveAudioPull:${options.mode}:${options.chatId}:${++nextDriverId}`;
    Api.requireConnection(connectionScope);

    const whenStopped = (async (): Promise<void> => {
        const client = liveAudioStreamsApi.liveAudioStreams;
        const settings = options.settings ?? { StreamKindFilter: LiveStreamKind.Audio };

        warnLog?.log(
            `start: mode=${options.mode} chatId=${options.chatId} session=${options.session.slice(0, 8)}…`);

        // Watchdog so silent hangs are visible.
        const watchdog = setInterval(() => {
            warnLog?.log(
                `watchdog: still awaiting ${options.mode} stream for ${options.chatId} ` +
                `(canConnect=${Api.canConnect}, requiresConnection=${Api.requiresConnection}, ` +
                `isDotNetRpcConnected=${Api.isDotNetRpcConnected})`);
        }, 3000);

        let stream: RpcStream<unknown>;
        try {
            stream = options.mode === LiveAudioPullMode.Replay
                ? await client.GetReplayStream(
                    options.session,
                    options.chatId,
                    toBigIntTicks(options.startAtTicks),
                    toBigIntTicks(options.rewindOffsetTicks),
                    options.speed ?? 1.0,
                )
                : await client.GetStream(options.session, options.chatId, settings);
        }
        catch (e) {
            errorLog?.log(`${options.mode === LiveAudioPullMode.Replay ? 'GetReplayStream' : 'GetStream'} RPC failed:`, e);
            throw e;
        }
        finally {
            clearInterval(watchdog);
        }

        warnLog?.log(`RPC returned stream, starting demuxer for ${options.chatId}`);

        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (stopped) return;

        const ownAuthorId = options.ownAuthorId ?? null;
        await runLiveStreamDemuxer(
            stream,
            (started) => {
                if (ownAuthorId !== null && started.streamInfo.AuthorId === ownAuthorId) {
                    debugLog?.log(
                        `skip own-audio sub-stream N${started.streamIndex} ` +
                        `(author=${started.streamInfo.AuthorId})`);
                    // Drain the frames iterator to release server-side backpressure.
                    void drainIgnored(started.frames);
                    return;
                }
                void handleStart(consumer, started, abort.signal);
            },
            abort.signal,
        );

        warnLog?.log(`demuxer returned for ${options.chatId}`);
    })();

    whenStopped.catch((e: unknown) => {
        if (!stopped) errorLog?.log(`stream failed:`, e);
    });
    // Always release the connection scope once the driver exits.
    void whenStopped.finally(() => Api.releaseConnection(connectionScope));

    return {
        whenStopped,
        async stop(): Promise<void> {
            if (stopped) return;
            stopped = true;
            abort.abort();
            // Swallow the error that may surface from an aborted demuxer run.
            try { await whenStopped; } catch { /* expected */ }
        },
    };
}

let nextDriverId = 0;

async function drainIgnored(frames: AsyncIterable<Uint8Array>): Promise<void> {
    try {
        for await (const _ of frames) { /* drop */ }
    }
    catch { /* ignore */ }
}

/** Coerce ticks into BigInt for msgpack int64 encoding. Numbers within
 *  Number.MAX_SAFE_INTEGER convert losslessly; larger numbers may already be
 *  lossy (the JS `number` representation can't hit every int64 exactly), so
 *  prefer BigInt at the caller when you have full precision available. */
function toBigIntTicks(v: bigint | number | undefined): bigint {
    if (v === undefined) return 0n;
    if (typeof v === 'bigint') return v;
    return BigInt(Math.trunc(v));
}

/**
 * Pull-path wire format note — read carefully, the .NET stream semantics aren't
 * obvious. The server (`LiveStreamMuxer.ProcessStream`) hands us one
 * `LiveAudioFrame.Data` per iteration, with:
 *   - the first frame carrying the 19-byte A_OPUS_S header (Offset < 0),
 *   - every subsequent frame carrying exactly one raw Opus packet.
 * There is NO uint16 length-prefix container wrapping on this path — that
 * wrap only exists when the server serializes the full AudioSource into the
 * A_OPUS_S byte stream (e.g. for storage or WebSocket byte streams). So we
 * don't run the streaming `ActualOpusStreamParser` here — we just peel off
 * the header on first frame, then pass each remaining chunk through as one
 * Opus packet.
 */
const OPUS_FRAME_DURATION_MS = 20;

async function handleStart(
    consumer: LiveAudioPullConsumer,
    started: LiveStreamStartedInfo,
    signal: AbortSignal,
): Promise<void> {
    const streamId = started.streamInfo.StreamId;
    warnLog?.log(`handleStart: stream=${streamId}, author=${started.streamInfo.AuthorId}`);
    let streamToken: unknown = undefined;
    let ended = false;
    let chunkCount = 0;
    let packetCount = 0;
    let offsetMs = 0;
    const bail = (err?: unknown): void => {
        if (ended) return;
        ended = true;
        warnLog?.log(
            `handleStart.bail: stream=${streamId}, chunks=${chunkCount}, packets=${packetCount}`,
            err ?? '');
        if (streamToken !== undefined) {
            try { consumer.onStreamEnded(streamToken, err); }
            catch (e) { warnLog?.log('onStreamEnded handler threw:', e); }
        }
    };

    try {
        for await (const chunk of started.frames) {
            if (signal.aborted) { bail(); return; }
            chunkCount++;
            if (chunkCount <= 2)
                warnLog?.log(`stream=${streamId}: chunk #${chunkCount} len=${chunk.length}`);

            if (streamToken === undefined) {
                // Live streams prepend a LiveAudioFrame carrying the A_OPUS_S
                // header; replay streams (ReplayStreamMuxer) hand us raw Opus
                // packets directly. Detect via the 8-byte prefix and fall back
                // to `streamInfo.Format.PreSkip` when absent.
                let header: ActualOpusStreamHeader;
                let chunkIsHeader = false;
                if (isActualOpusStreamHeader(chunk)) {
                    try {
                        header = parseActualOpusStreamHeader(chunk);
                    }
                    catch (e) {
                        errorLog?.log(`stream=${streamId}: bad header:`, e);
                        bail(e);
                        return;
                    }
                    chunkIsHeader = true;
                    warnLog?.log(
                        `stream=${streamId}: header parsed, preSkip=${header.preSkip}, creating renderer…`);
                } else {
                    const fmt = started.streamInfo.Format;
                    const preSkip = fmt?.PreSkip ?? 0;
                    header = { version: 3, preSkip, createdAtTicks: BigInt(0) };
                    warnLog?.log(
                        `stream=${streamId}: no A_OPUS_S header on wire (replay?), ` +
                        `preSkip=${preSkip} from Format, first chunk = opus packet`);
                }
                try {
                    streamToken = await consumer.onStreamStarted(
                        started.streamInfo,
                        started.playsAtTicks,
                        header,
                    );
                    warnLog?.log(`stream=${streamId}: renderer ready`);
                }
                catch (e) {
                    errorLog?.log(`onStreamStarted handler threw for ${streamId}:`, e);
                    bail(e);
                    return;
                }
                if (chunkIsHeader)
                    continue; // Header frame isn't an Opus packet.
                // Replay path: current chunk IS the first Opus packet — fall
                // through to the packet-feed branch below.
            }

            // Subsequent frames each carry exactly one Opus packet.
            packetCount++;
            const packet: ActualOpusPacket = { data: chunk, offsetMs };
            offsetMs += OPUS_FRAME_DURATION_MS;
            if (packetCount <= 3 || packetCount % 100 === 0)
                debugLog?.log(`stream=${streamId}: feed packet #${packetCount} len=${chunk.length}`);
            try { consumer.onOpusPacket(streamToken, packet); }
            catch (e) { warnLog?.log('onOpusPacket handler threw:', e); }
        }
        warnLog?.log(`stream=${streamId}: frames iterator ended, packets=${packetCount}`);
        bail();
    }
    catch (e) {
        errorLog?.log(`stream=${streamId}: frames iterator threw:`, e);
        bail(e);
    }
}
