import { getLogs } from 'logging';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { warnLog } = getLogs('VideoPipeline');

// Hardware-accelerated downscaler built on OffscreenCanvas 2D. drawImage(VideoFrame)
// runs through the browser's compositor (GPU on every shipping desktop / mobile
// browser). Far simpler than the WGSL compute path: no device-loss cascade, no
// shader pipelines, no per-frame drain dance. Quality is bilinear (the 2D context's
// imageSmoothing default) — adequate for the simulcast ladder the encoder consumes.
//
// Behavioural parity with WebGpuDownscaler that this implementation keeps:
//  - One output frame per LayerSpec, in order; each carries `input.timestamp`.
//  - Center-crop on aspect mismatch (matches `centerCrop: true` upstream).
//  - Output ownership flips to caller on success; on failure all produced frames
//    plus the input are closed before throwing.
//
// Source-reuse optimisation:
//  - Layers are processed top-down (highest ladder index first) so a lower
//    layer can use an already-painted higher-tier canvas as its source
//    instead of repainting from the original VideoFrame. Eligibility:
//    longEdge(higher) ≥ 2 × longEdge(target). We pick the smallest qualifying
//    higher-tier canvas (closest to the 2× threshold) to minimise drawImage
//    work; if none qualifies, fall back to the original input. The TOP layer
//    always uses the original input.
//
// Behaviours dropped on purpose:
//  - VideoFrame.rotation handling — we don't rotate. Mobile portrait capture is
//    fed pre-rotated by the worker; desktop and screencast never rotate.
//  - Per-target throttling. The compositor schedules drawImage; if a layer is
//    too slow it'll back-pressure naturally through the encoder pipeline.
export class CanvasDownscaler implements DownscalerLike {
    private slots: Slot[] = [];

    /**
     * Picks the source canvas/frame for layer `targetIdx` given the ladder
     * and the bookkeeping `paintedHigher` set (indices of higher tiers
     * already painted in this `process()` call). Exposed as a method only
     * so tests can verify the selection rule directly.
     */
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
            // Long-edge must be ≥ 2 × target; among qualifiers pick the
            // smallest (least drawImage work).
            if (lo >= 2 * targetLong && lo < bestLong) {
                bestIdx = i;
                bestLong = lo;
            }
        }
        return bestIdx >= 0 ? { kind: 'higher', idx: bestIdx } : { kind: 'original' };
    }

    // Async to satisfy DownscalerLike; Canvas2D drawImage is synchronous.
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const painted: number[] = [];
        let mustCloseInput = true;
        try {
            this.ensureSlots(layers);
            const inputW = input.codedWidth;
            const inputH = input.codedHeight;

            // Top-down: paint highest tier first so it's available as a
            // source for lower tiers that qualify under the ≥ 2× rule.
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
                    // Higher-tier canvas is already aspect-corrected via
                    // the original center-crop, so we draw the whole thing
                    // (full src rect → full dst rect).
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
        // OffscreenCanvas is GC'd; nothing else to release.
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
