import { tap, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import type { NormalizedFrame } from '../frame-envelopes';
import type { PreviewFramePresentation } from '../sender/recorder-worker-contract';

const { warnLog } = getLogs('VideoPipeline');
// Log first, then 1-in-N — prevents flooding when a device-level failure drops every clone.
const LogEveryN = 30;

export interface PreviewTapOptions {
    // Called per frame so the recorder can swap / detach without restarting.
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null;
    reportPresentation?: (presentation: PreviewFramePresentation) => void;
}

// Forwards a clone of the normalized sender surface to a writer (typically the
// self-view's MediaStreamTrackGenerator). Cloning is mandatory — pipeline owns
// the original; writer owns the clone.
export function previewTap(opts: PreviewTapOptions): PipeOperator<NormalizedFrame, NormalizedFrame> {
    const { getWriter, reportPresentation } = opts;
    let failures = 0;
    const reportFailure = (where: string, e: unknown): void => {
        failures++;
        if (failures === 1 || failures % LogEveryN === 0)
            warnLog?.log(`previewTap: ${where} failed (#${failures}):`, e);
    };
    return tap(async (envelope: NormalizedFrame): Promise<void> => {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null;
        try {
            writer = getWriter();
        } catch (e) {
            reportFailure('getWriter', e);
            return;
        }
        if (!writer) return;
        reportPresentation?.({ rotation: envelope.rotation });

        let clone: VideoFrame;
        try {
            clone = envelope.frame.clone();
        } catch (e) {
            reportFailure('frame clone', e);
            return;
        }
        try {
            await writer.write(clone);
        } catch (e) {
            try { clone.close(); } catch { /* ignore */ }
            reportFailure('writer.write', e);
        }
    });
}
