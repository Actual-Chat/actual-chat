import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
    EncoderPool,
    type EncoderHandle,
    type PooledEncoder,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/encoder-pool';
import { AsyncVideoEncoder } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/adapters';
import type {
    EncodedFrame,
    VideoRecordingStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';
import type { EncodeInput } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';

// ---- Mock VideoEncoder global --------------------------------------------
//
// `AsyncVideoEncoder` constructs `new VideoEncoder({ output, error })`, so
// we install a minimal Mock for the duration of every test. Same pattern
// as `tests/ts/unit/operators/encode.test.ts`.

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    state: 'unconfigured' | 'configured' | 'closed' = 'configured';
    output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
    error: (e: unknown) => void;
    constructor(init: {
        output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => void;
        error: (e: unknown) => void;
    }) {
        this.output = init.output;
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }
    encode(): void { /* not used in pool tests */ }
    close(): void { this.state = 'closed'; }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
}

beforeEach(() => {
    MockVideoEncoder.instances = [];
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
});

// ---- Helpers --------------------------------------------------------------

let factoryCallCount = 0;

function makeEncoderFactory(): () => PooledEncoder {
    return (): PooledEncoder => {
        factoryCallCount++;
        const enc = new AsyncVideoEncoder<EncodeInput, EncodedFrame>(
            (input, chunk, metadata): EncodedFrame => ({
                chunk,
                metadata,
                capturedAt: input.capturedAt,
                index: input.index,
                layerId: 0,
                sourceWidth: 0,
                sourceHeight: 0,
                encodedWidth: 0,
                encodedHeight: 0,
                stats: undefined as unknown as VideoRecordingStats,
            }),
            () => { /* swallow */ },
            { timeoutMs: 0 },
        );
        return enc;
    };
}

beforeEach(() => { factoryCallCount = 0; });

// ---- Tests ----------------------------------------------------------------

