// Follows the currently active VideoRecorder and renders its preview to either
// a caller-supplied <video srcObject> (generated track) or the paired canvas
// fallback. Multiple UI surfaces can attach independently.
// Caller-specific UI state (data-starting, data-has-video) is signalled via the
// onAttach/onDetach/onFirstFrame hooks — this class doesn't touch CSS.

import { getLogs } from 'logging';
import { frameHeight, frameWidth, type FrameSource } from 'web-codecs-compat/init';
import { DeviceOrientation, ScreenOrientation, normalizeRotationQuarter } from 'orientation';
import type { Subscription } from 'rxjs';
import { merge } from 'rxjs';
import {
    addActiveRecorderListener,
    getActiveRecorder,
    type PreviewFrameListener,
    type VideoRecorder,
} from '../../../Components/VideoPanel/video-recorder';
import { CanvasTarget } from './canvas-target';
import {
    BgCanvasRenderer,
    BG_CANVAS_WIDTH,
    BG_DRAW_INTERVAL_MS,
} from './bg-canvas';
import { applyRotationLayout, chooseFit, updateCollapsedIslandAspect } from './tile-fit';

const { infoLog, warnLog } = getLogs('VideoRecorder');
const BG_DRAW_GATE_TOLERANCE_MS = 20;

export interface RecorderPreviewViewOptions {
    // Blur overlay only — raw track always goes to videoEl.
    canvas: HTMLCanvasElement;
    videoEl: HTMLVideoElement;
    bgCanvas?: HTMLCanvasElement;
    // Recorder kinds to follow in priority order; defaults to [0] (camera).
    sourceKinds?: number[];
    onAttach?: (recorder: VideoRecorder) => void;
    onDetach?: () => void;
    onFirstFrame?: () => void;
    onStartingChange?: (starting: boolean) => void;
    onBlurChange?: (blurActive: boolean) => void;
}

// Forces WebKit to recomposite a <video> that decodes but never paints.
//
// WebKit bug 230922 (open since iOS 15, still reproducing on 26): a <video> fed
// by a MediaStream can hold a live, advancing stream and show nothing. Measured
// here on a re-attached self-preview - display:block, visibility:visible,
// paused:false, readyState:4, right size, right track, and drawImage(videoEl)
// returning FRESH pixels every frame - while the screen stayed empty. The
// element is fine; its GraphicsLayer never repaints.
//
// The trigger is compositing: `.video-track-player` is `contain: strict` and the
// surfaces inside carry `will-change: transform` (plus scaleX(-1) when
// mirrored). Both are load-bearing - see the contain/will-change notes in
// video-panel.css - so the layer gets nudged instead of unpromoted. Toggling
// display forces a fresh compositing pass and the frame appears instantly;
// every workaround in the upstream thread is a reset of this kind (background
// the app, split-view, reload, picture-in-picture).
//
// https://bugs.webkit.org/show_bug.cgi?id=230922, docs/ui/components.md.
function forceRecomposite(videoEl: HTMLVideoElement): void {
    videoEl.style.setProperty('display', 'none', 'important');
    void videoEl.offsetHeight;
    videoEl.style.removeProperty('display');
}

// A pending play() rejects with AbortError whenever a re-attach swaps srcObject
// underneath it - routine during recorder startup, and noise that used to reach
// the console as an unhandled rejection. Anything else is worth seeing.
function playPreview(videoEl: HTMLVideoElement): void {
    videoEl.play().catch((e: unknown) => {
        if (e instanceof DOMException && e.name === 'AbortError')
            return;

        warnLog?.log('playPreview: play() failed:', e);
    });
}

export class RecorderPreviewView {
    private readonly options: RecorderPreviewViewOptions;
    private readonly canvasTarget: CanvasTarget;
    private readonly bgCanvasTarget: BgCanvasRenderer | null;
    private readonly bgContainer: HTMLElement | null;
    private readonly sourceKinds: number[];
    private lastBgDrawTime = 0;

