import { describe, it, expect } from 'vitest';
import { rotate } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/rotate';
import type { CapturedFrame, VideoRecordingStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import { createEmptyRecordingStats } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

interface FakeFrame { rotation?: number; codedWidth: number; codedHeight: number }

function envelope(frame: FakeFrame, stats: VideoRecordingStats): CapturedFrame {
    return {
        frame: frame as unknown as VideoFrame,
        capturedAt: { timeMs: 0, epoch: 0 },
        index: 0,
        dropTrace: [],
        sourceWidth: frame.codedWidth, sourceHeight: frame.codedHeight,
        forceKeyframe: false,
        stats,
    };
}

function source(frames: FakeFrame[], stats: VideoRecordingStats): AsyncIterable<CapturedFrame> {
    return (async function*() {
        await Promise.resolve();
        for (const f of frames) yield envelope(f, stats);
    })();
}

async function collect(seg: AsyncIterable<CapturedFrame>): Promise<CapturedFrame[]> {
    const out: CapturedFrame[] = [];
    for await (const item of seg) out.push(item);
    return out;
}

describe('rotate', () => {
    it('sets rotation when getRotationDeg returns non-zero', async () => {
        const stats = createEmptyRecordingStats(0);
        const f1 = { codedWidth: 1280, codedHeight: 720 };
        const op = rotate({ getRotationDeg: () => 90 });
        const seg: AsyncIterable<CapturedFrame> = op(source([f1], stats));
        const out = await collect(seg);
        expect((out[0].frame as unknown as FakeFrame).rotation).toBe(90);
    });

    it('does not set rotation when getRotationDeg returns 0', async () => {
        const stats = createEmptyRecordingStats(0);
        const f1 = { codedWidth: 1280, codedHeight: 720 };
        const op = rotate({ getRotationDeg: () => 0 });
        const seg: AsyncIterable<CapturedFrame> = op(source([f1], stats));
        const out = await collect(seg);
        expect((out[0].frame as unknown as FakeFrame).rotation).toBeUndefined();
    });

    it('reads rotation per frame (allows mid-stream change)', async () => {
        const stats = createEmptyRecordingStats(0);
        const f1 = { codedWidth: 1280, codedHeight: 720 };
        const f2 = { codedWidth: 1280, codedHeight: 720 };
        const f3 = { codedWidth: 1280, codedHeight: 720 };
        let rot = 0;
        const op = rotate({ getRotationDeg: () => rot });
        const driver = (async function*() {
            await Promise.resolve();
            yield envelope(f1, stats);
            rot = 90;
            yield envelope(f2, stats);
            rot = 180;
            yield envelope(f3, stats);
        })();
        const out: CapturedFrame[] = [];
        for await (const item of op(driver)) out.push(item);
        expect((out[0].frame as unknown as FakeFrame).rotation).toBeUndefined();
        expect((out[1].frame as unknown as FakeFrame).rotation).toBe(90);
        expect((out[2].frame as unknown as FakeFrame).rotation).toBe(180);
    });

    it('passes the envelope through (other fields preserved)', async () => {
        const stats = createEmptyRecordingStats(0);
        const f1 = { codedWidth: 1280, codedHeight: 720 };
        const op = rotate({ getRotationDeg: () => 90 });
        const seg: AsyncIterable<CapturedFrame> = op(source([f1], stats));
        const out = await collect(seg);
        expect(out[0].sourceWidth).toBe(1280);
        expect(out[0].sourceHeight).toBe(720);
        expect(out[0].stats).toBe(stats);
    });
});
