import { fromEvent, Subject, takeUntil, filter } from 'rxjs';

const MIN_SCALE = 1;
const MAX_SCALE_MOBILE = 4;
const MAX_SCALE_DESKTOP = 2;
const WHEEL_ZOOM_STEP = 0.002;
const TAP_MOVE_THRESHOLD = 225; // 15px squared
const TAP_MAX_DURATION = 500;
const DOUBLE_TAP_INTERVAL = 300;
const ZOOM_TRANSITION_MS = 250;

export class VideoPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
    private parentElement: HTMLElement | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    // Screencast zoom/pan state
    private zoomScale = 1;
    private panX = 0;
    private panY = 0;
    private dragging = false;
    private lastTouchX = 0;
    private lastTouchY = 0;
    private lastMouseX = 0;
    private lastMouseY = 0;
    private mouseDragging = false;
    private pinching = false;
    private pinchInitialDist = 0;
    private pinchInitialScale = 0;
    private pinchContentX = 0;
    private pinchContentY = 0;
    private lastPinchEndTime = 0;
    // Unified tap / double-tap state (tracked inside touch handlers, not separate listeners)
    private tapStartX = 0;
    private tapStartY = 0;
    private tapStartTime = 0;
    private tapMoved = false;
    private singleTapTimer = 0;
    private lastTouchActionTime = 0; // suppress synthetic click
    // Touch identifiers to track only our gesture's touches
    private activeTouchIds = new Set<number>();

    static create(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject): VideoPanel {
        return new VideoPanel(videoPanel, blazorRef);
    }

    constructor(videoPanel: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.videoPanel = videoPanel;

        this.parentElement = this.videoPanel.parentElement;
        const needToShowElements = this.videoPanel.querySelectorAll('.show-with-delay');
        setTimeout(() => {
            needToShowElements.forEach(element => element.classList.add('show'));
            this.videoPanel.classList.remove('first-time-open');
        }, 1000);

        this.initGestures();

        // Escape key handler
        fromEvent<KeyboardEvent>(document, 'keydown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape')
            )
            .subscribe(() => this.onEscPress());
    }

    // region: Helpers

    private isExpanded(): boolean {
        return this.videoPanel.classList.contains('expanded');
    }

    private get maxScale(): number {
        return document.body.classList.contains('narrow') ? MAX_SCALE_MOBILE : MAX_SCALE_DESKTOP;
    }

    private getScreencastContainer(): HTMLElement | null {
        return this.videoPanel.querySelector<HTMLElement>('.remote-video-container.focused.screencast');
    }

    private getScreencastCanvas(): HTMLCanvasElement | null {
        return this.getScreencastContainer()?.querySelector<HTMLCanvasElement>('canvas.remote-video') ?? null;
    }

    private isOnVideo(target: HTMLElement): boolean {
        return target.closest('.remote-video-container') != null
            && !target.closest('.video-panel-toolbar')
            && !target.closest('.video-panel-chat');
    }

    private isOnScreencast(target: HTMLElement): boolean {
        return target.closest('.remote-video-container.screencast') != null;
    }

    private getContentRect(container: HTMLElement): { offsetX: number; offsetY: number; width: number; height: number } {
        const canvas = container.querySelector<HTMLCanvasElement>('canvas.remote-video');
        const rect = container.getBoundingClientRect();
        if (!canvas?.width || !canvas.height)
            return { offsetX: 0, offsetY: 0, width: rect.width, height: rect.height };

        const containerAR = rect.width / rect.height;
        const videoAR = canvas.width / canvas.height;

        if (videoAR > containerAR) {
            const contentHeight = rect.width / videoAR;
            return { offsetX: 0, offsetY: (rect.height - contentHeight) / 2, width: rect.width, height: contentHeight };
        }
        const contentWidth = rect.height * videoAR;
        return { offsetX: (rect.width - contentWidth) / 2, offsetY: 0, width: contentWidth, height: rect.height };
    }

    // endregion

    // region: Gesture init

    private initGestures(): void {
        // ── Desktop: mouse click for toolbar toggle ──
        // Suppressed when a touch tap just happened (prevents synthetic click double-toggle)
        fromEvent<MouseEvent>(this.videoPanel, 'click')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => {
                    if (!this.isExpanded()) return false;
                    if (Date.now() - this.lastTouchActionTime < 1000) return false;
                    return this.isOnVideo(e.target as HTMLElement);
                })
            )
            .subscribe(() => this.videoPanel.classList.toggle('toolbar-hidden'));

        // ── Desktop: wheel zoom ──
        fromEvent<WheelEvent>(this.videoPanel, 'wheel', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(e => this.isExpanded() && this.isOnScreencast(e.target as HTMLElement))
            )
            .subscribe(e => this.onWheel(e));

        // ── Desktop: mouse drag ──
        fromEvent<PointerEvent>(this.videoPanel, 'pointerdown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.pointerType === 'mouse' && this.isExpanded()
                    && this.isOnScreencast(e.target as HTMLElement) && e.button === 0 && this.zoomScale > 1)
            )
            .subscribe(e => {
                this.mouseDragging = true;
                this.lastMouseX = e.clientX;
                this.lastMouseY = e.clientY;
            });

        fromEvent<PointerEvent>(document, 'pointermove')
            .pipe(takeUntil(this.disposed$), filter(e => e.pointerType === 'mouse' && this.mouseDragging))
            .subscribe(e => this.onMouseDrag(e));

        const stopMouseDrag = () => this.mouseDragging = false;
        fromEvent<PointerEvent>(document, 'pointerup')
            .pipe(takeUntil(this.disposed$), filter(e => e.pointerType === 'mouse'))
            .subscribe(stopMouseDrag);
        fromEvent<PointerEvent>(document, 'pointercancel')
            .pipe(takeUntil(this.disposed$), filter(e => e.pointerType === 'mouse'))
            .subscribe(stopMouseDrag);

        // ── Touch: unified handler for tap, double-tap, drag, pinch ──
        fromEvent<TouchEvent>(this.videoPanel, 'touchstart', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => this.isExpanded())
            )
            .subscribe(e => this.onTouchStart(e));

        fromEvent<TouchEvent>(document, 'touchmove', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => this.dragging || this.pinching)
            )
            .subscribe(e => this.onTouchMove(e));

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.onTouchEnd(e));
    }

    // endregion

    // region: Touch handler — unified tap + drag + pinch

    private onTouchStart(e: TouchEvent): void {
        const target = e.target as HTMLElement;
        const onVideo = this.isOnVideo(target);
        const onScreencast = this.isOnScreencast(target);

        // Track screencast touches for move/end filtering
        if (onScreencast)
            for (const t of Array.from(e.changedTouches))
                this.activeTouchIds.add(t.identifier);

        // ── Pinch (2 fingers on screencast) ──
        if (onScreencast && e.touches.length === 2) {
            e.preventDefault();
            this.dragging = false;
            this.pinching = true;
            const [t0, t1] = [e.touches[0], e.touches[1]];
            this.pinchInitialDist = this.touchDistance(t0, t1);
            this.pinchInitialScale = this.zoomScale;
            const container = this.getScreencastContainer();
            if (container) {
                const rect = container.getBoundingClientRect();
                const midX = ((t0.clientX + t1.clientX) / 2 - rect.left) / rect.width;
                const midY = ((t0.clientY + t1.clientY) / 2 - rect.top) / rect.height;
                this.pinchContentX = (midX - this.panX) / this.zoomScale;
                this.pinchContentY = (midY - this.panY) / this.zoomScale;
            }
            return;
        }

        // ── Single-finger on screencast (expanded) ──
        if (onScreencast && e.touches.length === 1) {
            // Always preventDefault to block browser swipe-to-navigate in fullscreen
            e.preventDefault();
            if (this.zoomScale > 1) {
                this.dragging = true;
                this.lastTouchX = e.touches[0].clientX;
                this.lastTouchY = e.touches[0].clientY;
            }
        }

        // ── Tap tracking (any 1-finger touch on video) ──
        if (onVideo && e.touches.length === 1) {
            this.tapMoved = false;
            this.tapStartX = e.touches[0].clientX;
            this.tapStartY = e.touches[0].clientY;
            this.tapStartTime = Date.now();
        }
    }

    private onTouchMove(e: TouchEvent): void {
        if (!this.hasTrackedTouch(e)) return;
        e.preventDefault();

        // Track tap movement (even during drag, so we know it's not a tap)
        if (e.touches.length === 1) {
            const dx = e.touches[0].clientX - this.tapStartX;
            const dy = e.touches[0].clientY - this.tapStartY;
            if (dx * dx + dy * dy > TAP_MOVE_THRESHOLD)
                this.tapMoved = true;
        } else {
            this.tapMoved = true; // multi-touch = not a tap
        }

        if (this.pinching && e.touches.length >= 2) {
            const [t0, t1] = [e.touches[0], e.touches[1]];
            const dist = this.touchDistance(t0, t1);
            const ratio = dist / this.pinchInitialDist;
            this.zoomScale = Math.max(MIN_SCALE, Math.min(this.maxScale, this.pinchInitialScale * ratio));

            const container = this.getScreencastContainer();
            if (container) {
                const rect = container.getBoundingClientRect();
                const midX = ((t0.clientX + t1.clientX) / 2 - rect.left) / rect.width;
                const midY = ((t0.clientY + t1.clientY) / 2 - rect.top) / rect.height;
                this.panX = midX - this.zoomScale * this.pinchContentX;
                this.panY = midY - this.zoomScale * this.pinchContentY;
            }
            this.clampPan();
            this.applyTransform();
        } else if (this.dragging && e.touches.length === 1) {
            const container = this.getScreencastContainer();
            if (!container) return;
            const rect = container.getBoundingClientRect();
            const touch = e.touches[0];
            const dx = (touch.clientX - this.lastTouchX) / rect.width;
            const dy = (touch.clientY - this.lastTouchY) / rect.height;
            this.lastTouchX = touch.clientX;
            this.lastTouchY = touch.clientY;
            this.panX += dx;
            this.panY += dy;
            this.clampPan();
            this.applyTransform();
        }
    }

    private onTouchEnd(e: TouchEvent): void {
        // Clean up tracked touch IDs
        for (const t of Array.from(e.changedTouches))
            this.activeTouchIds.delete(t.identifier);

        if (this.pinching && e.touches.length < 2) {
            this.pinching = false;
            this.lastPinchEndTime = Date.now();
        }
        if (this.dragging && e.touches.length === 0)
            this.dragging = false;

        // ── Tap detection (all fingers lifted) ──
        if (e.touches.length === 0 && e.changedTouches.length === 1) {
            const elapsed = Date.now() - this.tapStartTime;
            if (!this.tapMoved && elapsed < TAP_MAX_DURATION && Date.now() - this.lastPinchEndTime > 500)
                this.handleTap(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
        }
    }

    // endregion

    // region: Tap / double-tap

    private handleTap(screenX: number, screenY: number): void {
        this.lastTouchActionTime = Date.now();

        if (this.singleTapTimer) {
            // Second tap → double-tap
            clearTimeout(this.singleTapTimer);
            this.singleTapTimer = 0;
            this.onDoubleTap(screenX, screenY);
        } else {
            // First tap → wait for possible second tap
            this.singleTapTimer = window.setTimeout(() => {
                this.singleTapTimer = 0;
                this.onSingleTap();
            }, DOUBLE_TAP_INTERVAL);
        }
    }

    private onSingleTap(): void {
        this.videoPanel.classList.toggle('toolbar-hidden');
    }

    private onDoubleTap(screenX: number, screenY: number): void {
        const container = this.getScreencastContainer();
        if (!container) {
            // Non-screencast video — toggle toolbar
            this.videoPanel.classList.toggle('toolbar-hidden');
            return;
        }

        // Cycle zoom: <2 → 2, <3 → 3, <4 → 4, >=maxScale → 1
        const oldScale = this.zoomScale;
        const max = this.maxScale;
        let newScale: number;
        if (oldScale < 2) newScale = 2;
        else if (oldScale < 3 && max >= 3) newScale = 3;
        else if (oldScale < 4 && max >= 4) newScale = 4;
        else newScale = 1;

        const rect = container.getBoundingClientRect();
        const screenNormX = (screenX - rect.left) / rect.width;
        const screenNormY = (screenY - rect.top) / rect.height;
        const contentX = (screenNormX - this.panX) / oldScale;
        const contentY = (screenNormY - this.panY) / oldScale;

        this.zoomScale = newScale;
        if (newScale <= 1) {
            this.panX = 0;
            this.panY = 0;
        } else {
            this.panX = screenNormX - this.zoomScale * contentX;
            this.panY = screenNormY - this.zoomScale * contentY;
        }
        this.clampPan();
        // Animate only zoom-in; zoom-out to 1 snaps instantly (avoids clamp violations during transition)
        this.applyTransform(newScale > oldScale);
    }

    // endregion

    // region: Mouse handlers

    private onWheel(e: WheelEvent): void {
        e.preventDefault();
        const container = this.getScreencastContainer();
        if (!container) return;

        const rect = container.getBoundingClientRect();
        const screenNormX = (e.clientX - rect.left) / rect.width;
        const screenNormY = (e.clientY - rect.top) / rect.height;

        const oldScale = this.zoomScale;
        const delta = -e.deltaY * WHEEL_ZOOM_STEP;
        this.zoomScale = Math.max(MIN_SCALE, Math.min(this.maxScale, this.zoomScale + delta * this.zoomScale));

        const contentX = (screenNormX - this.panX) / oldScale;
        const contentY = (screenNormY - this.panY) / oldScale;
        this.panX = screenNormX - this.zoomScale * contentX;
        this.panY = screenNormY - this.zoomScale * contentY;
        this.clampPan();
        this.applyTransform();
    }

    private onMouseDrag(e: PointerEvent): void {
        const container = this.getScreencastContainer();
        if (!container) return;
        const rect = container.getBoundingClientRect();
        const dx = (e.clientX - this.lastMouseX) / rect.width;
        const dy = (e.clientY - this.lastMouseY) / rect.height;
        this.lastMouseX = e.clientX;
        this.lastMouseY = e.clientY;
        this.panX += dx;
        this.panY += dy;
        this.clampPan();
        this.applyTransform();
    }

    // endregion

    // region: Transform & clamp

    private hasTrackedTouch(e: TouchEvent): boolean {
        for (const t of Array.from(e.touches))
            if (this.activeTouchIds.has(t.identifier)) return true;
        for (const t of Array.from(e.changedTouches))
            if (this.activeTouchIds.has(t.identifier)) return true;
        return false;
    }

    private touchDistance(t0: Touch, t1: Touch): number {
        const dx = t1.clientX - t0.clientX;
        const dy = t1.clientY - t0.clientY;
        return Math.sqrt(dx * dx + dy * dy);
    }

    private clampPan(): void {
        const container = this.getScreencastContainer();
        if (!container) return;
        const rect = container.getBoundingClientRect();
        if (!rect.width || !rect.height) return;

        const cr = this.getContentRect(container);
        const cL = cr.offsetX / rect.width;
        const cR = (cr.offsetX + cr.width) / rect.width;
        const cT = cr.offsetY / rect.height;
        const cB = (cr.offsetY + cr.height) / rect.height;
        const S = this.zoomScale;

        const minX = 1 - S * cR;
        const maxX = -S * cL;
        this.panX = minX <= maxX ? Math.max(minX, Math.min(maxX, this.panX)) : (minX + maxX) / 2;

        const minY = 1 - S * cB;
        const maxY = -S * cT;
        this.panY = minY <= maxY ? Math.max(minY, Math.min(maxY, this.panY)) : (minY + maxY) / 2;
    }

    private applyTransform(animate = false): void {
        const canvas = this.getScreencastCanvas();
        const container = this.getScreencastContainer();
        if (!canvas || !container) return;

        if (animate) {
            canvas.style.transition = `transform ${ZOOM_TRANSITION_MS}ms ease-out`;
            const cleanup = () => {
                canvas.style.transition = '';
                canvas.removeEventListener('transitionend', cleanup);
            };
            canvas.addEventListener('transitionend', cleanup);
            setTimeout(cleanup, ZOOM_TRANSITION_MS + 50);
        }

        if (this.zoomScale <= 1) {
            canvas.style.transform = '';
            canvas.style.transformOrigin = '';
            return;
        }

        // Use px to avoid % being relative to canvas size (not container)
        const rect = container.getBoundingClientRect();
        const tx = this.panX * rect.width;
        const ty = this.panY * rect.height;
        canvas.style.transformOrigin = '0 0';
        canvas.style.transform = `translate(${tx}px, ${ty}px) scale(${this.zoomScale})`;
    }

    private resetZoom(): void {
        this.zoomScale = 1;
        this.panX = 0;
        this.panY = 0;
        this.applyTransform();
    }

    // endregion

    // region: Panel expand/collapse

    public dispose() {
        if (this.disposed$.closed)
            return;

        if (this.singleTapTimer) {
            clearTimeout(this.singleTapTimer);
            this.singleTapTimer = 0;
        }
        this.collapse();
        this.disposed$.next();
        this.disposed$.complete();
    }

    public toggleExpand(): void {
        if (!this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.add('expanded');
            document.body.appendChild(this.videoPanel);
            void this.blazorRef.invokeMethodAsync('OnExpanded');
        } else {
            this.collapse();
        }
    }

    public collapse() {
        if (!this.videoPanel.classList.contains('expanded'))
            return;

        this.resetZoom();
        this.videoPanel.classList.remove('expanded', 'toolbar-hidden');
        this.parentElement?.appendChild(this.videoPanel);
        void this.blazorRef.invokeMethodAsync('OnCollapsed');
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded'))
            this.collapse();
    }

    public startClosing() {
        this.videoPanel.classList.remove('first-time-open');
        this.videoPanel.classList.add('closing');

        const content = this.videoPanel.querySelector('.video-panel-content')!;
        let handled = false;
        const complete = () => {
            if (handled) return;
            handled = true;
            content.removeEventListener('animationend', complete);
            void this.blazorRef.invokeMethodAsync('CloseVideoPanel');
        };

        content.addEventListener('animationend', complete);
        setTimeout(complete, 500);
    }

    // endregion
}