    private attachedRecorder: VideoRecorder | null = null;
    private attachedTrack: MediaStreamTrack | null = null;
    private unsubscribeFrames: (() => void) | null = null;
    private firstFrameFired = false;
    private lastStarting = false;
    private lastBlurActive = false;
    private disposed = false;
    private _paused = false;
    private videoLoadedDataListener: (() => void) | null = null;
    private bgPumpHandle: number | null = null;
    private unsubscribeRegistry: (() => void) | null = null;
    // May differ from attachedRecorder until the followed recorder's track goes live.
    private followedRecorder: VideoRecorder | null = null;
    private followUnsubscribers: (() => void)[] = [];
    // Rotation + fit wiring for the self-preview surfaces. Same model as the
    // receiver's render backends: CSS owns the rotated layout; JS only
    // publishes the quarter-turn and picks cover/contain from cropLoss.
    private orientationSubscription: Subscription | null = null;
    private parentResizeObserver: ResizeObserver | null = null;
    private videoResizeListener: (() => void) | null = null;
    private currentFit: 'cover' | 'contain' = 'cover';
    private lastFrameWidth = 0;
    private lastFrameHeight = 0;
    // Captured on first frame after attach. Self-preview's CSS rotation is
    // (current device-vs-screen delta) − (initial device-vs-screen delta).
    // Zero while initial == current → browser's natural page rotation
    // handles it. Non-zero only when one of the two channels diverges
    // from the start (e.g. screen-lock-on + device physically rotated).
    private initialDeviceScreenDelta: number | null = null;

    static create(options: RecorderPreviewViewOptions): RecorderPreviewView {
        return new RecorderPreviewView(options);
    }

    constructor(options: RecorderPreviewViewOptions) {
        this.options = options;
        this.canvasTarget = new CanvasTarget(options.canvas);
        this.bgCanvasTarget = options.bgCanvas
            ? new BgCanvasRenderer(options.bgCanvas)
            : null;
        this.bgContainer = options.bgCanvas?.parentElement ?? null;
        this.sourceKinds = options.sourceKinds ?? [0];

        // Reconcile immediately — recorder may already be registered before this view exists.
        this.unsubscribeRegistry = addActiveRecorderListener((_recorder, kind) => {
            if (!this.sourceKinds.includes(kind))
                return;

            this.followRecorder(this.pickRecorder());
        });
        this.followRecorder(this.pickRecorder());
        this.startOrientationAndFitForwarding();
    }

    private startOrientationAndFitForwarding(): void {
        // Apply rotation/fit on every device- OR screen-orientation flip.
        // The delta is what matters for self-preview: when screen rotates
        // WITH the device (no lock), browser already reorients the page
        // and the camera; no CSS transform needed. When the screen is
        // locked, only the device pose changes — that's when we rotate
        // the videoEl to compensate.
        this.orientationSubscription = merge(DeviceOrientation.change$, ScreenOrientation.change$)
            .subscribe(() => this.applyRotationAndFit());
        const parent = this.options.videoEl.parentElement;
        if (parent) {
            this.parentResizeObserver = new ResizeObserver(() => this.applyRotationAndFit());
            this.parentResizeObserver.observe(parent);
        }
        this.videoResizeListener = () => this.applyRotationAndFit();
        this.options.videoEl.addEventListener('resize', this.videoResizeListener);
        this.applyRotationAndFit();
    }

    private reportPreviewSize(): void {
        const parent = this.options.videoEl.parentElement;
        const rect = (parent ?? this.options.videoEl).getBoundingClientRect();
        const dpr = globalThis.devicePixelRatio || 1;
        this.attachedRecorder?.setPreviewSize(rect.width * dpr, rect.height * dpr);
    }

