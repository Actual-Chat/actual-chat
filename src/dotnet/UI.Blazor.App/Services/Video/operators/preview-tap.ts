import { tap, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import type { CapturedFrame } from '../frame-envelopes';

const { warnLog } = getLogs('VideoPipeline');
// Throttle: log first, then 1-in-N to avoid drowning the console if a
// device-level failure (GPU OOM, lost surface) starts dropping every clone.
const LogEveryN = 30;

export interface PreviewTapOptions {
    /** Returns the writer to forward to, or `null` to detach. Called per
     *  frame so the recorder can swap / detach without restarting. */
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null;
}

// Side-effect operator: forwards a clone of every frame to a writer
// (typically the local self-view's `MediaStreamTrackGenerator`).
// Cloning is mandatory — pipeline owns the original; writer owns the clone.
export function previewTap(opts: PreviewTapOptions): PipeOperator<CapturedFrame, CapturedFrame> {
    const { getWriter } = opts;
    return tap(async (envelope: CapturedFrame): Promise<void> => {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null;
        try {
            writer = getWriter();
        } catch (e) {
            envelope.stats.previewClonesFailed++;
            if (envelope.stats.previewClonesFailed === 1
                || envelope.stats.previewClonesFailed % LogEveryN === 0) {
                warnLog?.log(`previewTap: getWriter failed (#${envelope.stats.previewClonesFailed}):`, e);
            }
            return;
        }
        if (!writer) return;

        let clone: VideoFrame;
        try {
            clone = envelope.frame.clone();
        } catch (e) {
            envelope.stats.previewClonesFailed++;
            if (envelope.stats.previewClonesFailed === 1
                || envelope.stats.previewClonesFailed % LogEveryN === 0) {
                warnLog?.log(`previewTap: frame clone failed (#${envelope.stats.previewClonesFailed}):`, e);
            }
            return;
        }
        try {
            await writer.write(clone);
        } catch (e) {
            try { clone.close(); } catch { /* ignore */ }
            envelope.stats.previewClonesFailed++;
            if (envelope.stats.previewClonesFailed === 1
                || envelope.stats.previewClonesFailed % LogEveryN === 0) {
                warnLog?.log(`previewTap: writer.write failed (#${envelope.stats.previewClonesFailed}):`, e);
            }
        }
    });
}
