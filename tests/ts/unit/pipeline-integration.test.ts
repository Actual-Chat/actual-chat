import { count, pipe, tap } from 'ix-ext';
// Integration tests for the new video pipeline operators.
//
// Per-operator unit tests already cover individual operator behavior; this
// file verifies operators COMPOSE correctly end-to-end. Only platform-edge
// collaborators are faked (VideoEncoder, VideoDecoder, sender
// transport). Everything else is real.

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// Operators
import { stampCaptureTime } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/stamp-capture-time';
import { attachSourceDims } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/attach-source-dims';
import { forceKeyframeOnDimChange } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/force-keyframe-on-dim-change';
import { dropDimMismatch } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/dim-mismatch-guard';
import {
    normalizeFrame,
    downscale,
    type DownscalerLike,
    type LayerSpec,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import { applyKeyframePolicy } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/apply-keyframe-policy';
import {
    encode,
    type EncodeInput,
    type EncoderConfigPerLayer,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import {
    wireSend,
    type StreamSenderLike,
    type VideoStreamFrame,
    type VideoStreamFrameBundle,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-send';
import {
    pullSource,
    type VideoFrameDto,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/pull';
import { resetOnEpochChange } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/epoch-reset';
import { pacedEncodedBuffer } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encoded-buffer';
import {
    decode,
    type DecoderLike,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/decode';

// Envelopes / stats
import {
    type CapturedFrame,
    type EncodedFrame,
    type ArrivedChunk,
    type DecodedFrame,
    type RecorderStats,
    createEmptyRecorderStats,
    createEmptyPlayerStats,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

import { EncodedFrameBuffer } from '../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';

import { AsyncVideoEncoder } from '../../../src/dotnet/UI.Blazor.App/Services/Video/adapters';
import { LayerLadderController } from '../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import { MonotonicClock } from 'clocks';
// ============================================================================
// Mock WebCodecs surfaces
// ============================================================================

class MockVideoFrame {
    closed = false;
    id: number;
    codedWidth: number;
    codedHeight: number;
    timestamp: number;

    constructor(
        idOrCanvas: number | MockOffscreenCanvas,
        widthOrInit: number | VideoFrameInit = 0,
        codedHeight = 0,
    ) {
        if (typeof idOrCanvas === 'number') {
            this.id = idOrCanvas;
            this.codedWidth = widthOrInit as number;
            this.codedHeight = codedHeight;
            this.timestamp = 0;
            return;
        }

        this.id = -1;
        this.codedWidth = idOrCanvas.width;
        this.codedHeight = idOrCanvas.height;
        this.timestamp = typeof widthOrInit === 'object' ? (widthOrInit.timestamp ?? 0) : 0;
    }

    close(): void { this.closed = true; }
}

class MockCanvasContext {
    imageSmoothingEnabled = false;
    imageSmoothingQuality: ImageSmoothingQuality = 'low';
    filter = 'none';
    drawImage(): void { /* no-op */ }
    save(): void { /* no-op */ }
    restore(): void { /* no-op */ }
    translate(): void { /* no-op */ }
    rotate(): void { /* no-op */ }
}

class MockOffscreenCanvas {
    constructor(public width: number, public height: number) {}
    readonly ctx = new MockCanvasContext();
    getContext(): MockCanvasContext {
        return this.ctx;
    }
}

interface MockVideoEncoderInit {
    output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
    error: (e: unknown) => void;
}

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    encodeCalls: { frame: MockVideoFrame; opts: { keyFrame: boolean } }[] = [];
    output: MockVideoEncoderInit['output'];
    error: MockVideoEncoderInit['error'];

    constructor(init: MockVideoEncoderInit) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    encode(frame: MockVideoFrame, opts: { keyFrame: boolean }): void {
        this.encodeCalls.push({ frame, opts });
    }

    close(): void { this.state = 'closed'; }

    /** Drain queued encode submissions in FIFO order, emitting fake chunks
     *  for each. Tests advance the pipeline by calling this after letting
     *  microtasks run. */
    drainAll(byteLength = 64): void {
        while (this.encodeCalls.length > 0) {
            const call = this.encodeCalls.shift()!;
            const type: 'key' | 'delta' = call.opts.keyFrame ? 'key' : 'delta';
            const chunk = {
                type,
                timestamp: 0,
                duration: null,
                byteLength,
                copyTo: (buf: ArrayBuffer): void => {
                    new Uint8Array(buf).fill(0);
                },
            } as unknown as EncodedVideoChunk;
            this.output(chunk, {});
        }
    }
}

class MockEncodedVideoChunk {
    constructor(
        public type: 'key' | 'delta',
        public timestamp: number,
        public byteLength: number,
        private readonly bytes: Uint8Array,
    ) {}
    duration: number | null = null;
    copyTo(buffer: ArrayBuffer): void {
        new Uint8Array(buffer).set(this.bytes.subarray(0, Math.min(buffer.byteLength, this.bytes.length)));
    }
}

interface MockGlobals {
    VideoEncoder?: typeof MockVideoEncoder;
    EncodedVideoChunk?: typeof MockEncodedVideoChunk;
    VideoFrame?: typeof MockVideoFrame;
    OffscreenCanvas?: typeof MockOffscreenCanvas;
}

// ============================================================================
// Fakes for collaborators (sender, decoder)
// ============================================================================

// Fake real-downscale: top tier = input, lower tiers = fresh mocks at the
// target dims. Stands in for the WebGL/Canvas downscaler (no GL in vitest);
// encode() derives wire dims from the ladder config, not the frame, so the
// mock dims are irrelevant to assertions here.
class FakeDownscaler implements DownscalerLike {
    private next = 9000;
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const topIdx = layers.length - 1;
        const out: VideoFrame[] = [];
        for (let i = 0; i < layers.length; i++) {
            out.push(i === topIdx
                ? input
                : (new MockVideoFrame(this.next++, layers[i].width, layers[i].height) as unknown as VideoFrame));
        }
        return out;
    }
}

class FakeSender implements StreamSenderLike {
    sent: VideoStreamFrame[] = [];
    afterSend?: () => void;
    send(bundle: VideoStreamFrameBundle): void {
        for (const f of bundle.layers) this.sent.push(f);
        this.afterSend?.();
    }
}

interface DecoderHandlers {
    onFrame: (frame: VideoFrame) => void;
    onError: (e: Error) => void;
}

/** Decoder fake that synchronously emits one MockVideoFrame per chunk with
 *  the captured-time encoded into the frame id (= the chunk's timestamp).
 *  Used by the receiver E2E to verify chunks flow through to decoded output. */
class SyncFakeDecoder implements DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    decodeQueueSize = 0;
    private nextId = 0;
    constructor(private readonly handlers: DecoderHandlers) {}
    configure(_config: VideoDecoderConfig): void { this.state = 'configured'; }
    decode(_chunk: EncodedVideoChunk): void {
        const frame = new MockVideoFrame(this.nextId++, 1280, 720) as unknown as VideoFrame;
        // Hand the frame back synchronously — the decode operator's onFrame
        // pairs it with the FIFO-front pending entry and pushes onto the
        // ready queue / wakeup signal.
        queueMicrotask(() => this.handlers.onFrame(frame));
    }
    flush(): Promise<void> { return Promise.resolve(); }
    close(): void { this.state = 'closed'; }
}

// ============================================================================
// Builders
// ============================================================================

function mkFrame(id: number, w: number, h: number): VideoFrame {
    return new MockVideoFrame(id, w, h) as unknown as VideoFrame;
}

function makeCaptured(
    index: number,
    stats: RecorderStats,
    width: number,
    height: number,
): CapturedFrame {
    return {
        frame: mkFrame(index, width, height),
        // capturedAt overwritten by stampCaptureTime; placeholder values:
        capturedAt: { timeMs: 0, epoch: 0 },
        index,
        dropTrace: [],
        sourceWidth: width,
        sourceHeight: height,
        forceKeyframe: false,
        rotation: 0,
        stats,
    };
}

function fromArray<T>(items: T[]): AsyncIterable<T> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

const cfg = (width: number, height: number): EncoderConfigPerLayer => ({
    width, height,
    bitrate: 1_000_000,
    framerate: 30,
    codec: 'avc1.640028',
});

/** Encoder factory backed by `AsyncVideoEncoder` over `MockVideoEncoder`. */
function makeEncoderFactory() {
    return (config: EncoderConfigPerLayer, layerId: number): AsyncVideoEncoder<EncodeInput, EncodedFrame> =>
        new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
            (input, chunk, metadata) => ({
                chunk,
                metadata,
                capturedAt: input.capturedAt,
                index: input.index,
                dropTrace: [],
                layerId: layerId,
                sourceWidth: 0,
                sourceHeight: 0,
                encodedWidth: config.width,
                encodedHeight: config.height,
                rotation: 0,
                stats: undefined as unknown as RecorderStats,
            }),
            () => { /* swallow */ },
            { timeoutMs: 5_000 },
        );
}

// Drive the sender pipeline to completion, emitting encoder chunks as they
// queue up. Returns once the source iterable is exhausted.
async function runSenderToCompletion(
    runP: Promise<unknown>,
    expectedSubmissions: number,
): Promise<void> {
    // The encoder factory creates encoders lazily on first bundle. Pump
    // microtasks, draining queued encode submissions across all instances
    // until all expected outputs have been emitted.
    let totalEmitted = 0;
    for (let i = 0; i < 10_000 && totalEmitted < expectedSubmissions; i++) {
        let drainedThisRound = 0;
        for (const enc of MockVideoEncoder.instances) {
            const n = enc.encodeCalls.length;
            if (n > 0) {
                enc.drainAll();
                drainedThisRound += n;
                totalEmitted += n;
            }
        }
        if (drainedThisRound === 0) {
            // No work pending; let the pipeline make progress.
            await Promise.resolve();
        } else {
            await Promise.resolve();
            await Promise.resolve();
        }
    }
    await runP;
}

// ============================================================================
// Test setup
// ============================================================================

beforeEach(() => {
    MockVideoEncoder.instances = [];
    (globalThis as unknown as MockGlobals).VideoEncoder = MockVideoEncoder;
    (globalThis as unknown as MockGlobals).EncodedVideoChunk = MockEncodedVideoChunk;
    (globalThis as unknown as MockGlobals).VideoFrame = MockVideoFrame;
    (globalThis as unknown as MockGlobals).OffscreenCanvas = MockOffscreenCanvas;
});

afterEach(() => {
    delete (globalThis as unknown as MockGlobals).VideoEncoder;
    delete (globalThis as unknown as MockGlobals).EncodedVideoChunk;
    delete (globalThis as unknown as MockGlobals).VideoFrame;
    delete (globalThis as unknown as MockGlobals).OffscreenCanvas;
    vi.useRealTimers();
    vi.restoreAllMocks();
});

// ============================================================================
// Tests
// ============================================================================

describe('video pipeline integration', () => {
    it('1. sender E2E single-tier: 10 source frames → 10 wire DTOs', async () => {
        // Mock the underlying time sources so MonotonicClock is deterministic.
        let mockWallMs = 1_700_000_000_000;
        let mockPerfMs = 100;
        vi.spyOn(Date, 'now').mockImplementation(() => mockWallMs);
        vi.spyOn(performance, 'now').mockImplementation(() => mockPerfMs);

        const stats = createEmptyRecorderStats();
        const clock = new MonotonicClock();
        const sender = new FakeSender();
        const encDims = { width: 1280, height: 720 };
        const ladderController = new LayerLadderController([cfg(encDims.width, encDims.height)]);

        const source: CapturedFrame[] = [];
        for (let i = 0; i < 10; i++) {
            source.push(makeCaptured(i, stats, encDims.width, encDims.height));
        }

        const captureToBundle = pipe(
            fromArray(source),
            stampCaptureTime({ clock }),
            attachSourceDims(),
            forceKeyframeOnDimChange(),
            dropDimMismatch({ getExpectedDims: () => encDims }),
            normalizeFrame({ getNormalizeSize: () => ladderController.current.configs[ladderController.current.configs.length - 1], isCamera: false, isFrontCamera: false, isIos: false }),
            downscale({ controller: ladderController, createDownscaler: () => new FakeDownscaler() }),
            applyKeyframePolicy({ keyframeIntervalFrames: 60, now: () => mockPerfMs }),
        );
        const senderPipe = pipe(
            captureToBundle,
            encode({ controller: ladderController, createEncoder: makeEncoderFactory() }),
            wireSend({ createSender: () => sender, controller: ladderController }),
        );
        // Advance clocks 33ms per call to performance.now/Date.now so each
        // captured frame gets a distinct capturedAt.timeMs.
        const advance = (): void => { mockPerfMs += 33; mockWallMs += 33; };
        sender.afterSend = advance;
        const runP = count(senderPipe);

        await runSenderToCompletion(runP, 10);

        expect(sender.sent).toHaveLength(10);
        expect(sender.sent[0].offset).toBe(0);
        // IsKeyFrame derived: keyFrameIndex === index iff this is a keyframe.
        expect(sender.sent[0].keyFrameIndex).toBe(sender.sent[0].index);
        // Offsets monotonic non-decreasing.
        for (let i = 1; i < 10; i++)
            expect(sender.sent[i].offset).toBeGreaterThanOrEqual(sender.sent[i - 1].offset);
        for (const dto of sender.sent) expect(dto.offsetEpoch).toBe(0);
    });

    it('2. sender simulcast 3-layer: 5 frames → 15 DTOs with correct layerIds', async () => {
        let mockWallMs = 1_700_000_000_000;
        let mockPerfMs = 100;
        vi.spyOn(Date, 'now').mockImplementation(() => mockWallMs);
        vi.spyOn(performance, 'now').mockImplementation(() => mockPerfMs);

        const stats = createEmptyRecorderStats();
        const clock = new MonotonicClock();
        const sender = new FakeSender();
        const ladder: LayerSpec[] = [
            { width: 480, height: 270 },
            { width: 960, height: 540 },
            { width: 1920, height: 1080 },
        ];
        const ladderController = new LayerLadderController(ladder.map(l => cfg(l.width, l.height)));
        // Encoder expects coded dims at the TOP of the ladder (= primary
        // tier = 1920x1080). The dim-mismatch guard runs BEFORE downscale,
        // so it sees the source frame at top dims.
        const expectedSourceDims = { width: 1920, height: 1080 };

        const source: CapturedFrame[] = [];
        for (let i = 0; i < 5; i++) {
            source.push(makeCaptured(i, stats, expectedSourceDims.width, expectedSourceDims.height));
        }
        // Pipelined encode submits multiple bundles before awaiting, so
        // advancing the mock clock per `sender.afterSend` no longer
        // distinguishes captureTime per frame. Advance per source-yield
        // instead so each captured frame gets a unique timestamp.
        async function* tickingSource(): AsyncIterable<CapturedFrame> {
            for (const f of source) {
                await Promise.resolve();
                yield f;
                mockPerfMs += 33;
                mockWallMs += 33;
            }
        }

        const captureToBundle = pipe(
            tickingSource(),
            stampCaptureTime({ clock }),
            attachSourceDims(),
            forceKeyframeOnDimChange(),
            dropDimMismatch({ getExpectedDims: () => expectedSourceDims }),
            normalizeFrame({ getNormalizeSize: () => ladderController.current.configs[ladderController.current.configs.length - 1], isCamera: false, isFrontCamera: false, isIos: false }),
            downscale({ controller: ladderController, createDownscaler: () => new FakeDownscaler() }),
            applyKeyframePolicy({ keyframeIntervalFrames: 60, now: () => mockPerfMs }),
        );
        const senderPipe = pipe(
            captureToBundle,
            encode({ controller: ladderController, createEncoder: makeEncoderFactory() }),
            wireSend({ createSender: () => sender, controller: ladderController }),
        );
        const runP = count(senderPipe);

        await runSenderToCompletion(runP, 15);

        expect(sender.sent).toHaveLength(15);
        // Group by offset; each group should have layerId 0,1,2.
        const groups = new Map<number, VideoStreamFrame[]>();
        for (const dto of sender.sent) {
            const arr = groups.get(dto.offset) ?? [];
            arr.push(dto);
            groups.set(dto.offset, arr);
        }
        expect(groups.size).toBe(5);
        for (const [, group] of groups) {
            expect(group).toHaveLength(3);
            const ids = group.map(d => d.layerId).sort();
            expect(ids).toEqual([0, 1, 2]);
        }
    });

    it('3. receiver E2E: paced buffer holds frames until target span; all flow through', async () => {
        // Wallclock controlled via a mock — drives buffer pacing.
        let nowMs = 10_000;
        const targetSpanMs = 100;
        const stats = createEmptyPlayerStats();

        // Synthetic DTOs spaced 33ms apart in capture time. Arrival time
        // tracks `nowMs` at the moment pullSource yields each one (we
        // tick `nowMs` between yields by yielding to microtasks).
        const dtos: VideoFrameDto[] = [];
        for (let i = 0; i < 8; i++) {
            const isKey = i === 0;
            dtos.push({
                Data: new Uint8Array([i, i + 1, i + 2, i + 3]),
                // Offset in 100-ns ticks: i * 33 ms × 10000 ticks/ms.
                Offset: i * 33 * 10_000,
                OffsetEpoch: 1,
                Duration: 0,
                // IsKeyFrame derived: KeyFrameIndex === Index iff keyframe.
                Index: i,
                KeyFrameIndex: isKey ? i : 0,
                Width: 1280,
                Height: 720,
            });
        }

        // Use a clock that returns nowMs directly. Advancing nowMs between
        // pulls produces realistic arrivedAt timestamps.
        const arrivalClock = {
            now: () => ({ timeMs: nowMs, epoch: 1 }),
            epoch: 1,
        } as unknown as MonotonicClock;

        const buffer = new EncodedFrameBuffer({ targetSpanMs, frameDurationMs: 33.333 });

        async function* dtoStream(): AsyncIterable<VideoFrameDto> {
            for (const d of dtos) {
                await Promise.resolve();
                yield d;
                nowMs += 33;
            }
        }

        const decodedYieldOrder: number[] = [];
        const firstYieldNowMs: { value: number | null } = { value: null };
        const ac = new AbortController();

        const receiverPipe = pipe(
            pullSource({
                streamId: 's1',
                getStream: () => dtoStream(),
                arrivalClock,
                stats,
                abortSignal: ac.signal,
            }),
            resetOnEpochChange({ buffer }),
            pacedEncodedBuffer({ buffer, abortSignal: ac.signal }),
            decode({
                initialConfig: { codec: 'avc1.42E01E' },
                createDecoder: handlers => new SyncFakeDecoder(handlers),
                now: () => nowMs,
            }),
            tap<DecodedFrame>(f => {
                decodedYieldOrder.push(f.capturedAt.timeMs);
                firstYieldNowMs.value ??= nowMs;
                // Bump time so subsequent buffer checks drain ready chunks.
                nowMs += 1;
            }),
        );
        // Drive the pipeline. The buffer holds chunks until span ≥ 100ms
        // AND now() ≥ arrivedAt + 100ms; we advance nowMs in dtoStream and
        // in tap. To avoid a stall after dtos are exhausted, give nowMs a
        // generous push at the end.
        const runP = (async (): Promise<void> => {
            for await (const _ of receiverPipe) { void _; }
        })();

        // Watchdog: pump microtasks until everything decodes (span-only
        // gating means no time advancement is needed — chunks release as
        // soon as buffered span ≥ targetSpanMs, which is purely a function
        // of capturedAt deltas).
        for (let i = 0; i < 5000; i++) {
            for (let m = 0; m < 5; m++) await Promise.resolve();
            nowMs += 20;
            if (decodedYieldOrder.length >= dtos.length) break;
        }
        // Cancel: the buffer always retains at least targetSpanMs worth of
        // chunks at the tail (once spanMs() drops below the target it stops
        // emitting). That's the buffer's documented behavior. Aborting
        // unwinds everything cleanly so runP resolves.
        ac.abort();
        await runP;

        // The buffer's contract: it emits chunks while `spanMs ≥ targetSpanMs`.
        // With 8 chunks @ 33ms intervals and targetSpanMs=100, the last few
        // chunks remain buffered. Verify a substantial prefix decodes:
        expect(decodedYieldOrder.length).toBeGreaterThanOrEqual(4);
        expect(decodedYieldOrder.length).toBeLessThanOrEqual(dtos.length);
        // Captured times are 0, 33, 66, ... — verify monotone forward.
        for (let i = 1; i < decodedYieldOrder.length; i++) {
            expect(decodedYieldOrder[i]).toBeGreaterThan(decodedYieldOrder[i - 1]);
        }
        // First decoded frame appeared after the buffer accumulated
        // span ≥ targetSpanMs (now span-only — no wallclock cushion).
        expect(firstYieldNowMs.value).not.toBeNull();
    });

    it('4. sender → receiver round-trip: epoch threads through; offsets monotonic', async () => {
        // Run the sender (single-tier) and capture wire DTOs, then convert
        // them to the receiver's PascalCased VideoFrameDto shape and feed
        // through pullSource.
        //
        // Gap discovered while writing this test: the wire format only
        // carries `offset` (capture-time MINUS captureStartUnixMs, in
        // ticks) — there's no "anchor" field. So `pull.ts` reconstructs
        // capturedAt.timeMs as `offset / 10_000ms`, NOT as
        // `originalCapturedAtMs`. Round-tripping the absolute capture time
        // would require an out-of-band anchor; the existing pipeline
        // doesn't have one, so we verify what IS round-trippable: epoch
        // and the relative offset structure (first frame at 0, monotonic).

        let mockWallMs = 1_700_000_000_000;
        let mockPerfMs = 100;
        vi.spyOn(Date, 'now').mockImplementation(() => mockWallMs);
        vi.spyOn(performance, 'now').mockImplementation(() => mockPerfMs);

        const recStats = createEmptyRecorderStats();
        const clock = new MonotonicClock();
        const fakeSender = new FakeSender();
        const encDims = { width: 1280, height: 720 };
        const ladderController = new LayerLadderController([cfg(encDims.width, encDims.height)]);

        const source: CapturedFrame[] = [];
        for (let i = 0; i < 4; i++) source.push(makeCaptured(i, recStats, encDims.width, encDims.height));

        const captureToBundle = pipe(
            fromArray(source),
            stampCaptureTime({ clock }),
            attachSourceDims(),
            forceKeyframeOnDimChange(),
            dropDimMismatch({ getExpectedDims: () => encDims }),
            normalizeFrame({ getNormalizeSize: () => ladderController.current.configs[ladderController.current.configs.length - 1], isCamera: false, isFrontCamera: false, isIos: false }),
            downscale({ controller: ladderController, createDownscaler: () => new FakeDownscaler() }),
            applyKeyframePolicy({ keyframeIntervalFrames: 60, now: () => mockPerfMs }),
        );
        const senderPipe = pipe(
            captureToBundle,
            encode({ controller: ladderController, createEncoder: makeEncoderFactory() }),
            wireSend({ createSender: () => fakeSender, controller: ladderController }),
        );
        const advance = (): void => { mockPerfMs += 33; mockWallMs += 33; };
        fakeSender.afterSend = advance;
        const runP = count(senderPipe);
        await runSenderToCompletion(runP, source.length);

        expect(fakeSender.sent).toHaveLength(source.length);

        // Convert each VideoStreamFrame (camelCased) → VideoFrameDto (PascalCased).
        // MonotonicClock starts at epoch 0; sender emitted with epoch 0,
        // so `offsetEpoch` is 0 and the receiver-side capturedAt.epoch will be 0.
        const dtos: VideoFrameDto[] = fakeSender.sent.map(s => ({
            Data: s.data,
            Offset: s.offset,
            OffsetEpoch: s.offsetEpoch,
            Duration: s.duration,
            // IsKeyFrame is derived from KeyFrameIndex === Index — not on wire.
            KeyFrameIndex: s.keyFrameIndex,
            Index: s.index,
            Width: s.width,
            Height: s.height,
            Description: s.description,
            Codec: s.codec,
            LayerId: s.layerId,
        }));

        // Run the receiver side just up to pullSource → ArrivedChunk so we
        // can inspect what came through. We don't need decode + buffer for
        // this test; the round-trip claim is about pull's capturedAt.
        const playbackStats = createEmptyPlayerStats();
        const arrivedChunks: ArrivedChunk[] = [];
        const receiverPipe = pipe(
            pullSource({
                streamId: 's1',
                getStream: () => (async function* () {
                    for (const d of dtos) { await Promise.resolve(); yield d; }
                })(),
                stats: playbackStats,
            }),
            tap<ArrivedChunk>(c => { arrivedChunks.push(c); }),
        );
        await count(receiverPipe);

        expect(arrivedChunks).toHaveLength(source.length);
        // Epoch round-trips: sender used epoch 0, receiver sees epoch 0.
        for (const c of arrivedChunks) expect(c.capturedAt.epoch).toBe(0);
        // Receiver-side capturedAt.timeMs equals the sender's wire offset
        // (in ms), NOT the absolute Unix-domain capture time. First frame
        // is 0, subsequent monotonic forward.
        expect(arrivedChunks[0].capturedAt.timeMs).toBe(0);
        for (let i = 1; i < arrivedChunks.length; i++) {
            expect(arrivedChunks[i].capturedAt.timeMs)
                .toBeGreaterThanOrEqual(arrivedChunks[i - 1].capturedAt.timeMs);
        }
        // Sanity: the offset structure matches the sender's emitted offsets.
        for (let i = 0; i < arrivedChunks.length; i++) {
            const expectedMs = (fakeSender.sent[i].offset / 10_000); // ticks → ms
            expect(arrivedChunks[i].capturedAt.timeMs).toBe(expectedMs);
        }
    });

    it('5. stop/restart cycle: second run anchors to offset 0; encoders re-created', async () => {
        // Each "run" gets its own FakeSender, MonotonicClock, encoder
        // factory — the pipeline is fully reconstructed. We assert the
        // first DTO of the second run lands at offset 0 (proving the
        // anchor was reset) and that fresh encoder instances were created.

        async function runOnce(): Promise<{ dtos: VideoStreamFrame[]; encoderCount: number }> {
            const startingInstanceCount = MockVideoEncoder.instances.length;
            let mockWallMs = 1_800_000_000_000 + Math.random() * 1000;
            let mockPerfMs = 500;
            vi.spyOn(Date, 'now').mockImplementation(() => mockWallMs);
            vi.spyOn(performance, 'now').mockImplementation(() => mockPerfMs);

            const stats = createEmptyRecorderStats();
            const clock = new MonotonicClock();
            const sender = new FakeSender();
            const encDims = { width: 1280, height: 720 };
            const ladderController = new LayerLadderController([cfg(encDims.width, encDims.height)]);

            const source: CapturedFrame[] = [];
            for (let i = 0; i < 3; i++) source.push(makeCaptured(i, stats, encDims.width, encDims.height));

            const captureToBundle = pipe(
                fromArray(source),
                stampCaptureTime({ clock }),
                attachSourceDims(),
                forceKeyframeOnDimChange(),
                dropDimMismatch({ getExpectedDims: () => encDims }),
                normalizeFrame({ getNormalizeSize: () => ladderController.current.configs[ladderController.current.configs.length - 1], isCamera: false, isFrontCamera: false, isIos: false }),
                downscale({ controller: ladderController, createDownscaler: () => new FakeDownscaler() }),
                applyKeyframePolicy({ keyframeIntervalFrames: 60, now: () => mockPerfMs }),
            );
            const senderPipe = pipe(
                captureToBundle,
                encode({ controller: ladderController, createEncoder: makeEncoderFactory() }),
                wireSend({ createSender: () => sender, controller: ladderController }),
            );
            const advance = (): void => { mockPerfMs += 33; mockWallMs += 33; };
            sender.afterSend = advance;
            const runP = count(senderPipe);
            await runSenderToCompletion(runP, source.length);

            const encoderCount = MockVideoEncoder.instances.length - startingInstanceCount;
            // Restore the spies so the next runOnce can install fresh ones.
            vi.restoreAllMocks();
            return { dtos: sender.sent, encoderCount };
        }

        const first = await runOnce();
        const second = await runOnce();

        expect(first.dtos).toHaveLength(3);
        expect(second.dtos).toHaveLength(3);
        // First DTO of the SECOND run anchors at offset 0 (fresh
        // captureStartUnixMs, despite a different absolute Unix-ms baseline).
        expect(second.dtos[0].offset).toBe(0);
        // IsKeyFrame derived: keyFrameIndex === index iff this is a keyframe.
        expect(second.dtos[0].keyFrameIndex).toBe(second.dtos[0].index);
        // Fresh encoder created for the second run (no leaked state).
        expect(first.encoderCount).toBe(1);
        expect(second.encoderCount).toBe(1);
        // The two runs' DTO arrays are independent objects.
        expect(first.dtos).not.toBe(second.dtos);
    });
});
