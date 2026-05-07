import { describe, it, expect } from 'vitest';
import { PlaybackSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/session';
import { MonotonicClock } from 'clocks';

describe('PlaybackSession', () => {
    it('constructs with fresh clock, decoder pool, and zeroed stats', () => {
        const session = new PlaybackSession();
        expect(session.arrivalClock).toBeInstanceOf(MonotonicClock);
        expect(session.decoderPool).toBeDefined();
        expect(session.stats.chunksArrived).toBe(0);
        expect(session.stats.framesDecoded).toBe(0);
        expect(session.stats.activeStreams).toBe(0);
        expect(session.stats.sessionStartedAtMs).toBeGreaterThan(0);
        expect(session.isDisposed()).toBe(false);
    });

    it('honors createArrivalClock override', () => {
        const myClock = new MonotonicClock();
        const session = new PlaybackSession({ createArrivalClock: () => myClock });
        expect(session.arrivalClock).toBe(myClock);
    });

    it('register/unregister increment and decrement activeStreams', () => {
        const session = new PlaybackSession();
        session.registerStream();
        session.registerStream();
        expect(session.stats.activeStreams).toBe(2);
        session.unregisterStream();
        expect(session.stats.activeStreams).toBe(1);
        session.unregisterStream();
        session.unregisterStream(); // floor at 0
        expect(session.stats.activeStreams).toBe(0);
    });

    it('reset clears counters but preserves activeStreams', () => {
        const session = new PlaybackSession();
        session.registerStream();
        session.stats.chunksArrived = 5;
        session.stats.framesDecoded = 3;
        session.stats.bytesReceived = 1234;
        session.stats.decodeTimeMsSum = 42;
        session.stats.decodeTimeMsCount = 7;

        session.reset();
        expect(session.stats.chunksArrived).toBe(0);
        expect(session.stats.framesDecoded).toBe(0);
        expect(session.stats.bytesReceived).toBe(0);
        expect(session.stats.decodeTimeMsSum).toBe(0);
        expect(session.stats.decodeTimeMsCount).toBe(0);
        expect(session.stats.activeStreams).toBe(1); // preserved
    });

    it('dispose disposes the decoder pool and is idempotent', () => {
        const session = new PlaybackSession();
        const pool = session.decoderPool;

        // Park a decoder so we can observe pool teardown.
        const h = pool.acquire('avc1.42001f', () => ({
            state: 'unconfigured' as const,
            decodeQueueSize: 0,
            configure: () => { /* nothing */ },
            decode: () => { /* nothing */ },
            flush: () => Promise.resolve(),
            close: () => { /* nothing */ },
        }));
        h.release();
        expect(pool.parkedCount()).toBe(1);

        session.dispose();
        expect(session.isDisposed()).toBe(true);
        expect(pool.parkedCount()).toBe(0);

        // Idempotent.
        session.dispose();
        expect(session.isDisposed()).toBe(true);
    });
});
