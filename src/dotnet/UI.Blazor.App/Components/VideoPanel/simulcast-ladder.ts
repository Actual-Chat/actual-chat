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

// Returns true when the incoming ladder's top tier is taller than the existing
// one — used to decide whether to replace the existing ladder. Compares by
// height (matches the height-based bitrate table). The ladder-persistence path
// uses this to accept upgrades while rejecting same- or lower-quality pushes
// from C#.
export function hasHigherTopTier(
    incoming: readonly SpatialLayerConfig[],
    existing: readonly SpatialLayerConfig[],
): boolean {
    if (incoming.length === 0 || existing.length === 0) return false;
    const incomingTop = incoming[incoming.length - 1];
    const existingTop = existing[existing.length - 1];
    return incomingTop.height > existingTop.height;
}

// Cap on simulcast tier count. Caps at 2 because each additional tier costs
// another concurrent HW encoder slot + another `new VideoFrame(canvas)`
// implicit GPU sync per source frame in the WebGPU downscaler — both scarce
// on iOS Safari. Two tiers gives a clean 2× drop ratio with one identity
// (top = source, no canvas ctor) and one rendered (source/2).
export const MAX_SIMULCAST_TIERS = 2;

export interface LadderBuildInput {
    /** Layer count requested by the server (caps via MaxSpatialLayer). */
    count: number;
    /** Source dims as the running encoder sees them. */
    srcWidth: number;
    srcHeight: number;
    /** Bitrate provider — caller injects the codec/mode-aware lookup. */
    bitrateFor: (height: number) => number;
}

// Builds a simulcast ladder shaped to the source. Top tier is the source
// itself (becomes the downscaler's identity slot — `clone()` instead of a
// canvas-backed VideoFrame, no GPU sync). Second tier is source/2 with even-
// rounded dims so encoders accept them. Result is bottom-first.
//
// Capped to MAX_SIMULCAST_TIERS regardless of `count` — the cap is the
// power/heat-bound limit, not a server-side knob.
export function buildLadderForSource(input: LadderBuildInput): SpatialLayerConfig[] {
    const { count, srcWidth, srcHeight, bitrateFor } = input;
    if (count <= 0 || srcWidth <= 0 || srcHeight <= 0)
        return [];

    const top: SpatialLayerConfig = {
        width: srcWidth,
        height: srcHeight,
        bitrate: bitrateFor(srcHeight),
    };

    const effectiveCount = Math.min(count, MAX_SIMULCAST_TIERS);
    if (effectiveCount === 1) return [top];

    const halfW = roundToEven(srcWidth / 2);
    const halfH = roundToEven(srcHeight / 2);
    const bottom: SpatialLayerConfig = {
        width: halfW,
        height: halfH,
        bitrate: bitrateFor(halfH),
    };
    return [bottom, top];
}

function roundToEven(value: number): number {
    return Math.max(2, Math.round(value / 2) * 2);
}
