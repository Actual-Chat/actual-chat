import { getLogs } from 'logging';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoPipeline');

// Compositor-backed (GPU) downscaler via OffscreenCanvas 2D drawImage(VideoFrame).
// Layers are processed top-down so a lower tier can reuse an already-painted
// higher-tier canvas when longEdge(higher) >= 2 * longEdge(target).
// Does not rotate — capture is pre-rotated upstream.
export class CanvasDownscaler implements DownscalerLike {
    private slots: Slot[] = [];

    static pickSource(
        targetIdx: number,
        ladder: readonly LayerSpec[],
        paintedHigherIdxs: readonly number[],
    ): { kind: 'original' } | { kind: 'higher'; idx: number } {
        if (targetIdx >= ladder.length - 1)
            return { kind: 'original' };
        const targetLong = longEdge(ladder[targetIdx]);
        let bestIdx = -1;
        let bestLong = Number.POSITIVE_INFINITY;
        for (const i of paintedHigherIdxs) {
            if (i <= targetIdx) continue;
            const lo = longEdge(ladder[i]);
            if (lo >= 2 * targetLong && lo < bestLong) {
                bestIdx = i;
                bestLong = lo;
            }
        }
        return bestIdx >= 0 ? { kind: 'higher', idx: bestIdx } : { kind: 'original' };
    }

    // Async only to satisfy DownscalerLike — Canvas2D drawImage is synchronous.
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const painted: number[] = [];
        let mustCloseInput = true;
        try {
            this.ensureSlots(layers);
            const inputW = input.codedWidth;
            const inputH = input.codedHeight;

            // Top-down: highest tier first so lower tiers can reuse it as source.
            for (let i = layers.length - 1; i >= 0; i--) {
                const { width, height } = layers[i];
                const slot = this.slots[i];
                if (slot.canvas.width !== width)
                    slot.canvas.width = width;
                if (slot.canvas.height !== height)
                    slot.canvas.height = height;

                const choice = CanvasDownscaler.pickSource(i, layers, painted);
                if (choice.kind === 'original') {
                    const crop = computeCenterCrop(inputW, inputH, width, height);
                    slot.ctx.drawImage(
                        input,
                        crop.sx, crop.sy, crop.sw, crop.sh,
                        0, 0, width, height,
                    );
                } else {
                    // Higher-tier canvas is already aspect-corrected; full src → full dst.
                    const srcSlot = this.slots[choice.idx];
                    slot.ctx.drawImage(
                        srcSlot.canvas,
                        0, 0, srcSlot.canvas.width, srcSlot.canvas.height,
                        0, 0, width, height,
                    );
                }
                results[i] = new VideoFrame(slot.canvas, {
                    timestamp: input.timestamp,
                    alpha: 'discard',
                });
                painted.push(i);
            }

            mustCloseInput = false;
            try { input.close(); } catch { /* already closed */ }
            return results as VideoFrame[];
        } catch (e) {
            warnLog?.log('CanvasDownscaler.process failed:', e);
            for (const r of results) {
                if (r) {
                    try { r.close(); } catch { /* ignore */ }
                }
            }
            if (mustCloseInput)
                try { input.close(); } catch { /* ignore */ }

            throw e;
        }
    }

    dispose(): void {
        this.slots = [];
    }

    // Private methods

    private ensureSlots(layers: readonly LayerSpec[]): void {
        while (this.slots.length < layers.length)
            this.slots.push(createSlot());
    }
}

interface Slot {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

function createSlot(): Slot {
    const canvas = new OffscreenCanvas(0, 0);
    const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
    if (!ctx)
        throw new Error('CanvasDownscaler: 2D context unavailable on OffscreenCanvas');

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    return { canvas, ctx };
}

function longEdge(spec: LayerSpec): number {
    return Math.max(spec.width, spec.height);
}

function computeCenterCrop(
    sx: number, sy: number,
    dx: number, dy: number,
): { sx: number; sy: number; sw: number; sh: number } {
    if (sx <= 0 || sy <= 0 || dx <= 0 || dy <= 0)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    const srcAspect = sx / sy;
    const dstAspect = dx / dy;
    if (Math.abs(srcAspect - dstAspect) < 1e-3)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    if (srcAspect > dstAspect) {
        const sw = Math.round(sy * dstAspect);
        return { sx: Math.floor((sx - sw) / 2), sy: 0, sw, sh: sy };
    }
    const sh = Math.round(sx / dstAspect);
    return { sx: 0, sy: Math.floor((sy - sh) / 2), sw: sx, sh };
}
