// One simulcast layer. Defined here (a leaf module with no imports) so test
// code and the worker contract can both reference the same type without test
// imports pulling in the full RPC/logging dependency chain via the worker
// contract module. The base-layer dims/bitrate live on `EncoderConfig`;
// additional layers (higher-res for closer peers) are enumerated as
// `SpatialLayerConfig[]` and produce parallel encoder instances tagged with
// `SpatialLayerId = index + 1` on each emitted chunk.
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

// When a hot setSpatialLayers is applied and the running base encoder is
// taller than the incoming ladder's top tier, the resulting wire layout is
// non-monotonic in spatial-id (base is bigger than every "extra"), and the
// receiver-side filter (which sorts by spatial-id) can't reach the base via
// its quality-driven cap. Returning a ladder with an extra tier matching the
// running base restores monotonicity at the wire level — at the cost of
// running a duplicate encoder at the same resolution. Returns the input
// unchanged when augmentation is unnecessary or impossible.
export function maybeAugmentLadderForRunningBase(
    incoming: readonly SpatialLayerConfig[],
    runningBase: { width: number; height: number; bitrate: number } | null,
): SpatialLayerConfig[] {
    if (runningBase === null || runningBase.height <= 0 || incoming.length === 0)
        return [...incoming];
    const top = incoming[incoming.length - 1];
    if (runningBase.height <= top.height)
        return [...incoming];
    return [...incoming, {
        width: runningBase.width,
        height: runningBase.height,
        bitrate: runningBase.bitrate,
    }];
}
