// Hot-path-friendly metrics aggregation.
//
// Previous implementation kept four `Map<string, …>` instances and built
// template-literal keys on every `recordReceived`/`recordSent` call. At
// ~4.4k delivered frames/sec across 300 consumers that showed up in the
// profile as ~1.6% self time + non-trivial GC pressure (the per-frame
// string allocations). The storage layout here is a pair of typed arrays
// indexed by a precomputed integer key, so the inner loop never allocates.
//
// Keys:
//   sentIdx(chatIdx, prodIdx, frameIdx)
//     = (chatIdx * streamsPerChat + prodIdx) * maxFramesPerStream + frameIdx
//   consumerIdx(chatIdx, consIdx, streamIdx)
//     = (chatIdx * consumersPerChat + consIdx) * streamsPerChat + streamIdx
//
// Frame index is recovered from VideoFrame.Offset / FrameDurationTicks on
// the receive side, so producers don't have to thread an index through the
// stream sender — only timing anchors are actually on the wire.

import { int64ToNumber, type Int64 } from './service-defs.js';

export interface MetricsConfig {
    chatCount: number;
    streamsPerChat: number;
    consumersPerChat: number;
    /** Upper bound on frames a single producer will emit in this run.
     *  Used to size the sent-timestamp typed array. */
    maxFramesPerStream: number;
    /** Ticks per frame (.NET TimeSpan ticks, 100 ns units). Used to
     *  recover frameIdx from a frame's Offset on the receive side. */
    frameDurationTicks: number;
    /** Upper bound on frames a consumer will receive — sized slightly
     *  larger than maxFramesPerStream to avoid boundary drops. */
    maxLatencySamplesPerConsumer: number;
}

/** Sentinel for "this frame was never recorded as sent". */
const NOT_SENT = 0;

export class Metrics {
    private readonly cfg: MetricsConfig;

    /** Flat Float64Array keyed by sentIdx → wall-clock send time (ms).
     *  `NOT_SENT` (0) means no producer recorded a send for that slot. */
    private readonly _sentAt: Float64Array;

    /** Per-consumer frame counter — Uint32Array keyed by consumerIdx. */
    private readonly _framesReceived: Uint32Array;
    /** Per-consumer byte counter — Float64Array (sum may exceed 2^32). */
    private readonly _bytesReceived: Float64Array;
    /** Per-consumer latency sample ring buffers.
     *  `_latencies[consumerIdx]` is a preallocated `Float64Array`,
     *  `_latencyCounts[consumerIdx]` is how many samples are in it.
     *  Once full the sample is dropped (typical runs stay well below). */
    private readonly _latencies: Float64Array[];
    private readonly _latencyCounts: Uint32Array;

    constructor(cfg: MetricsConfig) {
        this.cfg = cfg;
        const totalStreams = cfg.chatCount * cfg.streamsPerChat;
        const totalConsumers = cfg.chatCount * cfg.consumersPerChat * cfg.streamsPerChat;

        this._sentAt = new Float64Array(totalStreams * cfg.maxFramesPerStream);
        this._framesReceived = new Uint32Array(totalConsumers);
        this._bytesReceived = new Float64Array(totalConsumers);
        this._latencyCounts = new Uint32Array(totalConsumers);
        this._latencies = new Array<Float64Array>(totalConsumers);
        for (let i = 0; i < totalConsumers; i++)
            this._latencies[i] = new Float64Array(cfg.maxLatencySamplesPerConsumer);
    }

    // --- Hot-path producer API ---

    recordSent(chatIdx: number, prodIdx: number, offsetTicks: Int64, now = Date.now()): void {
        const frameIdx = (int64ToNumber(offsetTicks) / this.cfg.frameDurationTicks) | 0;
        const idx = (chatIdx * this.cfg.streamsPerChat + prodIdx) * this.cfg.maxFramesPerStream + frameIdx;
        if (idx >= 0 && idx < this._sentAt.length)
            this._sentAt[idx] = now;
    }

    // --- Hot-path consumer API ---

    recordReceived(
        chatIdx: number,
        consIdx: number,
        streamIdx: number,
        offsetTicks: Int64,
        byteSize: number,
    ): void {
        const ci = (chatIdx * this.cfg.consumersPerChat + consIdx) * this.cfg.streamsPerChat + streamIdx;
        this._framesReceived[ci] = this._framesReceived[ci] + 1;
        this._bytesReceived[ci] = this._bytesReceived[ci] + byteSize;

        const frameIdx = (int64ToNumber(offsetTicks) / this.cfg.frameDurationTicks) | 0;
        const sentIdx = (chatIdx * this.cfg.streamsPerChat + streamIdx) * this.cfg.maxFramesPerStream + frameIdx;
        if (sentIdx >= 0 && sentIdx < this._sentAt.length) {
            const sentAt = this._sentAt[sentIdx];
            if (sentAt !== NOT_SENT) {
                const latencyMs = Date.now() - sentAt;
                const count = this._latencyCounts[ci];
                if (count < this._latencies[ci].length) {
                    this._latencies[ci][count] = latencyMs;
                    this._latencyCounts[ci] = count + 1;
                }
            }
        }
    }

    // --- Report-time read API (matches the old Map-based shape) ---

