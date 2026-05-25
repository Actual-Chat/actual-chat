// `VideoFrameInit.rotation` / `flip` ship in Chrome 117+ but are not yet in
// lib.dom.d.ts and not in Firefox / Safari. Augment the init types locally
// and probe at module load.

export interface VideoFrameRotationInit {
    rotation?: number;
    flip?: boolean;
}

// True when `new VideoFrame(src, { rotation })` is honored as display
// metadata by the current engine. Constructs a 1x1 BGRA probe frame with
// rotation: 90 and verifies the constructed frame echoes it back.
export const HAS_VF_ROTATION_INIT: boolean = (() => {
    if (typeof VideoFrame === 'undefined')
        return false;
    try {
        const buf = new Uint8Array(4);
        const init: VideoFrameBufferInit & VideoFrameRotationInit = {
            format: 'BGRA',
            codedWidth: 1,
            codedHeight: 1,
            timestamp: 0,
            rotation: 90,
        };
        const probe = new VideoFrame(buf, init);
        const ok = (probe as VideoFrame & VideoFrameRotationInit).rotation === 90;
        probe.close();
        return ok;
    } catch {
        return false;
    }
})();

// Wrap a VideoFrame with display-rotation metadata. Returns the original
// frame untouched when rotation is 0 or the engine doesn't support the
// init field — caller doesn't need to feature-check separately.
//
// `quarters` is a RotationQuarter (0..3); converted to degrees internally.
// The wrap shares the source's GPU plane; the caller owns the lifetime of
// the returned frame and MUST NOT close the source separately if it
// transfers ownership in. Typical call sites close the source after wrap.
export function wrapWithRotation(frame: VideoFrame, quarters: number): VideoFrame {
    if (!HAS_VF_ROTATION_INIT || quarters === 0)
        return frame;
    const init: VideoFrameInit & VideoFrameRotationInit = {
        rotation: quarters * 90,
    };
    return new VideoFrame(frame, init);
}
