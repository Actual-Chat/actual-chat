import { tap, type PipeOperator } from 'ix-ext';
import { VIDEO } from 'app-constants';
import { getLogs } from 'logging';
import type { MonotonicTime } from 'clocks';
import type { NormalizedFrame } from '../frame-envelopes';
import type { PreviewFramePresentation } from '../sender/recorder-worker-contract';
import { HAS_VF_ROTATION_INIT, wrapWithRotation } from '../video-frame-caps';

const { warnLog } = getLogs('VideoPipeline');
// Log first, then 1-in-N — prevents flooding when a device-level failure drops every clone.
const LogEveryN = 30;

export interface PreviewForwarderOptions {
    // Called per frame so the recorder can swap / detach without restarting.
    getWriter: () => WritableStreamDefaultWriter<VideoFrame> | null;
    reportFrame?: (frame: VideoFrame) => void | Promise<void>;
    reportPresentation?: (presentation: PreviewFramePresentation) => void;
    maxBufferedFrames?: number;
    frameDurationMs?: number;
    nowMs?: () => number;
    sleep?: (delayMs: number) => Promise<void>;
}

interface PreviewQueueItem {
    frame: VideoFrame;
    capturedAt: MonotonicTime;
    rotation: number;
    writer: WritableStreamDefaultWriter<VideoFrame> | null;
    reportFrame?: (frame: VideoFrame) => void | Promise<void>;
}

// Forwards a clone of the normalized sender surface to a writer (typically the
// self-view's MediaStreamTrackGenerator). Cloning is mandatory — pipeline owns
// the original; the selected preview sink observes a short-lived clone.
export function previewForwarder(opts: PreviewForwarderOptions): PipeOperator<NormalizedFrame, NormalizedFrame> {
    const { getWriter, reportFrame, reportPresentation } = opts;
    const maxBufferedFrames = Math.max(1, Math.floor(opts.maxBufferedFrames ?? 3));
    const frameDurationMs = opts.frameDurationMs ?? getFrameDurationMs();
    const nowMs = opts.nowMs ?? (() => performance.now());
    const sleep = opts.sleep ?? ((delayMs: number) => new Promise<void>(resolve => setTimeout(resolve, delayMs)));
    const pacer = new PreviewPacer(frameDurationMs);
    const queue: PreviewQueueItem[] = [];
    let failures = 0;
    let pumpRunning = false;
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
    const enqueue = (item: PreviewQueueItem): void => {
        while (queue.length >= maxBufferedFrames) {
            const dropped = queue.shift();
            if (dropped)
                closeFrame(dropped.frame);
        }
        queue.push(item);
        startPump();
    };
    const startPump = (): void => {
        if (pumpRunning)
            return;

        pumpRunning = true;
        void pump();
    };
    const pump = async (): Promise<void> => {
        try {
            while (queue.length > 0) {
                const item = queue.shift()!;
                try {
                    const plan = pacer.plan(item.capturedAt, nowMs());
                    if (plan === 'skip')
                        continue;
                    if (plan.delayMs > 0)
                        await sleep(plan.delayMs);

                    if (item.writer) {
                        const desiredSize = item.writer.desiredSize;
                        if (desiredSize !== null && desiredSize <= 0)
                            continue;

                        reportPresentationOnce(item.rotation);
                        await item.writer.write(item.frame);
                    } else if (item.reportFrame) {
                        reportPresentationOnce(item.rotation);
                        await item.reportFrame(item.frame);
                    }
                } catch (e) {
                    reportFailure(item.writer ? 'writer.write' : 'reportFrame', e);
                } finally {
                    closeFrame(item.frame);
                }
            }
        } finally {
            pumpRunning = false;
            if (queue.length > 0)
                startPump();
        }
    };
    return tap((envelope: NormalizedFrame): void => {
        let writer: WritableStreamDefaultWriter<VideoFrame> | null;
        try {
            writer = getWriter();
        } catch (e) {
            reportFailure('getWriter', e);
            return;
        }
        if (!writer && !reportFrame) return;

        if (writer) {
            const desiredSize = writer.desiredSize;
            if (desiredSize !== null && desiredSize <= 0)
                return;
        }

        let clone: VideoFrame;
        try {
            clone = envelope.frame.clone();
        } catch (e) {
            reportFailure('frame clone', e);
            return;
        }

        // MSTG path (Chromium): attach display rotation as VideoFrame
        // metadata so the <video> element auto-rotates; report rotation=0
        // to the presentation callback so the CSS --video-rotation path
        // doesn't double-rotate. Canvas-preview path (writer === null)
        // keeps the legacy path — canvas drawImage ignores VideoFrame
        // rotation metadata, so it relies on CSS rotation.
        let frame = clone;
        let displayRotation = envelope.rotation;
        if (writer && HAS_VF_ROTATION_INIT && envelope.rotation !== 0) {
            frame = wrapWithRotation(clone, envelope.rotation);
            closeFrame(clone);
            displayRotation = 0;
        }

        enqueue({
            frame,
            capturedAt: envelope.capturedAt,
            rotation: displayRotation,
            writer,
            reportFrame,
        });
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
