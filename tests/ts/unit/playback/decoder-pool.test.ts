import { describe, it, expect } from 'vitest';
import {
    DecoderPool,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/decoder-pool';
import type { DecoderLike } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/decode';

class MockDecoder implements DecoderLike {
    static seq = 0;
    readonly id = ++MockDecoder.seq;
    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    decodeQueueSize = 0;
    closed = false;

    configure(): void { this.state = 'configured'; }
    decode(): void { /* nothing */ }
    flush(): Promise<void> { return Promise.resolve(); }
    close(): void { this.state = 'closed'; this.closed = true; }
}

class FakeClock {
    constructor(public t: number) {}
    now = (): number => this.t;
    advance(dt: number): void { this.t += dt; }
}

describe('DecoderPool', () => {
    it('acquire builds a fresh decoder when no slot is parked', () => {
        const pool = new DecoderPool();
        let calls = 0;
        const handle = pool.acquire('avc1.42001f', () => {
            calls++;
            return new MockDecoder();
        });
        expect(calls).toBe(1);
        expect(handle.decoder).toBeInstanceOf(MockDecoder);
        expect(pool.parkedCount()).toBe(0);
    });

    it('release parks the decoder; subsequent acquire reuses it (no factory call)', () => {
        const pool = new DecoderPool();
        let calls = 0;
        const factory = (): DecoderLike => { calls++; return new MockDecoder(); };

        const h1 = pool.acquire('avc1.42001f', factory);
        const decoder1 = h1.decoder;
        h1.release();
        expect(pool.parkedCount()).toBe(1);

        const h2 = pool.acquire('avc1.42001f', factory);
        expect(h2.decoder).toBe(decoder1);
        expect(calls).toBe(1); // factory not called twice
        expect(pool.parkedCount()).toBe(0);
        expect((decoder1 as MockDecoder).closed).toBe(false);
    });

    it('codec mismatch on acquire evicts mismatched parked slots', () => {
        const pool = new DecoderPool();
        const h1 = pool.acquire('avc1.42001f', () => new MockDecoder());
        const decoderA = h1.decoder as MockDecoder;
        h1.release();
        expect(pool.parkedCount()).toBe(1);

        // Different codec — pool should evict the parked AVC slot before
        // building the HEVC one.
        const h2 = pool.acquire('hev1.1.6.L93.B0', () => new MockDecoder());
        expect(decoderA.closed).toBe(true);
        expect(h2.decoder).not.toBe(decoderA);
        expect(pool.parkedCount()).toBe(0);
    });

    it('sweep closes slots idle longer than parkTtlMs', () => {
        const clock = new FakeClock(1_000);
        const pool = new DecoderPool({ parkTtlMs: 500, now: clock.now });

        const h = pool.acquire('avc1.42001f', () => new MockDecoder());
        const decoder = h.decoder as MockDecoder;
        h.release();
        expect(pool.parkedCount()).toBe(1);

        // Within TTL — sweep is a no-op.
        clock.advance(400);
        pool.sweep();
        expect(decoder.closed).toBe(false);
        expect(pool.parkedCount()).toBe(1);

        // Past TTL — sweep evicts.
        clock.advance(200); // total +600ms
        pool.sweep();
        expect(decoder.closed).toBe(true);
        expect(pool.parkedCount()).toBe(0);
    });

    it('dispose closes parked decoders and neutralizes outstanding leases', () => {
        const pool = new DecoderPool();
        const parkedHandle = pool.acquire('avc1.42001f', () => new MockDecoder());
        const parkedDecoder = parkedHandle.decoder as MockDecoder;
        parkedHandle.release();
        expect(pool.parkedCount()).toBe(1);

        const leasedHandle = pool.acquire('hev1.1.6.L93.B0', () => new MockDecoder());
        const leasedDecoder = leasedHandle.decoder as MockDecoder;

        pool.dispose();
        expect(parkedDecoder.closed).toBe(true);
        // Releasing the leased handle after dispose: the pool should
        // close the leased decoder (no parking back into a disposed pool).
        leasedHandle.release();
        expect(leasedDecoder.closed).toBe(true);

        // Subsequent acquire must throw.
        expect(() => pool.acquire('avc1.42001f', () => new MockDecoder())).toThrow(/dispose/);
    });
});
