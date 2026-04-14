// Audio stream test — pushes fake audio frames via IStreamServer.PushAudio
// and verifies they arrive continuously via IStreamServer.GetAudio for 2+ minutes.
// Reproduces the 250-frame stall bug.
//
// Usage:
//   npx tsx src/nodejs/app.audio-stream-test/index.ts [-d:120] [-u:https://local.voxt.ai]

// --- Dev TLS bypass ---
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

process.on('unhandledRejection', (reason) => {
    console.error('[unhandledRejection]', reason);
});
process.on('uncaughtException', (err) => {
    console.error('[uncaughtException]', err);
});

import { randomUUID } from 'node:crypto';
import { RpcHub, RpcClientPeer, RpcClientStreamSender } from '../src/actuallab-rpc/index.js';
import { defineRpcService, RpcType } from '../src/actuallab-rpc/index.js';

// --- Node WS adapter (inline, same as video load test) ---
import WebSocket from 'ws';
import type { WebSocketLike } from '../src/actuallab-rpc/index.js';

function createNodeWsFactory(sessionId: string): (url: string) => WebSocketLike {
    return (url: string): WebSocketLike => {
        const ws = new WebSocket(url, {
            headers: { Session: sessionId },
            rejectUnauthorized: false,
            perMessageDeflate: false,
        });
        (ws as any).binaryType = 'arraybuffer';
        return wrapWs(ws);
    };
}

function wrapWs(ws: WebSocket): WebSocketLike {
    const like: WebSocketLike = {
        get readyState() { return ws.readyState; },
        get binaryType() { return (ws as any).binaryType; },
        set binaryType(v: string) { (ws as any).binaryType = v; },
        send(data) { ws.send(data as Buffer | ArrayBuffer | string); },
        close(code?: number, reason?: string) { try { ws.close(code, reason); } catch {} },
        onopen: null, onmessage: null, onclose: null, onerror: null,
    };
    ws.on('open', (ev: unknown) => like.onopen?.(ev));
    ws.on('message', (data: Buffer | ArrayBuffer | Buffer[]) => {
        let payload: Uint8Array | string;
        if (typeof data === 'string') payload = data;
        else if (Array.isArray(data)) {
            const total = data.reduce((n, b) => n + b.byteLength, 0);
            const out = new Uint8Array(total);
            let o = 0;
            for (const b of data) { out.set(b, o); o += b.byteLength; }
            payload = out;
        } else if (data instanceof ArrayBuffer) {
            payload = new Uint8Array(data);
        } else {
            payload = data;
        }
        like.onmessage?.({ data: payload });
    });
    ws.on('close', (code: number, reason: Buffer) => {
        if (code !== 1000 && code !== 1005)
            console.warn(`[ws close] code=${code} reason="${reason.toString('utf8')}"`);
        like.onclose?.({ code, reason: reason.toString('utf8') });
    });
    ws.on('error', (err: Error) => {
        console.error(`[ws error]`, err.message);
        like.onerror?.(err);
    });
    return like;
}

// --- RPC service definitions ---
const EmailAuthDef = defineRpcService('IEmailAuth', {
    OnValidateTotp: { args: ['command'] },
});

const SecureTokensDef = defineRpcService('ISecureTokens', {
    CreateForSession: { args: ['session'] },
});

