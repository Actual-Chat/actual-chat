import { describe, it, expect } from 'vitest';
import {
    EncodedFrameBuffer,
    type EncodedFrameBufferPushResult,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type PlayerStats,
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
    stats?: PlayerStats;
}

interface ChunkWithDispose extends ArrivedChunk {
    dispose?: () => void;
    disposed?: boolean;
}

function mkChunk(opts: ChunkOpts): ChunkWithDispose {
    const stats = opts.stats ?? createEmptyPlayerStats();
    const out: ChunkWithDispose = {
        chunk: {} as EncodedVideoChunk,
        arrivedAt: { timeMs: opts.arrivedAtMs ?? opts.capturedAtMs, epoch: 0 },
        capturedAt: { timeMs: opts.capturedAtMs, epoch: opts.epoch ?? 0 },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame: opts.isKeyFrame,
        layerId: opts.layerId ?? 0,
        width: opts.width ?? 640,
        height: opts.height ?? 480,
        rawByteLength: opts.rawByteLength ?? 1024,
        rotation: 0,
        stats,
        disposed: false,
    };
    out.dispose = () => { out.disposed = true; };
    return out;
}

// ---- Tests ----------------------------------------------------------------

describe('EncodedFrameBuffer', () => {
    it('starts in reset state with count 0, spanMs 0, not ready', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
        expect(buf.spanMs()).toBe(0);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
    });

    it('drops deltas while in reset state and disposes them; remains in reset', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const delta = mkChunk({ capturedAtMs: 100, isKeyFrame: false });
        const result: EncodedFrameBufferPushResult = buf.push(delta);
        expect(result).toBe('droppedReset');
        expect(delta.disposed).toBe(true);
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
    });

    it('first keyframe transitions reset → armed; result is "armed"', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        const kf = mkChunk({ capturedAtMs: 100, isKeyFrame: true });
        const result = buf.push(kf);
        expect(result).toBe('armed');
        expect(buf.isReset()).toBe(false);
        expect(buf.count()).toBe(1);
    });

    it('subsequent deltas accepted in armed state', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        buf.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        const r = buf.push(mkChunk({ capturedAtMs: 133, isKeyFrame: false }));
        expect(r).toBe('accepted');
        expect(buf.count()).toBe(2);
        expect(buf.isReset()).toBe(false);
    });

    it('not ready until span ≥ targetSpanMs', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        // 5 frames at 33 ms cadence: last-first = 132, +33.333 trailing-frame
        // approximation = 165.333 ms (< 200) — not ready.
        for (let i = 0; i < 5; i++) {
            buf.push(mkChunk({ capturedAtMs: 100 + i * 33, isKeyFrame: i === 0 }));
        }
        expect(buf.spanMs()).toBeCloseTo(165.333, 3);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();

        // Add one more — 165 + 33.333 = 198.333, still under target.
        buf.push(mkChunk({ capturedAtMs: 100 + 5 * 33, isKeyFrame: false }));
        expect(buf.spanMs()).toBeCloseTo(198.333, 3);
        expect(buf.isReady()).toBe(false);
    });

    it('ready as soon as span ≥ targetSpanMs (no wallclock gate)', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, frameDurationMs: 33.333 });
        // 6 frames @ 33 ms: last-first=165, +33.333 = 198.333 — still < 200.
        for (let i = 0; i < 6; i++) {
            buf.push(mkChunk({ capturedAtMs: 100 + i * 33, isKeyFrame: i === 0 }));
        }
        expect(buf.spanMs()).toBeCloseTo(198.333, 3);
        expect(buf.isReady()).toBe(false);
        // 7th frame: last-first=198, +33.333 = 231.333 — clears 200.
        buf.push(mkChunk({ capturedAtMs: 100 + 6 * 33, isKeyFrame: false }));
        expect(buf.spanMs()).toBeCloseTo(231.333, 3);
        expect(buf.isReady()).toBe(true);
    });

    it('tryPull drains FIFO until residual span drops below target', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
        // 5 chunks @ 33 ms: capturedAt = 0,33,66,99,132. spanMs = 132+33.333 = 165.333.
        // After pulling chunk 0: residual = 99+33.333 = 132.333 (≥ 100) → pull again.
        // After pulling chunk 1: residual = 66+33.333 = 99.333 (< 100) → stop.
        const chunks: ChunkWithDispose[] = [];
        for (let i = 0; i < 5; i++) {
            const c = mkChunk({ capturedAtMs: i * 33, isKeyFrame: i === 0 });
            chunks.push(c);
            buf.push(c);
        }
        expect(buf.tryPull()).toBe(chunks[0]);
        expect(buf.tryPull()).toBe(chunks[1]);
        expect(buf.tryPull()).toBeNull();
        expect(buf.count()).toBe(3);
    });

    it('tryPull drains multiple chunks in one go when span permits', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 50, frameDurationMs: 33.333 });
        // 5 chunks @ 33 ms: spanMs = 132+33.333 = 165.333.
        // After chunk 0: residual = 99+33.333 = 132.333 (≥ 50) → pull.
        // After chunk 1: residual = 66+33.333 = 99.333 (≥ 50) → pull.
        // After chunk 2: residual = 33+33.333 = 66.333 (≥ 50) → pull.
        // After chunk 3: residual = 0+33.333 = 33.333 (< 50) → stop.
        const chunks: ChunkWithDispose[] = [];
        for (let i = 0; i < 5; i++) {
            const c = mkChunk({ capturedAtMs: i * 33, isKeyFrame: i === 0 });
            chunks.push(c);
            buf.push(c);
        }
        const drained: ArrivedChunk[] = [];
        let next: ArrivedChunk | null;
        while ((next = buf.tryPull()) !== null) drained.push(next);
        expect(drained).toEqual([chunks[0], chunks[1], chunks[2], chunks[3]]);
        expect(buf.count()).toBe(1);
    });

    it('reset clears all chunks, returns to reset state, disposes content', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
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
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
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
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
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
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
        buf.push(mkChunk({ capturedAtMs: 5000, isKeyFrame: true, epoch: 1 }));
        const newEpoch = mkChunk({ capturedAtMs: 100, isKeyFrame: true, epoch: 2 });
        expect(buf.detectRegression(newEpoch, 50)).toBe(false);
    });

    it('single keyframe contributes one frame duration; ready iff target ≤ frameDurationMs', () => {
        // With one chunk the trailing-frame approximation gives spanMs ≈
        // frameDurationMs (33.333). So target=1 → ready, target=100 → not.
        const tinyTarget = new EncodedFrameBuffer({ targetSpanMs: 1, frameDurationMs: 33.333 });
        tinyTarget.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        expect(tinyTarget.spanMs()).toBeCloseTo(33.333, 3);
        expect(tinyTarget.isReady()).toBe(true);

        const largeTarget = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
        largeTarget.push(mkChunk({ capturedAtMs: 100, isKeyFrame: true }));
        expect(largeTarget.isReady()).toBe(false);
        expect(largeTarget.tryPull()).toBeNull();
    });

    it('empty buffer: spanMs is exactly 0 (no frames at all)', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, frameDurationMs: 33.333 });
        expect(buf.spanMs()).toBe(0);
    });
});
