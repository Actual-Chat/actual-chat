import { MonotonicClock } from 'clocks';
import {
    createEmptyPlaybackStats,
    type VideoPlaybackStats,
} from '../frame-envelopes';
import { DecoderPool, type DecoderPoolOptions } from './decoder-pool';

export interface PlaybackSessionOptions {
    createArrivalClock?: () => MonotonicClock;
    decoderPool?: DecoderPoolOptions;
}

// Session-level state shared across every concurrent playback
// pipeline inside a single playback worker. Owns the receiver-side
// MonotonicClock (so cross-stream latency comparisons are coherent),
// the DecoderPool (so codec switches across stream restarts can reuse
// hardware decoder slots), and the aggregated VideoPlaybackStats.
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

    // Resets counters but preserves activeStreams (currently-running
    // pipes keep contributing). Decoder pool is left alone — its TTL
    // governs decoder lifetime independently of session resets.
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

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.decoderPool.dispose();
    }

    registerStream(): void {
        this.stats.activeStreams++;
    }

    // Floors at zero in case of a stray double-unregister.
    unregisterStream(): void {
        if (this.stats.activeStreams > 0) {
            this.stats.activeStreams--;
        }
    }

    isDisposed(): boolean {
        return this.disposed;
    }
}
