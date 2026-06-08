// The default downscaler. Lower tiers are produced by wrapping the ceiling in a
// new VideoFrame with a smaller displayWidth/Height; the coded plane stays at the
// ceiling size and the HW encoder rescales coded→config when it encodes the tier.
// No GPU resize, no texture upload — keeps the downscale off the GPU, which (per
// Perfetto sender traces) removes the per-frame GPU-sync stall and GPU contention
// the WebGL path incurred. See docs/live-video/02-sender.md.
//
// Caveat: this trusts the HW encoder to SCALE rather than top-left-CROP when
// coded > config. Most encoders scale; a few HW/codec combos crop. Where that
// shows (notably Edge HEVC), force 'webgl'/'canvas' via video.debug.downscalerMode.
//
// Same contract as the real downscalers: top tier (matching ceiling dims) is the
// input passed through; the input is NOT owned (never closed here).

import type { DownscalerLike, LayerSpec } from '../operators/downscale';

export class MetadataDownscaler implements DownscalerLike {
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        const inW = input.displayWidth || input.codedWidth;
        const inH = input.displayHeight || input.codedHeight;
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        try {
            for (let i = 0; i < layers.length; i++) {
                const { width, height } = layers[i];
                results[i] = width === inW && height === inH
                    ? input
                    : new VideoFrame(input, {
                        displayWidth: width,
                        displayHeight: height,
                        timestamp: input.timestamp,
                    });
            }
            return results as VideoFrame[];
        } catch (e) {
            for (const r of results)
                if (r && r !== input) try { r.close(); } catch { /* ignore */ }
            throw e;
        }
    }

    dispose(): void { /* no GPU resources */ }
}
