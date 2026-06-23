// Central gate for the project's RPC API — a static `Api` that exposes the
// shared `RpcHub` and its default `RpcClientPeer`, and initializes them via
// `Api.init(source, { ... })`.
//
// Naming convention — wire types declared in *-api.ts modules mirror .NET
// record names verbatim: `VideoFormat`, `ReceiveQuality`, `PlaybackStreamInfo`,
// etc. The `Dto` suffix is used ONLY when the bare name would collide with a
// browser global or another in-scope type — for example `VideoFrameDto`
// (vs. WebCodecs `VideoFrame`) and `AudioFrameDto` (the matching pair, kept
// for symmetry). Don't apply the suffix prophylactically; bare names keep
// the TS↔C# mapping obvious. When you add a new wire DTO, prefer the bare
// C# name and only fall back to `Dto` if TypeScript actually complains.
//
// Until `Api.init(...)` is called, `Api.hub` and `Api.peer` both throw.
// After it's called:
//   - `Api.hub` is the single shared hub for this module graph (main thread,
//     one per worker, one per Node process).
//   - `Api.peer` is the hub's default client peer, created lazily.
//   - Every module passed to `Api.init(source, { modules })` has run its
//     `register(hub)` — including modules listed via `deps`.
//
// Connectivity gating:
//   The peer is allowed to attempt a connection only when `canConnect` is
//   true, derived from two signals:
//     - `requiresConnection` — at least one scope has been requested via
//       `requireConnection(scope)` and not yet released. Workers typically
//       have a single scope; the main thread refcounts multiple VideoPlayers.
//     - `isDotNetRpcConnected` — the .NET-side rpc is connected (pushed in
//       from ConnectivityUI on the main thread, from WorkerConnectivityUI
//       on workers). If .NET can't reach the server, neither can we.
//   The three states are exposed as getters plus matching
//   `*Changed: EventHandlerSet<boolean>` events.

import { EventHandlerSet } from 'actuallab-core';
import {
    RpcHub,
    RpcPeerRefBuilder,
    RpcSerializationFormat,
    RpcSerializationFormatResolver,
    RpcMessagePackSerializationFormat,
    RpcMessagePackCompactSerializationFormat,
    RpcClientPeer,
    defaultConnectionUrlResolver,
    type RpcStreamOptions,
} from 'actuallab-rpc';
import { AUDIO, VIDEO } from 'app-constants';
import { getLogs } from 'logging';

import { ApiReconnectDelayer } from './api-reconnect-delayer.js';

const { infoLog, warnLog, errorLog } = getLogs('Api');

const SERIALIZATION_FORMAT = 'msgpack6ck';
const SESSION_TOKEN_QUERY_PARAMETER = 'session';
const DEFAULT_SESSION_TOKEN_MIN_LIFESPAN_MS = 10_000;

(RpcSerializationFormat.All as RpcSerializationFormat[]).push(
    new RpcMessagePackSerializationFormat('msgpack6k'),
    new RpcMessagePackCompactSerializationFormat('msgpack6ck'),
);
RpcSerializationFormatResolver.Default = new RpcSerializationFormatResolver(SERIALIZATION_FORMAT);

/** Identifies which peer / realm a subscriber or {@link Api.disconnect} call
 *  targets. Debug tooling uses this to drop peers; the reconnect loop brings
 *  them back automatically. */
export enum WorkerKind {
    /** Main-thread peer (shared with {@link VideoPlayback}). */
    UI = 'UI',
    /** Main-thread peer used by video playback (shared with {@link UI}). */
    VideoPlayback = 'VideoPlayback',
    /** Opus encoder worker peer. */
    Recording = 'Recording',
    /** Video processing worker peer. */
    VideoCapture = 'VideoCapture',
}

export interface ApiModule {
    /** Modules whose `register(hub)` must run before this module's. */
    readonly deps?: readonly ApiModule[];
    /** Registration hook — typically calls `hub.registry.registerService(...)`
     *  and/or caches typed clients via `hub.addClient(hub.defaultPeer, ...)`. */
    register(hub: RpcHub): void;
}

