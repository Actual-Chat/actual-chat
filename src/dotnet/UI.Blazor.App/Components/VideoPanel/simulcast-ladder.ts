// One simulcast layer. Defined here (a leaf module with no imports of the
// worker contract) so test code and the worker contract can both reference
// the same type without test imports pulling in the full RPC/logging
// dependency chain via the worker contract module. The base-layer dims/bitrate
// live on `EncoderConfig`; additional layers (higher-res for closer peers) are
// enumerated as `SpatialLayerConfig[]` and produce parallel encoder instances
// tagged with `SpatialLayerId = index + 1` on each emitted chunk.
export interface SpatialLayerConfig {
    width: number;
    height: number;
    bitrate: number;
    /** Overrides EncoderConfig.scalabilityMode for this layer (e.g. 'L1T3'). */
    scalabilityMode?: string;
}

// Cap on simulcast tier count. Webcam ceiling at 3 (720p/360p/180p, each
// ¼ pixels of the previous). iOS Safari HW-encoder budget is preserved via
// a probe-gated 3rd webcam tier (drops to 2 on probe-fail). Screencast is
// a separate fixed 2-tier ladder (1080p/540p) — no probe.
export const MAX_SIMULCAST_TIERS = 3;

export interface LadderBuildInput {
    /** Top-tier width — the largest tier in the ladder. */
    topWidth: number;
    /** Top-tier height — the largest tier in the ladder. */
    topHeight: number;
    /** Number of tiers (1..MAX_SIMULCAST_TIERS). Higher count = more layers below the top. */
    tierCount: number;
    /** Bitrate provider — caller injects the codec/mode-aware lookup. */
    bitrateFor: (height: number) => number;
}

// Builds a simulcast ladder. Top tier is `(topWidth, topHeight)` — the
// caller's chosen source dim (becomes the downscaler's identity slot:
// `clone()` instead of a canvas-backed VideoFrame, no GPU sync). Each lower
// tier is ½ width × ½ height (¼ pixels) of the next, even-rounded so encoders
// accept them. Result is bottom-first.
//
// Examples:
//  - Webcam 3-tier @ 720p: (1280,720,3) → [320×180, 640×360, 1280×720]
//  - Webcam 2-tier dropTop @ 360p: (640,360,2) → [320×180, 640×360]
//  - Screencast 2-tier @ 1080p: (1920,1080,2) → [960×540, 1920×1080]
//
// Capped to MAX_SIMULCAST_TIERS regardless of `tierCount`.
export function buildLadder(input: LadderBuildInput): SpatialLayerConfig[] {
    const { topWidth, topHeight, tierCount, bitrateFor } = input;
    if (tierCount <= 0 || topWidth <= 0 || topHeight <= 0)
        return [];

    const effectiveCount = Math.min(tierCount, MAX_SIMULCAST_TIERS);
    const ladder: SpatialLayerConfig[] = [];
    // Build top-down then reverse to keep the bottom-first invariant.
    let w = topWidth;
    let h = topHeight;
    for (let i = 0; i < effectiveCount; i++) {
        ladder.push({ width: w, height: h, bitrate: bitrateFor(h) });
        w = roundToEven(w / 2);
        h = roundToEven(h / 2);
    }
    return ladder.reverse();
}

function roundToEven(value: number): number {
    return Math.max(2, Math.round(value / 2) * 2);
}
