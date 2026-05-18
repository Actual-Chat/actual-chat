// Same-category cache of `AsyncVideoEncoder` instances with TTL
// park-after-release semantics. Matching is by CODEC CATEGORY, not exact
// codec string: a category change clears the parked set so the pool never
// mixes encoder families. No warmup — only released encoders get parked.
//
// Why not the generic `ObjectPool` (src/nodejs/src/object-pool.ts)? Each
// parked encoder holds a real OS/GPU HW encoder slot (e.g. NVENC has a
// bounded concurrent-session limit on consumer NVIDIA cards). The generic
// pool keeps objects forever, has no max-size, no category mixing rule,
// and no way to intercept `dispose()` so callers' `finally { enc.dispose() }`
// parks instead of really destroying. All four are essential here.

import { AsyncVideoEncoder } from '../adapters';
import type { EncodedFrame } from '../frame-envelopes';
import type { EncodeInput } from '../operators/encode';

const DEFAULT_PARK_TTL_MS = 5_000;
const DEFAULT_MAX_PARKED_ENCODERS = 3;

export type EncoderCodecCategory = 'h264' | 'hevc' | 'av1' | 'vp9';
export type PooledEncoder = AsyncVideoEncoder<EncodeInput, EncodedFrame>;
export type EncoderFactory = () => PooledEncoder;

export interface EncoderPoolOptions {
    parkTtlMs?: number;
    maxParkedEncoders?: number;
    now?: () => number;
}

export interface EncoderHandle {
    readonly encoder: PooledEncoder;
    release(): void;
}

interface ParkedEntry {
    encoder: PooledEncoder;
    parkedAtMs: number;
}

export class EncoderPool {
    private readonly parkTtlMs: number;
    private readonly maxParkedEncoders: number;
    private readonly now: () => number;
    private readonly parked: ParkedEntry[] = [];
    private parkedCategory: EncoderCodecCategory | undefined;
    private disposed = false;

    constructor(opts: EncoderPoolOptions = {}) {
        this.parkTtlMs = opts.parkTtlMs ?? DEFAULT_PARK_TTL_MS;
        const maxParkedEncoders = opts.maxParkedEncoders ?? DEFAULT_MAX_PARKED_ENCODERS;
        this.maxParkedEncoders = Number.isFinite(maxParkedEncoders)
            ? Math.max(0, Math.floor(maxParkedEncoders))
            : DEFAULT_MAX_PARKED_ENCODERS;
        this.now = opts.now ?? (() => Date.now());
    }

    get parkedCount(): number { return this.parked.length; }
    get isDisposed(): boolean { return this.disposed; }

    // Caller MUST call `encoder.configure(...)` after acquire — the pool only
    // resets pending state on reused encoders, not the codec config.
    acquire(
        category: EncoderCodecCategory,
        factory: EncoderFactory,
    ): EncoderHandle {
        if (this.disposed)
            throw new Error('EncoderPool: pool is disposed');

        this.sweep();
        if (this.parkedCategory !== undefined && this.parkedCategory !== category)
            this.clearParked();

        let encoder: PooledEncoder | undefined;
        while (this.parked.length > 0) {
            const parked = this.parked.pop()!;
            if (this.parked.length === 0)
                this.parkedCategory = undefined;
            if (!this.canReuse(parked.encoder)) {
                try { parked.encoder.dispose(); } catch { /* ignore */ }
                continue;
            }
            encoder = parked.encoder;
            encoder.handleEncoderReset();
            break;
        }
        encoder ??= factory();
        return this.makeHandle(category, encoder);
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.clearParked();
    }

    sweep(): void {
        if (this.disposed) return;
        const cutoff = this.now() - this.parkTtlMs;
        for (let i = this.parked.length - 1; i >= 0; i--) {
            const entry = this.parked[i];
            if (entry.parkedAtMs <= cutoff) {
                try { entry.encoder.dispose(); } catch { /* ignore */ }
                this.parked.splice(i, 1);
            }
        }
        if (this.parked.length === 0)
            this.parkedCategory = undefined;
    }

    // ---- internals --------------------------------------------------------

    // Intercepts the encoder's `dispose` while checked out: the encode
    // operator's `finally` calls dispose() to release per-run resources, but
    // for pooled encoders we want to PARK instead — destroying the underlying
    // NVENC slot defeats the pool. The interception routes that first call
    // through release(); pool.dispose()/sweep() later do the genuine teardown.
    private makeHandle(category: EncoderCodecCategory, encoder: PooledEncoder): EncoderHandle {
        let released = false;
        const overridable = encoder as unknown as { dispose: () => void };
        const originalDisposeFn = overridable.dispose;
        const originalDispose = (): void => { originalDisposeFn.call(encoder); };
        const release: () => void = () => {
            if (released) return;
            released = true;
            overridable.dispose = originalDispose;
            this.park(category, encoder);
        };
        overridable.dispose = release;
        return { encoder, release };
    }

    private park(category: EncoderCodecCategory, encoder: PooledEncoder): void {
        if (this.disposed || !this.canReuse(encoder)) {
            try { encoder.dispose(); } catch { /* ignore */ }
            return;
        }
        if (this.parkedCategory !== undefined && this.parkedCategory !== category)
            this.clearParked();

        encoder.handleEncoderReset();
        this.parkedCategory = category;
        this.parked.push({
            encoder,
            parkedAtMs: this.now(),
        });
        while (this.parked.length > this.maxParkedEncoders) {
            const evicted = this.parked.shift();
            if (evicted)
                try { evicted.encoder.dispose(); } catch { /* ignore */ }
        }
    }

    private clearParked(): void {
        for (const entry of this.parked) {
            try { entry.encoder.dispose(); } catch { /* ignore */ }
        }
        this.parked.length = 0;
        this.parkedCategory = undefined;
    }

    private canReuse(encoder: PooledEncoder): boolean {
        return !encoder.isDisposed && encoder.state !== 'closed';
    }
}
