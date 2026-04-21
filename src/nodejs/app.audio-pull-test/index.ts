// AudioPullTest — diagnostic smoke test for the TS-side audio pull path.
//
// Pushes synthetic AudioFrameDto values into a chat via
// IStreamServer.PushAudio, then subscribes via ILiveAudioStreams.GetStream to
// pull the (re-muxed into A_OPUS_S) bytes back out. Demuxes + parses the
// result and reports frame counts + any wire-level surprises. If this script
// works end-to-end, the silence in the browser client is NOT an RPC wiring
// bug — look at the renderer / AudioContext side.

import { randomBytes, randomUUID } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';

import {
    RpcHub,
    RpcClientPeer,
    RpcStream,
    RpcPeerRefBuilder,
    defineRpcService,
    RpcRemoteExecutionMode,
    RpcType,
} from '../src/actuallab-rpc/index.js';

import { parseActualOpusStreamHeader } from '../src/audio/actual-opus-stream-parser.js';
import { runLiveStreamDemuxer } from '../src/audio/live-stream-demuxer.js';

import { createNodeWsFactory } from '../app.video-load-test/node-ws.js';
import { signIn } from '../app.video-load-test/auth.js';

// --- Local service defs (trimmed copies so this harness stays standalone) ---

const LiveAudioStreamsDef = defineRpcService('ILiveAudioStreams', {
    GetStream: { args: ['session', 'chatId', 'settings'], returns: RpcType.stream },
});

interface LiveAudioStreamsClient {
    GetStream(session: string, chatId: string, settings: { StreamKindFilter: number }): Promise<AsyncIterable<unknown>>;
}

const StreamServerDef = defineRpcService('IStreamServer', {
    PushAudio: {
        args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartOffset', 'preSkip', 'frameStream'],
        remoteExecutionMode: RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect,
    },
});

interface AudioFrameDto {
    Data: Uint8Array;
    Offset: number;
    Duration: number;
    IsKeyFrame: boolean;
}

interface StreamServerClient {
    PushAudio(
        session: string,
        chatId: string,
        repliedChatEntryId: string | null,
        clientStartOffset: number,
        preSkip: number,
        frameStreamRef: unknown,
    ): Promise<void>;
}

// --- CLI ---

interface Args {
    apiUrl: string;
    chatId: string;
    durationSec: number;
    email: string;
}

function parseArgs(): Args {
    const out: Args = {
        apiUrl: 'https://local.voxt.ai',
        chatId: 'the-actual-one',
        durationSec: 10,
        email: 'test-audiopull@actual.chat',
    };
    for (const arg of process.argv.slice(2)) {
        const m = /^-(?:u|url):(.+)$/.exec(arg); if (m) { out.apiUrl = m[1]; continue; }
        const c = /^-(?:chat|chatId):(.+)$/.exec(arg); if (c) { out.chatId = c[1]; continue; }
        const d = /^-(?:d|duration):(\d+)$/.exec(arg); if (d) { out.durationSec = Number(d[1]); continue; }
        const e = /^-email:(.+)$/.exec(arg); if (e) { out.email = e[1]; continue; }
    }
    return out;
}

// --- Main ---

