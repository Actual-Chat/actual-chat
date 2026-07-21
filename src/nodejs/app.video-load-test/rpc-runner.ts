// Fusion RPC producer / consumer harnesses.
//
// Connection model — mirrors the C# App.VideoLoadTest RPC path and the
// production browser video streaming path:
// ONE shared `RpcClientPeer` multiplexes every producer and every consumer
// over a single WebSocket. That's how Fusion RPC is designed to be used.
// Sharing a peer collapses all producer/consumer dispatch into a single socket
// with multiplexed stream IDs (`peer.sharedObjects.nextId()` hands out distinct
// IDs for every producer sender).
//
// The `RpcHarnessBundle` is built once in main() and handed to every
// consumer/producer task. Clients are resolved off the same hub so there's
// exactly one `LiveVideoStreamsClient` shared across the whole run.

import { RpcHub, RpcClientPeer, RpcStream, RpcRefBuilder } from '../src/actuallab-rpc';

import { createNodeWsFactory } from './node-ws.js';
import { FrameConfig, generateFrame, paceFrame } from './frame-gen.js';
import type { Metrics } from './metrics.js';
import {
    LiveVideoStreamsDef,
    type LiveVideoStreamsClient,
    type VideoFrameDto,
    type VideoFormat,
    type VideoStreamInfo,
} from './service-defs.js';

const RPC_SERIALIZATION_FORMAT = 'msgpack6';

export interface RpcRunContext {
    apiUrl: string;
    sessionId: string;
    metrics: Metrics;
    abort: AbortSignal;
}

/** Shared RPC plumbing passed into every producer and consumer task. */
export interface RpcHarnessBundle {
    hub: RpcHub;
    peer: RpcClientPeer;
    liveVideoStreams: LiveVideoStreamsClient;
}

/** Build the single shared bundle used by the whole test run. */
export async function createRpcHarnessBundle(
    ctx: Omit<RpcRunContext, 'metrics'>,
): Promise<RpcHarnessBundle> {
    const hub = new RpcHub();
    const url = RpcRefBuilder.forClient(ctx.apiUrl, RPC_SERIALIZATION_FORMAT);
    // mustStart=false — set the Node-specific webSocketFactory before start.
    const peer = hub.getClientPeer(url, (h, r) => new RpcClientPeer(h, r, false));
    peer.webSocketFactory = createNodeWsFactory({ sessionId: ctx.sessionId });
    const whenConnected = peer.whenConnected();
    peer.start();
    await whenConnected;
    const liveVideoStreams = hub.addClient(peer, LiveVideoStreamsDef) as unknown as LiveVideoStreamsClient;
    return { hub, peer, liveVideoStreams };
}

export function closeRpcHarnessBundle(bundle: RpcHarnessBundle): void {
    try { bundle.peer.close(); } catch { /* ignore */ }
}

export async function runRpcProducer(
    bundle: RpcHarnessBundle,
    ctx: RpcRunContext,
    chatIdx: number,
    prodIdx: number,
    chatId: string,
): Promise<void> {
    try {
        const { liveVideoStreams } = bundle;
        const format: VideoFormat = {
            Codec: FrameConfig.Codec,
            CodecSettings: '',
            LayerId: 0,
            Size: { Width: FrameConfig.Width, Height: FrameConfig.Height },
            SourceSize: { Width: FrameConfig.Width, Height: FrameConfig.Height },
        };
        const clientStartAtSec = Date.now() / 1000;  // Unix epoch (seconds, double)

        // Real-time video stream: isRealTime=true, allowReconnect=false, ackPeriod=5, ackAdvance=31.
        // toRef() creates the sender, registers it, and starts pumping in the background.
        const stream = new RpcStream<VideoFrameDto>((async function* () {
            const start = Date.now();
            for (let i = 0; !ctx.abort.aborted; i++) {
                await paceFrame(start, i);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- aborted may flip during the await
                if (ctx.abort.aborted) break;
                const frame = generateFrame(i);
                ctx.metrics.recordSent(chatIdx, prodIdx, frame.Offset);
                yield frame;
            }
        })(), { isRealTime: true, allowReconnect: false, ackPeriod: 5, ackAdvance: 31 });

        // PushStream is a long-lived call — the server holds it open until
        // the frame stream ends. Fire-and-forget; surface any rejection so
        // it's visible via the global unhandledRejection handler.
        void liveVideoStreams
            .PushStream('~', chatId, clientStartAtSec, format, 0 /* VideoSourceKind.Camera */, stream.toRef(bundle.peer))
            .catch((err: unknown) => {
                if (!ctx.abort.aborted)
                    console.error(
                        `[rpc producer chat=${chatIdx} prod=${prodIdx}] PushStream rejected:`, err);
            });

        await stream.whenSent;
    } catch (e) {
        if (!ctx.abort.aborted) {
            console.error(
                `[rpc producer chat=${chatIdx} prod=${prodIdx}] ${(e as Error).message}`);
        }
    }
}

