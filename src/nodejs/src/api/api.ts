// Central gate for the project's RPC API — a static `Api` that exposes the
// shared `RpcHub` and its default `RpcClientPeer`, and initializes them via
// a list of `ApiModule`s.
//
// Until `Api.init(...)` is called, `Api.hub` and `Api.peer` both throw.
// After it's called:
//   - `Api.hub` is the single shared hub for this module graph (main thread,
//     one per worker, one per Node process).
//   - `Api.peer` is the hub's default client peer, created lazily.
//   - Every listed module's `register(hub)` has run — including any modules
//     those listed via `deps`.
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
    type RpcClientPeer,
} from 'actuallab-rpc';
import { getLogs } from 'logging';

import { ApiReconnectDelayer } from './api-reconnect-delayer.js';

const { infoLog, warnLog } = getLogs('Api');

const SERIALIZATION_FORMAT = 'msgpack6ck';

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

/** An opt-in chunk of RPC wiring — a group of service registrations and/or
 *  client-side setup, typically expressed as a static class so consumers can
 *  also reach typed accessors on it (e.g. `StreamingApi.streamServer`).
 *  Modules are composed via `deps` so a module that depends on another can
 *  list it and rely on it being registered first. Registration order is
 *  pre-order over the dep graph; duplicates are deduped by reference, so two
 *  modules can safely share a common dep. */
export interface ApiModule {
    /** Modules whose `register(hub)` must run before this module's. */
    readonly deps?: readonly ApiModule[];
    /** Registration hook — typically calls `hub.registry.registerService(...)`
     *  and/or caches typed clients via `hub.addClient(hub.defaultPeer, ...)`. */
    register(hub: RpcHub): void;
}

export class Api {
    /** Default peer URL. Main-thread code sets this once (e.g. BrowserInit
     *  on startup); workers receive the URL through the `url` argument to
     *  {@link Api.init}. `Api.init` reads `url ?? Api.url` and throws if
     *  still undefined. */
    static url: string | undefined;

    private static _hub: RpcHub | undefined;
    private static readonly _delayer = new ApiReconnectDelayer();
    private static readonly _scopes = new Set<string>();
    /** Default `true` — module graphs without a bridge (e.g. the video
     *  capture worker today) behave as if .NET is connected and rely on
     *  WebSocket-level failure to back off. `bindDotNetRpcConnected` wires
     *  the real signal in on main thread and audio worker. */
    private static _isDotNetRpcConnected = true;
    private static _canConnect = false;
    /** Per-worker "debug-disconnect requested" event. Workers subscribe so that
     *  {@link Api.disconnect} can reach across Worker boundaries — the main
     *  thread peer is dropped inline, but each worker must clear its own hub. */
    private static readonly _disconnectRequested = new Map<WorkerKind, EventHandlerSet<void>>();

    /** Fires when `requiresConnection` flips (0↔≥1 scopes). */
    static readonly requiresConnectionChanged = new EventHandlerSet<boolean>();
    /** Fires when `isDotNetRpcConnected` flips. */
    static readonly isDotNetRpcConnectedChanged = new EventHandlerSet<boolean>();
    /** Fires when `canConnect` (= `requiresConnection && isDotNetRpcConnected`)
     *  flips. Drives the internal reconnect-delayer gate. */
    static readonly canConnectChanged = new EventHandlerSet<boolean>();

    /** Initialize the shared RpcHub with a URL and zero or more modules.
     *  Must be called once at startup before `Api.hub` / `Api.peer` are
     *  accessed. A second call is a no-op (logs a warning) — the hub is
     *  already live and changing the URL mid-flight would strand in-flight
     *  calls.
     *  @param url Default peer URL (WebSocket, e.g. `wss://host/rpc/ws`).
     *         If `undefined`, falls back to the module-level {@link Api.url}
     *         (typically set by main-thread bootstrap). Throws if neither is
     *         provided. The serialization format is fixed (`msgpack6`) and
     *         appended automatically.
     *  @param modules Modules to register. Dependencies declared via each
     *         module's `deps` are registered first (transitively, deduped). */
    static init(url: string | undefined, ...modules: ApiModule[]): void {
        if (Api._hub !== undefined) {
            warnLog?.log('Api.init called more than once — ignoring subsequent call.');
            return;
        }
        Api.url = url ?? Api.url;
        if (!Api.url)
            throw new Error('Api.init: url is not provided and Api.url is not set.');
        const hub = new RpcHub();
        hub.defaultPeerUrl = RpcPeerRefBuilder.forClient(Api.url, SERIALIZATION_FORMAT);
        hub.reconnectDelayer = Api._delayer; // shared across all peers in this hub
        const done = new Set<ApiModule>();
        const visit = (m: ApiModule): void => {
            if (done.has(m)) return;
            done.add(m); // mark first — terminates cycles and blocks duplicate registration
            for (const d of m.deps ?? []) visit(d);
            m.register(hub);
        };
        for (const m of modules) visit(m);
        Api._hub = hub;
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

    /** Derived: `requiresConnection && isDotNetRpcConnected`. While false, the
     *  peer's run loop parks on the reconnect delayer and does not open a
     *  WebSocket. Already-open connections are NOT torn down by this flag. */
    static get canConnect(): boolean {
        return Api._canConnect;
    }

    /** Flip the .NET-side connectivity signal. Called by the bridge:
     *  `ConnectivityUI` → JS interop on the main thread, `WorkerConnectivityUI`
     *  forward on workers. Safe to call before `Api.init`. */
    static setDotNetRpcConnected(value: boolean): void {
        if (Api._isDotNetRpcConnected === value) return;

        Api._isDotNetRpcConnected = value;
        Api.isDotNetRpcConnectedChanged.trigger(value);
        Api._recomputeCanConnect();
    }

    /** Bind this module graph's `isDotNetRpcConnected` to a `ConnectivityUI`
     *  or `WorkerConnectivityUI`-shaped source — both expose the same
     *  `isConnected` + `isConnectedChanged` pair. Structurally-typed because
     *  the two sides use different `EventHandlerSet` classes (actuallab-core
     *  vs the project's own `event-handling` module). Idempotent. */
    static bindDotNetRpcConnected(source: {
        readonly isConnected: boolean;
        readonly isConnectedChanged: { add(handler: (v: boolean) => void): unknown };
    }): void {
        Api.setDotNetRpcConnected(source.isConnected);
        source.isConnectedChanged.add(v => Api.setDotNetRpcConnected(v));
    }

    /** Add a scope that requires the peer to be connectable. Idempotent; the
     *  peer is allowed to connect iff at least one scope is currently held. */
    static requireConnection(scope: string): void {
        if (Api._scopes.has(scope)) return;
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
        if (!Api._scopes.delete(scope)) return;
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

    private static _recomputeCanConnect(): void {
        const value = Api.requiresConnection && Api._isDotNetRpcConnected;
        if (Api._canConnect === value) return;
        Api._canConnect = value;
        Api.canConnectChanged.trigger(value);
        Api._delayer.setAllowed(value);
    }
}
