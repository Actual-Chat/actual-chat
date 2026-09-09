export interface DeadPeerSample {
    isPullActive: boolean;
    isWorkerConnected: boolean;
    isMainConnected: boolean;
}

const DEFAULT_DEAD_AFTER_MS = 15_000;

// Detects a player worker whose RPC peer stays down while the main-thread
// peer is up — the state a parked or stopped worker reconnect loop leaves
// behind, where every pull restart waits on a connection that never comes and
// nothing surfaces an error. Pure: feed it liveness-poll samples.
export class DeadPeerDetector {
    private readonly deadAfterMs: number;
    private deadSinceMs = -1;

    constructor(deadAfterMs?: number) {
        this.deadAfterMs = deadAfterMs ?? DEFAULT_DEAD_AFTER_MS;
    }

    reset(): void {
        this.deadSinceMs = -1;
    }

    onSample(sample: DeadPeerSample, nowMs: number): boolean {
        if (!sample.isPullActive || !sample.isMainConnected || sample.isWorkerConnected) {
            this.deadSinceMs = -1;
            return false;
        }
        if (this.deadSinceMs < 0) {
            this.deadSinceMs = nowMs;
            return false;
        }
        if (nowMs - this.deadSinceMs < this.deadAfterMs)
            return false;

        this.deadSinceMs = -1;
        return true;
    }
}
