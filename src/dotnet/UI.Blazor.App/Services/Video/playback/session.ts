import { MonotonicClock } from 'clocks';
import { DecoderPool, type DecoderPoolOptions } from './decoder-pool';

export interface PlaybackSessionOptions {
    createArrivalClock?: () => MonotonicClock;
    decoderPool?: DecoderPoolOptions;
}

// Session-level state shared across every concurrent playback pipeline
// inside a single playback worker. Owns the receiver-side MonotonicClock
// (so cross-stream latency comparisons are coherent) and the DecoderPool
// (so codec switches across stream restarts can reuse hardware decoder
// slots). Per-stream PlayerStats live on each Player.
export class PlaybackSession {
    readonly arrivalClock: MonotonicClock;
    readonly decoderPool: DecoderPool;
    private activeStreamCount = 0;
    private disposed = false;

    constructor(opts: PlaybackSessionOptions = {}) {
        this.arrivalClock = (opts.createArrivalClock ?? (() => new MonotonicClock()))();
        this.decoderPool = new DecoderPool(opts.decoderPool);
    }

    get activeStreams(): number {
        return this.activeStreamCount;
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.decoderPool.dispose();
    }

    registerStream(): void {
        this.activeStreamCount++;
    }

    // Floors at zero in case of a stray double-unregister.
    unregisterStream(): void {
        if (this.activeStreamCount > 0) {
            this.activeStreamCount--;
        }
    }

    isDisposed(): boolean {
        return this.disposed;
    }
}
