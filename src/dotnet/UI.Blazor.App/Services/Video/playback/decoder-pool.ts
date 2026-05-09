import type { DecoderLike } from '../operators/decode';

export interface DecoderHandle {
    decoder: DecoderLike;
    release(): void;
}

export interface DecoderPoolOptions {
    // Default 30_000 ms matches the legacy cooldown so a quick stop/start
    // (camera toggle, codec switch) reuses the same HW session.
    parkTtlMs?: number;
    now?: () => number;
}

interface Slot {
    decoder: DecoderLike;
    parkedAtMs: number;
}

// Codec-keyed pool of decoder instances. A leased handle is owned by
// the caller until release(); released handles are parked under their
// codec key for reuse, evicted on codec-mismatch acquire, or swept
// after parkTtlMs.
export class DecoderPool {
    private readonly parkTtlMs: number;
    private readonly now: () => number;
    private readonly slotsByCodec = new Map<string, Slot>();
    private readonly leasedReleases = new Set<() => void>();
    private disposed = false;

    constructor(opts: DecoderPoolOptions = {}) {
        this.parkTtlMs = opts.parkTtlMs ?? 30_000;
        this.now = opts.now ?? (() => Date.now());
    }

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
            const existing = this.slotsByCodec.get(codec);
            if (existing) {
                this.closeQuiet(existing.decoder);
            }
            this.slotsByCodec.set(codec, { decoder, parkedAtMs: this.now() });
        };
        this.leasedReleases.add(release);
        return { decoder, release };
    }

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

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        for (const slot of this.slotsByCodec.values()) {
            this.closeQuiet(slot.decoder);
        }
        this.slotsByCodec.clear();
        this.leasedReleases.clear();
    }

    parkedCount(): number {
        return this.slotsByCodec.size;
    }

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
