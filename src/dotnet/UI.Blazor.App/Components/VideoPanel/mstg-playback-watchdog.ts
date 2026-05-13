import { getLogs } from 'logging';

// MSTG playback watchdog. Runs every 2 s and watches <video>.currentTime
// for the off-thread (MediaStreamTrackGenerator + <video srcObject>) render
// path. Three independent recoveries:
//   - Consecutive-stall: ≥2 ticks with dCt < 50 ms while track is live
//     → tryPlay(); after 3 retries still stalled → signal fallback to host.
//   - Intermittent-stall score: counts net-stall ticks even when individual
//     ticks pass; fires fallback at score ≥ 4 (~8 s of cumulative no
//     progress) so skip-to-live gets first shot before we tear down.
//   - Startup-no-output: 4 ticks with videoWidth=0/readyState=0 while a
//     live track is attached → signal fallback. Workaround for Chromium
//     MSTG attach quirks that leave the element black with no decoded
//     frames despite a healthy track.
// Originally a workaround layer for browser-specific MSTG bugs and pipeline
// churn; the current pipeline should keep currentTime advancing on its own.
// Flip IS_MSTG_WATCHDOG_ENABLED to false to confirm the pipeline is healthy
// without the safety net.

const { warnLog } = getLogs('VideoPlayer');

export const IS_MSTG_WATCHDOG_ENABLED = true;
export const isMstgWatchdogEnabled = (): boolean => IS_MSTG_WATCHDOG_ENABLED;

const TICK_INTERVAL_MS = 2000;
const STALL_DCT_THRESHOLD_S = 0.05;
const CONSECUTIVE_STALL_TICKS_BEFORE_RETRY = 2;
const PLAY_RETRIES_BEFORE_FALLBACK = 3;
const INTERMITTENT_STALL_FALLBACK_SCORE = 4;
const STARTUP_NO_OUTPUT_TICKS_BEFORE_FALLBACK = 4;

export interface MstgPlaybackStallReport {
    reason: string;
    currentTime: number;
    readyState: number;
    videoWidth: number;
    videoHeight: number;
    tracks: string;
}

export interface MstgPlaybackWatchdogOptions {
    videoEl: HTMLVideoElement;
    // Retry play() — caller owns the logging context, retry counter is
    // managed inside the watchdog (resets on healthy ticks).
    tryPlay: (reason: string) => void;
    onStall: (report: MstgPlaybackStallReport) => void;
    // QC may park this stream (Float/Hide); the server drops every frame
    // until quality lifts, so dCt stays at 0 by design — caller wires
    // this to the same "expectedPaused" flag the backend uses elsewhere.
    isPaused: () => boolean;
}

export class MstgPlaybackWatchdog {
    private timer: ReturnType<typeof setInterval> | null = null;
    private lastCurrentTime = 0;
    private lastAtMs = 0;
    private consecutiveStallTicks = 0;
    private consecutivePlayRetries = 0;
    private intermittentStallScore = 0;
    private startupNoOutputTicks = 0;
    private startupNoOutputReported = false;
    private disposed = false;

    constructor(private readonly opts: MstgPlaybackWatchdogOptions) {}

    start(): void {
        if (!isMstgWatchdogEnabled()) return;
        if (this.timer !== null) return;
        if (this.disposed) return;
        this.lastCurrentTime = this.opts.videoEl.currentTime;
        this.lastAtMs = performance.now();
        this.timer = setInterval(() => this.tick(), TICK_INTERVAL_MS);
    }

    stop(): void {
        if (this.timer === null) return;
        clearInterval(this.timer);
        this.timer = null;
    }

    dispose(): void {
        this.disposed = true;
        this.stop();
    }

    // Called by the host on resume to clear counters before the first
    // post-resume tick — there's a natural dCt=0 gap while the server's
    // keyframe-lock re-yields its first frame.
    resetCounters(): void {
        this.consecutiveStallTicks = 0;
        this.consecutivePlayRetries = 0;
        this.intermittentStallScore = 0;
        this.startupNoOutputTicks = 0;
    }

