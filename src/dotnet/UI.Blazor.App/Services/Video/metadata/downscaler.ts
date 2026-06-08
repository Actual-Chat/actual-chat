// Metadata-only "downscaler" — the cheap baseline. Lower tiers are produced by
// wrapping the ceiling in a new VideoFrame with a smaller displayWidth/Height;
// the coded plane stays at the ceiling size and the HW encoder rescales. No GPU
// resize, no texture upload — the lowest-cost path, kept ONLY as a diagnostics
// comparison mode. It reproduces the Edge HEVC top-left crop on lower tiers
// (coded != display ⇒ conformance window), so it must not ship as the default.
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