export type SessionTokenProvider = (minLifespanMs?: number) => Promise<string>;

export interface ApiConnectivityUI {
    readonly isConnected: boolean;
    readonly isConnectedChanged: { add(handler: (v: boolean) => void): unknown };
}

export class MediaRpcStreamOptions {
    static videoRecording<T>(canSkipTo: (item: T) => boolean): RpcStreamOptions<T> {
        return {
            isRealTime: true,
            allowReconnect: true,
            ackPeriod: VIDEO.rpcStreamAckPeriod,
            ackAdvance: VIDEO.rpcStreamAckAdvance,
            // Local sender buffer sized for ≈ keyframe period × 1.33 of source
            // moments. Fusion's pump fills it continuously while connected,
            // and canSkipTo=isKeyFrame absorbs slow-wire spans by skipping
            // older non-anchor items inside the buffer rather than blocking
            // the capture pipeline.
            bufferSize: VIDEO.senderBufferSize,
            canSkipTo,
        };
    }

    static audioRecording<T>(): RpcStreamOptions<T> {
        return {
            isRealTime: false,
            allowReconnect: true,
            ackPeriod: AUDIO.stream.recordingRpcStreamAckPeriod,
        };
    }

    static audioDelivery<T>(): RpcStreamOptions<T> {
        return {
            isRealTime: false,
            allowReconnect: true,
            ackPeriod: AUDIO.stream.deliveryRpcStreamAckPeriod,
        };
    }

    static transcriptDelivery<T>(): RpcStreamOptions<T> {
        return MediaRpcStreamOptions.audioDelivery<T>();
    }

    // File upload (client -> server). Mirrors .NET Constants.Uploads:
    // ack ~256 KB, in-flight window ~4 MB at 16 KB sub-chunks.
    static upload<T>(): RpcStreamOptions<T> {
        return {
            isRealTime: false,
            allowReconnect: true,
            ackPeriod: 16,
            ackAdvance: 256,
        };
    }
}

export interface ApiInitOptions {
    readonly url?: string;
    readonly modules?: readonly ApiModule[];
    readonly connectivityUI?: ApiConnectivityUI;
    readonly sessionTokenProvider?: SessionTokenProvider;
    readonly requireConnection?: boolean,
}

export class Api {
    private static _url: string | undefined;
    private static _hub: RpcHub | undefined;
    private static readonly _delayer = new ApiReconnectDelayer();
    private static readonly _scopes = new Set<string>();
    /** Default `true` — module graphs without a bridge (e.g. the video
     *  capture worker today) behave as if .NET is connected and rely on
     *  WebSocket-level failure to back off. The `connectivityUI` init option
     *  wires the real signal in on main thread and workers. */
    private static _isDotNetRpcConnected = true;
    private static _canConnect = false;
    private static _connectivityUI: ApiConnectivityUI | undefined;
    private static _sessionTokenProvider: SessionTokenProvider | undefined;
    private static readonly _registeredModules = new Set<ApiModule>();
    /** Per-worker "debug-disconnect requested" event. Workers subscribe so that
     *  {@link Api.disconnect} can reach across Worker boundaries — the main
     *  thread peer is dropped inline, but each worker must clear its own hub. */
    private static readonly _disconnectRequested = new Map<WorkerKind, EventHandlerSet<void>>();

    static get url() : string {
        if (!Api._url)
            throw new Error('Api.init(...) must be called first.');

        return Api._url;
    }

    /** Fires when `requiresConnection` flips (0↔≥1 scopes). */
    static readonly requiresConnectionChanged = new EventHandlerSet<boolean>();
    /** Fires when `isDotNetRpcConnected` flips. */
    static readonly isDotNetRpcConnectedChanged = new EventHandlerSet<boolean>();
    /** Fires when `canConnect` (= `requiresConnection && isDotNetRpcConnected`)
     *  flips. Drives the internal reconnect-delayer gate. */
    static readonly canConnectChanged = new EventHandlerSet<boolean>();

