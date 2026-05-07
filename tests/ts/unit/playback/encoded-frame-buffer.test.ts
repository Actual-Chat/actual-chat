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
    arrivedAtMs: number;
    isKeyFrame: boolean;
    epoch?: number;
    spatialLayerId?: number;
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
        arrivedAt: { timeMs: opts.arrivedAtMs, epoch: 0 },
        capturedAt: { timeMs: opts.capturedAtMs, epoch: opts.epoch ?? 0 },
        isKeyFrame: opts.isKeyFrame,
        spatialLayerId: opts.spatialLayerId ?? 0,
        width: opts.width ?? 640,
        height: opts.height ?? 480,
        rawByteLength: opts.rawByteLength ?? 1024,
        stats,
        disposed: false,
    };
    out.dispose = () => { out.disposed = true; };
    return out;
}

class MockNow {
    constructor(public t: number) {}
    fn = (): number => this.t;
    advance(ms: number): void { this.t += ms; }
    setTo(ms: number): void { this.t = ms; }
}

// ---- Tests ----------------------------------------------------------------

describe('EncodedFrameBuffer', () => {
    it('starts in reset state with count 0, spanMs 0, not ready', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
        expect(buf.spanMs()).toBe(0);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
    });

    it('drops deltas while in reset state and disposes them; remains in reset', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        const delta = mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: false });
        const result: EncodedFrameBufferPushResult = buf.push(delta);
        expect(result).toBe('droppedReset');
        expect(delta.disposed).toBe(true);
        expect(buf.isReset()).toBe(true);
        expect(buf.count()).toBe(0);
    });

    it('first keyframe transitions reset → armed; result is "armed"', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        const kf = mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: true });
        const result = buf.push(kf);
        expect(result).toBe('armed');
        expect(buf.isReset()).toBe(false);
        expect(buf.count()).toBe(1);
    });

    it('subsequent deltas accepted in armed state', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        buf.push(mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: true }));
        const r = buf.push(mkChunk({ capturedAtMs: 133, arrivedAtMs: 1033, isKeyFrame: false }));
        expect(r).toBe('accepted');
        expect(buf.count()).toBe(2);
        expect(buf.isReset()).toBe(false);
    });

    it('not ready until span ≥ targetSpanMs', () => {
        const clock = new MockNow(10_000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        // 5 frames at 33 ms cadence = 132 ms span (< 200) — not ready.
        const arrived = 1000;
        for (let i = 0; i < 5; i++) {
            buf.push(mkChunk({
                capturedAtMs: 100 + i * 33,
                arrivedAtMs: arrived + i * 33,
                isKeyFrame: i === 0,
            }));
        }
        expect(buf.spanMs()).toBe(132);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();

        // Add one more — span 165, still under target.
        buf.push(mkChunk({ capturedAtMs: 100 + 5 * 33, arrivedAtMs: arrived + 5 * 33, isKeyFrame: false }));
        expect(buf.spanMs()).toBe(165);
        expect(buf.isReady()).toBe(false);
    });

    it('isReady true once span ≥ targetSpanMs AND dueAt has arrived', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 200, now: clock.fn });
        // 7 frames @ 33 ms span = 198 — still < 200; need 8.
        for (let i = 0; i < 8; i++) {
            buf.push(mkChunk({
                capturedAtMs: 100 + i * 33,
                arrivedAtMs: 1000 + i * 33,
                isKeyFrame: i === 0,
            }));
        }
        expect(buf.spanMs()).toBe(231);
        // Front arrivedAt = 1000, target = 200 → due at 1200.
        clock.setTo(1199);
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
        clock.setTo(1200);
        expect(buf.isReady()).toBe(true);
    });

    it('tryPull returns chunks in FIFO order, gated by each one\'s dueAt', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.fn });
        // Arrival cadence 33 ms, dueAt = arrivedAt + 100. Push enough
        // chunks that span stays ≥ 100 even after the first pull (we
        // need at least 7 to keep span ≥ 100 once chunk 1 is at front).
        const chunks: ChunkWithDispose[] = [];
        for (let i = 0; i < 8; i++) {
            const c = mkChunk({
                capturedAtMs: 100 + i * 33,
                arrivedAtMs: 1000 + i * 33,
                isKeyFrame: i === 0,
            });
            chunks.push(c);
            buf.push(c);
        }

        clock.setTo(1099);
        expect(buf.tryPull()).toBeNull();
        clock.setTo(1100); // chunk 0 due
        const out0 = buf.tryPull();
        expect(out0).toBe(chunks[0]);
        // Now front is chunk 1, dueAt = 1133.
        clock.setTo(1132);
        expect(buf.tryPull()).toBeNull();
        clock.setTo(1133);
        expect(buf.tryPull()).toBe(chunks[1]);
    });

    it('reset clears all chunks, returns to reset state, disposes content', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.fn });
        const a = mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: true });
        const b = mkChunk({ capturedAtMs: 133, arrivedAtMs: 1033, isKeyFrame: false });
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
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.fn });
        buf.push(mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: true }));
        buf.reset();
        const delta = mkChunk({ capturedAtMs: 200, arrivedAtMs: 1100, isKeyFrame: false });
        expect(buf.push(delta)).toBe('droppedReset');
        expect(delta.disposed).toBe(true);
        const kf = mkChunk({ capturedAtMs: 233, arrivedAtMs: 1133, isKeyFrame: true });
        expect(buf.push(kf)).toBe('armed');
        expect(buf.isReset()).toBe(false);
    });

    it('detectRegression: false on forward progress, true on backwards beyond tolerance', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.fn });
        buf.push(mkChunk({ capturedAtMs: 1000, arrivedAtMs: 1000, isKeyFrame: true }));
        const fwd = mkChunk({ capturedAtMs: 1033, arrivedAtMs: 1033, isKeyFrame: false });
        expect(buf.detectRegression(fwd, 50)).toBe(false);
        const back = mkChunk({ capturedAtMs: 900, arrivedAtMs: 1100, isKeyFrame: false });
        expect(buf.detectRegression(back, 50)).toBe(true);
        // Within tolerance.
        const slightlyBack = mkChunk({ capturedAtMs: 970, arrivedAtMs: 1100, isKeyFrame: false });
        expect(buf.detectRegression(slightlyBack, 50)).toBe(false);
    });

    it('detectRegression returns false when comparing across epoch boundary', () => {
        const clock = new MockNow(1000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 100, now: clock.fn });
        buf.push(mkChunk({ capturedAtMs: 5000, arrivedAtMs: 1000, isKeyFrame: true, epoch: 1 }));
        const newEpoch = mkChunk({ capturedAtMs: 100, arrivedAtMs: 1100, isKeyFrame: true, epoch: 2 });
        expect(buf.detectRegression(newEpoch, 50)).toBe(false);
    });

    it('isReady requires count ≥ 2 (single keyframe with span 0 is not ready)', () => {
        const clock = new MockNow(2000);
        const buf = new EncodedFrameBuffer({ targetSpanMs: 0, now: clock.fn });
        buf.push(mkChunk({ capturedAtMs: 100, arrivedAtMs: 1000, isKeyFrame: true }));
        // span is 0 (only one chunk) → not ready even with target 0.
        expect(buf.isReady()).toBe(false);
        expect(buf.tryPull()).toBeNull();
    });

    it('default now() falls back to Date.now when not injected', () => {
        const buf = new EncodedFrameBuffer({ targetSpanMs: 1_000_000_000 });
        // Just exercise the no-options-now path; tryPull returns null
        // because the target span is unreachable.
        const c = mkChunk({ capturedAtMs: 100, arrivedAtMs: 100, isKeyFrame: true });
        buf.push(c);
        expect(buf.tryPull()).toBeNull();
    });
});
