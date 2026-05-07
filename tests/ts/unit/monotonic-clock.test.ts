import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { MonotonicClock } from 'clocks';

// Each test mocks Date.now() and performance.now() so we can simulate sleep,
// NTP jumps, and zero-elapsed-time queries deterministically. The mocks are
// installed in beforeEach (BEFORE constructing the clock) and restored in
// afterEach. Tests advance time by mutating mockWallMs / mockPerfMs.
describe('MonotonicClock', () => {
    let mockWallMs = 0;
    let mockPerfMs = 0;
    type MockFn = ReturnType<typeof vi.fn>;
    let dateNowSpy!: MockFn;
    let perfNowSpy!: MockFn;

    beforeEach(() => {
        mockWallMs = 1_700_000_000_000; // arbitrary fixed Unix-ms baseline
        mockPerfMs = 100;
        dateNowSpy = vi.spyOn(Date, 'now').mockImplementation(() => mockWallMs);
        perfNowSpy = vi.spyOn(performance, 'now').mockImplementation(() => mockPerfMs);
    });

    afterEach(() => {
        dateNowSpy.mockRestore();
        perfNowSpy.mockRestore();
    });

    it('starts at epoch 0', () => {
        const clock = new MonotonicClock();
        expect(clock.epoch).toBe(0);
    });

    it('first now() returns time near Date.now() with epoch 0', () => {
        const clock = new MonotonicClock();
        const snap = clock.now();
        expect(snap.timeMs).toBe(mockWallMs);
        expect(snap.epoch).toBe(0);
    });

    it('advances timeMs in lockstep with performance.now() when no drift', () => {
        const clock = new MonotonicClock();
        const snap1 = clock.now();

        mockWallMs += 100;
        mockPerfMs += 100;
        const snap2 = clock.now();

        expect(snap2.timeMs).toBe(snap1.timeMs + 100);
        expect(snap2.epoch).toBe(0);
    });

    it('enforces minTickMs when source clocks do not advance', () => {
        const clock = new MonotonicClock({ minTickMs: 33 });
        const snap1 = clock.now();
        // Source clocks frozen — every subsequent query should still tick by minTickMs.
        const snap2 = clock.now();
        const snap3 = clock.now();

        expect(snap2.timeMs).toBe(snap1.timeMs + 33);
        expect(snap3.timeMs).toBe(snap2.timeMs + 33);
        expect(snap2.epoch).toBe(0);
        expect(snap3.epoch).toBe(0);
    });

    it('does not enforce minTickMs when source clocks advance enough', () => {
        const clock = new MonotonicClock({ minTickMs: 33 });
        // Advance before the first read so the constructor-initialized
        // lastTimeMs doesn't trigger the bump on snap1.
        mockWallMs += 50;
        mockPerfMs += 50;
        const snap1 = clock.now();

        mockWallMs += 50;
        mockPerfMs += 50;
        const snap2 = clock.now();

        expect(snap2.timeMs).toBe(snap1.timeMs + 50);
    });

    it('bumps epoch when wall-clock drifts forward of perf clock past threshold', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 500 });
        const snap1 = clock.now();
        expect(snap1.epoch).toBe(0);

        // Sleep simulation: wall advanced 10s, perf barely moved.
        mockWallMs += 10_000;
        mockPerfMs += 50;
        const snap2 = clock.now();

        expect(snap2.epoch).toBe(1);
    });

    it('bumps epoch on backward wall-clock jump (NTP regression) past threshold', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 500, minTickMs: 1 });
        const snap1 = clock.now();

        // Backward wall jump while perf advances normally.
        mockWallMs -= 5_000;
        mockPerfMs += 100;
        const snap2 = clock.now();

        expect(snap2.epoch).toBe(1);
        // Monotonicity is preserved — time never goes backward.
        expect(snap2.timeMs).toBeGreaterThanOrEqual(snap1.timeMs);
    });

    it('does not bump epoch when drift stays within threshold', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 500 });
        clock.now();

        // 100ms drift, under the 500ms threshold.
        mockWallMs += 200;
        mockPerfMs += 100;
        const snap = clock.now();

        expect(snap.epoch).toBe(0);
    });

    it('with acceptForwardWallClockJumps=true, resync jumps timeMs to current Date.now()', () => {
        const clock = new MonotonicClock({
            driftThresholdMs: 500,
            acceptForwardWallClockJumps: true,
        });
        clock.now();

        // Sleep
        mockWallMs += 10_000;
        mockPerfMs += 100;
        const snap = clock.now();

        expect(snap.epoch).toBe(1);
        expect(snap.timeMs).toBe(mockWallMs);
    });

    it('with acceptForwardWallClockJumps=false, resync preserves continuity (ignores wall jump)', () => {
        const clock = new MonotonicClock({
            driftThresholdMs: 500,
            minTickMs: 1,
            acceptForwardWallClockJumps: false,
        });
        const snap1 = clock.now();

        // Sleep — wall jumps 10s, perf only 100ms.
        mockWallMs += 10_000;
        mockPerfMs += 100;
        const snap2 = clock.now();

        expect(snap2.epoch).toBe(1);
        // Time continues from lastTimeMs + minTickMs, NOT from Date.now().
        expect(snap2.timeMs).toBe(snap1.timeMs + 1);
        expect(snap2.timeMs).toBeLessThan(mockWallMs); // far less than the jumped Date.now()
    });

    it('multiple resyncs increment epoch monotonically', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 100 });
        clock.now();
        expect(clock.epoch).toBe(0);

        mockWallMs += 5_000; mockPerfMs += 10;
        clock.now();
        expect(clock.epoch).toBe(1);

        mockWallMs += 3_000; mockPerfMs += 10;
        clock.now();
        expect(clock.epoch).toBe(2);

        // Stable interval — no further resync.
        mockWallMs += 50; mockPerfMs += 50;
        clock.now();
        expect(clock.epoch).toBe(2);
    });

    it('timeMs is monotonic across many queries with mixed events', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 500, minTickMs: 1 });
        const samples: number[] = [];
        samples.push(clock.now().timeMs);

        // Normal advance
        for (let i = 0; i < 10; i++) {
            mockWallMs += 33;
            mockPerfMs += 33;
            samples.push(clock.now().timeMs);
        }
        // Frozen clocks — minTickMs takes over
        for (let i = 0; i < 5; i++) {
            samples.push(clock.now().timeMs);
        }
        // Sleep event
        mockWallMs += 10_000;
        mockPerfMs += 50;
        samples.push(clock.now().timeMs);
        // NTP backward jump
        mockWallMs -= 2_000;
        mockPerfMs += 100;
        samples.push(clock.now().timeMs);
        // More normal advance
        for (let i = 0; i < 5; i++) {
            mockWallMs += 33;
            mockPerfMs += 33;
            samples.push(clock.now().timeMs);
        }

        for (let i = 1; i < samples.length; i++) {
            expect(samples[i]).toBeGreaterThanOrEqual(samples[i - 1]);
        }
    });

    it('nowDate returns Date matching timeMs', () => {
        const clock = new MonotonicClock();
        const snap = clock.nowDate();
        expect(snap.date.getTime()).toBe(snap.timeMs);
        expect(snap.epoch).toBe(0);
    });

    it('nowDate reflects subsequent now()-style updates', () => {
        const clock = new MonotonicClock();
        clock.now();
        mockWallMs += 100;
        mockPerfMs += 100;
        const snap = clock.nowDate();
        expect(snap.date.getTime()).toBe(snap.timeMs);
        expect(snap.timeMs).toBe(mockWallMs);
    });

    it('epoch property reflects internal counter without invoking now()', () => {
        const clock = new MonotonicClock({ driftThresholdMs: 500 });
        expect(clock.epoch).toBe(0);
        clock.now();
        expect(clock.epoch).toBe(0);

        mockWallMs += 10_000;
        mockPerfMs += 50;
        clock.now();
        expect(clock.epoch).toBe(1);
    });
});
