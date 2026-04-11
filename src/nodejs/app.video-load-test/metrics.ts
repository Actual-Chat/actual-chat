// Metrics aggregation mirrored from the C# harness — per-frame latencies and
// throughput are keyed by (chatIdx, consumerIdx, streamIdx) so the aggregate
// report looks the same as the .NET one.

export type SentKey = string; // "chat|prod|offset"
export type ConsumerKey = string; // "chat|cons|stream"

function sentKey(chatIdx: number, prodIdx: number, offsetTicks: number): SentKey {
    return `${chatIdx}|${prodIdx}|${offsetTicks}`;
}

function consumerKey(chatIdx: number, consIdx: number, streamIdx: number): ConsumerKey {
    return `${chatIdx}|${consIdx}|${streamIdx}`;
}

export class Metrics {
    /** Per-frame send timestamps (wall-clock ms) tagged at producer. */
    readonly sent = new Map<SentKey, number>();
    /** Per-consumer frame count. */
    readonly framesReceived = new Map<ConsumerKey, number>();
    /** Per-consumer byte count (uses the MessagePack size for SignalR, or the
     *  decoded Data.byteLength for RPC). Matches the C# accounting. */
    readonly bytesReceived = new Map<ConsumerKey, number>();
    /** Per-consumer latency samples in milliseconds. */
    readonly latencies = new Map<ConsumerKey, number[]>();

    recordSent(chatIdx: number, prodIdx: number, offsetTicks: number, now = Date.now()): void {
        this.sent.set(sentKey(chatIdx, prodIdx, offsetTicks), now);
    }

    recordReceived(
        chatIdx: number,
        consIdx: number,
        streamIdx: number,
        offsetTicks: number,
        byteSize: number,
    ): void {
        const ck = consumerKey(chatIdx, consIdx, streamIdx);
        this.framesReceived.set(ck, (this.framesReceived.get(ck) ?? 0) + 1);
        this.bytesReceived.set(ck, (this.bytesReceived.get(ck) ?? 0) + byteSize);

        const sk = sentKey(chatIdx, streamIdx, offsetTicks);
        const sentAt = this.sent.get(sk);
        if (sentAt !== undefined) {
            const latencyMs = Date.now() - sentAt;
            let bag = this.latencies.get(ck);
            if (!bag) { bag = []; this.latencies.set(ck, bag); }
            bag.push(latencyMs);
        }
    }

    initConsumer(chatIdx: number, consIdx: number, streamIdx: number): void {
        const ck = consumerKey(chatIdx, consIdx, streamIdx);
        this.framesReceived.set(ck, 0);
        this.bytesReceived.set(ck, 0);
        this.latencies.set(ck, []);
    }
}

export function percentile(values: number[], p: number): number {
    if (values.length === 0) return 0;
    const sorted = values.slice().sort((a, b) => a - b);
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
                const ck = `${ci}|${cons}|${si}`;
                chatFrames += r.metrics.framesReceived.get(ck) ?? 0;
                chatBytes += r.metrics.bytesReceived.get(ck) ?? 0;
                const bag = r.metrics.latencies.get(ck);
                if (bag) chatLat.push(...bag);
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
    let aggFrames = 0;
    let aggBytes = 0;
    const aggLat: number[] = [];
    for (const v of r.metrics.framesReceived.values()) aggFrames += v;
    for (const v of r.metrics.bytesReceived.values()) aggBytes += v;
    for (const v of r.metrics.latencies.values()) aggLat.push(...v);

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
