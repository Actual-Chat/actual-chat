/**
 * RecorderPreviewView
 *
 * Follows the currently active VideoRecorder's preview output and draws it to a
 * caller-owned canvas. Encapsulates the "watch the active recorder, reattach on
 * camera switch" render loop so multiple UI surfaces (VideoPanel's self-preview,
 * JoinVideoCallModal in Settings mode) can each render the recorder feed
 * independently without coordinating with one another.
 *
 * Handles both rendering paths:
 *  - Raw preview track (when blur is off) via a CanvasVideoRenderer;
 *  - Blurred VideoFrames (when blur is on) via `recorder.addPreviewFrameListener`.
 *
 * Does NOT toggle caller-specific UI classes (`.starting`, `.has-video`, etc.) —
 * callers do that themselves via the onAttach / onDetach / onFirstFrame hooks.
 */

import { getLogs } from 'logging';
import {
    addActiveRecorderListener,
    getActiveRecorder,
    type PreviewFrameListener,
    type VideoRecorder,
} from '../../../Components/VideoPanel/video-recorder';
import { CanvasVideoRenderer } from './canvas-video-renderer';
import { CanvasTarget } from './canvas-target';
import { BG_CANVAS_WIDTH, BG_DRAW_INTERVAL_MS, BG_FILTER } from './bg-canvas-settings';

const { infoLog } = getLogs('VideoRecorder');

export interface RecorderPreviewViewOptions {
    /** Main canvas where the preview (raw or blurred) is drawn. */
    canvas: HTMLCanvasElement;
    /** Optional low-resolution background canvas (e.g. for a focused-view blur backdrop). */
    bgCanvas?: HTMLCanvasElement;
    /** Unique per-instance key for fastRaf scheduling. */
    rafKey: string;
    /** Which recorder kind to follow. Defaults to webcam (0). */
    streamKind?: number;
    /** Called when we attach to a recorder with a live preview track. */
    onAttach?: (recorder: VideoRecorder) => void;
    /** Called when we detach (track gone, recorder gone). */
    onDetach?: () => void;
    /** Called once per attach, after the first frame reaches the canvas. */
    onFirstFrame?: () => void;
    /** Called when the "starting" state changes (recorder intends to produce video but first frame hasn't landed yet). */
    onStartingChange?: (starting: boolean) => void;
}

export class RecorderPreviewView {
    private readonly options: RecorderPreviewViewOptions;
    private readonly canvasTarget: CanvasTarget;
    private readonly bgCanvasTarget: CanvasTarget | null;
    private readonly bgContainer: HTMLElement | null;
    private readonly streamKind: number;
    private lastBgDrawTime = 0;

    private renderer: CanvasVideoRenderer | null = null;
    private attachedRecorder: VideoRecorder | null = null;
    private attachedTrack: MediaStreamTrack | null = null;
    private unsubscribeFrames: (() => void) | null = null;
    private firstFrameFired = false;
    private lastStarting = false;
    private disposed = false;
    private _paused = false;
    // Registry subscription — fires on recorder register/unregister for our streamKind.
    private unsubscribeRegistry: (() => void) | null = null;
    // The recorder currently being followed (may differ from attachedRecorder
    // until its track goes live). We hold track/state/blur subscriptions on it.
    private followedRecorder: VideoRecorder | null = null;
    private followUnsubscribers: (() => void)[] = [];
    // Per-attach counter so each CanvasVideoRenderer instance gets a unique
    // fastRaf key. Without this, detach+attach happening in the same frame
    // (e.g. after unpausing into a newly-swapped camera track) would dedup the
    // new renderer's initial fastRaf against the old renderer's still-pending
    // schedule — the new renderer would never get its first frame scheduled.
    private attachSeq = 0;

    static create(options: RecorderPreviewViewOptions): RecorderPreviewView {
        return new RecorderPreviewView(options);
    }

    constructor(options: RecorderPreviewViewOptions) {
        this.options = options;
        this.canvasTarget = new CanvasTarget(options.canvas);
        this.bgCanvasTarget = options.bgCanvas ? new CanvasTarget(options.bgCanvas, false, BG_FILTER) : null;
        this.bgContainer = options.bgCanvas?.parentElement ?? null;
        this.streamKind = options.streamKind ?? 0;

        // Subscribe to recorder register/unregister events and immediately
        // reconcile with whatever recorder is already active (common case:
        // the recorder registers synchronously in its constructor, before
        // this view is even constructed).
        this.unsubscribeRegistry = addActiveRecorderListener((recorder, kind) => {
            if (kind !== this.streamKind) return;
            this.followRecorder(recorder);
        });
        this.followRecorder(getActiveRecorder(this.streamKind));
    }

    /** True when currently attached to a recorder with a live track. */
    public isAttached(): boolean {
        return this.attachedRecorder !== null;
    }

    /**
     * When true, this view stops drawing new frames (both raw and blurred)
     * and keeps the last frame on the canvas — attach/detach logic also
     * freezes so a reattach won't wipe the canvas. Independent of the
     * recorder's own pause flag honored via `respectPauseFlag`.
     */
    public get paused(): boolean {
        return this._paused;
    }

    public set paused(value: boolean) {
        if (this._paused === value) return;
        this._paused = value;
        // Flipping pause may require attach (resume) or just renderer.paused refresh.
        this.syncAttachment();
    }

