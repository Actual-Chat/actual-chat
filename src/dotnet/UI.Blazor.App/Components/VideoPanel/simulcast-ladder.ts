// Minimal structural type for ladder operations — kept here (instead of
// re-exporting from the worker contract) so test code can import this module
// without pulling in the full RPC/logging dependency chain.
export interface LadderTier {
    width: number;
    height: number;
    bitrate?: number;
}

// Returns true when the incoming ladder's top tier is taller than the existing
// one — used to decide whether to replace the existing ladder. Compares by
// height (matches the height-based bitrate table). Bug N's ladder-persistence
// logic uses this to accept upgrades while rejecting same- or lower-quality
// pushes from C#.
export function hasHigherTopTier(incoming: readonly LadderTier[], existing: readonly LadderTier[]): boolean {
    if (incoming.length === 0 || existing.length === 0) return false;
    const incomingTop = incoming[incoming.length - 1];
    const existingTop = existing[existing.length - 1];
    return incomingTop.height > existingTop.height;
}
