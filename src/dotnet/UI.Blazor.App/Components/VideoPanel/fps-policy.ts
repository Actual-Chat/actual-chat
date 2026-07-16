export const THUMBNAIL_FPS = 10;
// Capture floor while the encode target is shed: the camera ISP burns more than
// encode+render combined. 15, not THUMBNAIL_FPS — Android modes are 15/30.
export const CAPTURE_SHED_FPS = 15;

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

// Capture-fps follower policy: renegotiate the camera down to CAPTURE_SHED_FPS
// whenever the encode target sits at or below it (thumbnail shed, thermal
// ceiling), so the vendor ISP pipeline sheds too; never undershoots the target.
export function computeCaptureFps(targetFps: number, requestedFps: number): number {
    return targetFps <= CAPTURE_SHED_FPS
        ? Math.min(CAPTURE_SHED_FPS, requestedFps)
        : requestedFps;
}
