// One layer. Defined here (a leaf module with no imports of the
// worker contract) so test code and the worker contract can both reference
// the same type without test imports pulling in the full RPC/logging
// dependency chain via the worker contract module. The base-layer dims/bitrate
// live on `EncoderConfig`; additional layers (higher-res for closer peers) are
// enumerated as `LayerConfig[]` and produce parallel encoder instances
// tagged with `layerId = index` on each emitted chunk.
export interface LayerConfig {
    width: number;
    height: number;
    baseBitrateKbps?: number;
    bitrateKbps: number;
}

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
    /** Bottom-first bitrate ladder in kbps. The last value belongs to the top tier. */
    bitratesKbps: readonly number[];
    /** Explicit bottom-first tier sizes. When set, bypasses ½-derivation — the
        ladder uses these exact dims, for ladders that aren't a clean ½ chain
        (e.g. 180/360/720/1080, where 720→1080 is ×1.5). Last entry is the top tier. */
    tierSizes?: readonly { width: number; height: number }[];
}

// Builds a simulcast ladder. Top tier is `(topWidth, topHeight)` — the
// caller's chosen source dim (becomes the downscaler's identity slot:
// `clone()` instead of a canvas-backed VideoFrame, no GPU sync). Each lower
// tier is ½ width × ½ height (¼ pixels) of the next, even-rounded so encoders
// accept them. Result is bottom-first.
//
// Examples:
//  - Camera 3-tier @ 720p: (1280,720,3) → [320×180, 640×360, 1280×720]
//  - Camera 2-tier dropTop @ 360p: (640,360,2) → [320×180, 640×360]
//  - ScreenCast 2-tier @ 1080p: (1920,1080,2) → [960×540, 1920×1080]
//
// Capped to maxTierCount regardless of `tierCount`.
export function buildLadder(input: LadderBuildInput): LayerConfig[] {
    const { topWidth, topHeight, tierCount, maxTierCount, bitratesKbps, tierSizes } = input;
    if (tierCount <= 0 || topWidth <= 0 || topHeight <= 0)
        return [];

    const minSmallAxis = input.minSmallAxis ?? MIN_SIMULCAST_SMALL_AXIS;

    if (tierSizes && tierSizes.length > 0)
        return buildExplicitLadder(tierSizes, tierCount, maxTierCount, bitratesKbps, minSmallAxis);

    const effectiveCount = Math.min(tierCount, maxTierCount, bitratesKbps.length);
    if (effectiveCount <= 0)
        return [];

    const ladder: LayerConfig[] = [];
    // Build top-down then reverse to keep the bottom-first invariant.
    let w = topWidth;
    let h = topHeight;
    for (let i = 0; i < effectiveCount; i++) {
        // Always keep the top layer; prune only lower derived layers that are
        // too small to be useful as simulcast alternatives.
        if (i > 0 && Math.min(w, h) < minSmallAxis)
            break;
        ladder.push({
            width: w,
            height: h,
            baseBitrateKbps: bitratesKbps[effectiveCount - i - 1],
            bitrateKbps: bitratesKbps[effectiveCount - i - 1],
        });
        w = roundToEven(w / 2);
        h = roundToEven(h / 2);
    }
    return ladder.reverse();
}

// Bottom-first explicit-size ladder. Resolutions come straight from
// `tierSizes`; bitrates pair to them from the top down (top tier ↔ last
// bitrate). A cap below `tierSizes.length` keeps the top tiers and drops the
// smallest (bottom) ones — matching the ½-derived path's "keep top" rule.
function buildExplicitLadder(
    tierSizes: readonly { width: number; height: number }[],
    tierCount: number,
    maxTierCount: number,
    bitratesKbps: readonly number[],
    minSmallAxis: number,
): LayerConfig[] {
    const count = Math.min(tierCount, maxTierCount, bitratesKbps.length, tierSizes.length);
    if (count <= 0)
        return [];

    const start = tierSizes.length - count;
    const ladder: LayerConfig[] = [];
    let started = false;
    for (let i = start; i < tierSizes.length; i++) {
        const size = tierSizes[i];
        const isTop = i === tierSizes.length - 1;
        // Drop the smallest bottom tiers below the usable threshold; always keep the top.
        if (!started && !isTop && Math.min(size.width, size.height) < minSmallAxis)
            continue;
        started = true;
        const bitrate = bitratesKbps[bitratesKbps.length - (tierSizes.length - i)];
        ladder.push({
            width: roundToEven(size.width),
            height: roundToEven(size.height),
            baseBitrateKbps: bitrate,
            bitrateKbps: bitrate,
        });
    }
    return ladder;
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

export function cameraTopSize(width: number, height: number): Size {
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