const StreamServerDef = defineRpcService('IStreamServer', {
    PushAudio: { args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartOffset', 'preSkip', 'frameStream'] },
    GetAudio: { args: ['streamId', 'skipTo'], returns: RpcType.stream },
});

const LiveAudioStreamsDef = defineRpcService('ILiveAudioStreams', {
    List: { args: ['session', 'chatId'], callTypeId: 1 },
});

// --- Audio frame types ---
interface AudioFrameDto {
    Data: Uint8Array;
    Offset: number;       // TimeSpan ticks (100ns units)
    Duration: number;     // TimeSpan ticks
    IsKeyFrame: boolean;
}

// 20ms Opus frame = 200_000 ticks
const FRAME_DURATION_TICKS = 200_000;
// ~80 bytes per frame (typical Opus at 24kbps)
const FRAME_SIZE_BYTES = 80;

function generateAudioFrame(index: number): AudioFrameDto {
    const data = new Uint8Array(FRAME_SIZE_BYTES);
    // Write frame index into first 4 bytes for identification
    data[0] = (index >> 24) & 0xFF;
    data[1] = (index >> 16) & 0xFF;
    data[2] = (index >> 8) & 0xFF;
    data[3] = index & 0xFF;
    // Fill rest with pseudo-random data
    for (let i = 4; i < FRAME_SIZE_BYTES; i++)
        data[i] = (index * 7 + i * 13) & 0xFF;
    return {
        Data: data,
        Offset: index * FRAME_DURATION_TICKS,
        Duration: FRAME_DURATION_TICKS,
        IsKeyFrame: true,
    };
}

// --- CLI args ---
const durationSec = parseInt(
    process.argv.find(a => a.startsWith('-d:'))?.slice(3) ?? '120', 10);
const baseUrl = process.argv.find(a => a.startsWith('-u:'))?.slice(3) ?? 'https://local.voxt.ai';
const chatId = 'the-actual-one';
const rpcWsUrl = `${baseUrl.replace(/^http/, 'ws')}/rpc/ws`;

// --- Auth ---
async function signIn(sessionId: string): Promise<void> {
    const hub = new RpcHub();
    const peer = new RpcClientPeer(hub, rpcWsUrl, 'msgpack6');
    const wsFactory = createNodeWsFactory(sessionId);
    const whenConnected = peer.connected.whenNext();
    void peer.run(wsFactory);
    await whenConnected;
    // Wait for handshake
    const deadline = Date.now() + 5000;
    while (!peer.isConnected && Date.now() < deadline)
        await new Promise(r => setTimeout(r, 20));
    if (!peer.isConnected) throw new Error('Handshake timeout');

    const emailAuth = hub.addClient(peer, EmailAuthDef) as any;
    const ok = await emailAuth.OnValidateTotp({
        Session: sessionId,
        Email: 'test-videoload@actual.chat',
        Totp: 111111,
    });
    if (!ok) throw new Error('Sign-in failed');
    console.log('[auth] Signed in');
    peer.close();
}

// --- Main ---
async function main(): Promise<void> {
    console.log(`Audio Stream Test: chat=${chatId}, duration=${durationSec}s, url=${baseUrl}`);

    const sessionId = randomUUID().replace(/-/g, '');
    await signIn(sessionId);

    // Create SEPARATE peers for producer and consumer to eliminate WebSocket contention
    async function createPeer(label: string) {
        const hub = new RpcHub();
        const peer = new RpcClientPeer(hub, rpcWsUrl, 'msgpack6');
        const wsFactory = createNodeWsFactory(sessionId);
        const whenConnected = peer.connected.whenNext();
        void peer.run(wsFactory);
        await whenConnected;
        const deadline = Date.now() + 5000;
        while (!peer.isConnected && Date.now() < deadline)
            await new Promise(r => setTimeout(r, 20));
        if (!peer.isConnected) throw new Error(`${label} handshake timeout`);
        console.log(`[rpc] ${label} connected`);
        return { hub, peer };
    }
    const producer = await createPeer('producer');
    const consumer = await createPeer('consumer');

    const streamServerProd = producer.hub.addClient(producer.peer, StreamServerDef) as any;
    const streamServerCons = consumer.hub.addClient(consumer.peer, StreamServerDef) as any;
    const liveAudioStreams = consumer.hub.addClient(consumer.peer, LiveAudioStreamsDef) as any;

    const abortController = new AbortController();
    const abort = abortController.signal;
    process.on('SIGINT', () => { console.log('\nSIGINT'); abortController.abort(); });

    // --- Producer ---
    const sender = new RpcClientStreamSender<AudioFrameDto>(producer.peer);
    const clientStartOffset = Date.now() / 1000;
    console.log(`[producer] Starting PushAudio, clientStartOffset=${clientStartOffset.toFixed(3)}`);

    void streamServerProd
        .PushAudio('~', chatId, null, clientStartOffset, 0, sender.toRef())
        .catch((err: unknown) => {
            if (!abort.aborted)
                console.error('[producer] PushAudio rejected:', err);
        });

    // Producer loop via writeFrom
    let producerFrameCount = 0;
    const producerStart = Date.now();

    async function* audioFrameSource(): AsyncIterable<AudioFrameDto> {
        for (let i = 0; !abort.aborted; i++) {
            // Pace at 50 fps (20ms per frame)
            const targetMs = producerStart + i * 20;
            const now = Date.now();
            if (targetMs > now)
                await new Promise(r => setTimeout(r, targetMs - now));
            if (abort.aborted) break;
            const frame = generateAudioFrame(i);
            producerFrameCount++;
            if (producerFrameCount <= 3 || producerFrameCount % 250 === 0)
                console.log(`[producer] frame #${producerFrameCount}, offset=${frame.Offset}`);
            yield frame;
        }
    }

    const producerTask = sender.writeFrom(audioFrameSource());

    // --- Wait for stream to appear ---
    console.log('[discovery] Waiting for stream to appear...');
    let streamId: string | null = null;
    const discoveryDeadline = Date.now() + 30_000;
    while (!streamId && !abort.aborted && Date.now() < discoveryDeadline) {
        try {
            const streams = await liveAudioStreams.List('~', chatId);
            if (Array.isArray(streams) && streams.length > 0) {
                streamId = streams[0].StreamId;
                console.log(`[discovery] Found stream: ${streamId}`);
            }
        } catch (e) {
            console.warn('[discovery] List error:', (e as Error).message);
        }
        if (!streamId)
            await new Promise(r => setTimeout(r, 500));
    }
    if (!streamId) {
        console.error('[discovery] No stream found within 30s');
        abortController.abort();
        await producerTask;
        peer.close();
        process.exit(1);
    }

    // --- Consumer ---
    console.log(`[consumer] Starting GetAudio for stream ${streamId}`);
    let consumerFrameCount = 0;
    let lastConsumerFrameAt = Date.now();
    let maxGapMs = 0;
    let gapCount = 0;

    const consumerTask = (async () => {
        try {
            const stream = await streamServerCons.GetAudio(streamId, 0);
            for await (const frame of stream) {
                if (abort.aborted) break;
                consumerFrameCount++;
                const now = Date.now();
                const gapMs = now - lastConsumerFrameAt;
                if (gapMs > 1000 && consumerFrameCount > 1) {
                    gapCount++;
                    console.warn(`[consumer] GAP detected: ${gapMs}ms after frame #${consumerFrameCount}`);
                }
                if (gapMs > maxGapMs) maxGapMs = gapMs;
                lastConsumerFrameAt = now;

                if (consumerFrameCount <= 3 || consumerFrameCount % 250 === 0)
                    console.log(`[consumer] frame #${consumerFrameCount}, bytes=${(frame as any).byteLength ?? (frame as any).Data?.byteLength ?? '?'}`);
            }
            console.log('[consumer] Stream ended');
        } catch (e) {
            if (!abort.aborted)
                console.error('[consumer] Error:', (e as Error).message);
        }
    })();

    // --- Status reporting ---
    const statusInterval = setInterval(() => {
        if (abort.aborted) return;
        const elapsed = ((Date.now() - producerStart) / 1000).toFixed(1);
        console.log(
            `[status ${elapsed}s] producer=${producerFrameCount} consumer=${consumerFrameCount} ` +
            `gap=${maxGapMs}ms gaps>1s=${gapCount}`);
    }, 5000);

    // --- Run for duration ---
    await new Promise<void>(resolve => {
        const timer = setTimeout(resolve, durationSec * 1000);
        abort.addEventListener('abort', () => { clearTimeout(timer); resolve(); }, { once: true });
    });

    console.log('\nStopping...');
    abortController.abort();
    clearInterval(statusInterval);
    await Promise.allSettled([producerTask, consumerTask]);
    producer.peer.close();
    consumer.peer.close();

    // --- Report ---
    const totalSec = (Date.now() - producerStart) / 1000;
    console.log('\n=== Audio Stream Test Report ===');
    console.log(`Duration: ${totalSec.toFixed(1)}s`);
    console.log(`Producer: ${producerFrameCount} frames (${(producerFrameCount / totalSec).toFixed(1)} fps)`);
    console.log(`Consumer: ${consumerFrameCount} frames (${(consumerFrameCount / totalSec).toFixed(1)} fps)`);
    console.log(`Max gap: ${maxGapMs}ms`);
    console.log(`Gaps > 1s: ${gapCount}`);
    console.log(`Result: ${gapCount === 0 && consumerFrameCount > producerFrameCount * 0.8 ? 'PASS' : 'FAIL'}`);
}

main().catch((err: unknown) => {
    console.error(err);
    process.exit(1);
});
