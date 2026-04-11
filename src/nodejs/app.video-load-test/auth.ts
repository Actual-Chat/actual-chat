// Sign in flow that mirrors the C# harness:
//   1. Generate a fresh Session Id (random GUID-ish, matches Session.New()).
//   2. Open an RPC peer with that Id in the `Session` header.
//   3. Call IEmailAuth.OnValidateTotp(command) to sign the session in.
//   4. Call ISecureTokens.CreateForSession(session) to get a SignalR sessionToken.
//   5. Return { sessionId, sessionToken } so producers/consumers can share the
//      same identity across their individual connections.

import { randomUUID } from 'node:crypto';
import { RpcHub, RpcClientPeer } from '../src/actuallab-rpc/index.js';

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
    const sessionId = newSessionId();
    const hub = new RpcHub();
    const peer = new RpcClientPeer(hub, opts.rpcWsUrl, 'msgpack6');
    const wsFactory = createNodeWsFactory({ sessionId });

    // Run the peer's reconnect loop in the background. It sends the handshake
    // on connect; we wait for .connected to fire before making RPC calls.
    const whenConnected = peer.connected.whenNext();
    void peer.run(wsFactory);
    await whenConnected;

    try {
        const emailAuth = hub.addClient(peer, EmailAuthDef) as unknown as EmailAuthClient;
        const secureTokens = hub.addClient(peer, SecureTokensDef) as unknown as SecureTokensClient;

        console.log(`[auth] Signing in as ${opts.email} (session=${sessionId.slice(0, 8)}…)`);
        const ok = await emailAuth.OnValidateTotp({
            Session: sessionId,
            Email: opts.email,
            Totp: opts.totp,
        });
        if (!ok)
            throw new Error(
                `Sign-in failed for ${opts.email} (OTP=${opts.totp}). ` +
                `Is the dev OTP bypass active on the server?`);

        console.log('[auth] Session validated. Fetching secure token…');
        const secureToken = await secureTokens.CreateForSession(sessionId);
        if (!secureToken.Token)
            throw new Error('CreateForSession returned empty token.');

        console.log('[auth] Got secure token.');
        return { sessionId, sessionToken: secureToken.Token };
    } finally {
        peer.close();
    }
}
