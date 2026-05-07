import { tap, type PipeOperator } from 'ix-ext';
import { getLogs } from 'logging';
import type { CapturedFrame } from '../frame-envelopes';

const { debugLog } = getLogs('VideoPipeline');

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
            debugLog?.log('previewTap: getWriter failed:', e);
            return;
        }
        if (!writer) return;

        let clone: VideoFrame;
        try {
            clone = envelope.frame.clone();
        } catch (e) {
            debugLog?.log('previewTap: frame clone failed:', e);
            return;
        }
        try {
            await writer.write(clone);
        } catch (e) {
            try { clone.close(); } catch { /* ignore */ }
            debugLog?.log('previewTap: writer.write failed:', e);
        }
    });
}
