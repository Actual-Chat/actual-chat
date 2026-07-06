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
    // Canonical index in the full ladder (stable layer→resolution map on the
    // wire). Defaults to the array index — set only on sparse demand subsets.
    layerId?: number;
}

export const MIN_SIMULCAST_SMALL_AXIS = 150;

// Derived (lower) tier dims are rounded up to this multiple. HEVC's minimum
// coding block is 8×8, so a non-mod-8 height (e.g. 180) is encoded at the
// rounded-up coded size (184) with a conformance window — which Edge HEVC HW
// mis-renders as a top-left-corner crop. mod-8 keeps coded == display.
const DERIVED_TIER_MULTIPLE = 8;

// fps for the bottom (unfocused-thumbnail) tier; higher tiers run full rate.
export const LOW_TIER_FPS = 10;

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
// tier is ~½ width × ½ height (¼ pixels) of the next, rounded up to a multiple
// of `DERIVED_TIER_MULTIPLE` so coded == display (no HEVC conformance window).
// Result is bottom-first.
//
// Examples:
//  - Camera 3-tier @ 720p: (1280,720,3) → [320×184, 640×360, 1280×720]
//  - Camera 2-tier dropTop @ 360p: (640,360,2) → [320×184, 640×360]
//  - ScreenCast 2-tier @ 1080p: (1920,1080,2) → [960×544, 1920×1080]
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
        w = roundToMultiple(w / 2, DERIVED_TIER_MULTIPLE);
        h = roundToMultiple(h / 2, DERIVED_TIER_MULTIPLE);
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
            // Top stays exact (source/identity slot); lower tiers mod-8 so HEVC
            // coded == display (see DERIVED_TIER_MULTIPLE).
            width: isTop ? roundToEven(size.width) : roundToMultiple(size.width, DERIVED_TIER_MULTIPLE),
            height: isTop ? roundToEven(size.height) : roundToMultiple(size.height, DERIVED_TIER_MULTIPLE),
            baseBitrateKbps: bitrate,
            bitrateKbps: bitrate,
        });
    }
    return ladder;
}

// Demand-driven target fps from the highest layer any viewer requests. Only
// the BOTTOM tier (the tiny unfocused thumbnail, layerId 0) is paced down — any
// higher request is a real, focused-sized view that must stay smooth, even when
// the focused viewer's screen only warrants L1 (e.g. a focused tile on a phone).
// Keying on the requested layer (not focus directly) is what the sender can see;
// thumbnails are the only thing rendered small enough to request L0.
//   bottom tier (L0, unfocused thumbnail) → LOW_TIER_FPS (10)
//   any higher tier (focused / real view) → captureFps (full)
// `maxLayerId < 0` (nobody subscribed / all paused) → 0 = idle (unused in the
// live path). Unknown/out-of-range ladder → full rate (don't pace).
// The caller (applyTargetFps) invokes this only under an active thermal
// ceiling — low fps looks bad, so it's thermal load-shedding, not a default.
export function fpsForRequestedLayer(
    ladder: readonly LayerConfig[] | null,
    maxLayerId: number,
    captureFps: number,
): number {
    if (maxLayerId < 0)
        return 0;
    if (!ladder || ladder.length === 0 || maxLayerId >= ladder.length)
        return captureFps;
    // L0 is only a "thumbnail" when a higher tier exists above it; in a 1-tier
    // ladder L0 IS the full view and must stay smooth.
    const fps = maxLayerId === 0 && ladder.length > 1 ? LOW_TIER_FPS : captureFps;
    return Math.min(fps, captureFps);
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

function roundToMultiple(value: number, multiple: number): number {
    return Math.max(multiple, Math.round(value / multiple) * multiple);
}

function roundToEven(value: number): number {
    return roundToMultiple(value, 2);
}