    private tick(): void {
        if (this.disposed) return;
        const { videoEl, tryPlay, onStall, isPaused } = this.opts;
        const nowMs = performance.now();
        const nowCt = videoEl.currentTime;
        const dCt = nowCt - this.lastCurrentTime;
        this.lastAtMs = nowMs;
        this.lastCurrentTime = nowCt;
        if (isPaused()) {
            this.consecutiveStallTicks = 0;
            this.intermittentStallScore = 0;
            this.startupNoOutputTicks = 0;
            return;
        }
        const stream = videoEl.srcObject;
        const tracks = stream instanceof MediaStream ? stream.getTracks() : [];
        const trackInfo = tracks.map(t => `${t.kind}:${t.readyState}:muted=${t.muted}:enabled=${t.enabled}`).join('|');
        const isStalled = videoEl.paused || dCt < STALL_DCT_THRESHOLD_S;
        if (isStalled) {
            this.consecutiveStallTicks++;
            if (!videoEl.paused && tracks.length > 0 && nowCt > STALL_DCT_THRESHOLD_S)
                this.intermittentStallScore++;
            if (this.consecutiveStallTicks >= CONSECUTIVE_STALL_TICKS_BEFORE_RETRY && tracks.length > 0) {
                this.consecutivePlayRetries++;
                warnLog?.log(
                    `tickWatchdog: stall detected (paused=${videoEl.paused}, ` +
                    `dCt=${dCt.toFixed(3)}s, ticks=${this.consecutiveStallTicks}, ` +
                    `playRetries=${this.consecutivePlayRetries}), retrying play()`);
                if (!videoEl.paused && this.consecutivePlayRetries >= PLAY_RETRIES_BEFORE_FALLBACK) {
                    warnLog?.log(
                        `tickWatchdog: live MSTG playback remained stalled after ` +
                        `${this.consecutivePlayRetries} play retries, requesting fallback`);
                    onStall({
                        reason: 'live-playback-stall',
                        currentTime: nowCt,
                        readyState: videoEl.readyState,
                        videoWidth: videoEl.videoWidth,
                        videoHeight: videoEl.videoHeight,
                        tracks: trackInfo,
                    });
                    return;
                }
                this.consecutiveStallTicks = 0;
                tryPlay('watchdog-stall');
            }
        } else {
            this.consecutiveStallTicks = 0;
            this.consecutivePlayRetries = 0;
            this.intermittentStallScore = Math.max(0, this.intermittentStallScore - 1.0);
        }
        if (!videoEl.paused && this.intermittentStallScore >= INTERMITTENT_STALL_FALLBACK_SCORE && tracks.length > 0) {
            warnLog?.log(
                `tickWatchdog: intermittent MSTG playback stalls detected ` +
                `(score=${this.intermittentStallScore.toFixed(1)}, currentTime=${nowCt.toFixed(3)}s), ` +
                `requesting fallback`);
            onStall({
                reason: 'intermittent-live-playback-stall',
                currentTime: nowCt,
                readyState: videoEl.readyState,
                videoWidth: videoEl.videoWidth,
                videoHeight: videoEl.videoHeight,
                tracks: trackInfo,
            });
            return;
        }
        const hasLiveTrack = tracks.some(t => t.readyState === 'live');
        const startupHasNoOutput =
            hasLiveTrack &&
            nowCt < STALL_DCT_THRESHOLD_S &&
            videoEl.videoWidth <= 0 &&
            videoEl.videoHeight <= 0 &&
            videoEl.readyState === 0;
        if (startupHasNoOutput)
            this.startupNoOutputTicks++;
        else
            this.startupNoOutputTicks = 0;
        if (!this.startupNoOutputReported && this.startupNoOutputTicks >= STARTUP_NO_OUTPUT_TICKS_BEFORE_FALLBACK) {
            this.startupNoOutputReported = true;
            warnLog?.log(
                `tickWatchdog: startup MSTG output never appeared ` +
                `(ticks=${this.startupNoOutputTicks}, tracks=[${trackInfo}]), requesting fallback`);
            onStall({
                reason: 'startup-no-output',
                currentTime: nowCt,
                readyState: videoEl.readyState,
                videoWidth: videoEl.videoWidth,
                videoHeight: videoEl.videoHeight,
                tracks: trackInfo,
            });
        }
    }
}
