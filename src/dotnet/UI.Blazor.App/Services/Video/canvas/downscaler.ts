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
// Behaviours dropped on purpose:
//  - VideoFrame.rotation handling — we don't rotate. Mobile portrait capture is
//    fed pre-rotated by the worker; desktop and screencast never rotate.
//  - Per-target throttling. The compositor schedules drawImage; if a layer is
//    too slow it'll back-pressure naturally through the encoder pipeline.
export class CanvasDownscaler implements DownscalerLike {
    private slots: Slot[] = [];

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const results: VideoFrame[] = [];
        let mustCloseInput = true;
        try {
            this.ensureSlots(layers);
            const sx = input.codedWidth;
            const sy = input.codedHeight;
            for (let i = 0; i < layers.length; i++) {
                const { width, height } = layers[i];
                const slot = this.slots[i];
                if (slot.canvas.width !== width)
                    slot.canvas.width = width;
                if (slot.canvas.height !== height)
                    slot.canvas.height = height;

                const crop = computeCenterCrop(sx, sy, width, height);
                slot.ctx.drawImage(
                    input,
                    crop.sx, crop.sy, crop.sw, crop.sh,
                    0, 0, width, height,
                );
                results.push(new VideoFrame(slot.canvas, {
                    timestamp: input.timestamp,
                    alpha: 'discard',
                }));
            }
            mustCloseInput = false;
            try { input.close(); } catch { /* already closed */ }
            return results;
        } catch (e) {
            warnLog?.log('CanvasDownscaler.process failed:', e);
            for (const r of results)
                try { r.close(); } catch { /* ignore */ }
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
