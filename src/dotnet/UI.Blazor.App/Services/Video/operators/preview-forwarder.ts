import { tap, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import type { CapturedBundle } from '../frame-envelopes';
import type { PreviewFramePresentation } from '../sender/recorder-worker-contract';
import { HAS_VF_ROTATION_INIT, wrapWithRotation } from '../video-frame-caps';

const { warnLog } = getLogs('VideoPipeline');
// Log first, then 1-in-N — prevents flooding when a device-level failure drops every clone.
const LogEveryN = 30;

export interface PreviewForwarderOptions {
    // iOS Safari's <video> auto-orients a generated (VTG) track to upright on
    // its own — verified: a 640x360 landscape capture renders as 360x640 in
    // the element. So the CSS --video-rotation layer must NOT add a second
    // turn for the generated-track path there. Threaded from config (worker
    // realm), not probed.
    isIos?: boolean;
    // Called per frame so the recorder can swap / detach without restarting.
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null;
    reportFrame?: (frame: VideoFrame) => void | Promise<void>;
    reportPresentation?: (presentation: PreviewFramePresentation) => void;
}

// Forwards a clone of the bundle's full-res ceiling surface to a writer
// (typically the self-view's MediaStreamTrackGenerator). Cloning is mandatory —
// the pipeline owns the original; the selected preview sink observes a
// short-lived clone. Also the closer of the ceiling-when-orphan: when the
// ceiling is NOT one of the bundle's layers (the active ladder shrank below it),
// nothing downstream closes it, so this tap does after cloning.
//
// No internal queue or timer-based pacing. The upstream rVFC pump already
// drives frames at the source's natural cadence (30 Hz on a 30 fps camera),
// and the downstream <video> element renders each MSTG-fed frame as it
// arrives. A per-frame `setTimeout` pacer was previously inserted here to
// align display deltas to capture deltas — that's an inversion of effort
// (the browser already paces playback). It also showed up in profiles as the
// dominant timer churn (~25 ms / s combined across workers). Frames whose
// writer is backpressured are dropped on the spot rather than buffered:
// the bottleneck is the renderer, and a buffer here only delays the drop.
export function previewForwarder(opts: PreviewForwarderOptions): PipeOperator<CapturedBundle, CapturedBundle> {
    const { isIos, getWriter, reportFrame, reportPresentation } = opts;
    let failures = 0;
    let lastReportedRotation: number | null = null;
    const reportFailure = (where: string, e: unknown): void => {
        failures++;
        if (failures === 1 || failures % LogEveryN === 0)
            warnLog?.log(`previewForwarder: ${where} failed (#${failures}):`, e);
    };
    const reportPresentationOnce = (rotation: number): void => {
        if (!reportPresentation || lastReportedRotation === rotation)
            return;
        lastReportedRotation = rotation;
        try {
            reportPresentation({ rotation });
        } catch (e) {
            reportFailure('reportPresentation', e);
        }
    };
    const closeFrame = (frame: VideoFrame): void => {
        try { frame.close(); } catch { /* ignore */ }
    };
    return tap((bundle: CapturedBundle): void => {
        const ceiling = bundle.ceiling;
        // Closed here only when it is not also a layer (encode closes layers).
        const orphan = !bundle.layers.some(l => l.frame === ceiling);
        try {
            let writer: WritableStreamDefaultWriter<VideoFrame> | null;
            try {
                writer = getWriter();
            } catch (e) {
                reportFailure('getWriter', e);
                return;
            }
            if (!writer && !reportFrame) return;

            // Writer back-pressure: drop instead of buffer. The downstream
            // <video> element drains MSTG at its render cadence; if desiredSize
            // is exhausted, the renderer is stalled and queueing here only
            // delays the same drop while holding a GPU plane.
            if (writer && writer.desiredSize !== null && writer.desiredSize <= 0)
                return;

            let clone: VideoFrame;
            try {
                clone = ceiling.clone();
            } catch (e) {
                reportFailure('frame clone', e);
                return;
            }

            // Generated-track path (<video srcObject>): the element auto-orients,
            // so report rotation=0 to keep the CSS --video-rotation layer from
            // double-rotating. Two engines, two mechanisms:
            //   * Chromium (HAS_VF_ROTATION_INIT): stamp display rotation as
            //     VideoFrame metadata; the <video> rotates from it.
            //   * iOS Safari (VTG): the <video> auto-orients the track natively
            //     (no metadata field exists) — just skip the CSS turn.
            // Canvas-preview path (writer === null) keeps the legacy path — canvas
            // drawImage ignores VideoFrame rotation metadata, so it relies on CSS.
            let frame = clone;
            let displayRotation = bundle.rotation;
            if (writer && bundle.rotation !== 0 && (HAS_VF_ROTATION_INIT || isIos)) {
                if (HAS_VF_ROTATION_INIT) {
                    frame = wrapWithRotation(clone, bundle.rotation);
                    closeFrame(clone);
                }
                displayRotation = 0;
            }

            reportPresentationOnce(displayRotation);

            if (writer) {
                writer.write(frame)
                    .catch((e: unknown) => reportFailure('writer.write', e))
                    .finally(() => closeFrame(frame));
            } else if (reportFrame) {
                const result = reportFrame(frame);
                if (result && typeof result.then === 'function') {
                    result
                        .catch((e: unknown) => reportFailure('reportFrame', e))
                        .finally(() => closeFrame(frame));
                } else {
                    closeFrame(frame);
                }
            }
        } finally {
            if (orphan) closeFrame(ceiling);
        }
    });
}