    /** Initialize or extend the shared RpcHub. Later calls may add modules and
     *  bind setup options, but changing the live URL is ignored. */
    static init(source: string, options: ApiInitOptions = {}): void {
        if (Api._hub !== undefined) {
            errorLog?.log('init (recurring, ignored):', source, options);
            return;
        }

        infoLog?.log('init:', source, options);
        if (!options.url)
            throw new Error('init: url is not provided.');

        // Set _url, _sessionTokenProvider, _connectivityUI
        Api._url = options.url;
        Api._sessionTokenProvider = options.sessionTokenProvider;
        Api._connectivityUI = options.connectivityUI;
        if (Api._connectivityUI !== undefined) {
            Api.isDotNetRpcConnected = Api._connectivityUI.isConnected;
            Api._connectivityUI.isConnectedChanged.add(v => Api.isDotNetRpcConnected = v);
        }

        // Create RpcHub
        const hub = new RpcHub();
        hub.defaultPeerUrl = RpcPeerRefBuilder.forClient(Api.url, SERIALIZATION_FORMAT);
        hub.defaultPeerFactory = (h, r) => {
            const peer = new RpcClientPeer(h, r, false);
            Api.configurePeer(peer);
            peer.start();
            return peer;
        };
        hub.reconnectDelayer = Api._delayer; // shared across all peers in this hub
        Api._hub = hub;

        // Register modules
        Api._registerModules(hub, options.modules ?? []);

        // Optionally require connection
        if (options.requireConnection)
            Api.requireConnection(source);
    }

    /** The shared `RpcHub`. Throws if `Api.init` has not been called. */
    static get hub(): RpcHub {
        if (Api._hub === undefined)
            throw new Error('Api.init was not called.');
        return Api._hub;
    }

    /** The default client peer. Throws if `Api.init` has not been called;
     *  after that the peer is created on first access via the hub's
     *  `defaultPeerFactory` (or the hub's built-in default factory). */
    static get peer(): RpcClientPeer {
        return Api.hub.defaultPeer;
    }

    /** True iff at least one `requireConnection(scope)` is held without a
     *  matching `releaseConnection(scope)`. */
    static get requiresConnection(): boolean {
        return Api._scopes.size > 0;
    }

    /** Last value pushed in via `setDotNetRpcConnected`. */
    static get isDotNetRpcConnected(): boolean {
        return Api._isDotNetRpcConnected;
    }
    private static set isDotNetRpcConnected(value: boolean) {
        if (Api._isDotNetRpcConnected === value)
            return;

        Api._isDotNetRpcConnected = value;
        Api.isDotNetRpcConnectedChanged.trigger(value);
        Api._recomputeCanConnect();
    }

    /** Derived: `requiresConnection && isDotNetRpcConnected`. While false, the
     *  peer's run loop parks on the reconnect delayer and does not open a
     *  WebSocket. Already-open connections are NOT torn down by this flag. */
    static get canConnect(): boolean {
        return Api._canConnect;
    }

    static async getSessionToken(minLifespanMs = DEFAULT_SESSION_TOKEN_MIN_LIFESPAN_MS): Promise<string> {
        const provider = Api._sessionTokenProvider;
        if (!provider)
            throw new Error('Session token provider not configured.');

        return provider(minLifespanMs);
    }

    static configurePeer(peer: RpcClientPeer): RpcClientPeer {
        peer.connectionUrlResolver = async p => {
            const connectionUrl = await defaultConnectionUrlResolver(p);
            const sessionToken = await Api.getSessionToken();
            if (!sessionToken)
                return connectionUrl;

            try {
                const url = new URL(connectionUrl);
                url.searchParams.set(SESSION_TOKEN_QUERY_PARAMETER, sessionToken);
                return url.toString();
            } catch {
                const sep = connectionUrl.includes('?') ? '&' : '?';
                return connectionUrl + sep + `${SESSION_TOKEN_QUERY_PARAMETER}=${encodeURIComponent(sessionToken)}`;
            }
        };
        return peer;
    }

