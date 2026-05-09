import { describe, it, expect } from 'vitest';
import {
    EncodedFrameBuffer,
    type EncodedFrameBufferPushResult,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import {
    createEmptyPlaybackStats,
    type ArrivedChunk,
    type VideoPlaybackStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// ---- Helpers --------------------------------------------------------------

interface ChunkOpts {
    capturedAtMs: number;
    arrivedAtMs?: number;
    isKeyFrame: boolean;
    epoch?: number;
    layerId?: number;
    rawByteLength?: number;
    width?: number;
    height?: number;
    stats?: VideoPlaybackStats;
}

interface ChunkWithDispose extends ArrivedChunk {
    dispose?: () => void;
    disposed?: boolean;
}

function mkChunk(opts: ChunkOpts): ChunkWithDispose {
    const stats = opts.stats ?? createEmptyPlaybackStats(0);
    const out: ChunkWithDispose = {
        chunk: {} as EncodedVideoChunk,
        arrivedAt: { timeMs: opts.arrivedAtMs ?? opts.capturedAtMs, epoch: 0 },
        capturedAt: { timeMs: opts.capturedAtMs, epoch: opts.epoch ?? 0 },
        isKeyFrame: opts.isKeyFrame,
        layerId: opts.layerId ?? 0,
        width: opts.width ?? 640,
        height: opts.height ?? 480,
        rawByteLength: opts.rawByteLength ?? 1024,
        stats,
        disposed: false,
    };
    out.dispose = () => { out.disposed = true; };
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('EncodedFrameBuffer', () => {
    it('starts in reset state with count 0, spanMs 0, not ready', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
        expect(buf.spanMs()).toBe(0);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
    });

    it('drops deltas while in reset state and disposes them; remains in reset', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        const delta = mkChunk({ capturedAtMs: 100, isKeyFrame: false });
        const result: EncodedFrameBufferPushResult = buf.push(delta);
        expect(result).toBe('droppedReset');
        expect(delta.disposed).toBe(true);
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
    });

    it('first keyframe transitions reset → armed; result is "armed"', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        const kf = mkChunk({ capturedAtMs: 100, isKeyFrame: true });
        const result = buf.push(kf);
        expect(result).toBe('armed');
        expect(buf.isReset()).toBe(false);
        expect(buf.count()).toBe(1);
    });

    it('subsequent deltas accepted in armed state', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        buf.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        const r = buf.push(mkChunk({ capturedAtMs: 133, isKeyFrame: false }));
        expect(r).toBe('accepted');
        expect(buf.count()).toBe(2);
        expect(buf.isReset()).toBe(false);
    });

    it('not ready until span ≥ targetSpanMs', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        // 5 frames at 33 ms cadence = 132 ms span (< 200) — not ready.
        for (let i = 0; i < 5; i++) {
            buf.push(mkChunk({ capturedAtMs: 100 + i * 33, isKeyFrame: i === 0 }));
        }
        expect(buf.spanMs()).toBe(132);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();

        // Add one more — span 165, still under target.
        buf.push(mkChunk({ capturedAtMs: 100 + 5 * 33, isKeyFrame: false }));
        expect(buf.spanMs()).toBe(165);
        expect(buf.isReady()).toBe(false);
    });

    it('ready as soon as span ≥ targetSpanMs (no wallclock gate)', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200 });
        // 7 frames @ 33 ms span = 198 — still < 200; need 8.
        for (let i = 0; i < 7; i++) {
            buf.push(mkChunk({ capturedAtMs: 100 + i * 33, isKeyFrame: i === 0 }));
        }
        expect(buf.spanMs()).toBe(198);
        expect(buf.isReady()).toBe(false);
        buf.push(mkChunk({ capturedAtMs: 100 + 7 * 33, isKeyFrame: false }));
        expect(buf.spanMs()).toBe(231);
        expect(buf.isReady()).toBe(true);
    });

    it('tryPull drains FIFO until residual span drops below target', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100 });
        // 5 chunks @ 33 ms cadence: capturedAt = 0,33,66,99,132. Span 132.
        // After pulling chunk 0: residual span = 132 - 33 = 99 (< 100) → stop.
        const chunks: ChunkWithDispose[] = [];
        for (let i = 0; i < 5; i++) {
            const c = mkChunk({ capturedAtMs: i * 33, isKeyFrame: i === 0 });
            chunks.push(c);
            buf.push(c);
        }
        expect(buf.tryPull()).toBe(chunks[0]);
        expect(buf.tryPull()).toBeNull();
        expect(buf.count()).toBe(4);
    });

    it('tryPull drains multiple chunks in one go when span permits', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 50 });
        // 5 chunks @ 33 ms: span 132.
        // After chunk 0 pulled: residual = 99 (≥ 50) → pull again.
        // After chunk 1 pulled: residual = 66 (≥ 50) → pull again.
        // After chunk 2 pulled: residual = 33 (< 50) → stop.
        const chunks: ChunkWithDispose[] = [];
        for (let i = 0; i < 5; i++) {
            const c = mkChunk({ capturedAtMs: i * 33, isKeyFrame: i === 0 });
            chunks.push(c);
            buf.push(c);
        }
        const drained: ArrivedChunk[] = [];
        let next: ArrivedChunk | null;
        while ((next = buf.tryPull()) !== null) drained.push(next);
        expect(drained).toEqual([chunks[0], chunks[1], chunks[2]]);
        expect(buf.count()).toBe(2);
    });

    it('reset clears all chunks, returns to reset state, disposes content', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100 });
        const a = mkChunk({ capturedAtMs: 100, isKeyFrame: true });
        const b = mkChunk({ capturedAtMs: 133, isKeyFrame: false });
        buf.push(a);
        buf.push(b);
        expect(buf.count()).toBe(2);

        buf.reset();
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
        expect(buf.spanMs()).toBe(0);
        expect(a.disposed).toBe(true);
        expect(b.disposed).toBe(true);
    });

    it('after reset, deltas drop and only the next keyframe re-arms the buffer', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100 });
        buf.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        buf.reset();
        const delta = mkChunk({ capturedAtMs: 200, isKeyFrame: false });
        expect(buf.push(delta)).toBe('droppedReset');
        expect(delta.disposed).toBe(true);
        const kf = mkChunk({ capturedAtMs: 233, isKeyFrame: true });
        expect(buf.push(kf)).toBe('armed');
        expect(buf.isReset()).toBe(false);
    });

    it('detectRegression: false on forward progress, true on backwards beyond tolerance', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100 });
        buf.push(mkChunk({ capturedAtMs: 1000, isKeyFrame: true }));
        const fwd = mkChunk({ capturedAtMs: 1033, isKeyFrame: false });
        expect(buf.detectRegression(fwd, 50)).toBe(false);
        const back = mkChunk({ capturedAtMs: 900, isKeyFrame: false });
        expect(buf.detectRegression(back, 50)).toBe(true);
        // Within tolerance.
        const slightlyBack = mkChunk({ capturedAtMs: 970, isKeyFrame: false });
        expect(buf.detectRegression(slightlyBack, 50)).toBe(false);
    });

    it('detectRegression returns false when comparing across epoch boundary', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100 });
        buf.push(mkChunk({ capturedAtMs: 5000, isKeyFrame: true, epoch: 1 }));
        const newEpoch = mkChunk({ capturedAtMs: 100, isKeyFrame: true, epoch: 2 });
        expect(buf.detectRegression(newEpoch, 50)).toBe(false);
    });

    it('single keyframe is not ready when targetSpanMs > 0 (span 0 < target)', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 1 });
        buf.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        // span is 0 (only one chunk) → not ready.
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
    });
});