    private applyRotationAndFit(): void {
        if (this.disposed)
            return;

        this.reportPreviewSize();

        const videoEl = this.options.videoEl;
        const parent = videoEl.parentElement;
        const currentDelta = normalizeRotationQuarter(DeviceOrientation.quarter - ScreenOrientation.quarter);
        this.initialDeviceScreenDelta ??= currentDelta;
        const previewPresentation = this.attachedRecorder?.getPreviewFramePresentation() ?? null;
        const rotation = previewPresentation
            ? normalizeRotationQuarter(previewPresentation.rotation)
            : normalizeRotationQuarter(currentDelta - this.initialDeviceScreenDelta);
        applyRotationLayout(videoEl, rotation);
        applyRotationLayout(this.options.canvas, rotation);
        if (this.options.bgCanvas) {
            // bg pixels are pre-mirrored by the painter, but the main canvas's
            // CSS does `scaleX(-1) rotate(θ)` (rotate then mirror). Reflection
            // conjugates rotation to its inverse, so to make
            // `bg-CSS ∘ scaleX(-1) ≡ scaleX(-1) ∘ rotate(θ)` we apply
            // rotate(-θ) on the bg whenever the painter mirrors it. No-op for
            // θ ∈ {0, 2} where rotate commutes with scaleX(-1).
            const bgRotation = this.shouldMirrorPreview()
                ? normalizeRotationQuarter(-rotation)
                : rotation;
            applyRotationLayout(this.options.bgCanvas, bgRotation);
        }
        if (!parent)
            return;

        const swap = (rotation & 1) === 1;
        const sourceW = videoEl.videoWidth || this.lastFrameWidth || 0;
        const sourceH = videoEl.videoHeight || this.lastFrameHeight || 0;
        const frameW = swap ? sourceH : sourceW;
        const frameH = swap ? sourceW : sourceH;
        // Keep the collapsed island sized to the source aspect — without this
        // the panel stays at the default 16:9 and a portrait camera ends up
        // letterboxed in a tiny landscape tile.
        updateCollapsedIslandAspect(
            videoEl.closest<HTMLElement>('.video-panel'),
            frameW,
            frameH);
        // Non-focused tiles (sidebar squares, PiP overlay during screencast)
        // always use cover — the crop is invisible at that size and the
        // letterbox bars would dominate the small square. Same rule the
        // receiver follows in VideoPlayer.applyFitDecision.
        const focused = parent.classList.contains('item-focused');
        const fit = focused
            ? chooseFit(frameW, frameH, parent.clientWidth, parent.clientHeight)
            : 'cover';
        if (fit !== this.currentFit) {
            this.currentFit = fit;
            videoEl.style.objectFit = fit;
            this.options.canvas.style.objectFit = fit;
        }
    }

    private pickRecorder(): VideoRecorder | null {
        for (const k of this.sourceKinds) {
            const r = getActiveRecorder(k);
            if (r)
                return r;
        }
        return null;
    }

    public isAttached(): boolean {
        return this.attachedRecorder !== null;
    }

    // While paused, drawing and attach/detach are frozen so the last frame stays
    // on the canvas (independent of the recorder's own pause flag).
    public get paused(): boolean {
        return this._paused;
    }

    public set paused(value: boolean) {
        if (this._paused === value)
            return;

        this._paused = value;
        this.syncAttachment();
    }

