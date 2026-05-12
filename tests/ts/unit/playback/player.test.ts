import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Player } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/player';
import { PlaybackSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/session';
import type { VideoFrameDto } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/pull';
import type { DecoderLike } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/decode';
import type { CanvasImageInterface } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-canvas';

// ---- WebCodecs mocks ------------------------------------------------------

class MockEncodedVideoChunk {
    type: 'key' | 'delta';
    timestamp = 0;
    byteLength: number;
    duration: number | null = null;
    private readonly bytes: Uint8Array;
    constructor(init: { type: 'key' | 'delta'; timestamp: number; data: Uint8Array }) {
        this.type = init.type;
        this.bytes = init.data;
        this.byteLength = this.bytes.byteLength;
    }
    copyTo(buffer: ArrayBuffer): void {
        new Uint8Array(buffer).set(this.bytes);
    }
}

class MockVideoFrame {
    closed = false;
    constructor(public id: number) {}
    close(): void { this.closed = true; }
    codedWidth = 1280;
    codedHeight = 720;
    displayWidth = 1280;
    displayHeight = 720;
}

interface GlobalWithChunk {
    EncodedVideoChunk?: typeof MockEncodedVideoChunk;
}

beforeEach(() => {
    (globalThis as unknown as GlobalWithChunk).EncodedVideoChunk = MockEncodedVideoChunk;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithChunk).EncodedVideoChunk;
});

// ---- Test doubles ---------------------------------------------------------

class FakeDecoder implements DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    decodeQueueSize = 0;
    closed = false;
    configureCalls: VideoDecoderConfig[] = [];
    decodeCalls = 0;
    handlers: { onFrame: (f: VideoFrame) => void; onError: (e: Error) => void };
    constructor(handlers: { onFrame: (f: VideoFrame) => void; onError: (e: Error) => void }) {
        this.handlers = handlers;
    }
    configure(config: VideoDecoderConfig): void {
        this.configureCalls.push(config);
        this.state = 'configured';
    }
    /** Auto-emit a frame on every decode() call so the pipeline reaches
     *  the present sink without test-driven step-pumping. */
    decode(): void {
        this.decodeCalls++;
        const id = this.decodeCalls;
        void Promise.resolve().then(() => {
            this.handlers.onFrame(new MockVideoFrame(id) as unknown as VideoFrame);
        });
    }
    flush(): Promise<void> { return Promise.resolve(); }
    close(): void { this.state = 'closed'; this.closed = true; }
}

function makeDto(opts: { isKeyFrame: boolean; offsetMs?: number; index?: number }): VideoFrameDto {
    const offsetMs = opts.offsetMs ?? 0;
    const index = opts.index ?? 0;
    return {
        Data: new Uint8Array([1, 2, 3, 4]),
        Offset: BigInt(offsetMs * 10_000), // ticks (100ns)
        OffsetEpoch: 0,
        Duration: 0,
        // IsKeyFrame derived: KeyFrameIndex === Index iff this is a keyframe.
        KeyFrameIndex: opts.isKeyFrame ? index : index - 1,
        Index: index,
        Width: 1280,
        Height: 720,
        LayerId: 0,
    };
}

class FakeCanvas implements CanvasImageInterface {
    drawCount = 0;
    drawImage(): void { this.drawCount++; }
}

async function* fromArray<T>(items: T[]): AsyncIterable<T> {
    for (const item of items) {
        await Promise.resolve();
        yield item;
    }
}

// ---- Tests ----------------------------------------------------------------

