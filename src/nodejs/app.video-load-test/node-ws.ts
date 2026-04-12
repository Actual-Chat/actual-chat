// Adapter that wraps the Node.js `ws` package's WebSocket so it matches the
// browser-style `WebSocketLike` interface expected by RpcClientPeer. The key
// reason we can't use Node's native global `WebSocket` is that it doesn't
// support setting request headers on construction, which we need to attach
// the `Session` header to the RPC handshake.

import WebSocket from 'ws';
import type { WebSocketLike } from '../src/actuallab-rpc/index.js';

export type NodeWsFactory = (url: string) => WebSocketLike;

/**
 * Build a wsFactory that opens a `ws.WebSocket` with the given cookies/headers
 * and adapts it to the `WebSocketLike` contract. Passes headers through the
 * request upgrade so the server's RPC middleware can resolve the session via
 * `TryGetSessionFromHeader` / `TryGetSessionFromCookie`.
 *
 * Forces `rejectUnauthorized: false` so the dev cert on `local.voxt.ai`
 * (self-signed / mkcert) doesn't trip Node's default CA bundle. This is a
 * dev-test-only harness — do not lift this adapter into production code.
 */
export function createNodeWsFactory(opts: {
    sessionId?: string;
    cookie?: string;
}): NodeWsFactory {
    const headers: Record<string, string> = {};
    if (opts.sessionId) headers['Session'] = opts.sessionId;
    if (opts.cookie) headers['Cookie'] = opts.cookie;

    return (url: string): WebSocketLike => {
        const ws = new WebSocket(url, {
            headers,
            rejectUnauthorized: false, // dev cert bypass — see file header
            // Disable RFC 7692 permessage-deflate. Video frames are already
            // high-entropy, so deflate buys nothing
            perMessageDeflate: false,
        });
        // `ws` delivers binary payloads as Node Buffer by default. Forcing
        // arraybuffer keeps splitBinaryFrame / the browser code path happy.
        (ws as unknown as { binaryType: string }).binaryType = 'arraybuffer';
        return wrap(ws, url);
    };
}

/** Minimal shim — ws already exposes the w3c event setters, but typed as
 *  a Node EventTarget which TS can't narrow to our WebSocketLike directly.
 *  `url` is captured only so error/close diagnostics can say which peer
 *  they belong to when the harness opens many concurrent sockets. */
function wrap(ws: WebSocket, url: string): WebSocketLike {
    const like: WebSocketLike = {
        get readyState() { return ws.readyState; },
        get binaryType() { return (ws as unknown as { binaryType: string }).binaryType; },
        set binaryType(value: string) { (ws as unknown as { binaryType: string }).binaryType = value; },
        send(data) {
            // `ws` accepts Buffer | ArrayBuffer | string — matches our union.
            ws.send(data as Buffer | ArrayBuffer | string);
        },
        close(code?: number, reason?: string) {
            try { ws.close(code, reason); }
            catch { /* ignore — already closed */ }
        },
        onopen: null,
        onmessage: null,
        onclose: null,
        onerror: null,
    };

    ws.on('open', (ev: unknown) => like.onopen?.(ev));
    ws.on('message', (data: Buffer | ArrayBuffer | Buffer[]) => {
        // IMPORTANT: zero-copy delivery for binary frames.
        //
        // Node's `Buffer` already extends `Uint8Array`, so `ws` hands us a
        // typed-array view that `RpcWebSocketConnection` and
        // `splitBinaryFrame` can consume directly. An earlier version of
        // this adapter called `toArrayBuffer(data)` on every message to
        // "match the browser's ArrayBuffer delivery shape" — that
        // allocated a fresh buffer and memcpy-ed every inbound frame. At
        // 300 pulls × 30 fps × ~11 KB that was ~99 MB/sec of pure copy +
        // GC pressure (visible in the profile as ~2% FastBuffer). Pass
        // the Uint8Array view through instead; `rpc-connection.ts` handles
        // `ArrayBuffer.isView` on the receive side.
        let payload: Uint8Array | string;
        if (typeof data === 'string') {
            payload = data;
        } else if (Array.isArray(data)) {
            // Fragmented binary frame — ws only does this for very large
            // payloads. Concatenate into one Uint8Array so the receiver
            // sees a single contiguous buffer.
            const total = data.reduce((n, b) => n + b.byteLength, 0);
            const out = new Uint8Array(total);
            let o = 0;
            for (const b of data) { out.set(b, o); o += b.byteLength; }
            payload = out;
        } else if (data instanceof ArrayBuffer) {
            payload = new Uint8Array(data);
        } else {
            // Node Buffer — already a Uint8Array view over its ArrayBuffer,
            // so pass it through directly. No allocation, no memcpy.
            payload = data;
        }
        like.onmessage?.({ data: payload });
    });
    ws.on('close', (code: number, reason: Buffer) => {
        // Abnormal closes (anything other than 1000 normal / 1005 no-status)
        // usually mean TLS rejection, handshake failure, or server-side
        // disconnect — surface them so the run doesn't hang silently.
        if (code !== 1000 && code !== 1005) {
            const reasonStr = reason.toString('utf8');
            console.warn(
                `[node-ws close] ${url} code=${code}` +
                (reasonStr ? ` reason="${reasonStr}"` : ''));
        }
        like.onclose?.({ code, reason: reason.toString('utf8') });
    });
    ws.on('error', (err: Error) => {
        console.error(`[node-ws error] ${url}:`, err.message);
        like.onerror?.(err);
    });

    return like;
}