    public dispose(): void {
        if (this.disposed)
            return;

        this.disposed = true;
        if (this.unsubscribeRegistry) {
            this.unsubscribeRegistry();
            this.unsubscribeRegistry = null;
        }
        for (const u of this.followUnsubscribers) u();
        this.followUnsubscribers = [];
        this.followedRecorder = null;
        if (this.orientationSubscription) {
            try { this.orientationSubscription.unsubscribe(); } catch { /* ignore */ }
            this.orientationSubscription = null;
        }
        if (this.parentResizeObserver) {
            try { this.parentResizeObserver.disconnect(); } catch { /* ignore */ }
            this.parentResizeObserver = null;
        }
        if (this.videoResizeListener) {
            try { this.options.videoEl.removeEventListener('resize', this.videoResizeListener); } catch { /* ignore */ }
            this.videoResizeListener = null;
        }
        this.detach();
        this.bgCanvasTarget?.dispose();
    }

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
            const refreshPresentation = () => this.applyRotationAndFit();
            this.followUnsubscribers = [
                recorder.addStateChangeListener(resync),
                recorder.addBlurChangeListener(resync),
                recorder.addPreviewPresentationListener(refreshPresentation),
            ];
        }
        this.syncAttachment();
    }

    private syncAttachment(): void {
        if (this.disposed)
            return;

        const recorder = this.followedRecorder;
        const paused = this._paused;

        // While paused, skip attach/detach: detach() would clear the canvas and
        // flash the spinner even though another consumer is driving rendering.
        if (!paused) {
            const track = recorder?.getPreviewTrack() ?? null;
            const canvasFallback = recorder?.getPreviewUsesCanvas() ?? false;
            const trackLive = track?.readyState === 'live';
            if (recorder && canvasFallback && (recorder !== this.attachedRecorder || this.attachedTrack !== null)) {
                this.attach(recorder, null);
            } else if (recorder && trackLive && track !== this.attachedTrack) {
                this.attach(recorder, track);
            } else if (this.attachedRecorder && (!recorder || (!canvasFallback && !trackLive))) {
                this.detach();
            }
        }

        if (this.attachedRecorder) {
            const videoEl = this.options.videoEl;
            if (paused) {
                videoEl.pause();
                // Release the generated-track sink while paused: a single
                // generator-backed track doesn't fan out to two <video>
                // elements, so a paused-but-still-attached sink starves the
                // surface taking over (Settings modal during live). Canvas
                // fallback (no track) keeps its last frame and needs no release.
                if (this.attachedTrack && videoEl.srcObject)
                    videoEl.srcObject = null;
            } else if (!this.attachedTrack) {
                videoEl.pause();
            } else {
                videoEl.srcObject ??= new MediaStream([this.attachedTrack]);
                playPreview(videoEl);
            }
        }

        const isStarting = !paused
            && recorder?.recordingState === 'starting'
            && !this.firstFrameFired;
        if (isStarting !== this.lastStarting) {
            this.lastStarting = isStarting;
            this.options.onStartingChange?.(isStarting);
        }

        const blurActive = recorder?.isBlurActive() ?? false;
        if (blurActive !== this.lastBlurActive) {
            this.lastBlurActive = blurActive;
            this.options.onBlurChange?.(blurActive);
        }
    }

    private attach(recorder: VideoRecorder, track: MediaStreamTrack | null): void {
        this.detach();
        this.attachedRecorder = recorder;
        this.attachedTrack = track;
        this.firstFrameFired = false;

        infoLog?.log('Attached to active recorder');

        const videoEl = this.options.videoEl;
        if (track) {
            videoEl.srcObject = new MediaStream([track]);
            playPreview(videoEl);
            this.videoLoadedDataListener = () => {
                forceRecomposite(videoEl);
                this.fireFirstFrame();
            };
            videoEl.addEventListener('loadeddata', this.videoLoadedDataListener);
        } else {
            videoEl.pause();
            videoEl.srcObject = null;
        }
        const parent = videoEl.parentElement;
        // An attribute, not a class: Blazor owns `class` and drops whatever JS put
        // there on its next re-render. See docs/ui/components.md.
        parent?.setAttribute('data-preview-backend', track === null ? 'canvas' : 'mstg');
        if (track && this.bgCanvasTarget) {
            this.bgPumpHandle = window.setInterval(() => {
                if (this._paused)
                    return;

                // When blur is on, the blur listener already paints the bg from the
                // blurred canvas — sourcing from raw video here would overwrite it.
                if (this.attachedRecorder?.isBlurActive())
                    return;
                if (videoEl.videoWidth === 0 || videoEl.videoHeight === 0)
                    return;

                this.drawBgFrameFromVideo(videoEl);
            }, BG_DRAW_INTERVAL_MS);
        }

        const blurListener: PreviewFrameListener = (frame: FrameSource) => {
            if (this._paused)
                return;

            const width = frameWidth(frame);
            const height = frameHeight(frame);
            this.canvasTarget.draw(frame, width, height);
            this.lastFrameWidth = width;
            this.lastFrameHeight = height;
            this.applyRotationAndFit();
            this.drawBgFrame(width, height);
            this.fireFirstFrame();
        };
        this.unsubscribeFrames = recorder.addPreviewFrameListener(blurListener);

        this.options.onAttach?.(recorder);
    }

    private detach(): void {
        if (!this.attachedRecorder)
            return;

        infoLog?.log('Detached from recorder');

        if (this.unsubscribeFrames) {
            this.unsubscribeFrames();
            this.unsubscribeFrames = null;
        }
        this.attachedRecorder = null;
        this.attachedTrack = null;

        const videoEl = this.options.videoEl;
        if (this.videoLoadedDataListener) {
            videoEl.removeEventListener('loadeddata', this.videoLoadedDataListener);
            this.videoLoadedDataListener = null;
        }
        videoEl.srcObject = null;
        if (this.bgPumpHandle !== null) {
            clearInterval(this.bgPumpHandle);
            this.bgPumpHandle = null;
        }
        const parent = videoEl.parentElement;
        parent?.removeAttribute('data-preview-backend');

        // Wipe stale frames so a failed re-attach doesn't leave the last good
        // frame on-screen while the new pipeline spins up.
        this.canvasTarget.clear();
        this.bgCanvasTarget?.clear();

        // Re-capture the initial device/screen delta on next attach.
        this.initialDeviceScreenDelta = null;
        this.lastFrameWidth = 0;
        this.lastFrameHeight = 0;

        this.firstFrameFired = false;
        this.options.onDetach?.();
    }

    private fireFirstFrame(): void {
        if (this.firstFrameFired)
            return;

        this.firstFrameFired = true;
        if (this.lastStarting) {
            this.lastStarting = false;
            this.options.onStartingChange?.(false);
        }
        this.options.onFirstFrame?.();
    }

    private shouldMirrorPreview(): boolean {
        return !this.options.canvas.classList.contains('no-mirror')
            && !this.options.videoEl.classList.contains('no-mirror');
    }

    private drawBgFrame(width: number, height: number): void {
        if (!this.bgCanvasTarget)
            return;
        if (!this.bgContainer?.classList.contains('item-focused'))
            return;

        // Match the receiver: only paint the backdrop when contain is active.
        if (this.currentFit !== 'contain')
            return;

        const now = performance.now();
        if (now - this.lastBgDrawTime < BG_DRAW_INTERVAL_MS - BG_DRAW_GATE_TOLERANCE_MS)
            return;

        this.lastBgDrawTime = now;

        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * height / Math.max(1, width)));
        // Source from the already-drawn main canvas to avoid a second
        // GPU->RGB conversion of the VideoFrame per frame.
        this.bgCanvasTarget.draw(this.canvasTarget.element, bgW, bgH, this.shouldMirrorPreview());
    }

    // Sourced from <video> because the main canvas is empty in the generated-track path.
    private drawBgFrameFromVideo(videoEl: HTMLVideoElement): void {
        if (!this.bgCanvasTarget)
            return;
        if (!this.bgContainer?.classList.contains('item-focused'))
            return;
        if (this.currentFit !== 'contain')
            return;

        const now = performance.now();
        if (now - this.lastBgDrawTime < BG_DRAW_INTERVAL_MS - BG_DRAW_GATE_TOLERANCE_MS)
            return;

        this.lastBgDrawTime = now;

        const w = videoEl.videoWidth;
        const h = videoEl.videoHeight;
        const bgW = BG_CANVAS_WIDTH;
        const bgH = Math.max(1, Math.round(bgW * h / Math.max(1, w)));
        this.bgCanvasTarget.draw(videoEl, bgW, bgH, this.shouldMirrorPreview());
    }
}
