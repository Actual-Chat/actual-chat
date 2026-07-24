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
    private lastSampleAtMs = -1;
    private lastBytes = -1;
    private lastDecoded = -1;
    private lastPresented = -1;
    private presentFrozenMs = 0;
    private decodeFrozenMs = 0;
    private lastSampleHadProgress = false;

    constructor(wedgeAfterMs?: number) {
        this.wedgeAfterMs = wedgeAfterMs ?? DEFAULT_WEDGE_AFTER_MS;
    }

    get hasProgress(): boolean {
        return this.lastSampleHadProgress;
    }

    reset(): void {
        this.lastSampleAtMs = -1;
        this.lastBytes = -1;
        this.lastDecoded = -1;
        this.lastPresented = -1;
        this.presentFrozenMs = 0;
        this.decodeFrozenMs = 0;
        this.lastSampleHadProgress = false;
    }

    onSample(stats: PlayerStats, nowMs: number): WedgeDiagnosis | null {
        const first = this.lastBytes < 0;
        const elapsed = first ? 0 : nowMs - this.lastSampleAtMs;
        const bytesAdvanced = stats.bytesReceived > this.lastBytes;
        const decodedAdvanced = stats.framesDecoded > this.lastDecoded;
        const presentedAdvanced = stats.presented > this.lastPresented;
        this.lastSampleAtMs = nowMs;
        this.lastBytes = stats.bytesReceived;
        this.lastDecoded = stats.framesDecoded;
        this.lastPresented = stats.presented;
        this.lastSampleHadProgress = !first && presentedAdvanced;
        if (first) {
            this.presentFrozenMs = 0;
            this.decodeFrozenMs = 0;
            return null;
        }
        if (presentedAdvanced)
            this.presentFrozenMs = 0;
        else if (bytesAdvanced)
            this.presentFrozenMs += elapsed;
        if (decodedAdvanced)
            this.decodeFrozenMs = 0;
        else if (bytesAdvanced)
            this.decodeFrozenMs += elapsed;

        if (this.presentFrozenMs < this.wedgeAfterMs)
            return null;

        const kind: WedgeKind = this.decodeFrozenMs >= this.wedgeAfterMs ? 'decode-wedge' : 'present-wedge';
        return { kind, frozenMs: this.presentFrozenMs, detail: formatDetail(stats, nowMs) };
    }
}

function age(nowMs: number, atMs: number): string {
    return atMs < 0 ? 'never' : `${((nowMs - atMs) / 1000).toFixed(1)}s`;
}

function formatDetail(s: PlayerStats, nowMs: number): string {
    return `present=${s.presentState} pump=${s.feedPumpState}`
        + ` ages[a=${age(nowMs, s.lastArrivalAtMs)} pl=${age(nowMs, s.lastBufferPullAtMs)}`
        + ` su=${age(nowMs, s.lastSubmitAtMs)} de=${age(nowMs, s.lastDecodeOutAtMs)}`
        + ` pr=${age(nowMs, s.lastPresentAtMs)}]`
        + ` buf=${s.encodedQueueCount} inflight=${s.decoderQueueSize} ready=${s.decodedReadyCount}`
        + ` decoded=${s.framesDecoded} presented=${s.presented}`;
}