describe('EncoderPool', () => {
    it('acquire-fresh: factory is called once, pool is empty afterwards', () => {
        const pool = new EncoderPool();
        const handle = pool.acquire('h264', makeEncoderFactory());
        expect(factoryCallCount).toBe(1);
        expect(pool.parkedCount).toBe(0);
        expect(handle.encoder).toBeDefined();
        // Cleanup so MockVideoEncoder slots don't leak across tests.
        handle.release();
        pool.dispose();
    });

    it('acquire-reuse: same category reuses parked encoder, no fresh factory call', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const h1 = pool.acquire('h264', factory);
        const enc1 = h1.encoder;
        h1.release();
        expect(pool.parkedCount).toBe(1);

        const h2 = pool.acquire('h264', factory);
        // Same instance returned — factory not called a second time.
        expect(h2.encoder).toBe(enc1);
        expect(factoryCallCount).toBe(1);
        expect(pool.parkedCount).toBe(0);
        h2.release();
        pool.dispose();
    });

    it('category-mismatch acquire: different category clears parked entries and builds fresh', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const h264Handle = pool.acquire('h264', factory);
        const h264Encoder = h264Handle.encoder;
        h264Handle.release();
        expect(pool.parkedCount).toBe(1);

        const hevcHandle = pool.acquire('hevc', factory);
        expect(factoryCallCount).toBe(2);
        expect(pool.parkedCount).toBe(0);
        expect(h264Encoder.isDisposed).toBe(true);
        hevcHandle.release();
        expect(pool.parkedCount).toBe(1);
        pool.dispose();
    });

    it('category-mismatch release: returned encoder of a different category clears parked entries', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const h264Handle = pool.acquire('h264', factory);
        const hevcHandle = pool.acquire('hevc', factory);
        const h264Encoder = h264Handle.encoder;
        const hevcEncoder = hevcHandle.encoder;

        h264Handle.release();
        expect(pool.parkedCount).toBe(1);
        hevcHandle.release();
        expect(pool.parkedCount).toBe(1);
        expect(h264Encoder.isDisposed).toBe(true);
        expect(hevcEncoder.isDisposed).toBe(false);

        const hevcReuse = pool.acquire('hevc', factory);
        expect(hevcReuse.encoder).toBe(hevcEncoder);
        expect(factoryCallCount).toBe(2);
        hevcReuse.release();
        pool.dispose();
    });

    it('parks up to three encoders of the same category and disposes the oldest', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const handles = [
            pool.acquire('h264', factory),
            pool.acquire('h264', factory),
            pool.acquire('h264', factory),
            pool.acquire('h264', factory),
        ];
        const encoders = handles.map(x => x.encoder);
        expect(factoryCallCount).toBe(4);

        for (const handle of handles)
            handle.release();

        expect(pool.parkedCount).toBe(3);
        expect(encoders[0].isDisposed).toBe(true);
        expect(encoders[1].isDisposed).toBe(false);
        expect(encoders[2].isDisposed).toBe(false);
        expect(encoders[3].isDisposed).toBe(false);

        const reusedHandles = [
            pool.acquire('h264', factory),
            pool.acquire('h264', factory),
            pool.acquire('h264', factory),
        ];
        const reused = reusedHandles.map(x => x.encoder);
        expect(factoryCallCount).toBe(4);
        expect(new Set(reused)).toEqual(new Set(encoders.slice(1)));
        for (const handle of reusedHandles)
            handle.release();
        pool.dispose();
    });

    it('TTL sweep: parked entries older than explicit TTL get disposed', () => {
        let now = 1_000_000;
        const pool = new EncoderPool({ parkTtlMs: 5_000, now: () => now });
        const factory = makeEncoderFactory();
        const handle = pool.acquire('h264', factory);
        const enc = handle.encoder;
        handle.release();
        // parkedAtMs = 1_000_000.

        // Sweep just before TTL — entry stays.
        now = 1_004_999;
        pool.sweep();
        expect(pool.parkedCount).toBe(1);
        expect(enc.isDisposed).toBe(false);

        // Sweep AT TTL boundary — entry is evicted (>= cutoff).
        now = 1_005_000;
        pool.sweep();
        expect(pool.parkedCount).toBe(0);
        expect(enc.isDisposed).toBe(true);

        pool.dispose();
    });

    it('default TTL is 5 seconds', () => {
        let now = 1_000_000;
        const pool = new EncoderPool({ now: () => now });
        const factory = makeEncoderFactory();
        const handle = pool.acquire('h264', factory);
        const enc = handle.encoder;
        handle.release();

        now = 1_004_999;
        pool.sweep();
        expect(pool.parkedCount).toBe(1);
        expect(enc.isDisposed).toBe(false);

        now = 1_005_000;
        pool.sweep();
        expect(pool.parkedCount).toBe(0);
        expect(enc.isDisposed).toBe(true);
    });

    it('dispose: drops every parked encoder and rejects subsequent acquires', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const h1 = pool.acquire('h264', factory);
        const enc1 = h1.encoder;
        const h2 = pool.acquire('h264', factory);
        const enc2 = h2.encoder;
        h1.release();
        h2.release();
        expect(pool.parkedCount).toBe(2);

        pool.dispose();
        expect(pool.parkedCount).toBe(0);
        expect(enc1.isDisposed).toBe(true);
        expect(enc2.isDisposed).toBe(true);
        expect(pool.isDisposed).toBe(true);
        expect(() => pool.acquire('h264', factory)).toThrow(/disposed/);
    });

    it('release is idempotent — second release does not double-dispose or double-park', () => {
        const pool = new EncoderPool();
        const handle: EncoderHandle = pool.acquire('h264', makeEncoderFactory());
        handle.release();
        expect(pool.parkedCount).toBe(1);
        // Second release is a no-op.
        handle.release();
        expect(pool.parkedCount).toBe(1);
        pool.dispose();
    });

    it('release of a closed encoder disposes instead of parking', () => {
        const pool = new EncoderPool();
        const handle = pool.acquire('h264', makeEncoderFactory());
        const encoder = handle.encoder;

        encoder.encoder.close();
        handle.release();

        expect(pool.parkedCount).toBe(0);
        expect(encoder.isDisposed).toBe(true);
        pool.dispose();
    });

    it('acquire skips closed parked encoders and builds fresh', () => {
        const pool = new EncoderPool();
        const factory = makeEncoderFactory();
        const h1 = pool.acquire('h264', factory);
        const encoder1 = h1.encoder;
        h1.release();
        expect(pool.parkedCount).toBe(1);

        encoder1.encoder.close();
        const h2 = pool.acquire('h264', factory);

        expect(h2.encoder).not.toBe(encoder1);
        expect(factoryCallCount).toBe(2);
        expect(encoder1.isDisposed).toBe(true);
        expect(pool.parkedCount).toBe(0);
        h2.release();
        pool.dispose();
    });
});
