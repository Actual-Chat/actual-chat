import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
    Recorder,
    type RecorderConfig,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/recorder';
import { SenderSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/session';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import type {
    StreamSenderLike,
    VideoStreamFrame,
    VideoStreamFrameBundle,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-send';
import { AsyncVideoEncoder } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/adapters';
import type {
    EncodedFrame,
    RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import type { EncodeInput } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';

// ---- Mocks ----------------------------------------------------------------

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    encodeCalls: { frame: MockVideoFrame; opts: { keyFrame: boolean } }[] = [];
    output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
    error: (e: unknown) => void;

    constructor(init: {
        output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
        error: (e: unknown) => void;
    }) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    encode(frame: MockVideoFrame, opts: { keyFrame: boolean }): void {
        this.encodeCalls.push({ frame, opts });
        queueMicrotask(() => this.drainAll());
    }

    close(): void { this.state = 'closed'; }

    /** Auto-emit one chunk per pending encode submission. Tests call
     *  this in a tight loop alongside iter.next() to drive frames
     *  through the pipeline. */
    drainAll(byteLength = 50): void {
        while (this.encodeCalls.length > 0) {
            const call = this.encodeCalls.shift()!;
            const type: 'key' | 'delta' = call.opts.keyFrame ? 'key' : 'delta';
            const chunk = {
                type,
                timestamp: 0,
                byteLength,
                duration: null,
                copyTo(buf: ArrayBuffer): void {
                    new Uint8Array(buf).fill(0);
                },
            } as unknown as EncodedVideoChunk;
            this.output(chunk, {});
        }
    }
}

class MockVideoFrame {
    public closed = false;
    public clones: MockVideoFrame[] = [];
    public rotation = 0;
    public timestamp = 0;
    constructor(
        idOrCanvas: number | MockOffscreenCanvas,
        widthOrInit: number | VideoFrameInit = 640,
        codedHeight = 360,
    ) {
        if (typeof idOrCanvas === 'number') {
            this.id = idOrCanvas;
            this.codedWidth = widthOrInit as number;
            this.codedHeight = codedHeight;
            return;
        }

        this.id = -1;
        this.codedWidth = idOrCanvas.width;
        this.codedHeight = idOrCanvas.height;
        this.timestamp = typeof widthOrInit === 'object' ? (widthOrInit.timestamp ?? 0) : 0;
    }
    public id: number;
    public codedWidth: number;
    public codedHeight: number;
    clone(): VideoFrame {
        const c = new MockVideoFrame(this.id + 10_000, this.codedWidth, this.codedHeight);
        this.clones.push(c);
        return c as unknown as VideoFrame;
    }
    close(): void { this.closed = true; }
}

class MockCanvasContext {
    imageSmoothingEnabled = false;
    imageSmoothingQuality: ImageSmoothingQuality = 'low';
    drawImage(): void { /* no-op */ }
    save(): void { /* no-op */ }
    restore(): void { /* no-op */ }
    translate(): void { /* no-op */ }
    rotate(): void { /* no-op */ }
}

class MockOffscreenCanvas {
    readonly ctx = new MockCanvasContext();
    constructor(public width: number, public height: number) {}
    getContext(): MockCanvasContext {
        return this.ctx;
    }
}

class FakeSender implements StreamSenderLike {
    public sent: VideoStreamFrame[] = [];
    send(bundle: VideoStreamFrameBundle): void {
        for (const f of bundle.layers) this.sent.push(f);
    }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
    VideoFrame?: typeof MockVideoFrame;
    OffscreenCanvas?: typeof MockOffscreenCanvas;
}

beforeEach(() => {
    MockVideoEncoder.instances = [];
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
    (globalThis as unknown as GlobalWithVideoEncoder).VideoFrame = MockVideoFrame;
    (globalThis as unknown as GlobalWithVideoEncoder).OffscreenCanvas = MockOffscreenCanvas;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoFrame;
    delete (globalThis as unknown as GlobalWithVideoEncoder).OffscreenCanvas;
});

// ---- Synthetic capture source --------------------------------------------

function makeProcessorFromQueue(
    frames: MockVideoFrame[],
): (track: MediaStreamTrack) => { readable: ReadableStream<VideoFrame> } {
    return (_track: MediaStreamTrack) => {
        let idx = 0;
        const readable = new ReadableStream<VideoFrame>({
            async pull(controller) {
                await Promise.resolve();
                if (idx >= frames.length) {
                    controller.close();
                    return;
                }
                controller.enqueue(frames[idx++] as unknown as VideoFrame);
            },
        });
        return { readable };
    };
}

// ---- Helpers --------------------------------------------------------------

const cfg: EncoderConfigPerLayer = {
    width: 640,
    height: 360,
    bitrate: 1_000_000,
    framerate: 30,
    codec: 'avc1.640028',
};

function makeEncoderFactory(opts: { timeoutMs?: number } = {}) {
    return (config: EncoderConfigPerLayer, layerId: number) =>
        new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
            (input, chunk, metadata): EncodedFrame => ({
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
            { timeoutMs: opts.timeoutMs ?? 0 },
        );
}

// Drives an in-flight `start()` promise to completion by repeatedly
// flushing pending encodes on every MockVideoEncoder until the run
// resolves. Bounded so a stuck pipeline fails fast.
async function driveToCompletion(runPromise: Promise<void>): Promise<void> {
    const state = { done: false };
    const watched = runPromise.then(
        () => { state.done = true; },
        (e: unknown) => { state.done = true; throw e instanceof Error ? e : new Error(String(e)); },
    );
    for (let i = 0; i < 1000; i++) {
        if (state.done) break;
        for (const enc of MockVideoEncoder.instances) enc.drainAll();
        await Promise.resolve();
    }
    await watched;
}

function buildConfig(overrides: Partial<RecorderConfig> = {}): RecorderConfig {
    const frames = [
        new MockVideoFrame(0),
        new MockVideoFrame(1),
        new MockVideoFrame(2),
    ];
    return {
        track: {} as MediaStreamTrack,
        createProcessor: makeProcessorFromQueue(frames),
        sourceKind: 0,
        isFrontCamera: false,
        isIos: false,
        encoderConfigs: [cfg],
        createEncoder: makeEncoderFactory(),
        keyframeIntervalFrames: 30,
        createSender: () => new FakeSender(),
        ...overrides,
    };
}

// ---- Tests ----------------------------------------------------------------

describe('Recorder', () => {
    it('start: drives 3 frames end-to-end → 3 wire DTOs, isRunning toggles', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);
        const sender = new FakeSender();
        const config = buildConfig({ createSender: () => sender });
        expect(recorder.isRunning()).toBe(false);
        const runPromise = recorder.start(config);
        // The pipeline is now active.
        await Promise.resolve();
        expect(recorder.isRunning()).toBe(true);
        await driveToCompletion(runPromise);
        expect(recorder.isRunning()).toBe(false);
        expect(sender.sent.length).toBe(3);
        // IsKeyFrame derived: keyFrameIndex === index iff this is a keyframe.
        expect(sender.sent[0].keyFrameIndex).toBe(sender.sent[0].index);
        session.dispose();
    });

    it('start: getStats() reflects per-run counters', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);
        const config = buildConfig();
        const runPromise = recorder.start(config);
        await driveToCompletion(runPromise);
        const stats = recorder.getStats();
        expect(stats).not.toBeNull();
        session.dispose();
    });

    it('stop: triggers shutdown of an in-flight run mid-stream; runPromise resolves cleanly', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);
        // A source that enqueues forever — the recording would never
        // end on its own. `stop()` now completes the capture source
        // first; the shared abort controller is only a delayed safety
        // fallback if the pipe refuses to drain.
        let enqueued = 0;
        const stoppingProcessor = (_track: MediaStreamTrack) => ({
            readable: new ReadableStream<VideoFrame>({
                async pull(controller) {
                    await Promise.resolve();
                    if (enqueued >= 50) {
                        controller.close();
                        return;
                    }
                    controller.enqueue(new MockVideoFrame(enqueued++) as unknown as VideoFrame);
                },
            }),
        });
        const runPromise = recorder.start(buildConfig({
            createProcessor: stoppingProcessor,
        }));
        // Let a couple frames flow, then ask the recorder to stop.
        for (let i = 0; i < 5; i++) {
            for (const enc of MockVideoEncoder.instances) enc.drainAll();
            await Promise.resolve();
        }
        expect(recorder.isRunning()).toBe(true);
        recorder.stop();
        await driveToCompletion(runPromise);
        expect(recorder.isRunning()).toBe(false);
        // Source was interrupted before reaching its 50-frame budget.
        expect(enqueued).toBeLessThan(50);
        session.dispose();
    });

    it('restart: every run creates a fresh VideoEncoder so the first chunk is a keyframe', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);

        // First run: 1 frame.
        const sender1 = new FakeSender();
        const run1 = recorder.start(buildConfig({
            createSender: () => sender1,
            createProcessor: makeProcessorFromQueue([new MockVideoFrame(0)]),
        }));
        await driveToCompletion(run1);
        const instancesAfterRun1 = MockVideoEncoder.instances.length;
        expect(instancesAfterRun1).toBeGreaterThan(0);

        // restart() — second run MUST create a new VideoEncoder, not reuse.
        // The fresh encoder's first encoded chunk is guaranteed to be a
        // keyframe (its an empty internal frame buffer; nothing to predict from).
        const sender2 = new FakeSender();
        const run2Promise = recorder.restart(buildConfig({
            createSender: () => sender2,
            createProcessor: makeProcessorFromQueue([new MockVideoFrame(0)]),
        }));
        await driveToCompletion(run2Promise);
        expect(MockVideoEncoder.instances.length).toBeGreaterThan(instancesAfterRun1);
        expect(sender2.sent.length).toBe(1);
        // First wire DTO of the second run is a real keyframe.
        expect(sender2.sent[0].keyFrameIndex).toBe(sender2.sent[0].index);
        session.dispose();
    });

    it('reconfigureLayers: grows ladder mid-stream without recreating the sender', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);
        // Many-frame source so we can reconfigure mid-flight.
        const senders: FakeSender[] = [];
        let enqueued = 0;
        const slowSource = (_track: MediaStreamTrack) => ({
            readable: new ReadableStream<VideoFrame>({
                async pull(controller) {
                    await Promise.resolve();
                    if (enqueued >= 8) {
                        controller.close();
                        return;
                    }
                    controller.enqueue(new MockVideoFrame(enqueued++) as unknown as VideoFrame);
                },
            }),
        });

        const cfgTop: EncoderConfigPerLayer = {
            width: 1280, height: 720, bitrate: 2_000_000, framerate: 30, codec: 'avc1.640028',
        };
        const cfgBase: EncoderConfigPerLayer = {
            width: 640, height: 360, bitrate: 1_000_000, framerate: 30, codec: 'avc1.640028',
        };
        const runPromise = recorder.start({
            ...buildConfig({
                createSender: () => {
                    const s = new FakeSender();
                    senders.push(s);
                    return s;
                },
                createProcessor: slowSource,
            }),
            encoderConfigs: [cfgTop],
        });
        // Pump until we see the first wire chunk land.
        for (let i = 0; i < 200; i++) {
            for (const enc of MockVideoEncoder.instances) enc.drainAll();
            await Promise.resolve();
            if (senders.length > 0 && senders[0].sent.length >= 1) break;
        }
        expect(senders.length).toBe(1);
        const initialEncoderInstances = MockVideoEncoder.instances.length;

        // Grow the ladder. The wire sender MUST NOT be recreated.
        recorder.reconfigureLayers([cfgBase, cfgTop]);
        await driveToCompletion(runPromise);

        // Same sender instance carried us across the reconfigure — RpcStream
        // identity is preserved.
        expect(senders.length).toBe(1);
        // The encoder set grew (a 2nd layer's encoder was created mid-run).
        expect(MockVideoEncoder.instances.length).toBeGreaterThan(initialEncoderInstances);
        // Wire DTOs after reconfigure carry layerCount=2.
        const post = senders[0].sent.filter(f => f.layerCount === 2);
        expect(post.length).toBeGreaterThan(0);
        session.dispose();
    });

    it('start: rejects when called while a run is already in flight', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);
        // Long-running source so the second start() races with an
        // active run.
        let enqueued = 0;
        const longSource = (_track: MediaStreamTrack) => ({
            readable: new ReadableStream<VideoFrame>({
                async pull(controller) {
                    await Promise.resolve();
                    if (enqueued >= 100) {
                        controller.close();
                        return;
                    }
                    controller.enqueue(new MockVideoFrame(enqueued++) as unknown as VideoFrame);
                },
            }),
        });
        const runPromise = recorder.start(buildConfig({
            createProcessor: longSource,
        }));
        // Settle one tick so the run is kicked off.
        await Promise.resolve();
        await expect(recorder.start(buildConfig())).rejects.toThrow(/already running/);
        recorder.stop();
        await driveToCompletion(runPromise);
        session.dispose();
    });
});