    public dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        if (this.unsubscribeRegistry) {
            this.unsubscribeRegistry();
            this.unsubscribeRegistry = null;
        }
        for (const u of this.followUnsubscribers) u();
        this.followUnsubscribers = [];
        this.followedRecorder = null;
        this.detach();
    }

    // Switch to following a different recorder (or none). Unsubscribes from the
    // previous recorder's lifecycle events and subscribes to the new one's.
    private followRecorder(recorder: VideoRecorder | null): void {
        if (this.followedRecorder === recorder) {
            this.syncAttachment();
            return;
        }
        for (const u of this.followUnsubscribers) u();
        this.followUnsubscribers = [];
        this.followedRecorder = recorder;
        if (recorder) {
            const resync = () => this.syncAttachment();
            this.followUnsubscribers = [
                recorder.addStateChangeListener(resync),
                recorder.addBlurChangeListener(resync),
            ];
        }
        this.syncAttachment();
    }

    // Reconcile attached state and derived flags with the currently-followed
    // recorder. Called initially, on any lifecycle event, and on paused flip.
    private syncAttachment(): void {
        if (this.disposed) return;
        const recorder = this.followedRecorder;
        const paused = this._paused;

        // While paused, skip attach/detach so the canvas keeps the last frame
        // intact — detaching would `canvasTarget.clear()` and flash the spinner
        // even though another consumer is driving rendering.
        if (!paused) {
            const track = recorder?.getPreviewTrack() ?? null;
            const trackLive = track?.readyState === 'live';
            if (recorder && trackLive && track !== this.attachedTrack) {
                this.attach(recorder, track);
            } else if (this.attachedRecorder && (!recorder || !trackLive)) {
                this.detach();
            }
        }

        // Raw-track rendering must pause while blur is driving the canvas via
        // preview-frame dispatches; otherwise the raw frames would flicker over
        // the blurred output. Also pause while preview is externally paused.
        if (this.attachedRecorder && this.renderer)
            this.renderer.paused = paused || this.attachedRecorder.isBlurActive();

        // Compute starting state: recorder intends to produce video but first frame hasn't landed yet
        const isStarting = !paused
            && recorder?.recordingState === 'starting'
            && !this.firstFrameFired;
        if (isStarting !== this.lastStarting) {
            this.lastStarting = isStarting;
            this.options.onStartingChange?.(isStarting);
        }
    }

    private attach(recorder: VideoRecorder, track: MediaStreamTrack): void {
        this.detach();
        this.attachedRecorder = recorder;
        this.attachedTrack = track;
        this.firstFrameFired = false;

        infoLog?.log('Attached to active recorder');

        this.renderer = new CanvasVideoRenderer({
            canvas: this.canvasTarget.element,
            rafKey: `${this.options.rafKey}#${++this.attachSeq}`,
            onFirstFrame: () => this.fireFirstFrame(),
            onAfterDraw: (video) => this.drawBgFrame(video.videoWidth, video.videoHeight),
        });
        this.renderer.start(track);

        const blurListener: PreviewFrameListener = (frame: VideoFrame) => {
            // Skip drawing while the view is paused (either by the consumer via
            // `paused` or by the recorder's global pause flag) — otherwise
            // blurred frames from the recorder pipeline would overwrite the
            // last preserved frame on the canvas.
            if (this._paused)
                return;
            this.canvasTarget.draw(frame, frame.displayWidth, frame.displayHeight);
            this.drawBgFrame(frame.displayWidth, frame.displayHeight);
            this.fireFirstFrame();
        };
        this.unsubscribeFrames = recorder.addPreviewFrameListener(blurListener);

        this.options.onAttach?.(recorder);
    }

    private detach(): void {
        if (!this.attachedRecorder) return;

        infoLog?.log('Detached from recorder');

        if (this.unsubscribeFrames) {
            this.unsubscribeFrames();
            this.unsubscribeFrames = null;
        }
        this.attachedRecorder = null;
        this.attachedTrack = null;

        if (this.renderer) {
            this.renderer.dispose();
            this.renderer = null;
        }

        // Wipe stale frames so a failed re-attach doesn't leave the last good
        // frame on-screen while the new pipeline spins up (or after a teardown).
        this.canvasTarget.clear();
        this.bgCanvasTarget?.clear();

        this.firstFrameFired = false;
        this.options.onDetach?.();
    }

    private fireFirstFrame(): void {
        if (this.firstFrameFired) return;
        this.firstFrameFired = true;
        // Clear starting state immediately so the spinner doesn't flicker for
        // one frame before the next tick() re-evaluates.
        if (this.lastStarting) {
            this.lastStarting = false;
            this.options.onStartingChange?.(false);
        }
        this.options.onFirstFrame?.();
    }

    private drawBgFrame(width: number, height: number): void {
        if (!this.bgCanvasTarget) return;
        // Skip when the bg canvas is hidden — CSS reveals it only on .item-focused
        if (!this.bgContainer?.classList.contains('item-focused')) return;
        // Throttle: the bg is blurred via CSS, updating every frame is wasted GPU work
        const now = performance.now();
        if (now - this.lastBgDrawTime < BG_DRAW_INTERVAL_MS) return;
        this.lastBgDrawTime = now;

        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * height / Math.max(1, width)));
        // Source from the already-drawn main canvas — avoids a second GPU→RGB
        // conversion of the VideoFrame / HTMLVideoElement per frame.
        this.bgCanvasTarget.draw(this.canvasTarget.element, bgW, bgH);
    }
}
