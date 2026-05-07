import type { DecoderLike } from '../operators/decode';

/**
 * A leased decoder instance. Released back to the pool by calling
 * `release()` exactly once. After release the holder must not touch
 * `decoder` — the pool may park it for reuse, dispose it on the next
 * `sweep()`, or hand it to a different acquirer.
 *
 * Mirrors the sender-side `EncoderPool` shape so the worker bookkeeping
 * for sender / receiver looks symmetric. Re-export-friendly so callers
 * don't have to reach into `operators/decode` for `DecoderLike`.
 */
export interface DecoderHandle {
    decoder: DecoderLike;
    release(): void;
}

export interface DecoderPoolOptions {
    /**
     * How long an idle (parked) decoder stays warm before `sweep()`
     * disposes it. Default: 30_000 ms — matches the legacy decoder
     * cooldown intent, which kept HW slots live for ~30 s after stop
     * so a quick stop/start (e.g. user toggling camera, codec switch)
     * could reuse the same hardware session instead of paying a fresh
     * `configure()` round-trip.
     */
    parkTtlMs?: number;
    /** Test override for `Date.now`. Used for park-TTL math only. */
    now?: () => number;
}

interface Slot {
    decoder: DecoderLike;
    parkedAtMs: number;
}

/**
 * Codec-keyed pool of decoder instances. Two-state lifecycle:
 *  - **leased**: returned via `acquire()`, owned by the holder until
 *    `release()` is called.
 *  - **parked**: idle in `slotsByCodec`, eligible for reuse on the next
 *    `acquire()` with the same codec key, or disposal on a `sweep()`
 *    after `parkTtlMs`.
 *
 * Re-acquiring a parked slot is a no-op for hardware: the same
 * underlying decoder (and HW session, if applicable) is reused. Codec
 * mismatch on `acquire()` evicts whatever is parked under the old codec
 * (its `close()` is called) before the new factory runs — we never park
 * a decoder for a codec that nobody asked for.
 *
 * Thread-safety: Single-threaded by construction. The receiver
 * `PlaybackSession` owns the pool; concurrent `Player`s under the same
 * session call `acquire`/`release` from the worker's single event loop.
 *
 * Disposal: `dispose()` closes everything (parked + leased — the leased
 * holders' subsequent `release()` becomes a no-op). After `dispose()`
 * the pool is unusable; create a fresh one for a new session.
 */
export class DecoderPool {
    private readonly parkTtlMs: number;
    private readonly now: () => number;
    /** Codec → most recently parked decoder for that codec. We keep
     *  one slot per codec key — multi-acquire with the same codec is
     *  rare in our workflow, and re-creating beats juggling N parked
     *  slots per codec. */
    private readonly slotsByCodec = new Map<string, Slot>();
    /** Leased handles' release fns; used by `dispose()` to neutralize
     *  outstanding handles. */
    private readonly leasedReleases = new Set<() => void>();
    private disposed = false;

    constructor(opts: DecoderPoolOptions = {}) {
        this.parkTtlMs = opts.parkTtlMs ?? 30_000;
        this.now = opts.now ?? (() => Date.now());
    }

    /**
     * Acquire (or build) a decoder for the given codec.
     *
     * If a parked slot exists under `codec`, reuse it. If a parked slot
     * exists under a DIFFERENT codec, evict it (close + drop) before
     * the factory runs; this keeps the pool's parked footprint at
     * O(distinct codecs in use), bounded in practice by the codec
     * switch cadence (≤ a handful per session).
     *
     * The factory is called only when no compatible parked slot exists.
     * It must construct a new `DecoderLike` (production: a wrapped
     * `VideoDecoder` plus the operator-supplied output / error handlers).
     */
    acquire(codec: string, factory: () => DecoderLike): DecoderHandle {
        if (this.disposed) {
            throw new Error('DecoderPool: cannot acquire after dispose()');
        }
        let decoder: DecoderLike;
        const parked = this.slotsByCodec.get(codec);
        if (parked) {
            this.slotsByCodec.delete(codec);
            decoder = parked.decoder;
        } else {
            // Codec mismatch: any other parked slots are no longer useful
            // for this acquirer; we evict ALL parked slots whose codec
            // doesn't match. This keeps the pool tight under bursty codec
            // switches (HW VRAM budgets are limited).
            this.evictMismatched(codec);
            decoder = factory();
        }

        let released = false;
        const release = (): void => {
            if (released) return;
            released = true;
            this.leasedReleases.delete(release);
            if (this.disposed) {
                this.closeQuiet(decoder);
                return;
            }
            // If a slot for this codec is already parked (shouldn't
            // happen under normal usage but defensive), close the older
            // one and keep the newer.
            const existing = this.slotsByCodec.get(codec);
            if (existing) {
                this.closeQuiet(existing.decoder);
            }
            this.slotsByCodec.set(codec, { decoder, parkedAtMs: this.now() });
        };
        this.leasedReleases.add(release);
        return { decoder, release };
    }

    /**
     * Close any parked decoder that has been idle for longer than
     * `parkTtlMs`. Cheap to call frequently — typical sweep is a no-op
     * (hot path) or closes a single stale slot. Production callers
     * invoke this from a low-cadence interval owned by the session.
     */
    sweep(): void {
        if (this.disposed) return;
        const cutoff = this.now() - this.parkTtlMs;
        for (const [codec, slot] of this.slotsByCodec) {
            if (slot.parkedAtMs <= cutoff) {
                this.slotsByCodec.delete(codec);
                this.closeQuiet(slot.decoder);
            }
        }
    }

    /**
     * Close every parked decoder and neutralize outstanding leases.
     * Idempotent. Subsequent `acquire()` calls throw.
     */
    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        for (const slot of this.slotsByCodec.values()) {
            this.closeQuiet(slot.decoder);
        }
        this.slotsByCodec.clear();
        // Outstanding leases: subsequent release() calls become no-ops
        // (early-return on `disposed`). No further bookkeeping needed.
        this.leasedReleases.clear();
    }

    /** Number of currently parked decoders. Test/diagnostic helper. */
    parkedCount(): number {
        return this.slotsByCodec.size;
    }

    // ---- Internals -------------------------------------------------------

    private evictMismatched(keepCodec: string): void {
        for (const [codec, slot] of this.slotsByCodec) {
            if (codec === keepCodec) continue;
            this.slotsByCodec.delete(codec);
            this.closeQuiet(slot.decoder);
        }
    }

    private closeQuiet(decoder: DecoderLike): void {
        try { decoder.close(); } catch { /* ignore */ }
    }
}