    getFramesReceived(chatIdx: number, consIdx: number, streamIdx: number): number {
        const ci = (chatIdx * this.cfg.consumersPerChat + consIdx) * this.cfg.streamsPerChat + streamIdx;
        return this._framesReceived[ci];
    }
    getBytesReceived(chatIdx: number, consIdx: number, streamIdx: number): number {
        const ci = (chatIdx * this.cfg.consumersPerChat + consIdx) * this.cfg.streamsPerChat + streamIdx;
        return this._bytesReceived[ci];
    }
    /** Returns a non-owning view of the latency samples for this consumer. */
    getLatencies(chatIdx: number, consIdx: number, streamIdx: number): Float64Array {
        const ci = (chatIdx * this.cfg.consumersPerChat + consIdx) * this.cfg.streamsPerChat + streamIdx;
        return this._latencies[ci].subarray(0, this._latencyCounts[ci]);
    }
    /** Aggregate counters for the whole run. */
    totals(): { frames: number; bytes: number } {
        let frames = 0;
        let bytes = 0;
        for (let i = 0; i < this._framesReceived.length; i++) {
            frames += this._framesReceived[i];
            bytes += this._bytesReceived[i];
        }
        return { frames, bytes };
    }
    /** Flat concatenation of all consumers' latency samples — used by the
     *  aggregate report row. Allocates once at report time, not per frame. */
    allLatencies(): Float64Array {
        let total = 0;
        for (const n of this._latencyCounts) total += n;
        const out = new Float64Array(total);
        let pos = 0;
        for (let i = 0; i < this._latencies.length; i++) {
            const n = this._latencyCounts[i];
            out.set(this._latencies[i].subarray(0, n), pos);
            pos += n;
        }
        return out;
    }
}

/** Percentile on a numeric sample set. Accepts either a plain array or
 *  a typed array — typed arrays are sorted in place via `.sort()`, so
 *  pass a `.slice()` if you need to preserve the original order. */
export function percentile(values: ArrayLike<number>, p: number): number {
    if (values.length === 0) return 0;
    // Clone to a fresh array to avoid mutating the caller's data.
    // For very large sample counts we could sort in place, but at typical
    // run sizes (< 30k samples) this is a single millisecond at most.
    const sorted = Array.from(values).sort((a, b) => a - b);
    const index = Math.max(0, Math.ceil(p * sorted.length) - 1);
    return sorted[index];
}

export interface ReportInputs {
    mode: string;
    durationSec: number;
    chatCount: number;
    streamsPerChat: number;
    consumersPerChat: number;
    metrics: Metrics;
}

export function printReport(r: ReportInputs): void {
    const totalStreams = r.chatCount * r.streamsPerChat;
    const pullsPerChat = r.consumersPerChat * (r.streamsPerChat - 1);
    const totalPulls = r.chatCount * pullsPerChat;

    console.log();
    console.log('=== VIDEO LOAD TEST RESULTS ===');
    console.log(`Mode: ${r.mode}`);
    console.log(
        `Duration: ${r.durationSec}s, Chats: ${r.chatCount}, ` +
        `Streams/chat: ${r.streamsPerChat}, Consumers/chat: ${r.consumersPerChat}`);
    console.log(`Total streams: ${totalStreams}, Total pulls: ${totalPulls}`);
    console.log();

    // Per-chat summary
    console.log('--- Per-Chat Summary ---');
    console.log(`${'Chat'.padEnd(6)}${'Frames'.padEnd(10)}${'MB/s'.padEnd(8)}` +
                `${'p50ms'.padEnd(8)}${'p95ms'.padEnd(8)}${'p99ms'.padEnd(8)}`);
    for (let ci = 0; ci < r.chatCount; ci++) {
        const chatLat: number[] = [];
        let chatFrames = 0;
        let chatBytes = 0;
        for (let cons = 0; cons < r.consumersPerChat; cons++) {
            for (let si = 0; si < r.streamsPerChat; si++) {
                if (si === cons) continue;
                chatFrames += r.metrics.getFramesReceived(ci, cons, si);
                chatBytes += r.metrics.getBytesReceived(ci, cons, si);
                const bag = r.metrics.getLatencies(ci, cons, si);
                for (const v of bag) chatLat.push(v);
            }
        }
        const mbps = chatBytes / (1024 * 1024) / r.durationSec;
        console.log(
            `${String(ci).padEnd(6)}${String(chatFrames).padEnd(10)}${mbps.toFixed(2).padEnd(8)}` +
            percentile(chatLat, 0.5).toFixed(1).padEnd(8) +
            percentile(chatLat, 0.95).toFixed(1).padEnd(8) +
            percentile(chatLat, 0.99).toFixed(1).padEnd(8));
    }

    // Aggregate
    const { frames: aggFrames, bytes: aggBytes } = r.metrics.totals();
    const aggLat = r.metrics.allLatencies();

    const aggMbps = aggBytes / (1024 * 1024) / r.durationSec;
    console.log();
    console.log('--- Aggregate ---');
    console.log(`Total frames received: ${aggFrames}`);
    console.log(`Total bytes: ${aggBytes.toLocaleString('en-US')} (${aggMbps.toFixed(2)} MB/s)`);
    if (aggLat.length > 0) {
        console.log(
            `Latency p50=${percentile(aggLat, 0.5).toFixed(1)}ms, ` +
            `p95=${percentile(aggLat, 0.95).toFixed(1)}ms, ` +
            `p99=${percentile(aggLat, 0.99).toFixed(1)}ms`);
    }
    const expectedFramesPerPull = r.durationSec * 30;
    const expectedTotal = expectedFramesPerPull * totalPulls;
    const pct = expectedTotal > 0 ? (100 * aggFrames / expectedTotal).toFixed(1) : '0.0';
    console.log(`Expected ~${expectedTotal} total frames, got ${aggFrames} (${pct}%)`);
}
