// SignalR producer / consumer harnesses. One HubConnection per producer and
// one per consumer — matches the C# `-rpc` OFF / non-unified default mode so
// the numbers line up. We do NOT implement the "unified" mode from C# (one
// connection per participant that both pushes + pulls) because the goal is an
// apples-to-apples comparison with the C# `SignalR` (non-mem, non-unified)
// number, which the harness emits when no mode flag is passed.

import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { decode as msgpackDecode } from '@msgpack/msgpack';

import { FrameConfig, generateFrame, encodeFrameForSignalR, paceFrame } from './frame-gen.js';
import type { Metrics } from './metrics.js';

function buildConnection(hubUrl: string): signalR.HubConnection {
    return new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            skipNegotiation: true,
            transport: signalR.HttpTransportType.WebSockets,
        })
        .withHubProtocol(new MessagePackHubProtocol())
        .configureLogging(signalR.LogLevel.Warning)
        .build();
}

export interface SignalRRunContext {
    hubUrl: string;
    sessionToken: string;
    metrics: Metrics;
    abort: AbortSignal;
}

export async function runSignalRProducer(
    ctx: SignalRRunContext,
    chatIdx: number,
    prodIdx: number,
    chatId: string,
): Promise<void> {
    const connection = buildConnection(ctx.hubUrl);
    try {
        await connection.start();
        const clientStartOffsetSec = Date.now() / 1000;
        const subject = new signalR.Subject<Uint8Array>();

        // SignalR .send returns after the upload-stream setup completes (the
        // server method signature has an IAsyncEnumerable argument — .send
        // primes the stream and returns, then we push items into the Subject).
        const sendPromise = connection.send(
            'PushVideo',
            ctx.sessionToken,
            chatId,
            FrameConfig.Codec,
            FrameConfig.Width,
            FrameConfig.Height,
            '',
            clientStartOffsetSec,
            0, // 0 = Webcam
            subject,
        );
        await sendPromise;

        const start = Date.now();
        try {
            for (let i = 0; !ctx.abort.aborted; i++) {
                await paceFrame(start, i);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- aborted may flip during the await
                if (ctx.abort.aborted) break;

                const frame = generateFrame(i);
                const bytes = encodeFrameForSignalR(frame);

                ctx.metrics.recordSent(chatIdx, prodIdx, frame.Offset);
                subject.next(bytes);
            }
        } finally {
            subject.complete();
        }
    } catch (e) {
        if (!ctx.abort.aborted) {
            console.error(
                `[signalr producer chat=${chatIdx} prod=${prodIdx}] ${(e as Error).message}`);
        }
    } finally {
        try { await connection.stop(); } catch { /* ignore */ }
    }
}

interface VideoFramePayload {
    Offset?: number;
    Data?: Uint8Array;
    IsKeyFrame?: boolean;
}

export async function runSignalRConsumer(
    ctx: SignalRRunContext,
    chatIdx: number,
    consumerIdx: number,
    streamIdx: number,
    streamId: string,
): Promise<void> {
    const connection = buildConnection(ctx.hubUrl);
    try {
        await connection.start();

        const stream = connection.stream<Uint8Array>(
            'GetVideo', ctx.sessionToken, streamId, 0.0);

        await new Promise<void>((resolve, reject) => {
            const subscription = stream.subscribe({
                next: (frameBytes) => {
                    if (ctx.abort.aborted) return;
                    try {
                        const frame = msgpackDecode(frameBytes) as VideoFramePayload;
                        const offsetTicks = frame.Offset ?? 0;
                        ctx.metrics.recordReceived(
                            chatIdx, consumerIdx, streamIdx, offsetTicks, frameBytes.length);
                    } catch {
                        // skip malformed frame
                    }
                },
                error: (err: unknown) => {
                    if (ctx.abort.aborted) { resolve(); return; }
                    reject(err instanceof Error ? err : new Error(String(err)));
                },
                complete: () => resolve(),
            });
            ctx.abort.addEventListener('abort', () => {
                try { subscription.dispose(); } catch { /* ignore */ }
                resolve();
            });
        });
    } catch (e) {
        if (!ctx.abort.aborted) {
            console.error(
                `[signalr consumer chat=${chatIdx} cons=${consumerIdx} stream=${streamIdx}] ${(e as Error).message}`);
        }
    } finally {
        try { await connection.stop(); } catch { /* ignore */ }
    }
}

/**
 * Polls `StreamHub.GetVideoStreamList`-like behavior via repeated short invokes.
 * SignalR has no "computed" observable on the TS side, so we simply wait until
 * each chat reports ≥ streamsPerChat streams and then snapshot.
 *
 * NOTE: SignalR doesn't expose ListLiveVideoStreams on the hub directly; the
 * only discovery path exposed to the browser is via the Fusion RPC client
 * (`ILiveVideoStreams.List`). For a SignalR-pure benchmark we still reuse that
 * RPC list via a short-lived RPC peer — cheap, and avoids adding a new hub
 * method just for this test. See `rpc-runner.ts:discoverStreams`.
 */
