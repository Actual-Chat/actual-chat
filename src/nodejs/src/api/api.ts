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

import { RpcHub, RpcPeerRefBuilder, type RpcClientPeer } from 'actuallab-rpc';
import { getLogs } from 'logging';

const { warnLog } = getLogs('Api');

/** Serialization format used by every Api peer. Binary MessagePack — matches
 *  the .NET server's binary path and keeps frame traffic small. */
const SERIALIZATION_FORMAT = 'msgpack6';

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
    private static _hub: RpcHub | undefined;

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

    /** Initialize the shared RpcHub with a URL and zero or more modules.
     *  Must be called once at startup before `Api.hub` / `Api.peer` are
     *  accessed. A second call is a no-op (logs a warning) — the hub is
     *  already live and changing the URL mid-flight would strand in-flight
     *  calls.
     *  @param url Default peer URL (WebSocket, e.g. `wss://host/rpc/ws`).
     *         The serialization format is fixed (`msgpack6`) and appended
     *         automatically.
     *  @param modules Modules to register. Dependencies declared via each
     *         module's `deps` are registered first (transitively, deduped). */
    static init(url: string, ...modules: ApiModule[]): void {
        if (Api._hub !== undefined) {
            warnLog?.log('Api.init called more than once — ignoring subsequent call.');
            return;
        }
        const hub = new RpcHub();
        hub.defaultPeerUrl = RpcPeerRefBuilder.forClient(url, SERIALIZATION_FORMAT);
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
}
