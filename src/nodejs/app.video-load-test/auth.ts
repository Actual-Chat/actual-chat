// Sign in flow that mirrors the C# harness:
//   1. Generate a fresh Session Id (random GUID-ish, matches Session.New()).
//   2. Open an RPC peer with that Id in the `Session` header.
//   3. Call IEmailAuth.OnValidateTotp(command) to sign the session in.
//   4. Return { sessionId } so producers/consumers can share the same
//      identity across their individual connections.

import { randomUUID } from 'node:crypto';
import { RpcHub, RpcClientPeer, RpcRefBuilder } from '../src/actuallab-rpc/index.js';

import { createNodeWsFactory } from './node-ws.js';
import {
    EmailAuthDef,
    type EmailAuthClient,
} from './service-defs.js';

export interface SignInOptions {
    apiUrl: string;
    email: string;
    totp: number;
}

export interface SignInResult {
    sessionId: string;
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
    const url = RpcRefBuilder.forClient(opts.apiUrl, 'msgpack6');
    // mustStart=false — set the Node-specific webSocketFactory before start.
    const peer = hub.getClientPeer(url, (h, r) => new RpcClientPeer(h, r, false));
    peer.webSocketFactory = createNodeWsFactory({ sessionId });

    stage(`Opening RPC peer ${opts.apiUrl} (session=${sessionId.slice(0, 8)}…)`);

    // Race handshake completion against a 15s deadline so we fail fast
    // instead of hanging. `peer.whenConnected()` only resolves after the
    // server's handshake has been received.
    const whenConnected = peer.whenConnected();
    const whenDeadline = new Promise<void>((_, reject) =>
        setTimeout(() => reject(new Error('RPC peer did not connect within 15s')), 15_000));
    peer.start();
    try {
        await Promise.race([whenConnected, whenDeadline]);
    } catch (e) {
        peer.close();
        throw e;
    }
    stage('RPC handshake complete');

    try {
        const emailAuth = hub.addClient(peer, EmailAuthDef) as unknown as EmailAuthClient;

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

        return { sessionId };
    } finally {
        peer.close();
    }
}
