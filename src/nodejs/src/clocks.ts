export type MonotonicTime = Readonly<{
    /** Synthetic monotonic wall-clock time, in milliseconds. */
    timeMs: number;
    /** Bumped every time the clock detects drift and resyncs. */
    epoch: number;
}>;

export type MonotonicClockOptions = Readonly<{
    /** Drift between Date.now() and performance.now() deltas that triggers resync. */
    driftThresholdMs?: number;
    /** Minimum forward progress when monotonic clamping is needed. */
    minTickMs?: number;

    /**
     * If true, resync may jump forward to Date.now(). Good for sleep-aware behavior.
     * If false, resync preserves continuity and ignores wall-clock jumps.
     */
    acceptForwardWallClockJumps?: boolean;
}>;

export class MonotonicClock {
    private readonly driftThresholdMs: number;
    private readonly minTickMs: number;
    private readonly acceptForwardWallClockJumps: boolean;

    private originWallMs: number;
    private originPerfMs: number;

    private lastWallMs: number;
    private lastPerfMs: number;
    private lastTimeMs: number;

    private epochValue = 0;

    constructor(options: MonotonicClockOptions = {}) {
        this.driftThresholdMs = options.driftThresholdMs ?? 500;
        this.minTickMs = options.minTickMs ?? 0.0;
        this.acceptForwardWallClockJumps = options.acceptForwardWallClockJumps ?? true;

        const wall = Date.now();
        const perf = performance.now();

        this.originWallMs = wall;
        this.originPerfMs = perf;

        this.lastWallMs = wall;
        this.lastPerfMs = perf;
        this.lastTimeMs = wall;
    }

    get epoch(): number {
        return this.epochValue;
    }

    now(): MonotonicTime {
        const wall = Date.now();
        const perf = performance.now();

        const wallDelta = wall - this.lastWallMs;
        const perfDelta = perf - this.lastPerfMs;
        const drift = wallDelta - perfDelta;

        if (Math.abs(drift) >= this.driftThresholdMs) {
            this.resync(wall, perf);
        }

        let timeMs = this.originWallMs + (perf - this.originPerfMs);

        if (timeMs <= this.lastTimeMs) {
            timeMs = this.lastTimeMs + this.minTickMs;
        }

        this.lastTimeMs = timeMs;
        this.lastWallMs = wall;
        this.lastPerfMs = perf;

        return {
            timeMs,
            epoch: this.epochValue,
        };
    }

    nowDate(): Readonly<{
        date: Date;
        timeMs: number;
        epoch: number;
    }> {
        const snapshot = this.now();
        return {
            date: new Date(snapshot.timeMs),
            timeMs: snapshot.timeMs,
            epoch: snapshot.epoch,
        };
    }

    private resync(wall: number, perf: number): void {
        this.epochValue++;

        this.originWallMs = this.acceptForwardWallClockJumps
            ? Math.max(wall, this.lastTimeMs + this.minTickMs)
            : this.lastTimeMs + this.minTickMs;
        this.originPerfMs = perf;
    }
}

// Offset in ms: serverNow ≈ Date.now() + offset
let offsetMs = 0;
let epoch = 0;
type OffsetListener = (offsetMs: number, epoch: number) => void;
const listeners: OffsetListener[] = [];

export class ServerClock {
    static get offsetMs(): number { return offsetMs; }

    /** Bumped when the offset moved by a confirmed clock step rather than a small
     *  refinement. Holders of absolute anchors (A/V sync, presentation lag) must
     *  rebase when it changes — a slewed refinement never bumps it. */
    static get epoch(): number { return epoch; }

    /** Returns server-aligned epoch ms */
    static now(): number { return Date.now() + offsetMs; }

    /** Accepts the current server time (epoch ms); same-realm callers only —
     *  converting an absolute timestamp across a realm/interop boundary skews
     *  the offset by the delivery delay (see SharedSettings.setServerClockOffsetMs). */
    static updateOffset(serverNowMs: number, newEpoch: number = epoch): void {
        offsetMs = serverNowMs - Date.now();
        epoch = newEpoch;
        for (const fn of listeners) fn(offsetMs, epoch);
    }

    /** Subscribe to offset changes. Returns unsubscribe function. */
    static onOffsetChanged(fn: OffsetListener): () => void {
        listeners.push(fn);
        return () => {
            const idx = listeners.indexOf(fn);
            if (idx >= 0) listeners.splice(idx, 1);
        };
    }
}