    /** Add a scope that requires the peer to be connectable. Idempotent; the
     *  peer is allowed to connect iff at least one scope is currently held. */
    static requireConnection(scope: string): void {
        if (Api._scopes.has(scope))
            return;

        const wasEmpty = Api._scopes.size === 0;
        Api._scopes.add(scope);
        if (wasEmpty) {
            Api.requiresConnectionChanged.trigger(true);
            Api._recomputeCanConnect();
        }
    }

    /** Release a scope previously added via `requireConnection(scope)`.
     *  Idempotent. */
    static releaseConnection(scope: string): void {
        if (!Api._scopes.delete(scope))
            return;

        if (Api._scopes.size === 0) {
            Api.requiresConnectionChanged.trigger(false);
            Api._recomputeCanConnect();
        }
    }

    /** Debug-only: force-disconnect the peer for a single {@link WorkerKind}.
     *  For main-thread kinds (UI, VideoPlayback) the current-realm peer's WS
     *  connection is closed; for worker kinds (Recording, VideoCapture) the
     *  request is dispatched via {@link onDisconnectRequested} subscribers
     *  that live in each worker's realm. The peer instance is preserved in
     *  every case — its reconnect loop reopens the WS. */
    static disconnect(workerKind: WorkerKind): void {
        // UI & VideoPlayback share the current-realm peer. Close its WS
        // connection; the peer's reconnect loop reopens it. The peer instance
        // itself is never destroyed — all cached client proxies stay valid.
        if (workerKind === WorkerKind.UI || workerKind === WorkerKind.VideoPlayback) {
            if (Api._hub?.defaultPeerUrl !== undefined) {
                Api._hub.peers.get(Api._hub.defaultPeerUrl)?.disconnect();
                infoLog?.log('disconnect: main peer disconnected');
            }
            return;
        }

        // Worker-backed kinds — subscribers live in each worker's realm.
        const set = Api._disconnectRequested.get(workerKind);
        if (set === undefined || set.count === 0) {
            infoLog?.log(`disconnect: no subscribers for ${workerKind}`);
            return;
        }
        try {
            set.trigger(undefined);
        } catch (e) {
            warnLog?.log(`disconnect: subscriber for ${workerKind} failed`, e);
        }
    }

    /** Event set subscribers attach to so {@link Api.disconnect} can reach
     *  them. The main thread's peer is removed directly by `disconnect`; worker
     *  pipelines subscribe here and call `Api.hub.removePeer(...)` in their own
     *  realm. `workerKind` picks the bucket — `Recording` for the audio-encoder
     *  worker, `VideoCapture` for the video-processing worker. */
    static onDisconnectRequested(workerKind: WorkerKind): EventHandlerSet<void> {
        let set = Api._disconnectRequested.get(workerKind);
        if (!set) {
            set = new EventHandlerSet<void>();
            Api._disconnectRequested.set(workerKind, set);
        }
        return set;
    }

    // Private methods

    private static _registerModules(hub: RpcHub, modules: readonly ApiModule[]): void {
        const visit = (m: ApiModule): void => {
            if (Api._registeredModules.has(m))
                return;

            Api._registeredModules.add(m); // mark first — terminates cycles and blocks duplicate registration
            for (const d of m.deps ?? [])
                visit(d);

            m.register(hub);
        };
        for (const m of modules)
            visit(m);
    }

    private static _recomputeCanConnect(): void {
        const value = Api.requiresConnection && Api._isDotNetRpcConnected;
        if (Api._canConnect === value)
            return;

        Api._canConnect = value;
        Api.canConnectChanged.trigger(value);
        Api._delayer.setAllowed(value);
    }
}
