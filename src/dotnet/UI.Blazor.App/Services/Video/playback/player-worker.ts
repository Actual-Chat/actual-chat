import type { VideoPlaybackStats } from '../frame-envelopes';
import type { DecoderLike } from '../operators/decode';
import type { VideoFrameDto } from '../operators/pull';
import { Player, type PlayerConfig } from './player';
import { PlaybackSession } from './session';
import type {
    PlayerWorker,
    PlayerWorkerOptions,
} from './player-worker-contract';

/**
 * Module-private state. The worker hosts at most one
 * `PlaybackSession` (concurrent streams share its decoder pool, clock,
 * and aggregated stats) and a registry of `Player`s keyed by
 * `streamId`.
 *
 * The worker is built around a session-singleton model rather than
 * per-call session creation: the decoder pool's TTL semantics depend
 * on persistent state across stop / start cycles. Recreating the
 * session on every `start()` would defeat the pool entirely.
 */
let session: PlaybackSession | null = null;
const players = new Map<string, Player>();

/**
 * Production hooks that the worker delegates to. Tests can override
 * them via `__setPlayerWorkerHooks` to drive synthetic streams /
 * decoders without monkey-patching globals. Production wires
 * `streamingApi.liveVideoStreams.GetStream` and a real `VideoDecoder`
 * (after borrowing a slot from the session's decoder pool).
 */
interface PlayerWorkerHooks {
    getStream: (streamId: string) => Promise<AsyncIterable<VideoFrameDto>> | AsyncIterable<VideoFrameDto>;
    createDecoder: (
        codec: string,
        handlers: { onFrame: (frame: VideoFrame) => void; onError: (e: Error) => void },
    ) => DecoderLike;
    /**
     * Build the rendering surface for a freshly-started stream. The
     * production wiring constructs a `MediaStreamTrackGenerator` (when
     * the platform supports it) or an `OffscreenCanvas` 2D context;
     * the resulting `RenderBackendConfig` is passed to the Player.
     */
    /**
     * Build the rendering surface AND optionally the
     * `MediaStreamTrack` to ship to the main thread for `<video
     * srcObject>` attachment. The MSTG path returns the generator's
     * `track` so the worker can fire `onTrackReady(streamId, kind,
     * track)`; the canvas path returns `null` (the main thread already
     * holds the canvas).
     */
    buildBackend: (opts: PlayerWorkerOptions) => {
        config: PlayerConfig['backend'];
        track: MediaStreamTrack | null;
    };
    /**
     * Notify the main thread that the rendering surface is ready. The
     * worker fires this from `start()` immediately after `buildBackend`
     * so the main thread can wire its `<video>` / `<canvas>` while the
     * pipeline is still spinning up. Test hooks can leave it undefined.
     */
    onTrackReady?: (
        streamId: string,
        kind: PlayerConfig['backend']['kind'],
        track: MediaStreamTrack | null,
    ) => void;
    /**
     * Latency reporter forwarded to the main thread. Optional: when
     * absent, the operator stays unwired for the run. The hook is
     * stream-scoped — the worker calls this once per `start()` with
     * the matching `streamId` to obtain a per-stream reporter.
     */
    makeReportLatency?: (streamId: string) => PlayerConfig['reportLatency'];
    /** Forwards the AppConstants payload to the host's
     *  `initAppConstants`. Tests can supply a no-op. */
    initAppConstants?: (appConstants: import('./player-worker-contract').AppConstantsLike) => void;
    /** Surface terminal pipeline failures to the main thread. */
    reportError?: (streamId: string, error: string) => void;
    /** Surface clean stream termination to the main thread. */
    reportStreamEnded?: (streamId: string, reason: string) => void;
    /** Eagerly initialise the worker's Fusion RPC peer so the WS
     *  handshake overlaps the rest of init. Production: stores
     *  `apiUrl` and runs the equivalent of `ensurePullApi()`. Tests
     *  with synthetic streams skip it. */
    prewarmRpc?: (apiUrl: string) => void;
}

