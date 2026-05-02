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

// Mode-specific caps. Webcam gets up to 3 tiers (720p/360p/180p), screencast
// gets up to 2 (top + half-size) to keep text legibility and encoder cost sane.
export const WEBCAM_MAX_SIMULCAST_TIERS = 3;
export const SCREENCAST_MAX_SIMULCAST_TIERS = 2;
export const MIN_SIMULCAST_SMALL_AXIS = 150;

export interface LadderBuildInput {
    /** Top-tier width — the largest tier in the ladder. */
    topWidth: number;
    /** Top-tier height — the largest tier in the ladder. */
    topHeight: number;
    /** Requested tier count. Higher count = more layers below the top. */
    tierCount: number;
    /** Mode-specific maximum tier count. */
    maxTierCount: number;
    /** Drop derived lower tiers whose smaller axis would be below this threshold. */
    minSmallAxis?: number;
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
// Capped to maxTierCount regardless of `tierCount`.
export function buildLadder(input: LadderBuildInput): SpatialLayerConfig[] {
    const { topWidth, topHeight, tierCount, maxTierCount, bitrateFor } = input;
    if (tierCount <= 0 || topWidth <= 0 || topHeight <= 0)
        return [];

    const effectiveCount = Math.min(tierCount, maxTierCount);
    const minSmallAxis = input.minSmallAxis ?? MIN_SIMULCAST_SMALL_AXIS;
    const ladder: SpatialLayerConfig[] = [];
    // Build top-down then reverse to keep the bottom-first invariant.
    let w = topWidth;
    let h = topHeight;
    for (let i = 0; i < effectiveCount; i++) {
        // Always keep the top layer; prune only lower derived layers that are
        // too small to be useful as simulcast alternatives.
        if (i > 0 && Math.min(w, h) < minSmallAxis)
            break;
        ladder.push({ width: w, height: h, bitrate: bitrateFor(h) });
        w = roundToEven(w / 2);
        h = roundToEven(h / 2);
    }
    return ladder.reverse();
}

export function fitWithin(width: number, height: number, maxWidth: number, maxHeight: number): Size {
    if (width <= 0 || height <= 0 || maxWidth <= 0 || maxHeight <= 0)
        return { width: 0, height: 0 };
    if (width <= maxWidth && height <= maxHeight)
        return { width: roundToEven(width), height: roundToEven(height) };

    const scale = Math.min(maxWidth / width, maxHeight / height);
    return {
        width: roundToEven(width * scale),
        height: roundToEven(height * scale),
    };
}

export function webcamTopSize(width: number, height: number): Size {
    if (width <= 0 || height <= 0)
        return { width: 0, height: 0 };

    const maxWidth = 1280;
    const maxHeight = 720;
    const aspect = maxWidth / maxHeight;
    let topW = Math.min(width, maxWidth);
    let topH = topW / aspect;
    if (topH > height) {
        topH = Math.min(height, maxHeight);
        topW = topH * aspect;
    }
    return {
        width: roundToEven(topW),
        height: roundToEven(topH),
    };
}

interface Size {
    width: number;
    height: number;
}

function roundToEven(value: number): number {
    return Math.max(2, Math.round(value / 2) * 2);
}
