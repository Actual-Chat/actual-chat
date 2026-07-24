import type { PlayerStats } from '../frame-envelopes';

export type WedgeKind = 'decode-wedge' | 'present-wedge';

export interface WedgeDiagnosis {
    kind: WedgeKind;
    frozenMs: number;
    detail: string;
}

const DEFAULT_WEDGE_AFTER_MS = 6_000;

// Detects "frames keep arriving but decode/present froze" — the silent state no
// pipeline-internal watchdog covers (arrival resets the stream stall timer, the
// decoder hang watchdog needs in-flight chunks, present has no watchdog). Pure:
// feed it getStats() snapshots; deltas only, so clock domains don't matter.
export class WedgeDetector {
    private readonly wedgeAfterMs: number;
    private lastBytes = -1;
    private lastDecoded = -1;
    private lastPresented = -1;
    private presentFrozenSinceMs: number | null = null;
    private decodeFrozenSinceMs: number | null = null;
    private lastSampleHadProgress = false;

    constructor(wedgeAfterMs?: number) {
        this.wedgeAfterMs = wedgeAfterMs ?? DEFAULT_WEDGE_AFTER_MS;
    }

    get hasProgress(): boolean {
        return this.lastSampleHadProgress;
    }

    reset(): void {
        this.lastBytes = -1;
        this.lastDecoded = -1;
        this.lastPresented = -1;
        this.presentFrozenSinceMs = null;
        this.decodeFrozenSinceMs = null;
        this.lastSampleHadProgress = false;
    }

    onSample(stats: PlayerStats, nowMs: number): WedgeDiagnosis | null {
        const first = this.lastBytes < 0;
        const bytesAdvanced = stats.bytesReceived > this.lastBytes;
        const decodedAdvanced = stats.framesDecoded > this.lastDecoded;
        const presentedAdvanced = stats.presented > this.lastPresented;
        this.lastBytes = stats.bytesReceived;
        this.lastDecoded = stats.framesDecoded;
        this.lastPresented = stats.presented;
        this.lastSampleHadProgress = !first && presentedAdvanced;
        if (first) {
            this.presentFrozenSinceMs = nowMs;
            this.decodeFrozenSinceMs = nowMs;
            return null;
        }
        if (presentedAdvanced)
            this.presentFrozenSinceMs = nowMs;
        if (decodedAdvanced)
            this.decodeFrozenSinceMs = nowMs;
        if (!bytesAdvanced)
            return null;

        const presentFrozenMs = nowMs - (this.presentFrozenSinceMs ?? nowMs);
        if (presentFrozenMs < this.wedgeAfterMs)
            return null;

        const decodeFrozenMs = nowMs - (this.decodeFrozenSinceMs ?? nowMs);
        const kind: WedgeKind = decodeFrozenMs >= this.wedgeAfterMs ? 'decode-wedge' : 'present-wedge';
        return { kind, frozenMs: presentFrozenMs, detail: formatDetail(stats, nowMs) };
    }
}

function age(nowMs: number, atMs: number): string {
    return atMs < 0 ? 'never' : `${((nowMs - atMs) / 1000).toFixed(1)}s`;
}

function formatDetail(s: PlayerStats, nowMs: number): string {
    return `present=${s.presentState} pump=${s.feedPumpState}`
        + ` ages[arr=${age(nowMs, s.lastArrivalAtMs)} pull=${age(nowMs, s.lastBufferPullAtMs)}`
        + ` sub=${age(nowMs, s.lastSubmitAtMs)} dec=${age(nowMs, s.lastDecodeOutAtMs)}`
        + ` presAtt=${age(nowMs, s.lastPresentAttemptAtMs)} pres=${age(nowMs, s.lastPresentAtMs)}]`
        + ` buf=${s.encodedQueueCount} inflight=${s.decoderQueueSize} ready=${s.decodedReadyCount}`
        + ` decoded=${s.framesDecoded} presented=${s.presented}`;
}