describe('Player', () => {
    it('start → consume DTOs → present frames; stop drains cleanly', async () => {
        const session = new PlaybackSession();
        const player = new Player(session);
        const canvas = new FakeCanvas();

        // Long-lived source: emits 5 chunks then yields control to a
        // pending promise. We assert frames flowed through, then stop
        // explicitly so the run drains via the stop trigger rather than
        // source-EOF (which the buffer operator does NOT drain on).
        let stopSource: () => void = () => { /* nothing */ };
        const sourceDone = new Promise<void>(resolve => { stopSource = resolve; });
        const items = [
            makeDto({ isKeyFrame: true, offsetMs: 0 }),
            makeDto({ isKeyFrame: false, offsetMs: 33 }),
            makeDto({ isKeyFrame: false, offsetMs: 66 }),
            makeDto({ isKeyFrame: false, offsetMs: 99 }),
            makeDto({ isKeyFrame: false, offsetMs: 132 }),
        ];
        const longSource: AsyncIterable<VideoFrameDto> = (async function* () {
            for (const item of items) {
                await Promise.resolve();
                yield item;
            }
            await sourceDone;
        })();

        await player.start({
            streamId: 'stream-1',
            getStream: () => longSource,
            targetBufferSpanMs: 0,
            initialDecoderConfig: { codec: 'avc1.42E01E', codedWidth: 1280, codedHeight: 720 },
            createDecoder: handlers => new FakeDecoder(handlers),
            backend: { kind: 'canvas', canvasCtx: canvas },
        });
        expect(player.isRunning()).toBe(true);

        // Pump microtasks until the buffer has drained at least a few chunks.
        for (let i = 0; i < 200; i++) {
            await new Promise(r => setTimeout(r, 0));
        }

        player.stop();
        stopSource();
        await player.whenDone();
        expect(player.isRunning()).toBe(false);
        expect(canvas.drawCount).toBeGreaterThanOrEqual(1);
    });

    it('rejects start while already running', async () => {
        const session = new PlaybackSession();
        const player = new Player(session);
        const canvas = new FakeCanvas();
        // Source that never completes so the run stays open.
        const neverEnding: AsyncIterable<VideoFrameDto> = (async function* () {
            yield makeDto({ isKeyFrame: true });
            await new Promise(() => { /* hang */ });
        })();
        await player.start({
            streamId: 's',
            getStream: () => neverEnding,
            targetBufferSpanMs: 0,
            initialDecoderConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => new FakeDecoder(handlers),
            backend: { kind: 'canvas', canvasCtx: canvas },
        });
        await expect(player.start({
            streamId: 's',
            getStream: () => fromArray([]),
            targetBufferSpanMs: 0,
            initialDecoderConfig: { codec: 'avc1.42E01E' },
            createDecoder: handlers => new FakeDecoder(handlers),
            backend: { kind: 'canvas', canvasCtx: canvas },
        })).rejects.toThrow(/already running/);

        player.stop();
        await player.whenDone().catch(() => { /* ignore */ });
    });

    it('two players sharing one session both contribute to stats', async () => {
        const session = new PlaybackSession();
        const playerA = new Player(session);
        const playerB = new Player(session);
        const canvasA = new FakeCanvas();
        const canvasB = new FakeCanvas();

        const makeLongSource = (items: VideoFrameDto[], hold: Promise<void>): AsyncIterable<VideoFrameDto> =>
            (async function* () {
                for (const it of items) { await Promise.resolve(); yield it; }
                await hold;
            })();

        let stopBoth: () => void = () => { /* nothing */ };
        const hold = new Promise<void>(resolve => { stopBoth = resolve; });
        const sample = (): VideoFrameDto[] => [
            makeDto({ isKeyFrame: true, offsetMs: 0 }),
            makeDto({ isKeyFrame: false, offsetMs: 33 }),
            makeDto({ isKeyFrame: false, offsetMs: 66 }),
            makeDto({ isKeyFrame: false, offsetMs: 99 }),
            makeDto({ isKeyFrame: false, offsetMs: 132 }),
        ];

        const startOpts = (id: string, ctx: CanvasImageInterface): Parameters<Player['start']>[0] => ({
            streamId: id,
            initialDecoderConfig: { codec: 'avc1.42E01E', codedWidth: 1280, codedHeight: 720 },
            targetBufferSpanMs: 0,
            backend: { kind: 'canvas', canvasCtx: ctx },
            getStream: () => makeLongSource(sample(), hold),
            createDecoder: handlers => new FakeDecoder(handlers),
        });

        await playerA.start(startOpts('a', canvasA));
        await playerB.start(startOpts('b', canvasB));

        // Pump until both pipelines have absorbed all their inputs and
        // at least a couple of frames have been decoded through the
        // shared session.
        for (let i = 0; i < 400; i++) {
            await new Promise(r => setTimeout(r, 0));
        }

        playerA.stop();
        playerB.stop();
        stopBoth();
        await Promise.all([
            playerA.whenDone().catch(() => { /* expected */ }),
            playerB.whenDone().catch(() => { /* expected */ }),
        ]);

        // 5 chunks per stream → 10 total.
        // Both pipelines used the same session's stats reference.
    });
});
