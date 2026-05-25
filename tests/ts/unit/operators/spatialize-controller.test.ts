import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { from, toArray } from 'ix-ext';
import { spatialize } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import { LayerLadderController } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import type {
    CapturedBundle,
    NormalizedFrame,
    RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { createEmptyRecorderStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

class MockVideoFrame {
    constructor(public id: number, public codedWidth: number, public codedHeight: number) {}
    displayWidth = 0;
    displayHeight = 0;
    timestamp = 0;
    close(): void { /* no-op */ }
}

class MockCanvasContext {
    imageSmoothingEnabled = true;
    imageSmoothingQuality: ImageSmoothingQuality = 'medium';
    drawImage(): void { /* no-op */ }
}

class MockOffscreenCanvas {
    constructor(public width: number, public height: number) {}
    readonly ctx = new MockCanvasContext();
    getContext(): MockCanvasContext { return this.ctx; }
}

const cfg = (width: number, height: number): EncoderConfigPerLayer => ({
    width, height,
    bitrate: 500_000,
    framerate: 30,
    codec: 'avc1.640028',
});

function mkNormalized(index: number, stats: RecorderStats): NormalizedFrame {
    return {
        frame: new MockVideoFrame(index, 1280, 720) as unknown as VideoFrame,
        capturedAt: { timeMs: index * 33, epoch: 0 },
        index,
        dropTrace: [],
        sourceWidth: 1280,
        sourceHeight: 720,
        forceKeyframe: false,
        rotation: 0,
        stats,
    };
}

interface MockGlobals {
    VideoFrame?: typeof MockVideoFrame;
    OffscreenCanvas?: typeof MockOffscreenCanvas;
}

describe('spatialize with LayerLadderController', () => {
    beforeEach(() => {
        (globalThis as unknown as MockGlobals).VideoFrame = MockVideoFrame;
        (globalThis as unknown as MockGlobals).OffscreenCanvas = MockOffscreenCanvas;
    });

    afterEach(() => {
        delete (globalThis as unknown as MockGlobals).VideoFrame;
        delete (globalThis as unknown as MockGlobals).OffscreenCanvas;
    });

    it('bundle.layers.length follows the controller across reconfigure', async () => {
        const controller = new LayerLadderController([cfg(1280, 720)]);
        const stats = createEmptyRecorderStats();
        const inputs: NormalizedFrame[] = [];
        for (let i = 0; i < 6; i++) inputs.push(mkNormalized(i, stats));

        async function* source(): AsyncIterable<NormalizedFrame> {
            for (let i = 0; i < inputs.length; i++) {
                if (i === 3) controller.setConfigs([cfg(640, 360), cfg(1280, 720)]);
                yield inputs[i];
                await Promise.resolve();
            }
        }

        const op = spatialize({ controller });
        const out: CapturedBundle[] = await toArray(op(from(source())));

        expect(out).toHaveLength(6);
        for (let i = 0; i < 3; i++) expect(out[i].layers).toHaveLength(1);
        for (let i = 3; i < 6; i++) expect(out[i].layers).toHaveLength(2);
    });

    it('shrinks: a 3-layer ladder dropping to 1 yields 1-layer bundles', async () => {
        const controller = new LayerLadderController([
            cfg(320, 180), cfg(640, 360), cfg(1280, 720),
        ]);
        const stats = createEmptyRecorderStats();
        const inputs: NormalizedFrame[] = [];
        for (let i = 0; i < 4; i++) inputs.push(mkNormalized(i, stats));

        async function* source(): AsyncIterable<NormalizedFrame> {
            for (let i = 0; i < inputs.length; i++) {
                if (i === 2) controller.setConfigs([cfg(1280, 720)]);
                yield inputs[i];
                await Promise.resolve();
            }
        }

        const op = spatialize({ controller });
        const out: CapturedBundle[] = await toArray(op(from(source())));

        expect(out[0].layers).toHaveLength(3);
        expect(out[1].layers).toHaveLength(3);
        expect(out[2].layers).toHaveLength(1);
        expect(out[3].layers).toHaveLength(1);
    });
});
