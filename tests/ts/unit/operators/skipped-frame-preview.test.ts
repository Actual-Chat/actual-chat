import { describe, it, expect } from 'vitest';
import {
    createSkippedFramePreview,
    NormalizeFrameOrientation,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/downscale';
import type { PreviewSink } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/preview-forwarder';
import type { RotationQuarter } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/orientation/quantize';

// Ceiling matches the frame dims, so the identity path runs - no canvas needed.
const CEIL_W = 640;
const CEIL_H = 480;

class MockVideoFrame {
    readonly codedWidth = CEIL_W;
    readonly codedHeight = CEIL_H;
    readonly displayWidth = CEIL_W;
    readonly displayHeight = CEIL_H;
    closed = false;
    constructor(public id: number) {}
    close(): void { this.closed = true; }
}

function mkPreview(sink: PreviewSink['forward']): (frame: VideoFrame) => void {
    return createSkippedFramePreview({
        orientation: new NormalizeFrameOrientation({ isCamera: false, isFrontCamera: false, isIos: false }),
        getNormalizeSize: () => ({ width: CEIL_W, height: CEIL_H }),
        preview: { forward: sink },
    });
}

describe('createSkippedFramePreview', () => {
    it('forwards the frame to the preview and then closes it', () => {
        // arrange
        const forwarded: { id: number; isClosed: boolean; rotation: RotationQuarter }[] = [];
        const preview = mkPreview((frame, rotation) => {
            const mock = frame as unknown as MockVideoFrame;
            forwarded.push({ id: mock.id, isClosed: mock.closed, rotation });
        });
        const frame = new MockVideoFrame(7);

        // act
        preview(frame as unknown as VideoFrame);

        // assert
        expect(forwarded).toEqual([{ id: 7, isClosed: false, rotation: 0 }]);
        expect(frame.closed).toBe(true);
    });

    it('closes the frame when the preview sink throws', () => {
        // arrange
        const preview = mkPreview(() => { throw new Error('synthetic'); });
        const frame = new MockVideoFrame(1);

        // act
        preview(frame as unknown as VideoFrame);

        // assert
        expect(frame.closed).toBe(true);
    });
});
