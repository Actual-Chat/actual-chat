import { count, pipe } from 'ix-ext';
import { describe, it, expect } from 'vitest';
import {
    wireSend,
    type StreamSenderLike,
    type StreamSenderStats,
    type StreamFormat,
    type VideoStreamFrame,
    type VideoStreamFrameBundle,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-send';
import {
    type EncodedBundle,
    type EncodedFrame,
    type RecorderStats,
    createEmptyRecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { LayerLadderController } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import { stampEncoderDescription } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/stamp-encoder-description';

// Default single-layer controller used by every test that doesn't exercise
// hot-apply directly. wireSend reads layer count from the controller; tests
// that need a specific count construct their own.
const defaultLayerConfig: EncoderConfigPerLayer = {
    width: 1920, height: 1080, bitrate: 1_000_000, framerate: 30, codec: 'avc1.640028',
};
const defaultController = (): LayerLadderController => new LayerLadderController([defaultLayerConfig]);
// ---- Mocks ----------------------------------------------------------------

class MockEncodedVideoChunk {
    public closed = false;
    constructor(
        public type: 'key' | 'delta',
        public byteLength: number,
        public duration: number | null = null,
        private readonly bytes: Uint8Array = new Uint8Array(byteLength),
    ) {}
    copyTo(buffer: ArrayBuffer): void {
        new Uint8Array(buffer).set(this.bytes.subarray(0, Math.min(buffer.byteLength, this.bytes.length)));
    }
    close(): void {
        this.closed = true;
    }
}

class FakeSender implements StreamSenderLike {
    /** Flattened per-layer wire frames across all bundles, in send order. */
    public sent: VideoStreamFrame[] = [];
    public sentBundles: VideoStreamFrameBundle[] = [];
    public formats: StreamFormat[] = [];
    public sendCount = 0;
    init(format: StreamFormat): void {
        this.formats.push(format);
    }
    send(bundle: VideoStreamFrameBundle): void {
        this.sentBundles.push(bundle);
        for (const f of bundle.layers) this.sent.push(f);
        this.sendCount++;
    }
}

class ResolvingSender extends FakeSender {
    public disposed = false;
    private resolveDisposed: () => void = () => { /* set by promise ctor */ };
    public readonly whenDisposed = new Promise<void>(resolve => {
        this.resolveDisposed = resolve;
    });
    override send(bundle: VideoStreamFrameBundle): void {
        super.send(bundle);
        if (this.sendCount === 1)
            this.resolveDisposed();
    }
    dispose(): void {
        this.disposed = true;
    }
}

class StatsSender extends FakeSender {
    public stats: StreamSenderStats = {
        addedFrameCount: 0,
        queueDepth: 0,
        maxQueueDepth: 0,
        rpcStreamSkipped: 0,
        floodGateSkipCount: 0,
        lastAckAgeMs: -1,
        isPeerConnected: false,
        ackedBytes: 0,
    };
    override send(bundle: VideoStreamFrameBundle): void {
        super.send(bundle);
        this.stats.addedFrameCount = this.sent.length;
    }
    getStats(): StreamSenderStats {
        return this.stats;
    }
}

interface BuildOpts {
    type?: 'key' | 'delta';
    capturedAt?: { timeMs: number; epoch: number };
    layerId?: number;
    encodedWidth?: number;
    encodedHeight?: number;
    sourceWidth?: number;
    sourceHeight?: number;
    description?: ArrayBuffer | Uint8Array;
    duration?: number | null;
    byteLength?: number;
    bytes?: Uint8Array;
    index?: number;
}

function makeEncoded(stats: RecorderStats, opts: BuildOpts = {}): EncodedFrame {
    const type = opts.type ?? 'delta';
    const byteLength = opts.byteLength ?? 16;
    const bytes = opts.bytes ?? new Uint8Array(Array.from({ length: byteLength }, (_, i) => i & 0xff));
    const chunk = new MockEncodedVideoChunk(type, byteLength, opts.duration ?? null, bytes) as unknown as EncodedVideoChunk;
    const metadata: EncodedVideoChunkMetadata = {};
    if (opts.description) {
        const desc = opts.description instanceof Uint8Array
            ? opts.description.buffer.slice(opts.description.byteOffset, opts.description.byteOffset + opts.description.byteLength)
            : opts.description;
        metadata.decoderConfig = { codec: 'avc1.42E01E', description: desc } as unknown as VideoDecoderConfig;
    }
    return {
        chunk,
        metadata,
        capturedAt: opts.capturedAt ?? { timeMs: 1_000, epoch: 0 },
        index: opts.index ?? 0,
        dropTrace: [],
        layerId: opts.layerId ?? 0,
        sourceWidth: opts.sourceWidth ?? 1920,
        sourceHeight: opts.sourceHeight ?? 1080,
        encodedWidth: opts.encodedWidth ?? 1920,
        encodedHeight: opts.encodedHeight ?? 1080,
        rotation: 0,
        stats,
    };
}

/** One length-1 EncodedBundle per EncodedFrame — preserves the legacy
 *  per-frame test semantics where each frame stood for one source moment. */
function source(items: EncodedFrame[]): AsyncIterable<EncodedBundle> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) {
            yield {
                layers: [item],
                index: item.index,
                dropTrace: [],
                rotation: 0,
                stats: item.stats,
            } as EncodedBundle;
        }
    })();
}

