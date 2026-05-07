import { MonotonicClock } from 'clocks';
import {
    createEmptyPlaybackStats,
    type VideoPlaybackStats,
} from '../frame-envelopes';
import { DecoderPool, type DecoderPoolOptions } from './decoder-pool';

export interface PlaybackSessionOptions {
    /**
     * Test override. Production: a fresh `MonotonicClock` with default
     * options is constructed inside the session.
     */
    createArrivalClock?: () => MonotonicClock;

    /**
     * Pool tuning forwarded to the underlying `DecoderPool`. Tests pass
     * `parkTtlMs: 0` + a controllable `now` to assert sweep behavior.
     */
    decoderPool?: DecoderPoolOptions;
}

/**
 * Session-level state shared across every concurrent playback pipeline
 * inside a single playback worker. Owns:
 *  - the receiver-side `MonotonicClock` (so all concurrent streams
 *    share one wallclock reference, which makes cross-stream latency
 *    comparisons coherent),
 *  - the `DecoderPool` (so codec switches across stream restarts can
 *    reuse hardware decoder slots without paying re-init overhead),
 *  - the aggregated `VideoPlaybackStats` (one object, every operator
 *    on every pipeline contributes to it; the worker reports on a
 *    cadence).
 *
 * Lifecycle:
 *  - `new PlaybackSession()` builds fresh state.
 *  - `registerStream()` / `unregisterStream()` update `stats.activeStreams`
 *    so the session can light up "playing" indicators or skip reports
 *    when no streams are active.
 *  - `reset()` resets stats counters (preserving `activeStreams` and
 *    `sessionStartedAtMs`) without disposing the decoder pool — the
 *    pool's parked decoders survive across pipeline restarts on
 *    purpose.
 *  - `dispose()` tears the pool down. After dispose the session is
 *    unusable; create a fresh one for a new worker session.
 */
export class PlaybackSession {
    readonly arrivalClock: MonotonicClock;
    readonly decoderPool: DecoderPool;
    readonly stats: VideoPlaybackStats;
    private disposed = false;

    constructor(opts: PlaybackSessionOptions = {}) {
        this.arrivalClock = (opts.createArrivalClock ?? (() => new MonotonicClock()))();
        this.decoderPool = new DecoderPool(opts.decoderPool);
        this.stats = createEmptyPlaybackStats(this.arrivalClock.now().timeMs);
    }

    /**
     * Reset the session's counters back to zero (preserving
     * `activeStreams` so currently-running pipes keep contributing) and
     * stamp a fresh `sessionStartedAtMs`. Decoder pool is left alone —
     * its TTL governs decoder lifetime independently of session resets.
     */
    reset(): void {
        const activeStreams = this.stats.activeStreams;
        const startedAtMs = this.arrivalClock.now().timeMs;
        this.stats.chunksArrived = 0;
        this.stats.chunksDroppedAtBuffer = 0;
        this.stats.chunksDroppedDecoderError = 0;
        this.stats.framesDecoded = 0;
        this.stats.framesPresented = 0;
        this.stats.bytesReceived = 0;
        this.stats.decodeTimeMsSum = 0;
        this.stats.decodeTimeMsCount = 0;
        this.stats.activeStreams = activeStreams;
        this.stats.sessionStartedAtMs = startedAtMs;
    }

    /**
     * Tear down the decoder pool and mark the session unusable.
     * Idempotent — a second call is a no-op.
     */
    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.decoderPool.dispose();
    }

    /** Increment `stats.activeStreams`. Caller (the player worker)
     *  pairs every `registerStream()` with exactly one
     *  `unregisterStream()` on pipeline finalize. */
    registerStream(): void {
        this.stats.activeStreams++;
    }

    /** Decrement `stats.activeStreams`. Floors at zero to keep the
     *  counter sane in the face of a stray double-unregister. */
    unregisterStream(): void {
        if (this.stats.activeStreams > 0) {
            this.stats.activeStreams--;
        }
    }

    /** True after `dispose()` has been called. */
    isDisposed(): boolean {
        return this.disposed;
    }
}
