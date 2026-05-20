import { describe, it, expect, vi } from 'vitest';
import { resetOnEpochChange } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/epoch-reset';
import { EncodedFrameBuffer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
// ---- Helpers --------------------------------------------------------------

function mkChunk(timeMs: number, epoch: number, isKeyFrame = false): ArrivedChunk {
    return {
        chunk: {} as EncodedVideoChunk,
        arrivedAt: { timeMs, epoch: 0 },
        capturedAt: { timeMs, epoch },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame,
        layerId: 0,
        width: 640,
        height: 480,
        rawByteLength: 1024,
        rotation: 0,
        stats: createEmptyPlayerStats(),
    };
}

function source(items: ArrivedChunk[]): AsyncIterable<ArrivedChunk> {
    return (async function* () {
        await Promise.resolve();
        for (const item of items) yield item;
    })();
}

async function drain<T>(seg: AsyncIterable<T>): Promise<T[]> {
    const out: T[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('resetOnEpochChange', () => {
    it('stable epoch: passes through unchanged, no reset called', async () => {
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const resetSpy = vi.spyOn(buffer, 'reset');
        const op = resetOnEpochChange({ buffer });
        const chunks = [
            mkChunk(100, 1, true),
            mkChunk(133, 1, false),
            mkChunk(166, 1, false),
        ];

        const out = await drain(op(source(chunks)));

        expect(out).toHaveLength(3);
        expect(out).toEqual(chunks);
        expect(resetSpy).not.toHaveBeenCalled();
    });

    it('first chunk does not trigger reset (no prior epoch to compare)', async () => {
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const resetSpy = vi.spyOn(buffer, 'reset');
        const op = resetOnEpochChange({ buffer });
        const chunks = [mkChunk(100, 5, true)]; // even non-zero epoch on first chunk

        const out = await drain(op(source(chunks)));

        expect(out).toHaveLength(1);
        expect(resetSpy).not.toHaveBeenCalled();
    });

    it('epoch change → buffer.reset() called once at the boundary, chunk yielded', async () => {
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const resetSpy = vi.spyOn(buffer, 'reset');
        const op = resetOnEpochChange({ buffer });
        const chunks = [
            mkChunk(100, 1, true),
            mkChunk(133, 1, false),
            mkChunk(166, 2, true),  // epoch change here
            mkChunk(200, 2, false),
        ];

        const out = await drain(op(source(chunks)));

        expect(out).toHaveLength(4);
        expect(out).toEqual(chunks);
        expect(resetSpy).toHaveBeenCalledTimes(1);
    });

    it('multiple epoch changes → multiple resets, one per boundary', async () => {
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const resetSpy = vi.spyOn(buffer, 'reset');
        const op = resetOnEpochChange({ buffer });
        const chunks = [
            mkChunk(100, 1, true),
            mkChunk(133, 2, true), // boundary 1→2
            mkChunk(166, 2, false),
            mkChunk(200, 3, true), // boundary 2→3
        ];

        await drain(op(source(chunks)));

        expect(resetSpy).toHaveBeenCalledTimes(2);
    });

    it('epoch 0 → 0 (sender never resyncs) does not trigger any reset', async () => {
        const buffer = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const resetSpy = vi.spyOn(buffer, 'reset');
        const op = resetOnEpochChange({ buffer });
        const chunks = [
            mkChunk(100, 0, true),
            mkChunk(133, 0, false),
            mkChunk(166, 0, false),
        ];

        await drain(op(source(chunks)));

        expect(resetSpy).not.toHaveBeenCalled();
    });
});
