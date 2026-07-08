import { getLogs } from 'logging';
import type { RotationQuarter } from '../orientation/quantize';
import { isPresentGridEnabled, whenNextPresentSlot } from '../present-grid';
import type { PreviewFramePresentation } from '../sender/recorder-worker-contract';
import { HAS_VF_ROTATION_INIT, wrapWithRotation } from '../video-frame-caps';

const { warnLog } = getLogs('VideoPipeline');
// Log first, then 1-in-N — prevents flooding when a device-level failure drops every clone.
const LogEveryN = 30;

export interface PreviewSinkOptions {
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

// The self-preview tap. `forward` takes a clone of the normalized ceiling
// surface and ships it to a writer (typically the self-view's
// MediaStreamTrackGenerator). Cloning is mandatory — the caller owns the
// passed frame; the selected preview sink observes a short-lived clone.
//
// Lives as a plain sink (not a pipe operator) because the ceiling never exists
// as its own stream: the fused normalizeDownscale stage produces it and calls
// `forward` directly for every kept frame. Folding the tap there keeps ceiling
// ownership in one place instead of threading it through the bundle.
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
export interface PreviewSink {
    // Clone `frame` and ship the clone to the preview writer / report callback.
    // Does NOT take ownership of `frame` (clones it); the caller still owns and
    // closes the passed frame.
    forward(frame: VideoFrame, rotation: RotationQuarter): void;
}

export function createPreviewSink(opts: PreviewSinkOptions): PreviewSink {
    const { isIos, getWriter, reportFrame, reportPresentation } = opts;
    let failures = 0;
    let lastReportedRotation: number | null = null;
    const reportFailure = (where: string, e: unknown): void => {
        failures++;
        if (failures === 1 || failures % LogEveryN === 0)
            warnLog?.log(`previewSink: ${where} failed (#${failures}):`, e);
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
    // Present-grid hold (mobile only): stash the newest clone and flush it on
    // the shared slot boundary so the self-preview invalidates the same output
    // frame as the remote tiles (see present-grid.ts). Latest-frame-wins — a
    // newer clone replaces (closes) the held one; no queue builds up. The slot
    // timer always fires (never cleared), so this does not reintroduce the
    // cleared-before-fire timer churn the old per-frame pacer was removed for.
    let pendingFrame: VideoFrame | null = null;
    let pendingRotation: RotationQuarter = 0;
    let slotTimerArmed = false;

    const deliver = (source: VideoFrame, rotation: RotationQuarter): void => {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null;
        try {
            writer = getWriter();
        } catch (e) {
            reportFailure('getWriter', e);
            closeFrame(source);
            return;
        }
        if (!writer && !reportFrame) {
            closeFrame(source);
            return;
        }

        // Writer back-pressure: drop instead of buffer. The downstream
        // <video> element drains MSTG at its render cadence; if desiredSize
        // is exhausted, the renderer is stalled and queueing here only
        // delays the same drop while holding a GPU plane.
        if (writer && writer.desiredSize !== null && writer.desiredSize <= 0) {
            closeFrame(source);
            return;
        }

        deliverFrame(writer, source, rotation);
    };

    const deliverFrame = (
        writer: WritableStreamDefaultWriter<VideoFrame> | null,
        clone: VideoFrame,
        rotation: RotationQuarter,
    ): void => {
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
        let displayRotation = rotation;
        if (writer && rotation !== 0 && (HAS_VF_ROTATION_INIT || isIos)) {
            if (HAS_VF_ROTATION_INIT) {
                frame = wrapWithRotation(clone, rotation);
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
    };

    return {
        forward(source: VideoFrame, rotation: RotationQuarter): void {
            if (!isPresentGridEnabled()) {
                let writer: WritableStreamDefaultWriter<VideoFrame> | null;
                try {
                    writer = getWriter();
                } catch (e) {
                    reportFailure('getWriter', e);
                    return;
                }
                if (!writer && !reportFrame)
                    return;
                if (writer && writer.desiredSize !== null && writer.desiredSize <= 0)
                    return;
                let clone: VideoFrame;
                try {
                    clone = source.clone();
                } catch (e) {
                    reportFailure('frame clone', e);
                    return;
                }
                deliverFrame(writer, clone, rotation);
                return;
            }

            // Grid path: clone up front (the caller closes `source` on
            // return), stash latest-wins, flush on the slot. deliver()
            // re-runs the writer/back-pressure checks at flush time.
            let clone: VideoFrame;
            try {
                clone = source.clone();
            } catch (e) {
                reportFailure('frame clone', e);
                return;
            }
            if (pendingFrame)
                closeFrame(pendingFrame);
            pendingFrame = clone;
            pendingRotation = rotation;
            if (slotTimerArmed)
                return;
            slotTimerArmed = true;
            // Shared slot ticker (present-grid.ts): the preview flushes in the
            // same timer callback as this realm's other slot waiters, so it
            // lands on the same vsync as the tiles instead of a neighboring
            // one (a stable ±16.7ms phase split, measured on device).
            void whenNextPresentSlot().then(() => {
                slotTimerArmed = false;
                const frame = pendingFrame;
                const frameRotation = pendingRotation;
                pendingFrame = null;
                if (frame)
                    deliver(frame, frameRotation);
            });
        },
    };
}