async function main(): Promise<void> {
    const args = parseArgs();
    const runId = randomUUID().slice(0, 8);
    const t0 = Date.now();
    const log = (s: string): void => {
        console.log(`[${String(Date.now() - t0).padStart(5, ' ')}ms][${runId}] ${s}`);
    };

    // Convert https://host → wss://host/rpc/ws (same as app.video-load-test).
    const wsUrl = `${args.apiUrl.replace(/^http/, 'ws')}/rpc/ws`;
    log(`Config: apiUrl=${args.apiUrl} wsUrl=${wsUrl} chat=${args.chatId} duration=${args.durationSec}s email=${args.email}`);

    // Dev TLS bypass if local
    if (/local\.voxt\.ai|localhost|127\.0\.0\.1/.test(args.apiUrl)) {
        process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
        log('NODE_TLS_REJECT_UNAUTHORIZED=0 — dev cert bypass active');
    }

    const { sessionId } = await signIn({ apiUrl: wsUrl, email: args.email, totp: 111111 });
    log(`Signed in: session=${sessionId.slice(0, 8)}…`);

    const hub = new RpcHub();
    const url = RpcPeerRefBuilder.forClient(wsUrl, 'msgpack6');
    const peer = hub.getClientPeer(url, (h, r) => new RpcClientPeer(h, r, false));
    peer.webSocketFactory = createNodeWsFactory({ sessionId });
    const whenConnected = peer.whenConnected();
    peer.start();
    await whenConnected;
    log('RPC peer connected');

    const liveAudio = hub.addClient<LiveAudioStreamsClient>(peer, LiveAudioStreamsDef);
    const streamServer = hub.addClient<StreamServerClient>(peer, StreamServerDef);

    const abort = new AbortController();

    // ===== Consumer =====
    const consumerStats = { startedStreams: 0, frames: 0, bytes: 0, packets: 0, firstItemLogged: false };
    const consumerDone = (async (): Promise<void> => {
        try {
            log(`Calling GetStream(${args.chatId})…`);
            const stream = await liveAudio.GetStream(
                sessionId,
                args.chatId,
                { StreamKindFilter: 1 /* Audio */ });
            log('GetStream returned, starting demuxer');

            await runLiveStreamDemuxer(
                stream,
                (started) => {
                    consumerStats.startedStreams++;
                    log(
                        `StreamStarted: index=${started.streamIndex}, ` +
                        `streamId=${started.streamInfo.StreamId}, ` +
                        `author=${started.streamInfo.AuthorId}, ` +
                        `beginsAt=${started.streamInfo.BeginsAt}, ` +
                        `playsAtTicks=${started.playsAtTicks}`);

                    void (async (): Promise<void> => {
                        let chunkCount = 0;
                        for await (const chunk of started.frames) {
                            if (abort.signal.aborted) break;
                            chunkCount++;
                            consumerStats.frames++;
                            consumerStats.bytes += chunk.byteLength;
                            if (chunkCount === 1) {
                                // First frame is the A_OPUS_S header.
                                const header = parseActualOpusStreamHeader(chunk);
                                log(
                                    `stream ${started.streamInfo.StreamId}: header ` +
                                    `len=${chunk.length}, preSkip=${header.preSkip}, ` +
                                    `createdAtTicks=${header.createdAtTicks}`);
                            } else {
                                // Subsequent frames are raw Opus packets.
                                consumerStats.packets++;
                            }
                        }
                        log(
                            `stream ${started.streamInfo.StreamId} ended: ` +
                            `chunks=${chunkCount}, packets=${consumerStats.packets}`);
                    })();
                },
                abort.signal,
            );
            log('Demuxer returned');
        } catch (e) {
            console.error('[consumer] ERROR:', e);
        }
    })();

    // Give the consumer a moment to subscribe before we start pushing.
    await sleep(500);

    // ===== Producer =====
    // Build one opus-shaped frame payload to reuse across packets. The actual
    // bytes don't need to decode as valid Opus for this test — we only care
    // that the push→mux→stream→demux→parse round-trip preserves them.
    const PAYLOAD = new Uint8Array(randomBytes(120));
    const OPUS_FRAME_DURATION_MS = 20;
    const TICKS_PER_MS = 10_000;
    const FRAME_DURATION_TICKS = OPUS_FRAME_DURATION_MS * TICKS_PER_MS;
    const pushStats = { sent: 0 };

    const producerDone = (async (): Promise<void> => {
        try {
            const clientStartOffsetSec = Date.now() / 1000;
            const stream = new RpcStream<AudioFrameDto>((async function* () {
                const start = Date.now();
                for (let i = 0; !abort.signal.aborted; i++) {
                    const targetMs = i * OPUS_FRAME_DURATION_MS;
                    const delay = targetMs - (Date.now() - start);
                    if (delay > 0) await sleep(delay);
                    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- aborted may flip during await
                    if (abort.signal.aborted) break;
                    pushStats.sent++;
                    yield {
                        Data: PAYLOAD,
                        Offset: i * FRAME_DURATION_TICKS,
                        Duration: FRAME_DURATION_TICKS,
                        IsKeyFrame: true,
                    };
                }
            })(), { isRealTime: true, allowReconnect: false, ackPeriod: 5, ackAdvance: 31 });

            log(`Calling PushAudio(${args.chatId})…`);
            void streamServer
                .PushAudio(sessionId, args.chatId, null, clientStartOffsetSec, 0, stream.toRef(peer))
                .catch((err: unknown) => {
                    if (!abort.signal.aborted)
                        console.error('[producer] PushAudio rejected:', err);
                });

            await stream.whenSent;
        } catch (e) {
            if (!abort.signal.aborted) console.error('[producer] ERROR:', e);
        }
    })();

    // Run for duration, then abort both sides.
    await sleep(args.durationSec * 1_000);
    log(`Test window elapsed (${args.durationSec}s) — stopping`);
    abort.abort();

    // Wait for cleanup, with timeout so we don't hang.
    await Promise.race([
        Promise.all([producerDone, consumerDone]),
        sleep(3_000),
    ]);

    peer.close();
    log('=== RESULTS ===');
    log(`pushed: ${pushStats.sent} frames`);
    log(`received: startedStreams=${consumerStats.startedStreams}, ` +
        `chunks=${consumerStats.frames}, ` +
        `bytes=${consumerStats.bytes}, ` +
        `parsedPackets=${consumerStats.packets}`);

    const pass = consumerStats.startedStreams > 0
        && consumerStats.frames > 0
        && consumerStats.packets > 0;
    log(pass ? 'PASS — full pull path works' : 'FAIL — pipeline stalled, see logs above');
    process.exit(pass ? 0 : 1);
}

main().catch((e: unknown) => {
    console.error('Fatal:', e);
    process.exit(2);
});