let hooks: PlayerWorkerHooks | null = null;
// Stream IDs that we stopped locally (via `stop()`) — so the lifecycle
// observer suppresses `onError` / `onStreamEnded` for the resulting
// abort. Without this, an MSTG-to-canvas fallback (which calls
// `playerWorker.stop(streamId)` then immediately `playerWorker.start(...)`
// the same stream) raises spurious terminal callbacks on the main thread
// while the restart is mid-flight.
const locallyStopped = new Set<string>();

/**
 * Test seam: install the hooks the worker delegates to. Production
 * wiring (separate file or a top-level entry) calls this once on
 * worker startup with the platform-specific factories.
 */
export function __setPlayerWorkerHooks(next: PlayerWorkerHooks | null): void {
    hooks = next;
}

/** Test seam: replace the session (or null it out). Used by tests
 *  that want to assert disposal / reset behavior without going through
 *  the full RPC surface. */
export function __setPlayerWorkerSession(next: PlaybackSession | null): void {
    session = next;
}

/** Test seam: read the current session (e.g. to assert
 *  `activeStreams` after a series of start / stop calls). */
export function __getPlayerWorkerSession(): PlaybackSession | null {
    return session;
}

function ensureSession(): PlaybackSession {
    session ??= new PlaybackSession();
    return session;
}

function ensureHooks(): PlayerWorkerHooks {
    if (!hooks) {
        throw new Error(
            'PlayerWorker: hooks not installed; '
            + 'call __setPlayerWorkerHooks before start()');
    }
    return hooks;
}

/**
 * Worker implementation of the {@link PlayerWorker} RPC contract.
 * Methods delegate to the module-private session + player registry.
 *
 * Concurrency model:
 *  - `start()` builds a `Player`, registers a stream with the session,
 *    stores it in the registry, and triggers the pipeline. The
 *    method resolves immediately after the pipeline starts (the
 *    pipeline runs in the background until `whenDone()` settles or
 *    `stop()` is called).
 *  - When the pipeline drains, the player auto-clears its internal
 *    `run`; we additionally remove it from the registry and
 *    `unregisterStream()` so `stats.activeStreams` stays accurate.
 *  - `stop(streamId?)` triggers `Player.stop()` and awaits drain.
 *  - Errors from the pipeline (rejection of `whenDone`) are swallowed
 *    here and reported via `onError`. We don't re-throw — the worker
 *    must keep running for the other streams.
 */
