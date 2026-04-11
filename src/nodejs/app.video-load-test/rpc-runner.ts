// Fusion RPC producer / consumer harnesses. One RpcClientPeer per producer
// and one per consumer — each opens its own WebSocket, mirroring the C# test's
// one-connection-per-task pattern.

import { RpcHub, RpcClientPeer, RpcClientStreamSender } from '../src/actuallab-rpc/index.js';

import { createNodeWsFactory } from './node-ws.js';
import { FrameConfig, generateFrame, paceFrame } from './frame-gen.js';
import type { Metrics } from './metrics.js';
import {
    LiveVideoStreamsDef,
    StreamServerDef,
    type LiveVideoStreamsClient,
    type StreamServerClient,
    type VideoFrameDto,
    type VideoFormatDto,
    type VideoStreamInfoDto,
} from './service-defs.js';

const RPC_SERIALIZATION_FORMAT = 'msgpack6';

export interface RpcRunContext {
    rpcWsUrl: string;
    sessionId: string;
    metrics: Metrics;
    abort: AbortSignal;
}

interface PeerBundle {
    hub: RpcHub;
    peer: RpcClientPeer;
}

async function connectPeer(ctx: RpcRunContext): Promise<PeerBundle> {
    const hub = new RpcHub();
    const peer = new RpcClientPeer(hub, ctx.rpcWsUrl, RPC_SERIALIZATION_FORMAT);
    const wsFactory = createNodeWsFactory({ sessionId: ctx.sessionId });
    const whenConnected = peer.connected.whenNext();
    void peer.run(wsFactory);
    await whenConnected;
    return { hub, peer };
}

function closePeer(bundle: PeerBundle): void {
    try { bundle.peer.close(); } catch { /* ignore */ }
}

export async function runRpcProducer(
    ctx: RpcRunContext,
    chatIdx: number,
    prodIdx: number,
    chatId: string,
): Promise<void> {
    let bundle: PeerBundle | null = null;
    try {
        bundle = await connectPeer(ctx);
        const streamServer = bundle.hub.addClient(bundle.peer, StreamServerDef) as unknown as StreamServerClient;
        const sender = new RpcClientStreamSender<VideoFrameDto>(bundle.peer);
        const format: VideoFormatDto = {
            Codec: FrameConfig.Codec,
            Width: FrameConfig.Width,
            Height: FrameConfig.Height,
            CodecSettings: '',
        };
        const clientStartOffsetSec = Date.now() / 1000;

        // PushVideo is a long-lived call — the server holds it open until
        // the frame stream ends. Fire-and-forget; surface any rejection so
        // it's visible via the global unhandledRejection handler.
        void streamServer
            .PushVideo('~', chatId, clientStartOffsetSec, format, sender.toRef())
            .catch((err: unknown) => {
                if (!ctx.abort.aborted)
                    console.error(
                        `[rpc producer chat=${chatIdx} prod=${prodIdx}] PushVideo rejected:`, err);
            });

        // Drive the producer loop via writeFrom() so the sender waits for
        // the server's initial $sys.Ack(0) before pumping items. Calling
        // sendItem() directly races the PushVideo invocation — items can
        // reach the server before the shared-stream handler is ready and
        // get silently dropped. This matches the production browser pattern
        // in workers/video-streaming.ts.
        async function* frameSource(): AsyncIterable<VideoFrameDto> {
            const start = Date.now();
            for (let i = 0; !ctx.abort.aborted; i++) {
                await paceFrame(start, i);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- aborted may flip during the await
                if (ctx.abort.aborted) break;
                const frame = generateFrame(i);
                ctx.metrics.recordSent(chatIdx, prodIdx, frame.Offset);
                yield frame;
            }
        }
        await sender.writeFrom(frameSource());
    } catch (e) {
        if (!ctx.abort.aborted) {
            console.error(
                `[rpc producer chat=${chatIdx} prod=${prodIdx}] ${(e as Error).message}`);
        }
    } finally {
        if (bundle) closePeer(bundle);
    }
}

export async function runRpcConsumer(
    ctx: RpcRunContext,
    chatIdx: number,
    consumerIdx: number,
    streamIdx: number,
    streamId: string,
): Promise<void> {
    let bundle: PeerBundle | null = null;
    try {
        bundle = await connectPeer(ctx);
        const client = bundle.hub.addClient(bundle.peer, LiveVideoStreamsDef) as unknown as LiveVideoStreamsClient;
        const stream = await client.GetStream('~', streamId, 0);

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
    } finally {
        if (bundle) closePeer(bundle);
    }
}

/**
 * Discover the live video streams per chat via a single short-lived RPC peer.
 * Polls `ILiveVideoStreams.List` until every chat reports ≥ `expected` streams
 * or the timeout is hit. Matches the C# discovery step (though without
 * Computed.WhenInvalidated since the TS RPC client has no compute subscription).
 */
export async function discoverStreams(
    ctx: Omit<RpcRunContext, 'metrics'>,
    chatIds: readonly string[],
    expected: number,
    timeoutMs: number,
): Promise<string[][]> {
    const bundle = await connectPeer(ctx as RpcRunContext);
    try {
        const client = bundle.hub.addClient(bundle.peer, LiveVideoStreamsDef) as unknown as LiveVideoStreamsClient;

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
                        result[ci] = streams.map((s: VideoStreamInfoDto) => s.StreamId);
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
    } finally {
        closePeer(bundle);
    }
}
