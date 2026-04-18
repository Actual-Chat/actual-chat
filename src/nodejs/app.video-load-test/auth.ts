// Sign in flow that mirrors the C# harness:
//   1. Generate a fresh Session Id (random GUID-ish, matches Session.New()).
//   2. Open an RPC peer with that Id in the `Session` header.
//   3. Call IEmailAuth.OnValidateTotp(command) to sign the session in.
//   4. Call ISecureTokens.CreateForSession(session) to get a SignalR sessionToken.
//   5. Return { sessionId, sessionToken } so producers/consumers can share the
//      same identity across their individual connections.

import { randomUUID } from 'node:crypto';
import { RpcHub, RpcClientPeer, RpcPeerRefBuilder } from '../src/actuallab-rpc/index.js';

import { createNodeWsFactory } from './node-ws.js';
import {
    EmailAuthDef,
    SecureTokensDef,
    type EmailAuthClient,
    type SecureTokensClient,
} from './service-defs.js';

export interface SignInOptions {
    rpcWsUrl: string;
    email: string;
    totp: number;
}

export interface SignInResult {
    sessionId: string;
    sessionToken: string;
}

/** Build the fixed-shape Session id the .NET SessionFactory produces. */
function newSessionId(): string {
    // .NET's DefaultSessionFactory creates a 16+ char alphanumeric id. The actual
    // length isn't constrained — anything ≥8 chars is valid per Session.cs:39.
    // Reuse randomUUID for simplicity; strip dashes so it's 32 chars.
    return randomUUID().replace(/-/g, '');
}

export async function signIn(opts: SignInOptions): Promise<SignInResult> {
    const t0 = Date.now();
    const stage = (msg: string): void => {
        console.log(`[auth t=${String(Date.now() - t0).padStart(5, ' ')}ms] ${msg}`);
    };

    const sessionId = newSessionId();
    const hub = new RpcHub();
    const url = RpcPeerRefBuilder.forClient(opts.rpcWsUrl, 'msgpack6');
    // mustStart=false — set the Node-specific webSocketFactory before start.
    const peer = hub.getClientPeer(url, (h, r) => new RpcClientPeer(h, r, false));
    peer.webSocketFactory = createNodeWsFactory({ sessionId });

    stage(`Opening RPC peer ${opts.rpcWsUrl} (session=${sessionId.slice(0, 8)}…)`);

    // Race the WebSocket-open event against a 10s deadline so we fail fast
    // instead of hanging if the underlying WS never reports open. The RPC
    // peer's own reconnect loop would otherwise retry silently forever.
    const whenConnected = peer.connected.whenNext();
    const whenDeadline = new Promise<void>((_, reject) =>
        setTimeout(() => reject(new Error('RPC peer did not connect within 10s')), 10_000));
    peer.start();
    try {
        await Promise.race([whenConnected, whenDeadline]);
    } catch (e) {
        peer.close();
        throw e;
    }
    stage('RPC WebSocket open, waiting for handshake');

    // `connected` fires on WS-open, but `peer.run()` nulls `_connection` while
    // the handshake is in flight. We don't have a direct "handshake complete"
    // event — poll isConnected briefly so the next call has a live connection.
    const handshakeDeadline = Date.now() + 5_000;
    while (!peer.isConnected && Date.now() < handshakeDeadline) {
        await new Promise((r) => setTimeout(r, 20));
    }
    if (!peer.isConnected) {
        peer.close();
        throw new Error('RPC handshake did not complete within 5s');
    }
    stage('RPC handshake complete');

    try {
        const emailAuth = hub.addClient(peer, EmailAuthDef) as unknown as EmailAuthClient;
        const secureTokens = hub.addClient(peer, SecureTokensDef) as unknown as SecureTokensClient;

        stage(`Signing in as ${opts.email}`);
        const ok = await emailAuth.OnValidateTotp({
            Session: sessionId,
            Email: opts.email,
            Totp: opts.totp,
        });
        if (!ok)
            throw new Error(
                `Sign-in failed for ${opts.email} (OTP=${opts.totp}). ` +
                `Is the dev OTP bypass active on the server?`);
        stage('OnValidateTotp returned true');

        stage('Calling CreateForSession');
        const secureToken = await secureTokens.CreateForSession(sessionId);
        if (!secureToken.Token)
            throw new Error('CreateForSession returned empty token.');
        stage('Got secure token');

        return { sessionId, sessionToken: secureToken.Token };
    } finally {
        peer.close();
    }
}
