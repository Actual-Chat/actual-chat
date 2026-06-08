import { describe, it, expect } from 'vitest';
import { from, toArray } from 'ix-ext';
import { normalizeDownscale } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import type { DownscalerLike, LayerSpec } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import { LayerLadderController } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';
import type {
    CapturedBundle,
    CapturedFrame,
    RecorderStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { createEmptyRecorderStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// Ceiling matches the captured-frame dims so normalizeDownscale takes the
// identity pass-through (ceiling === input) — no real canvas/WebGL needed.
const CEIL_W = 1280;
const CEIL_H = 720;

class MockVideoFrame {
    readonly codedWidth = CEIL_W;
    readonly codedHeight = CEIL_H;
    readonly displayWidth = CEIL_W;
    readonly displayHeight = CEIL_H;
    constructor(public id: number) {}
    close(): void { /* no-op */ }
}

// Fake downscaler: one frame per layer (top = input, lower tiers = fresh
// mocks). Exercises the operator's layer-count / controller plumbing without a
// real WebGL/Canvas context. Does NOT close the input (caller owns the ceiling).
class FakeDownscaler implements DownscalerLike {
    private next = 1000;
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const topIdx = layers.length - 1;
        const out: VideoFrame[] = [];
        for (let i = 0; i < layers.length; i++)
            out.push(i === topIdx ? input : (new MockVideoFrame(this.next++) as unknown as VideoFrame));
        return out;
    }
}

const cfg = (width: number, height: number): EncoderConfigPerLayer => ({
    width, height,
    bitrate: 500_000,
    framerate: 30,
    codec: 'avc1.640028',
});

function mkCaptured(index: number, stats: RecorderStats): CapturedFrame {
    return {
        frame: new MockVideoFrame(index) as unknown as VideoFrame,
        capturedAt: { timeMs: index * 33, epoch: 0 },
        index,
        dropTrace: [],
        sourceWidth: CEIL_W,
        sourceHeight: CEIL_H,
        forceKeyframe: false,
        rotation: 0,
        stats,
    };
}

function mkOp(controller: LayerLadderController) {
    return normalizeDownscale({
        controller,
        getNormalizeSize: () => ({ width: CEIL_W, height: CEIL_H }),
        isCamera: false,
        isFrontCamera: false,
        isIos: false,
        createDownscaler: () => new FakeDownscaler(),
    });
}

describe('normalizeDownscale with LayerLadderController', () => {
    it('bundle.layers.length follows the controller across reconfigure', async () => {
        const controller = new LayerLadderController([cfg(1280, 720)]);
        const stats = createEmptyRecorderStats();
        const inputs: CapturedFrame[] = [];
        for (let i = 0; i < 6; i++) inputs.push(mkCaptured(i, stats));

        async function* source(): AsyncIterable<CapturedFrame> {
            for (let i = 0; i < inputs.length; i++) {
                if (i === 3) controller.setConfigs([cfg(640, 360), cfg(1280, 720)]);
                yield inputs[i];
                await Promise.resolve();
            }
        }

        const out: CapturedBundle[] = await toArray(mkOp(controller)(from(source())));

        expect(out).toHaveLength(6);
        for (let i = 0; i < 3; i++) expect(out[i].layers).toHaveLength(1);
        for (let i = 3; i < 6; i++) expect(out[i].layers).toHaveLength(2);
        // Top tier matches the ceiling, so ceiling === the top layer's frame.
        for (const b of out) expect(b.ceiling).toBe(b.layers[b.layers.length - 1].frame);
    });

    it('shrinks: a 3-layer ladder dropping to 1 yields 1-layer bundles', async () => {
        const controller = new LayerLadderController([
            cfg(320, 184), cfg(640, 360), cfg(1280, 720),
        ]);
        const stats = createEmptyRecorderStats();
        const inputs: CapturedFrame[] = [];
        for (let i = 0; i < 4; i++) inputs.push(mkCaptured(i, stats));

        async function* source(): AsyncIterable<CapturedFrame> {
            for (let i = 0; i < inputs.length; i++) {
                if (i === 2) controller.setConfigs([cfg(1280, 720)]);
                yield inputs[i];
                await Promise.resolve();
            }
        }

        const out: CapturedBundle[] = await toArray(mkOp(controller)(from(source())));

        expect(out[0].layers).toHaveLength(3);
        expect(out[1].layers).toHaveLength(3);
        expect(out[2].layers).toHaveLength(1);
        expect(out[3].layers).toHaveLength(1);
    });
});
