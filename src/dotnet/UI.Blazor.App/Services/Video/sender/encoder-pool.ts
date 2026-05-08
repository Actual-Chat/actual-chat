// Short-lived same-codec cache of `AsyncVideoEncoder` instances with TTL
// park-after-release semantics. There is intentionally no warmup here:
// the pool only parks encoders that a real recording run already created.
//
// Matching is by CODEC CATEGORY (`h264` / `hevc` / `av1` / `vp9`), not
// exact codec string. We keep up to three parked encoders for the current
// category to cover layers. Returning or acquiring a different
// category clears the parked set so the pool never mixes encoder families.
//
// The pool exposes a simple acquire / release / sweep / dispose surface;
// the encoder factory is injected per-acquire so the operator-side
// `createEncoder` in `operators/encode.ts` stays pluggable.

import { AsyncVideoEncoder } from '../adapters';
import type { EncodedFrame } from '../frame-envelopes';
import type { EncodeInput } from '../operators/encode';

const DEFAULT_PARK_TTL_MS = 5_000;
const DEFAULT_MAX_PARKED_ENCODERS = 3;

/**
 * Coarse codec families. Encoders within the same family can be reused
 * across `encoder.configure()` calls (the underlying HW slot persists);
 * a category mismatch forces eviction.
 */
export type EncoderCodecCategory = 'h264' | 'hevc' | 'av1' | 'vp9';

/**
 * Per-layer encoder type expected by the rest of the pipeline. Aliased
 * here so callers don't have to import both `adapters` and the operator's
 * `EncodeInput` separately.
 */
export type PooledEncoder = AsyncVideoEncoder<EncodeInput, EncodedFrame>;

/**
 * Factory signature for constructing a fresh `AsyncVideoEncoder` when
 * the pool has no compatible parked instance. Production callers wire
 * this to a builder that constructs a `VideoEncoder`, configures it
 * with the requested `VideoEncoderConfig`, and binds the per-frame
 * `buildOutput` for the {@link EncodedFrame} envelope. Tests pass
 * fakes.
 */
export type EncoderFactory = () => PooledEncoder;

export interface EncoderPoolOptions {
    /**
     * Time-to-live for parked encoders, in milliseconds. After this
     * many ms without an acquire, the parked instance is disposed via
     * {@link sweep}. Default 5_000: this is only meant to bridge an
     * immediate restart/reconfigure window.
     */
    parkTtlMs?: number;
    /** Maximum number of same-category encoders to keep parked. Default 3. */
    maxParkedEncoders?: number;
    /** Test override of `Date.now`. Production: omitted. */
    now?: () => number;
}

/**
 * Handle returned by {@link EncoderPool.acquire}. The owner uses
 * `encoder` directly and calls `release()` when the recording run ends
 * — the encoder is then parked for reuse rather than disposed.
 */
export interface EncoderHandle {
    readonly encoder: PooledEncoder;
    /** Park the encoder back into the pool. Idempotent — calling
     *  release more than once is a no-op. */
    release(): void;
}

interface ParkedEntry {
    encoder: PooledEncoder;
    parkedAtMs: number;
}

/**
 * Codec-family-keyed pool of `AsyncVideoEncoder` instances.
 *
 * Lifecycle:
 *   - `acquire(category, factory)` returns a handle wrapping either a
 *     parked entry of the same category (after `handleEncoderReset()`
 *     to clear any stale pending state) or a fresh encoder built via
 *     the factory.
 *   - The owner uses `handle.encoder` (configures, encodes, ...) and
 *     calls `handle.release()` when the run ends.
 *   - `release()` parks the encoder; `sweep()` evicts stale entries
 *     past `parkTtlMs`.
 *   - `dispose()` synchronously drops all pooled encoders, regardless
 *     of TTL.
 *
 * The pool is single-threaded (worker context); no internal locking.
 * `acquire()` of a busy parked entry (one already handed out and not
 * released) doesn't happen — the pool only ever parks released
 * encoders. We keep at most THREE parked entries of the same category
 * at a time; a category change clears the parked set.
 */
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

    /** Number of currently parked encoders. Diagnostics / tests. */
    get parkedCount(): number { return this.parked.length; }

    /** True after `dispose()` was called. */
    get isDisposed(): boolean { return this.disposed; }

    /**
     * Get an encoder for the given codec category. Reuses a parked
     * entry if one matches; otherwise calls `factory()` to build a
     * fresh `AsyncVideoEncoder`.
     *
     * On reuse the parked encoder's pending state is cleared via
     * `handleEncoderReset()` — any leftover in-flight entries from
     * the previous run are dropped. The caller is expected to call
     * `encoder.configure(...)` (a reconfigure on the existing HW
     * session) after acquiring; the pool does NOT do this for them
     * because the configure-side glue lives inside the factory's
     * production binding.
     */
    acquire(
        category: EncoderCodecCategory,
        factory: EncoderFactory,
    ): EncoderHandle {
        if (this.disposed)
            throw new Error('EncoderPool: pool is disposed');

        this.sweep();
        if (this.parkedCategory !== undefined && this.parkedCategory !== category)
            this.clearParked();

        let encoder: PooledEncoder;
        const parked = this.parked.pop();
        if (parked) {
            if (this.parked.length === 0)
                this.parkedCategory = undefined;
            encoder = parked.encoder;
            encoder.handleEncoderReset();
        } else {
            encoder = factory();
        }
        return this.makeHandle(category, encoder);
    }

    /**
     * Drop everything and dispose every parked encoder synchronously.
     * Idempotent — subsequent calls are no-ops. After `dispose()`,
     * `acquire()` throws.
     */
    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.clearParked();
    }

    /**
     * Evict parked entries older than `parkTtlMs`. Production callers
     * invoke this from a periodic timer; tests advance `now` and
     * call this manually.
     */
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

    private makeHandle(category: EncoderCodecCategory, encoder: PooledEncoder): EncoderHandle {
        let released = false;
        // The encode operator's `finally` calls `encoder.dispose()` to
        // release per-run resources. For pooled encoders we want to PARK
        // instead — destroying the underlying NVENC slot defeats the
        // whole point of the pool. We intercept `dispose` while the
        // handle is checked out: the first call from the operator's
        // finally just routes to release(); the pool drives the actual
        // disposal later (TTL eviction or pool.dispose()).
        const overridable = encoder as unknown as { dispose: () => void };
        const originalDisposeFn = overridable.dispose;
        const originalDispose = (): void => { originalDisposeFn.call(encoder); };
        const release: () => void = () => {
            if (released) return;
            released = true;
            // Restore the real dispose so subsequent owners (or
            // pool.dispose() / sweep()) get the genuine teardown.
            overridable.dispose = originalDispose;
            this.park(category, encoder);
        };
        overridable.dispose = release;
        return { encoder, release };
    }

    private park(category: EncoderCodecCategory, encoder: PooledEncoder): void {
        if (this.disposed || encoder.isDisposed) {
            try { encoder.dispose(); } catch { /* ignore */ }
            return;
        }
        if (this.parkedCategory !== undefined && this.parkedCategory !== category)
            this.clearParked();

        // Clear pending state so the next acquire starts clean.
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
}
