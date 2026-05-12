import { describe, it, expect } from 'vitest';
import { PlaybackSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/session';
import { MonotonicClock } from 'clocks';

describe('PlaybackSession', () => {
    it('constructs with fresh clock, decoder pool, and zero active streams', () => {
        const session = new PlaybackSession();
        expect(session.arrivalClock).toBeInstanceOf(MonotonicClock);
        expect(session.decoderPool).toBeDefined();
        expect(session.activeStreams).toBe(0);
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
        expect(session.activeStreams).toBe(2);
        session.unregisterStream();
        expect(session.activeStreams).toBe(1);
        session.unregisterStream();
        session.unregisterStream(); // floor at 0
        expect(session.activeStreams).toBe(0);
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
