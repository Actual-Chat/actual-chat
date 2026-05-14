import { tap, type PipeOperator } from 'ix-ext';
import { VIDEO } from 'app-constants';
import { getLogs } from 'logging';
import type { MonotonicTime } from 'clocks';
import type { NormalizedFrame } from '../frame-envelopes';
import type { PreviewFramePresentation } from '../sender/recorder-worker-contract';

const { warnLog } = getLogs('VideoPipeline');
// Log first, then 1-in-N — prevents flooding when a device-level failure drops every clone.
const LogEveryN = 30;

export interface PreviewTapOptions {
    // Called per frame so the recorder can swap / detach without restarting.
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null;
    reportFrame?: (frame: VideoFrame) => void | Promise<void>;
    reportPresentation?: (presentation: PreviewFramePresentation) => void;
    frameDurationMs?: number;
    nowMs?: () => number;
    sleep?: (delayMs: number) => Promise<void>;
}

// Forwards a clone of the normalized sender surface to a writer (typically the
// self-view's MediaStreamTrackGenerator). Cloning is mandatory — pipeline owns
// the original; the selected preview sink observes a short-lived clone.
export function previewTap(opts: PreviewTapOptions): PipeOperator<NormalizedFrame, NormalizedFrame> {
    const { getWriter, reportFrame, reportPresentation } = opts;
    const frameDurationMs = opts.frameDurationMs ?? getFrameDurationMs();
    const nowMs = opts.nowMs ?? (() => performance.now());
    const sleep = opts.sleep ?? ((delayMs: number) => new Promise<void>(resolve => setTimeout(resolve, delayMs)));
    const pacer = new PreviewPacer(frameDurationMs);
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
        if (!writer && !reportFrame) return;
        reportPresentation?.({ rotation: envelope.rotation });

        if (writer) {
            const desiredSize = writer.desiredSize;
            if (desiredSize !== null && desiredSize <= 0)
                return;
        }

        const plan = pacer.plan(envelope.capturedAt, nowMs());
        if (plan === 'skip')
            return;
        if (plan.delayMs > 0)
            await sleep(plan.delayMs);

        if (writer) {
            const desiredSizeAfterDelay = writer.desiredSize;
            if (desiredSizeAfterDelay !== null && desiredSizeAfterDelay <= 0)
                return;
        }

        let clone: VideoFrame;
        try {
            clone = envelope.frame.clone();
        } catch (e) {
            reportFailure('frame clone', e);
            return;
        }
        try {
            if (writer) {
                await writer.write(clone);
            } else if (reportFrame) {
                await reportFrame(clone);
            }
        } catch (e) {
            reportFailure(writer ? 'writer.write' : 'reportFrame', e);
        } finally {
            try { clone.close(); } catch { /* ignore */ }
        }
    });
}

type PreviewPacePlan = { delayMs: number } | 'skip';

class PreviewPacer {
    private anchorEpoch: number | null = null;
    private anchorCaptureMs = 0;
    private anchorWallMs = 0;
    private lastDueMs: number | null = null;

    constructor(private readonly frameDurationMs: number) {}

    plan(capturedAt: MonotonicTime, nowMs: number): PreviewPacePlan {
        if (this.anchorEpoch !== capturedAt.epoch) {
            this.anchorEpoch = capturedAt.epoch;
            this.anchorCaptureMs = capturedAt.timeMs;
            this.anchorWallMs = nowMs;
            this.lastDueMs = nowMs;
            return { delayMs: 0 };
        }

        const naturalDueMs = this.anchorWallMs + capturedAt.timeMs - this.anchorCaptureMs;
        if (nowMs - naturalDueMs >= 2 * this.frameDurationMs)
            return 'skip';

        const minDueMs = this.lastDueMs === null
            ? nowMs
            : this.lastDueMs + this.frameDurationMs;
        const dueMs = Math.max(naturalDueMs, minDueMs);
        this.lastDueMs = dueMs;
        return { delayMs: Math.max(0, dueMs - nowMs) };
    }
}

function getFrameDurationMs(): number {
    const video = VIDEO as typeof VIDEO | undefined;
    return video?.frameDurationMs ?? 1000 / 30;
}