export async function runRpcConsumer(
    bundle: RpcHarnessBundle,
    ctx: RpcRunContext,
    chatIdx: number,
    consumerIdx: number,
    streamIdx: number,
    streamId: string,
): Promise<void> {
    try {
        const stream = await bundle.liveVideoStreams.GetStream('~', streamId);

        for await (const frame of stream) {
            if (ctx.abort.aborted) break;
            ctx.metrics.recordReceived(
                chatIdx, consumerIdx, streamIdx, frame.Offset, frame.Data.byteLength);
        }
    } catch (e) {
        if (!ctx.abort.aborted) {
            const msg = (e as Error).message;
            // "Peer disconnected." is expected on shutdown.
            if (!msg.includes('Peer disconnected'))
                console.error(
                    `[rpc consumer chat=${chatIdx} cons=${consumerIdx} stream=${streamIdx}] ${msg}`);
        }
    }
}

/**
 * Discover the live video streams per chat via a single short-lived RPC peer.
 * Polls `ILiveVideoStreams.List` until every chat reports ≥ `expected` streams
 * or the timeout is hit. Matches the C# discovery step (though without
 * Computed.WhenInvalidated since the TS RPC client has no compute subscription).
 */
export async function discoverStreams(
    bundle: RpcHarnessBundle,
    ctx: Omit<RpcRunContext, 'metrics'>,
    chatIds: readonly string[],
    expected: number,
    timeoutMs: number,
): Promise<string[][]> {
    {
        const client = bundle.liveVideoStreams;

        const deadline = Date.now() + timeoutMs;
        const result: string[][] = chatIds.map(() => []);
        const pending = new Set<number>(chatIds.map((_, i) => i));
        // Fail fast on repeated errors — a misconfigured call (wrong method
        // name, wrong CallTypeId, server-side exception) would otherwise
        // flood the server at 2 Hz until the outer timeout.
        const MAX_CONSEC_ERRORS = 3;
        let consecErrors = 0;
        let lastError: unknown;

        while (pending.size > 0 && !ctx.abort.aborted) {
            let hadError = false;
            for (const ci of [...pending]) {
                try {
                    const streams = await client.List('~', chatIds[ci]);
                    if (Array.isArray(streams) && streams.length >= expected) {
                        result[ci] = streams.map((s: VideoStreamInfo) => s.StreamId);
                        pending.delete(ci);
                    }
                    consecErrors = 0;
                } catch (e) {
                    hadError = true;
                    lastError = e;
                    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- aborted may flip during the await
                    if (!ctx.abort.aborted)
                        console.warn(
                            `[rpc discover chat=${chatIds[ci]}] ${(e as Error).message}`);
                }
            }
            if (hadError) consecErrors++;
            if (consecErrors >= MAX_CONSEC_ERRORS) {
                throw new Error(
                    `discoverStreams: ${consecErrors} consecutive errors — ` +
                    `last: ${(lastError as Error | undefined)?.message ?? String(lastError)}`);
            }
            if (pending.size === 0) break;
            if (Date.now() > deadline)
                throw new Error(
                    `discoverStreams: timed out waiting for ${pending.size} chat(s) to report ${expected} streams`);
            await new Promise<void>((r) => setTimeout(r, 500));
        }
        return result;
    }
}
