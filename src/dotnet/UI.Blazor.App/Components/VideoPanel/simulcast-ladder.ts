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

// Long-side schedule used to derive simulcast tier dims from the source's
// actual dims. Short side is computed from the source aspect so portrait
// sources produce portrait tiers and landscape sources produce landscape
// tiers. The 720-tier is dropped when the source already covers 1080+ (see
// `NEAR_TIER_THRESHOLD`) — running a 720p extra alongside a 1080p source is
// near-duplicate work for marginal quality gain.
const TIER_LONG_SIDES: readonly number[] = [320, 640, 1280, 1920];

// Drop a candidate tier when its long-side is greater than this fraction of
// the next-higher kept tier's long-side. 0.6 drops 1280 when 1920 is the top
// (1280/1920 ≈ 0.667) but keeps 640 when 1280 is the top (640/1280 = 0.5).
const NEAR_TIER_THRESHOLD = 0.6;

export interface LadderBuildInput {
    /** Layer count requested by the server (caps via MaxSpatialLayer). */
    count: number;
    /** Source dims as the running encoder sees them. */
    srcWidth: number;
    srcHeight: number;
    /** Bitrate provider — caller injects the codec/mode-aware lookup. */
    bitrateFor: (height: number) => number;
}

// Builds a simulcast ladder shaped to the source. Top tier is always the
// source itself (replaces the augment-with-running-base trick that used to
// add a transposed duplicate when the C# ladder was hardcoded landscape and
// the source was portrait). Lower tiers come from `TIER_LONG_SIDES`,
// near-tier-deduped and oriented to match the source aspect ratio. Result is
// truncated to `count`, keeping the top tiers (receivers cap by spatial-id;
// dropping from the bottom preserves the highest reachable quality).
export function buildLadderForSource(input: LadderBuildInput): SpatialLayerConfig[] {
    const { count, srcWidth, srcHeight, bitrateFor } = input;
    if (count <= 0 || srcWidth <= 0 || srcHeight <= 0)
        return [];

    const srcLong = Math.max(srcWidth, srcHeight);
    const srcShort = Math.min(srcWidth, srcHeight);
    const isPortrait = srcHeight > srcWidth;

    // Candidate long-sides ≤ source, plus the source itself as the top.
    const candidates = TIER_LONG_SIDES.filter(l => l < srcLong);
    candidates.push(srcLong);

    // Top-down near-tier dedupe.
    const keptLong: number[] = [];
    for (let i = candidates.length - 1; i >= 0; i--) {
        const cand = candidates[i];
        const above = keptLong.length === 0 ? Infinity : keptLong[keptLong.length - 1];
        if (cand / above > NEAR_TIER_THRESHOLD) continue;
        keptLong.push(cand);
    }
    keptLong.reverse(); // ascending again

    // Truncate to `count`, keeping the top.
    const startIndex = Math.max(0, keptLong.length - count);
    const finalLong = keptLong.slice(startIndex);

    return finalLong.map(longSide => {
        // Top tier reuses the running encoder's exact dims to avoid drift; the
        // base encoder is already running at those, so even-rounding is a no-op
        // there. Lower tiers derive shortSide from the source aspect, then
        // round to even — most encoders reject odd dims.
        const isTop = longSide === srcLong;
        const width = isTop
            ? srcWidth
            : (isPortrait ? roundToEven(longSide * srcShort / srcLong) : longSide);
        const height = isTop
            ? srcHeight
            : (isPortrait ? longSide : roundToEven(longSide * srcShort / srcLong));
        const bitrate = bitrateFor(height);
        return { width, height, bitrate };
    });
}

function roundToEven(value: number): number {
    return Math.max(2, Math.round(value / 2) * 2);
}
