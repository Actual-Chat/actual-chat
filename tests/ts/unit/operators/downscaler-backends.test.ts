import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
    createDownscalerForMode,
    type LayerSpec,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import { CanvasDownscaler } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/canvas/downscaler';
import { MetadataDownscaler } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/metadata/downscaler';

// Minimal VideoFrame mock: the ceiling carries display dims; a metadata wrap
// `new VideoFrame(src, { displayWidth, displayHeight })` records the requested
// display size and keeps the source's coded plane (the Edge-cropping behavior).
class MockVideoFrame {
    closed = false;
    codedWidth: number;
    codedHeight: number;
    displayWidth: number;
    displayHeight: number;
    timestamp = 0;
    constructor(
        source: number | MockVideoFrame,
        init?: { displayWidth?: number; displayHeight?: number; timestamp?: number },
    ) {
        if (typeof source === 'number') {
            this.codedWidth = 1280;
            this.codedHeight = 720;
            this.displayWidth = 1280;
            this.displayHeight = 720;
            return;
        }
        // Wrap: coded stays at the source plane; display overridden.
        this.codedWidth = source.codedWidth;
        this.codedHeight = source.codedHeight;
        this.displayWidth = init?.displayWidth ?? source.displayWidth;
        this.displayHeight = init?.displayHeight ?? source.displayHeight;
    }
    close(): void { this.closed = true; }
}

beforeEach(() => {
    (globalThis as unknown as { VideoFrame?: typeof MockVideoFrame }).VideoFrame = MockVideoFrame;
});
afterEach(() => {
    delete (globalThis as unknown as { VideoFrame?: typeof MockVideoFrame }).VideoFrame;
});

describe('createDownscalerForMode', () => {
    it('selects the canvas backend', () => {
        expect(createDownscalerForMode('canvas')).toBeInstanceOf(CanvasDownscaler);
    });
    it('selects the metadata backend', () => {
        expect(createDownscalerForMode('metadata')).toBeInstanceOf(MetadataDownscaler);
    });
});

describe('MetadataDownscaler', () => {
    const ladder: LayerSpec[] = [
        { width: 640, height: 360 },
        { width: 1280, height: 720 },
    ];

    it('passes the ceiling through for the matching top tier and wraps lower tiers by display size; never closes input', async () => {
        const input = new MockVideoFrame(0) as unknown as VideoFrame;
        const out = await new MetadataDownscaler().process(input, ladder);

        expect(out).toHaveLength(2);
        // Top tier (1280x720) == ceiling display dims → the input itself.
        expect(out[1]).toBe(input);
        // Lower tier wrap: coded stays at the ceiling plane, display shrinks.
        const lower = out[0] as unknown as MockVideoFrame;
        expect(lower).not.toBe(input);
        expect(lower.displayWidth).toBe(640);
        expect(lower.displayHeight).toBe(360);
        expect(lower.codedWidth).toBe(1280);
        expect(lower.codedHeight).toBe(720);
        // The caller owns the ceiling — the backend must not close it.
        expect((input as unknown as MockVideoFrame).closed).toBe(false);
    });
});
