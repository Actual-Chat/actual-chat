import { count, pipe } from 'ix-ext';
import { describe, it, expect } from 'vitest';
import {
    wireSend,
    type StreamSenderLike,
    type StreamFormat,
    type VideoStreamFrame,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-send';
import {
    type EncodedFrame,
    type VideoRecordingStats,
    createEmptyRecordingStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
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
    public sent: VideoStreamFrame[] = [];
    public formats: StreamFormat[] = [];
    public sendCount = 0;
    init(format: StreamFormat): void {
        this.formats.push(format);
    }
    send(dto: VideoStreamFrame): void {
        this.sent.push(dto);
        this.sendCount++;
    }
}

interface BuildOpts {
    type?: 'key' | 'delta';
    capturedAt?: { timeMs: number; epoch: number };
    spatialLayerId?: number;
    encodedWidth?: number;
    encodedHeight?: number;
    sourceWidth?: number;
    sourceHeight?: number;
    description?: ArrayBuffer | Uint8Array;
    duration?: number | null;
    byteLength?: number;
    bytes?: Uint8Array;
    temporalLayerId?: number;
    index?: number;
}

function makeEncoded(stats: VideoRecordingStats, opts: BuildOpts = {}): EncodedFrame {
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
    if (opts.temporalLayerId !== undefined)
        (metadata as { temporalLayerId?: number }).temporalLayerId = opts.temporalLayerId;
    return {
        chunk,
        metadata,
        capturedAt: opts.capturedAt ?? { timeMs: 1_000, epoch: 0 },
        index: opts.index ?? 0,
        spatialLayerId: opts.spatialLayerId ?? 0,
        sourceWidth: opts.sourceWidth ?? 1920,
        sourceHeight: opts.sourceHeight ?? 1080,
        encodedWidth: opts.encodedWidth ?? 1920,
        encodedHeight: opts.encodedHeight ?? 1080,
        stats,
    };
}

function source(items: EncodedFrame[]): AsyncIterable<EncodedFrame> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

async function runWith(
    seg: AsyncIterable<EncodedFrame>,
    op: ReturnType<typeof wireSend>,
): Promise<void> {
    await count(pipe(seg, op));
}

// ---- Tests ----------------------------------------------------------------

describe('wireSend', () => {
    it('first chunk pins captureStartUnixMs; offset is 0', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 12_345.678, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent).toHaveLength(1);
        expect(sender.sent[0].offset).toBe(0);
    });

    it('subsequent chunk offset = (capturedAt.timeMs − captureStartUnixMs) × 1000 µs (× 10 ticks)', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

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
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

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

    it('per-layer description cache: first keyframe with description sets cache; later keyframe without picks from it', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

        const desc0 = new Uint8Array([0xAA, 0xBB, 0xCC]);
        const desc1 = new Uint8Array([0x11, 0x22, 0x33, 0x44]);

        const items = [
            // Layer 0 keyframe with description → cache it.
            makeEncoded(stats, { type: 'key', spatialLayerId: 0, capturedAt: { timeMs: 1_000, epoch: 0 }, description: desc0 }),
            // Layer 1 keyframe with description → distinct cache entry.
            makeEncoded(stats, { type: 'key', spatialLayerId: 1, capturedAt: { timeMs: 1_000, epoch: 0 }, description: desc1 }),
            // Layer 0 keyframe WITHOUT description → fall back to cache.
            makeEncoded(stats, { type: 'key', spatialLayerId: 0, capturedAt: { timeMs: 5_000, epoch: 0 } }),
            // Layer 1 keyframe WITHOUT description → fall back to layer-1 cache.
            makeEncoded(stats, { type: 'key', spatialLayerId: 1, capturedAt: { timeMs: 5_000, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent).toHaveLength(4);
        expect(Array.from(sender.sent[0].description!)).toEqual([0xAA, 0xBB, 0xCC]);
        expect(Array.from(sender.sent[1].description!)).toEqual([0x11, 0x22, 0x33, 0x44]);
        expect(Array.from(sender.sent[2].description!)).toEqual([0xAA, 0xBB, 0xCC]);
        expect(Array.from(sender.sent[3].description!)).toEqual([0x11, 0x22, 0x33, 0x44]);
    });

    it('epoch change does not re-anchor captureStartUnixMs', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

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

    it('keyframe DTOs carry sourceWidth/Height; delta DTOs do not', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({ createSender: () => sender });

        const items = [
            makeEncoded(stats, { type: 'key', sourceWidth: 1920, sourceHeight: 1080, capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', sourceWidth: 1920, sourceHeight: 1080, capturedAt: { timeMs: 1_033, epoch: 0 } }),
        ];
        await runWith(source(items), sink);

        expect(sender.sent[0].isKeyFrame).toBe(true);
        expect(sender.sent[0].sourceWidth).toBe(1920);
        expect(sender.sent[0].sourceHeight).toBe(1080);

        expect(sender.sent[1].isKeyFrame).toBe(false);
        expect(sender.sent[1].sourceWidth).toBeUndefined();
        expect(sender.sent[1].sourceHeight).toBeUndefined();
    });

    it('createSender is called exactly once across the run', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        let createCount = 0;
        const sink = wireSend({
            createSender: () => {
                createCount++;
                return sender;
            },
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
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const sink = wireSend({
            createSender: () => sender,
            layerCount: 3,
            topLayerWidth: 1280,
            topLayerHeight: 720,
        });
        const topDescription = new Uint8Array([0x67, 0x42, 0x00, 0x1f]);

        await runWith(source([
            makeEncoded(stats, {
                type: 'key',
                spatialLayerId: 0,
                encodedWidth: 320,
                encodedHeight: 180,
                capturedAt: { timeMs: 1_000, epoch: 0 },
            }),
            makeEncoded(stats, {
                type: 'key',
                spatialLayerId: 1,
                encodedWidth: 640,
                encodedHeight: 360,
                capturedAt: { timeMs: 1_000, epoch: 0 },
            }),
            makeEncoded(stats, {
                type: 'key',
                spatialLayerId: 2,
                encodedWidth: 1280,
                encodedHeight: 720,
                capturedAt: { timeMs: 1_000, epoch: 0 },
                description: topDescription,
            }),
        ]), sink);

        expect(sender.sent.map(x => x.spatialLayerId)).toEqual([0, 1, 2]);
        expect(sender.formats).toHaveLength(1);
        expect(sender.formats[0].width).toBe(1280);
        expect(sender.formats[0].height).toBe(720);
        expect(sender.formats[0].codecSettings).not.toBe('');
    });

    // Ensure the helper is referenced so unused-import lints don't fire if we pare down later.
    it('runs cleanly with no items (empty stream)', async () => {
        const sender = new FakeSender();
        const op = wireSend({ createSender: () => sender });
        await runWith(source([]), op);
        expect(sender.sent).toEqual([]);
    });

    it('closes every encoded chunk after serializing it', async () => {
        const stats = createEmptyRecordingStats(0);
        const sender = new FakeSender();
        const items = [
            makeEncoded(stats, { type: 'key', capturedAt: { timeMs: 1_000, epoch: 0 } }),
            makeEncoded(stats, { type: 'delta', capturedAt: { timeMs: 1_033, epoch: 0 } }),
        ];

        await runWith(source(items), wireSend({ createSender: () => sender }));

        expect(items.map(x => (x.chunk as unknown as MockEncodedVideoChunk).closed)).toEqual([true, true]);
    });
});
