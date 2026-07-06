export const THUMBNAIL_FPS = 10;

export interface TargetFpsInputs {
    captureFps: number;
    // Thermal ceiling from C# QC; 0 = none.
    fpsCeiling: number;
    // Server aggregate ("every active viewer sees a thumbnail") held past the shed delay.
    thumbnailShedActive: boolean;
    isSpeaking: boolean;
    // 0 ⇒ the own preview is the large focused tile — shedding would pace it too.
    remoteStreamCount: number;
    isScreencast: boolean;
}

// Encoder fps policy: min(capture, thermal ceiling), plus the thumbnail shed —
// only when every active viewer displays a thumbnail, the local user is
// silent, the self-preview isn't the large tile, and the source is a camera.
export function computeTargetFps(inputs: TargetFpsInputs): number {
    let fps = inputs.captureFps;
    if (inputs.fpsCeiling > 0)
        fps = Math.min(fps, inputs.fpsCeiling);
    const shed = inputs.thumbnailShedActive
        && !inputs.isSpeaking
        && !inputs.isScreencast
        && inputs.remoteStreamCount > 0;
    return shed ? Math.min(fps, THUMBNAIL_FPS) : fps;
}