export const playerWorkerImpl: PlayerWorker = {
    init(appConstants): Promise<void> {
        const h = hooks;
        h?.initAppConstants?.(appConstants);
        return Promise.resolve();
    },
    async start(
        opts: PlayerWorkerOptions,
        mstgWritable?: WritableStream<VideoFrame>,
        canvas?: OffscreenCanvas,
    ): Promise<void> {
        if (players.has(opts.streamId)) {
            throw new Error(
                `PlayerWorker.start: stream ${opts.streamId} already running`);
        }
        const s = ensureSession();
        const h = ensureHooks();
        // Fold trailing transferable args back into opts for buildBackend.
        // Per the contract, these can't ride inside the structured-cloned
        // `opts` envelope — they have to land here as separate args.
        const fullOpts: PlayerWorkerOptions = {
            ...opts,
            ...(canvas !== undefined ? { canvas } : {}),
            ...(mstgWritable !== undefined ? { mstgWritable } : {}),
        };
        const { config: backend, track } = h.buildBackend(fullOpts);
        const player = new Player(s);
        players.set(opts.streamId, player);
        s.registerStream();

        // Tell the main thread about the rendering surface BEFORE the
        // pipeline starts pulling frames — keeps `<video srcObject>` /
        // canvas attachment off the critical path of the first keyframe.
        h.onTrackReady?.(opts.streamId, backend.kind, track);

        const playerConfig: PlayerConfig = {
            streamId: opts.streamId,
            initialDecoderConfig: opts.initialDecoderConfig,
            targetBufferSpanMs: opts.targetBufferSpanMs,
            backend,
            getStream: h.getStream,
            createDecoder: handlers => h.createDecoder(opts.initialDecoderConfig.codec, handlers),
            reportLatency: h.makeReportLatency?.(opts.streamId),
        };

        await player.start(playerConfig);

        // Track lifecycle in the background. The pipeline drains either
        // because it finished cleanly (sender stopped), an operator
        // threw, or we stopped it locally. Local stops (`locallyStopped`)
        // skip the terminal callback so an MSTG→canvas fallback's
        // intermediate abort doesn't race the restart. Failures fan to
        // `onError` ONLY — the host treats `onError` as terminal.
        player.whenDone().then(
            () => {
                if (!locallyStopped.has(opts.streamId))
                    h.reportStreamEnded?.(opts.streamId, 'completed');
            },
            (e: unknown) => {
                if (locallyStopped.has(opts.streamId))
                    return;
                const msg = e instanceof Error ? e.message : String(e);
                h.reportError?.(opts.streamId, msg);
            },
        ).finally(() => {
            if (players.get(opts.streamId) === player) {
                players.delete(opts.streamId);
                s.unregisterStream();
            }
            locallyStopped.delete(opts.streamId);
        });
    },

    async stop(streamId?: string): Promise<void> {
        if (streamId === undefined) {
            // Stop everything. Snapshot so iteration isn't perturbed by
            // the cleanup-on-drain handler racing us.
            const all = Array.from(players.entries());
            for (const [id, _p] of all) locallyStopped.add(id);
            for (const [, p] of all) p.stop();
            await Promise.allSettled(all.map(([, p]) => p.whenDone()));
            return;
        }
        const player = players.get(streamId);
        if (!player) return;
        locallyStopped.add(streamId);
        player.stop();
        try { await player.whenDone(); } catch { /* local stop — suppressed */ }
    },

    requestKeyframe(streamId?: string): Promise<void> {
        // Keyframe requests live outside the pipeline (they're an RPC
        // hint to the SENDER, not a pipeline operation) and are
        // currently a no-op on the worker side — the production wiring
        // forwards via the streaming API surface, but that's hooked up
        // in the wiring file rather than the contract impl. Kept here
        // for contract symmetry; future implementations should look
        // up `streamId` and dispatch the RPC.
        void streamId;
        return Promise.resolve();
    },

    getStats(): Promise<VideoPlaybackStats> {
        const s = ensureSession();
        // Return a shallow copy so the caller can't mutate session
        // counters by handle. The session itself owns the canonical
        // mutable instance.
        return Promise.resolve({ ...s.stats });
    },

    prewarmRpc(apiUrl: string): Promise<void> {
        // Production: caches the URL on the host and triggers
        // `Api.init` so the WS handshake overlaps player setup. Tests
        // that don't install the hook fall back to the lazy path
        // inside `getStream`.
        hooks?.prewarmRpc?.(apiUrl);
        return Promise.resolve();
    },

    onConnectivityUpdate(
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> {
        // Stub. Production wiring forwards to a worker-side connectivity
        // mirror; left as a no-op until that lands.
        void isOnline;
        void isConnected;
        void isBlazorServer;
        return Promise.resolve();
    },
};

/**
 * Tear down the entire worker state. Stops every running pipeline,
 * disposes the session, clears the registry. Idempotent.
 *
 * Production callers invoke this on worker termination (e.g. a
 * `worker.terminate()` precursor message); tests use it to reset
 * between cases.
 */
export async function disposePlayerWorker(): Promise<void> {
    if (players.size > 0) {
        const all = Array.from(players.values());
        for (const p of all) p.stop();
        await Promise.allSettled(all.map(p => p.whenDone()));
        players.clear();
    }
    if (session) {
        session.dispose();
        session = null;
    }
}
