import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
    Recorder,
    type RecorderConfig,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/recorder';
import { SenderSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/session';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import type { StreamSenderLike, VideoStreamFrame } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-send';
import { AsyncVideoEncoder } from 'async-video-encoder';
import type {
    EncodedFrame,
    VideoRecordingStats,
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
    constructor(
        public id: number,
        public codedWidth = 640,
        public codedHeight = 360,
    ) {}
    clone(): VideoFrame {
        const c = new MockVideoFrame(this.id + 10_000, this.codedWidth, this.codedHeight);
        this.clones.push(c);
        return c as unknown as VideoFrame;
    }
    close(): void { this.closed = true; }
}

class FakeSender implements StreamSenderLike {
    public sent: VideoStreamFrame[] = [];
    send(dto: VideoStreamFrame): void { this.sent.push(dto); }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
}

beforeEach(() => {
    MockVideoEncoder.instances = [];
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
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
                spatialLayerId: layerId,
                sourceWidth: 0,
                sourceHeight: 0,
                encodedWidth: config.width,
                encodedHeight: config.height,
                stats: undefined as unknown as VideoRecordingStats,
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
        encoderConfigs: [cfg],
        keyframeIntervalFrames: 30,
        createSender: () => new FakeSender(),
        createEncoder: makeEncoderFactory(),
        createProcessor: makeProcessorFromQueue(frames),
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
        expect(sender.sent[0].isKeyFrame).toBe(true);
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
        expect(stats!.framesCaptured).toBe(3);
        expect(stats!.chunksEncoded).toBe(3);
        expect(stats!.keyframesEncoded).toBeGreaterThanOrEqual(1);
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

    it('restart: stop+start cycle reuses the encoder pool, second run produces frames', async () => {
        const session = new SenderSession();
        const recorder = new Recorder(session);

        // First run: 1 frame; bind the encoder factory through the pool.
        const sender1 = new FakeSender();
        let acquired: { release: () => void }[] = [];
        const poolFactory: RecorderConfig['createEncoder'] = (lc, lid) => {
            const handle = session.encoderPool.acquire(
                'h264',
                () => makeEncoderFactory()(lc, lid),
            );
            acquired.push(handle);
            return handle.encoder;
        };
        const run1 = recorder.start(buildConfig({
            createSender: () => sender1,
            createEncoder: poolFactory,
            createProcessor: makeProcessorFromQueue([new MockVideoFrame(0)]),
        }));
        await driveToCompletion(run1);
        // Release back to pool so it can be reused on restart.
        for (const h of acquired) h.release();
        acquired = [];
        expect(session.encoderPool.parkedCount).toBe(1);

        // restart() — second run reuses the parked encoder; factory is
        // NOT called again at the operator boundary (acquire returns the
        // parked instance).
        const sender2 = new FakeSender();
        const factoryCallsBefore = MockVideoEncoder.instances.length;
        const run2Promise = recorder.restart(buildConfig({
            createSender: () => sender2,
            createEncoder: (lc, lid) => {
                const handle = session.encoderPool.acquire(
                    'h264',
                    () => makeEncoderFactory()(lc, lid),
                );
                acquired.push(handle);
                return handle.encoder;
            },
            createProcessor: makeProcessorFromQueue([new MockVideoFrame(0)]),
        }));
        await driveToCompletion(run2Promise);
        // No NEW MockVideoEncoder instance was created — pool reused the parked one.
        expect(MockVideoEncoder.instances.length).toBe(factoryCallsBefore);
        expect(sender2.sent.length).toBe(1);
        for (const h of acquired) h.release();
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