/** Group N EncodedFrames sharing a source moment into one bundle. Used by
 *  multi-layer init tests where the original publisher emits all layers
 *  for one source frame as a single bundle. */
function bundledSource(groups: EncodedFrame[][]): AsyncIterable<EncodedBundle> {
    return (async function* () {
        await Promise.resolve();
        for (const group of groups) {
            if (group.length === 0) continue;
            yield {
                layers: group,
                index: group[0].index,
                dropTrace: [],
                rotation: 0,
                stats: group[0].stats,
            } as EncodedBundle;
        }
    })();
}

async function runWith(
    seg: AsyncIterable<EncodedBundle>,
    op: ReturnType<typeof wireSend>,
): Promise<void> {
    await count(pipe(seg, op));
}

// wireSend reads description from EncodedFrame.description, which the upstream
// stampEncoderDescription operator fills from metadata.decoderConfig.description
// (and per-layer caches for keyframes that arrive without one). Tests that
// assert description forwarding route through that operator first.
async function runStamped(
    seg: AsyncIterable<EncodedBundle>,
    op: ReturnType<typeof wireSend>,
): Promise<void> {
    await count(pipe(seg, stampEncoderDescription(), op));
}

// ---- Tests ----------------------------------------------------------------

describe('wireSend', () => {
    it('first chunk pins captureStartUnixMs; offset is 0', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 12_345.678, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent).toHaveLength(1);
        expect(sender.sent[0].offset).toBe(0);
    });

    it('subsequent chunk offset = (capturedAt.timeMs − captureStartUnixMs) × 1000 µs (× 10 ticks)', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_033, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_066, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        // 33 ms == 33_000 µs == 330_000 ticks (× 10).
        expect(sender.sent.map(d => d.offset)).toEqual([0, 330_000, 660_000]);
    });

    it('offsetEpoch is included on every frame', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 5_000, epoch: 1 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 5_033, epoch: 1 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent[0].offsetEpoch).toBe(0);
        expect(sender.sent[1].offsetEpoch).toBe(1);
        expect(sender.sent[2].offsetEpoch).toBe(1);
    });

    it('forwards stamped per-layer description to the wire DTO, incl. cache fallback', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const desc0 = new Uint8Array([0xAA, 0xBB, 0xCC]);
        const desc1 = new Uint8Array([0x11, 0x22, 0x33, 0x44]);

        const items = [
            // Layer 0 keyframe with description → cache it.
            makeEncoded(stats, { type: 'key', layerId: 0, capturedAt: { timeMs: 1_000, epoch: 0 }, description: desc0 }),
            // Layer 1 keyframe with description → distinct cache entry.
            makeEncoded(stats, { type: 'key', layerId: 1, capturedAt: { timeMs: 1_000, epoch: 0 }, description: desc1 }),
            // Layer 0 keyframe WITHOUT description → fall back to cache.
            makeEncoded(stats, { type: 'key', layerId: 0, capturedAt: { timeMs: 5_000, epoch: 0 } }),
            // Layer 1 keyframe WITHOUT description → fall back to layer-1 cache.
            makeEncoded(stats, { type: 'key', layerId: 1, capturedAt: { timeMs: 5_000, epoch: 0 } }),
        ];
        await runStamped(source(items), sink);

        expect(sender.sent).toHaveLength(4);
        expect(Array.from(sender.sent[0].description!)).toEqual([0xAA, 0xBB, 0xCC]);
        expect(Array.from(sender.sent[1].description!)).toEqual([0x11, 0x22, 0x33, 0x44]);
        expect(Array.from(sender.sent[2].description!)).toEqual([0xAA, 0xBB, 0xCC]);
        expect(Array.from(sender.sent[3].description!)).toEqual([0x11, 0x22, 0x33, 0x44]);
    });

    it('epoch change does not re-anchor captureStartUnixMs', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_100, epoch: 0 } }),
            // Epoch flip — offset timeline stays anchored to the first chunk.
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 9_999, epoch: 1 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 10_050, epoch: 1 } }),
        ];
        await runWith(source(items), sink);

        // Pre-flip: 100 ms × 10_000 ticks/ms = 1_000_000.
        expect(sender.sent[0].offset).toBe(0);
        expect(sender.sent[1].offset).toBe(1_000_000);
        // Post-flip: still anchored to 1_000ms. Epoch tells the receiver to reset anchors.
        expect(sender.sent[2].offset).toBe(89_990_000);
        expect(sender.sent[3].offset).toBe(90_500_000);
    });

    it('keyframe DTOs have keyFrameIndex === index; delta DTOs differ', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'key', index: 0, capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', index: 1, capturedAt: { timeMs: 1_033, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        // IsKeyFrame is derived: keyFrameIndex === index iff this is a keyframe.
        expect(sender.sent[0].keyFrameIndex).toBe(sender.sent[0].index);
        expect(sender.sent[0].index).toBe(0);

        expect(sender.sent[1].keyFrameIndex).not.toBe(sender.sent[1].index);
        // Delta inherits the previous keyframe's index.
        expect(sender.sent[1].keyFrameIndex).toBe(0);
        expect(sender.sent[1].index).toBe(1);
    });

    it('pre-keyframe delta is tagged as non-keyframe via -1 sentinel', async () => {
        // Pool-reused encoders can emit a delta as their first chunk. Without a
        // sentinel, `keyFrameIndex` would fall back to `index` and the receiver
        // would mis-decode it as a keyframe — Chrome then throws "A key frame
        // is required after configure() or flush()".
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender, controller: defaultController() });

        const items = [
            makeEncoded(stats, { type: 'delta', index: 0, capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', index: 1, capturedAt: { timeMs: 1_033, epoch: 0 } }),
            makeEncoded(stats, { type: 'key', index: 2, capturedAt: { timeMs: 1_066, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', index: 3, capturedAt: { timeMs: 1_099, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent[0].keyFrameIndex).toBe(-1);
        expect(sender.sent[0].index).toBe(0);
        expect(sender.sent[1].keyFrameIndex).toBe(-1);
        expect(sender.sent[1].index).toBe(1);
        // Real keyframe arrives at index 2.
        expect(sender.sent[2].keyFrameIndex).toBe(2);
        expect(sender.sent[2].index).toBe(2);
        // Subsequent delta inherits the real keyframe's index.
        expect(sender.sent[3].keyFrameIndex).toBe(2);
        expect(sender.sent[3].index).toBe(3);
    });

    it('createSender is called exactly once across the run', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        let createCount = 0;
        const sink = wireSend({
            createSender: () => {
                createCount++;
                return sender;
            },
            controller: defaultController(),
        });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_033, epoch: 0 } }),
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 9_999, epoch: 1 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 10_050, epoch: 1 } }),
        ];
        await runWith(source(items), sink);

        expect(createCount).toBe(1);
        expect(sender.sendCount).toBe(4);
    });

    it('initializes the sender from the top-layer keyframe while sending bottom-first', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const sink = wireSend({
            createSender: () => sender,
            controller: new LayerLadderController([
                { width: 320, height: 180, bitrate: 250_000, framerate: 30, codec: 'avc1.640028' },
                { width: 640, height: 360, bitrate: 500_000, framerate: 30, codec: 'avc1.640028' },
                { width: 1280, height: 720, bitrate: 1_500_000, framerate: 30, codec: 'avc1.640028' },
            ]),
        });
        const topDescription = new Uint8Array([0x67, 0x42, 0x00, 0x1f]);

        // One bundle carrying all three layers — wireSend takes the top
        // (frames[length-1]) for the init payload.
        await runStamped(bundledSource([[
            makeEncoded(stats, {
                type: 'key',
                layerId: 0,
                encodedWidth: 320,
                encodedHeight: 180,
                capturedAt: { timeMs: 1_000, epoch: 0 },
            }),
            makeEncoded(stats, {
                type: 'key',
                layerId: 1,
                encodedWidth: 640,
                encodedHeight: 360,
                capturedAt: { timeMs: 1_000, epoch: 0 },
            }),
            makeEncoded(stats, {
                type: 'key',
                layerId: 2,
                encodedWidth: 1280,
                encodedHeight: 720,
                capturedAt: { timeMs: 1_000, epoch: 0 },
                description: topDescription,
            }),
        ]]), sink);

        expect(sender.sent.map(x => x.layerId)).toEqual([0, 1, 2]);
        expect(sender.formats).toHaveLength(1);
        expect(sender.formats[0].width).toBe(1280);
        expect(sender.formats[0].height).toBe(720);
        expect(sender.formats[0].codecSettings).not.toBe('');
    });

    // Ensure the helper is referenced so unused-import lints don't fire if we pare down later.
    it('runs cleanly with no items (empty stream)', async () => {
        const sender = new FakeSender();
        const op = wireSend({ createSender: () => sender, controller: defaultController() });
        await runWith(source([]), op);
        expect(sender.sent).toEqual([]);
    });

    it('closes every encoded chunk after serializing it', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_033, epoch: 0 } }),
        ];

        await runWith(source(items), wireSend({ createSender: () => sender, controller: defaultController() }));

        expect(items.map(x => (x.chunk as unknown as MockEncodedVideoChunk).closed)).toEqual([true, true]);
    });

    it('copies sender and RpcStream drop counters into recording stats', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new StatsSender();
        sender.stats.rpcStreamSkipped = 3;
        sender.stats.lastAckAgeMs = 123;
        sender.stats.isPeerConnected = true;

        await runWith(source([
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
        ]), wireSend({ createSender: () => sender, controller: defaultController() }));

        expect(stats.bundlesShipped).toBe(1);
        expect(stats.wireLastAckAgeMs).toBe(123);
        expect(stats.isPeerConnected).toBe(true);
    });

    it('stats: wireQueueDepthEma tracks senderStats.queueDepth per bundle', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new StatsSender();
        sender.stats.isPeerConnected = true;

        const items: EncodedFrame[] = [];
        // Bump queueDepth seen by getStats between sends so the EMA blends.
        let q = 5;
        const customSender = new (class extends StatsSender {
            getStats(): StreamSenderStats {
                this.stats.queueDepth = q;
                return this.stats;
            }
        })();
        for (let i = 0; i < 3; i++)
            items.push(makeEncoded(stats, {
                type: i === 0 ? 'key' : 'delta',
                capturedAt: { timeMs: 1_000 + i * 33, epoch: 0 },
            }));

        // Cycle queue depths: 5, 5, 5 → EMA = 5.
        await runWith(source(items), wireSend({ createSender: () => customSender, controller: defaultController() }));
        expect(stats.wireQueueDepthEma).toBeCloseTo(5, 6);

        // New run with depth 10 every bundle: EMA seeds at 10.
        const stats2 = createEmptyRecorderStats();
        q = 10;
        const sender2 = new (class extends StatsSender {
            getStats(): StreamSenderStats {
                this.stats.queueDepth = q;
                return this.stats;
            }
        })();
        const items2: EncodedFrame[] = [
            makeEncoded(stats2, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
        ];
        await runWith(source(items2), wireSend({ createSender: () => sender2, controller: defaultController() }));
        expect(stats2.wireQueueDepthEma).toBe(10);

        void sender; // keep variable in scope to avoid unused-var lint
    });

    it('stats: peerReconnectStreak bumps on connected→disconnected transitions', async () => {
        const stats = createEmptyRecorderStats();
        let connected = true;
        const sender = new (class extends StatsSender {
            getStats(): StreamSenderStats {
                this.stats.isPeerConnected = connected;
                return this.stats;
            }
        })();
        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_033, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_066, epoch: 0 } }),
        ];
        const sourceWithFlips = (async function* (): AsyncIterable<EncodedBundle> {
            await Promise.resolve();
            yield { layers: [items[0]], index: 0, dropTrace: [], rotation: 0, stats };
            connected = false;
            yield { layers: [items[1]], index: 1, dropTrace: [], rotation: 0, stats };
            yield { layers: [items[2]], index: 2, dropTrace: [], rotation: 0, stats };
        })();
        await runWith(sourceWithFlips, wireSend({ createSender: () => sender, controller: defaultController() }));
        // Streak = 1 from one true→false transition. Subsequent false samples
        // don't bump (we count transitions, not states).
        expect(stats.peerReconnectStreak).toBe(1);
    });

    it('recreates the sender if the wire pump completes while capture continues', async () => {
        const stats = createEmptyRecorderStats();
        const senders: ResolvingSender[] = [];
        const sink = wireSend({
            createSender: () => {
                const sender = new ResolvingSender();
                senders.push(sender);
                return sender;
            },
            controller: defaultController(),
        });

        await runWith(source([
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_033, epoch: 0 } }),
        ]), sink);

        expect(senders).toHaveLength(2);
        expect(senders[0].sent).toHaveLength(1);
        expect(senders[1].sent).toHaveLength(1);
        expect(senders.every(s => s.disposed)).toBe(true);
    });

    it('controller-driven layerCount stamps current count per bundle', async () => {
        const stats = createEmptyRecorderStats();
        const sender = new FakeSender();
        const cfg = (w: number, h: number): EncoderConfigPerLayer => ({
            width: w, height: h, bitrate: 500_000, framerate: 30, codec: 'avc1.640028',
        });
        const controller = new LayerLadderController([cfg(640, 360)]);
        const sink = wireSend({ createSender: () => sender, controller });

        // Source bundle 1: one layer; bundle 2: same; reconfigure before bundle 3
        // by setting controller to 2 layers; bundle 3 carries two encoded frames.
        async function* src(): AsyncIterable<EncodedBundle> {
            await Promise.resolve();
            yield {
                layers: [makeEncoded(stats, { type: 'key', layerId: 0, capturedAt: { timeMs: 1_000, epoch: 0 } })],
                index: 1, dropTrace: [], rotation: 0, stats,
            };
            yield {
                layers: [makeEncoded(stats, { type: 'delta', layerId: 0, capturedAt: { timeMs: 1_033, epoch: 0 } })],
                index: 2, dropTrace: [], rotation: 0, stats,
            };
            controller.setConfigs([cfg(640, 360), cfg(1280, 720)]);
            yield {
                layers: [
                    makeEncoded(stats, { type: 'key', layerId: 0, capturedAt: { timeMs: 1_066, epoch: 0 } }),
                    makeEncoded(stats, { type: 'key', layerId: 1, capturedAt: { timeMs: 1_066, epoch: 0 } }),
                ],
                index: 3, dropTrace: [], rotation: 0, stats,
            };
        }
        await runWith(src(), sink);

        // 1+1+2 = 4 wire layers total
        expect(sender.sent).toHaveLength(4);
        expect(sender.sent[0].layerCount).toBe(1);
        expect(sender.sent[1].layerCount).toBe(1);
        expect(sender.sent[2].layerCount).toBe(2);
        expect(sender.sent[3].layerCount).toBe(2);
    });
});
