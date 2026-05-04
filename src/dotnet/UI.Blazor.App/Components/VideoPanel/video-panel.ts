import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

// Inline drag constants
const INLINE_FULL_HEIGHT_REM = 12; // body.narrow min-h-48 max-h-48


function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function vibrate(): void {
    if ('vibrate' in navigator)
        navigator.vibrate(10);
}

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
    private lastMouseDragEndTime = 0;
    private pinching = false;
    private pinchInitialDist = 0;
    private pinchInitialScale = 0;
    private pinchContentX = 0;
    private pinchContentY = 0;
    private lastPinchEndTime = 0;
    // Unified tap / double-tap state (tracked inside touch handlers, not separate listeners)
    private tapTouchId = -1;
    private tapStartX = 0;
    private tapStartY = 0;
    private tapStartTime = 0;
    private tapMoved = false;
    private singleTapTimer = 0;
    private lastTouchActionTime = 0; // suppress synthetic click
    // Touch identifiers to track only our gesture's touches
    private activeTouchIds = new Set<number>();

    // Collapsed island drag state
    private islandDragging = false;
    private islandDragged = false; // true once user manually repositioned
    private islandStartX = 0;
    private islandStartY = 0;
    private islandOrigLeft = 0;
    private islandOrigTop = 0;
    private islandResizeObserver: ResizeObserver | null = null;
    private islandTeardown$: Subject<void> | null = null;
    private closeTimer = 0;
    private closeContent: Element | null = null;
    private closeComplete: (() => void) | null = null;
    private closing = false;

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
        this.setupDragHandle();
        this.initInlineDrag();

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

    private isCollapsed(): boolean {
        return this.videoPanel.classList.contains('collapsed');
    }

    private isInline(): boolean {
        return !this.isExpanded() && !this.isCollapsed();
    }

    private handleObserver: MutationObserver | null = null;
    private handleResizeObserver: ResizeObserver | null = null;
    private handleResizeUnsubscribe: (() => void) | null = null;

    private setDragHandleVisible(visible: boolean): void {
        const handle = document.querySelector<HTMLElement>('.c-drag-handle');
        if (handle)
            handle.style.display = visible ? '' : 'none';
    }

    // Move drag handle to document.body and position it via `style.top` below
    // layout-subheader (or layout-header). Reparenting to body escapes any
    // ancestor that creates a containing block (filter/transform/opacity), the
    // same constraint setupIsland() handles for the panel itself.
    //
    // The previous implementation used a MutationObserver whose callback called
    // Element.after(), which mutated the observed subtree and produced a
    // feedback cascade under heavy SignalR DOM-diff traffic — the cause of the
    // main-thread BusyHang. Here the callback only writes `style.top` on a node
    // that lives outside the observed subtree, so no MutationRecord is enqueued
    // by it and the cascade is structurally impossible.
    private setupDragHandle(): void {
        const handle = this.videoPanel.querySelector<HTMLElement>('.c-drag-handle');
        if (!handle) return;

        document.body.appendChild(handle);

        const updateTop = () => {
            const subheader = document.querySelector<HTMLElement>('.layout-subheader');
            const header = document.querySelector<HTMLElement>('.layout-header');
            const ref = (subheader && subheader.getBoundingClientRect().height > 0) ? subheader : header;
            if (!ref) return;
            handle.style.top = `${ref.getBoundingClientRect().bottom}px`;
        };

        updateTop();

        this.handleResizeObserver = new ResizeObserver(updateTop);
        const subheader = document.querySelector('.layout-subheader');
        const header = document.querySelector('.layout-header');
        if (subheader) this.handleResizeObserver.observe(subheader);
        if (header) this.handleResizeObserver.observe(header);

        // Catches subheader appearing/disappearing. Callback only writes
        // style.top — no DOM mutation in observed subtree → no cascade.
        const layoutParent = header?.parentElement;
        if (layoutParent) {
            this.handleObserver = new MutationObserver(updateTop);
            this.handleObserver.observe(layoutParent, { childList: true });
        }

        const onResize = () => updateTop();
        window.addEventListener('resize', onResize);
        this.handleResizeUnsubscribe = () => window.removeEventListener('resize', onResize);
    }

    private returnDragHandle(): void {
        this.handleObserver?.disconnect();
        this.handleObserver = null;
        this.handleResizeObserver?.disconnect();
        this.handleResizeObserver = null;
        this.handleResizeUnsubscribe?.();
        this.handleResizeUnsubscribe = null;
        document.querySelector('.c-drag-handle')?.remove();
    }

    private get maxScale(): number {
        return document.body.classList.contains('narrow') ? MAX_SCALE_MOBILE : MAX_SCALE_DESKTOP;
    }

    private getScreencastContainer(): HTMLElement | null {
        return this.videoPanel.querySelector<HTMLElement>('.remote-video-container.item-focused.screencast');
    }

    // Returns the visible render surface — canvas when canvas backend is active,
    // video element when MSTG backend is active (canvas is display:none in that case).
    private getScreencastSurface(): HTMLElement | null {
        const container = this.getScreencastContainer();
        if (!container)
            return null;

        const canvas = container.querySelector<HTMLCanvasElement>('canvas.remote-video');
        if (canvas && canvas.style.display !== 'none')
            return canvas;

        const video = container.querySelector<HTMLVideoElement>('video.remote-video');
        if (video && video.style.display !== 'none')
            return video;

        return canvas; // fallback
    }

    private isOnVideo(target: HTMLElement): boolean {
        return target.closest('.remote-video-container') != null
            && !target.closest('.video-panel-toolbar')
            && !target.closest('.video-panel-chat');
    }

    private isOnScreencast(target: HTMLElement): boolean {
        return target.closest('.remote-video-container.screencast') != null;
    }

    // Returns the intrinsic content dimensions of the screencast source.
    private getSourceDims(container: HTMLElement): { width: number; height: number } | null {
        const video = container.querySelector<HTMLVideoElement>('video.remote-video');
        if (video && video.style.display !== 'none' && video.videoWidth > 0 && video.videoHeight > 0)
            return { width: video.videoWidth, height: video.videoHeight };
        const canvas = container.querySelector<HTMLCanvasElement>('canvas.remote-video');
        if (canvas && canvas.width > 0 && canvas.height > 0)
            return { width: canvas.width, height: canvas.height };
        return null;
    }

    private getContentRect(container: HTMLElement): { offsetX: number; offsetY: number; width: number; height: number } {
        const rect = container.getBoundingClientRect();
        const dims = this.getSourceDims(container);
        if (!dims)
            return { offsetX: 0, offsetY: 0, width: rect.width, height: rect.height };

        const containerAR = rect.width / rect.height;
        const videoAR = dims.width / dims.height;

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
                    if (!this.isExpanded())
                        return false;

                    if (Date.now() - this.lastTouchActionTime < 1000)
                        return false;

                    if (Date.now() - this.lastMouseDragEndTime < 300)
                        return false;

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

        const stopMouseDrag = () => {
            if (this.mouseDragging)
                this.lastMouseDragEndTime = Date.now();
            this.mouseDragging = false;
        };
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
                filter(() => this.isExpanded())
            )
            .subscribe(e => this.onTouchMove(e));

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.onTouchEnd(e));

        fromEvent<TouchEvent>(document, 'touchcancel')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onTouchCancel());
    }

    // endregion

    // region: Touch handler — unified tap + drag + pinch

    private onTouchCancel(): void {
        this.dragging = false;
        this.pinching = false;
        this.tapTouchId = -1;
        this.activeTouchIds.clear();
        if (this.singleTapTimer) {
            clearTimeout(this.singleTapTimer);
            this.singleTapTimer = 0;
        }
    }

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
            if (this.singleTapTimer) {
                clearTimeout(this.singleTapTimer);
                this.singleTapTimer = 0;
            }
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
                if (this.singleTapTimer) {
                    clearTimeout(this.singleTapTimer);
                    this.singleTapTimer = 0;
                }
                this.dragging = true;
                this.lastTouchX = e.touches[0].clientX;
                this.lastTouchY = e.touches[0].clientY;
            }
        }

        // ── Tap tracking (any 1-finger touch on video) ──
        if (onVideo && e.touches.length === 1) {
            this.tapTouchId = e.touches[0].identifier;
            this.tapMoved = false;
            this.tapStartX = e.touches[0].clientX;
            this.tapStartY = e.touches[0].clientY;
            this.tapStartTime = Date.now();
        }
    }

    private onTouchMove(e: TouchEvent): void {
        // Track tap movement even when not dragging/pinching
        if (e.touches.length === 1) {
            const dx = e.touches[0].clientX - this.tapStartX;
            const dy = e.touches[0].clientY - this.tapStartY;
            if (dx * dx + dy * dy > TAP_MOVE_THRESHOLD)
                this.tapMoved = true;
        } else {
            this.tapMoved = true; // multi-touch = not a tap
        }

        if (!this.hasTrackedTouch(e))
            return;

        if (!this.dragging && !this.pinching)
            return;

        e.preventDefault();

        if (this.pinching && e.touches.length >= 2) {
            const [t0, t1] = [e.touches[0], e.touches[1]];
            const dist = this.touchDistance(t0, t1);
            if (this.pinchInitialDist <= 0)
                return;

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
            if (!container)
                return;

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

        // ── Tap detection (all fingers lifted, same touch that started on video) ──
        if (e.touches.length === 0 && e.changedTouches.length === 1
            && e.changedTouches[0].identifier === this.tapTouchId) {
            this.tapTouchId = -1;
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
        if (!container)
            return;

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
        if (!container)
            return;

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
            if (this.activeTouchIds.has(t.identifier))
                return true;

        for (const t of Array.from(e.changedTouches))
            if (this.activeTouchIds.has(t.identifier))
                return true;

        return false;
    }

    private touchDistance(t0: Touch, t1: Touch): number {
        const dx = t1.clientX - t0.clientX;
        const dy = t1.clientY - t0.clientY;
        return Math.sqrt(dx * dx + dy * dy);
    }

    private clampPan(): void {
        const container = this.getScreencastContainer();
        if (!container)
            return;

        const rect = container.getBoundingClientRect();
        if (!rect.width || !rect.height)
            return;

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
        const surface = this.getScreencastSurface();
        const container = this.getScreencastContainer();
        if (!surface || !container)
            return;

        if (animate) {
            surface.style.transition = `transform ${ZOOM_TRANSITION_MS}ms ease-out`;
            const cleanup = () => {
                surface.style.transition = '';
                surface.removeEventListener('transitionend', cleanup);
            };
            surface.addEventListener('transitionend', cleanup);
            setTimeout(cleanup, ZOOM_TRANSITION_MS + 50);
        }

        if (this.zoomScale <= 1) {
            surface.style.transform = '';
            surface.style.transformOrigin = '';
            return;
        }

        // Use px to avoid % being relative to element size (not container)
        const rect = container.getBoundingClientRect();
        const tx = this.panX * rect.width;
        const ty = this.panY * rect.height;
        surface.style.transformOrigin = '0 0';
        surface.style.transform = `translate(${tx}px, ${ty}px) scale(${this.zoomScale})`;
    }

    private resetZoom(): void {
        this.zoomScale = 1;
        this.panX = 0;
        this.panY = 0;
        this.applyTransform();
    }

    // endregion

    // region: Collapsed island positioning & drag

    // Called from Blazor when collapsed state changes.
    public updateIslandPosition(): void {
        this.videoPanel.classList.remove('minimized');
        if (!this.videoPanel.classList.contains('collapsed')) {
            this.teardownIsland();
            this.setDragHandleVisible(true);
            return;
        }
        this.setDragHandleVisible(false);
        this.setupIsland();
    }

    private setupIsland(): void {
        this.teardownIsland(); // clean up any previous island state
        this.islandDragged = false;
        this.islandTeardown$ = new Subject<void>();
        // Reparent to body so `position: fixed` works correctly.
        // (.list-view-layout has `filter: opacity(1)` which creates a containing
        // block that breaks fixed positioning for descendants.)
        document.body.appendChild(this.videoPanel);
        this.positionIslandDefault();
        this.initIslandDrag();

        // Watch subheader/banners for size changes to reposition.
        const subheader = document.querySelector('.layout-subheader');
        if (subheader && !this.islandResizeObserver) {
            this.islandResizeObserver = new ResizeObserver(() => {
                if (!this.islandDragged)
                    this.positionIslandDefault();
            });
            this.islandResizeObserver.observe(subheader);
        }

        // Clamp to viewport on resize/zoom.
        fromEvent(window, 'resize')
            .pipe(takeUntil(this.islandTeardown$))
            .subscribe(() => this.clampIslandToViewport());
    }

    private teardownIsland(): void {
        if (this.islandTeardown$) {
            this.islandTeardown$.next();
            this.islandTeardown$.complete();
            this.islandTeardown$ = null;
        }
        this.islandResizeObserver?.disconnect();
        this.islandResizeObserver = null;
        // Clear inline positioning and reparent back.
        this.videoPanel.style.top = '';
        this.videoPanel.style.left = '';
        this.videoPanel.style.right = '';
        this.parentElement?.appendChild(this.videoPanel);
    }

    // Place the island below the subheader (or header if no subheader), top-right.
    private positionIslandDefault(): void {
        const subheader = document.querySelector('.layout-subheader');
        let top: number;
        if (subheader && subheader.getBoundingClientRect().height > 0) {
            const rect = subheader.getBoundingClientRect();
            top = rect.bottom + 8; // 0.5rem gap
        } else {
            const header = document.querySelector('.layout-header');
            if (header) {
                const rect = header.getBoundingClientRect();
                top = rect.bottom + 8;
            } else {
                top = 64; // fallback
            }
        }
        this.videoPanel.style.top = `${top}px`;
        this.videoPanel.style.right = '0.5rem';
        this.videoPanel.style.left = '';
    }

    private initIslandDrag(): void {
        const teardown$ = this.islandTeardown$!;
        // Pointer events for unified mouse+touch drag.
        fromEvent<PointerEvent>(this.videoPanel, 'pointerdown')
            .pipe(
                takeUntil(teardown$),
                filter(() => this.videoPanel.classList.contains('collapsed')),
                filter(e => e.button === 0),
                filter(e => !(e.target as HTMLElement).closest('button')),
            )
            .subscribe(e => this.onIslandPointerDown(e));

        fromEvent<PointerEvent>(document, 'pointermove')
            .pipe(
                takeUntil(teardown$),
                filter(() => this.islandDragging),
            )
            .subscribe(e => this.onIslandPointerMove(e));

        fromEvent<PointerEvent>(document, 'pointerup')
            .pipe(
                takeUntil(teardown$),
                filter(() => this.islandDragging),
            )
            .subscribe(() => this.onIslandPointerUp());

        fromEvent<PointerEvent>(document, 'pointercancel')
            .pipe(
                takeUntil(teardown$),
                filter(() => this.islandDragging),
            )
            .subscribe(() => this.onIslandPointerUp());
    }

    private onIslandPointerDown(e: PointerEvent): void {
        e.preventDefault();
        e.stopPropagation();
        this.islandDragging = true;
        this.islandStartX = e.clientX;
        this.islandStartY = e.clientY;
        const rect = this.videoPanel.getBoundingClientRect();
        this.islandOrigLeft = rect.left;
        this.islandOrigTop = rect.top;
        // Switch to left-based positioning immediately so right doesn't fight.
        this.videoPanel.style.left = `${rect.left}px`;
        this.videoPanel.style.right = 'auto';
        this.videoPanel.setPointerCapture(e.pointerId);
        this.videoPanel.style.cursor = 'grabbing';
    }

    private onIslandPointerMove(e: PointerEvent): void {
        e.preventDefault();
        const dx = e.clientX - this.islandStartX;
        const dy = e.clientY - this.islandStartY;
        // Clamp to viewport while dragging.
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const w = this.videoPanel.offsetWidth;
        const h = this.videoPanel.offsetHeight;
        const newLeft = Math.max(0, Math.min(vw - w, this.islandOrigLeft + dx));
        const newTop = Math.max(0, Math.min(vh - h, this.islandOrigTop + dy));
        this.videoPanel.style.left = `${newLeft}px`;
        this.videoPanel.style.top = `${newTop}px`;
        if (Math.abs(dx) > 4 || Math.abs(dy) > 4)
            this.islandDragged = true;
    }

    private onIslandPointerUp(): void {
        this.islandDragging = false;
        this.videoPanel.style.cursor = '';
    }

    private clampIslandToViewport(): void {
        if (!this.videoPanel.classList.contains('collapsed'))
            return;

        const rect = this.videoPanel.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        let left = rect.left;
        let top = rect.top;
        let changed = false;
        if (left + rect.width > vw) { left = vw - rect.width; changed = true; }
        if (left < 0) { left = 0; changed = true; }
        if (top + rect.height > vh) { top = vh - rect.height; changed = true; }
        if (top < 0) { top = 0; changed = true; }
        if (changed) {
            this.videoPanel.style.left = `${left}px`;
            this.videoPanel.style.top = `${top}px`;
            this.videoPanel.style.right = 'auto';
        }
    }

    // endregion

    // region: Inline drag — swipe up to minimize, swipe down to restore

    private initInlineDrag(): void {
        const FADE_START_REM = 3; // c-container starts fading below this height

        let startX = 0;
        let startY = 0;
        let startHeight = 0;
        let dragActive = false;
        let dragStarted = false;
        let rejected = false;
        let container: HTMLElement | null = null;

        const applyVisuals = (height: number, rem: number) => {
            if (!container) return;
            const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
            const progress = height / fullHeight; // 1=full, 0=collapsed

            // Blur increases as panel shrinks
            const blur = (1 - progress) * 16;
            container.style.filter = blur > 0.5 ? `blur(${blur}px)` : '';

            // Fade out c-container in the last stretch
            const fadeStart = FADE_START_REM * rem;
            if (height < fadeStart) {
                const t = 1 - height / fadeStart; // 0→1
                container.style.opacity = `${1 - t}`;
            } else {
                container.style.opacity = '';
            }
        };

        const cleanupAll = () => {
            if (container) {
                container.style.filter = '';
                container.style.opacity = '';
            }
            container = null;
            handle?.classList.remove('dragging');
            this.videoPanel.style.minHeight = '';
            this.videoPanel.style.maxHeight = '';
            this.videoPanel.style.transition = '';
        };

        const handle = document.querySelector<HTMLElement>('.c-drag-handle');
        const startDragFrom = (e: TouchEvent) => {
            startX = e.touches[0].clientX;
            startY = e.touches[0].clientY;
            startHeight = this.videoPanel.offsetHeight;
            dragActive = true;
            dragStarted = false;
            rejected = false;
        };

        // Touch on video panel
        fromEvent<TouchEvent>(this.videoPanel, 'touchstart', { passive: true } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => this.isInline() && !ScreenSize.isWide()),
                filter(e => e.touches.length === 1),
                filter(e => !(e.target as HTMLElement).closest('button, .btn-h')),
            )
            .subscribe(startDragFrom);

        // Touch on drag handle (reparented outside video-panel)
        if (handle) {
            fromEvent<TouchEvent>(handle, 'touchstart', { passive: true } as AddEventListenerOptions)
                .pipe(
                    takeUntil(this.disposed$),
                    filter(() => this.isInline() && !ScreenSize.isWide()),
                    filter(e => e.touches.length === 1),
                )
                .subscribe(startDragFrom);
        }

        fromEvent<TouchEvent>(document, 'touchmove', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && !rejected),
            )
            .subscribe(e => {
                if (e.touches.length !== 1) return;
                const dy = e.touches[0].clientY - startY;
                const dx = e.touches[0].clientX - startX;

                if (!dragStarted) {
                    const absDy = Math.abs(dy);
                    const absDx = Math.abs(dx);
                    if (absDy < 8 && absDx < 8) return;
                    if (absDx > absDy) {
                        rejected = true;
                        return;
                    }
                    dragStarted = true;
                    this.videoPanel.classList.remove('minimized');
                    handle?.classList.add('dragging');
                    container = this.videoPanel.querySelector<HTMLElement>('.c-container');
                    const currentH = this.videoPanel.offsetHeight;
                    this.videoPanel.style.minHeight = `${currentH}px`;
                    this.videoPanel.style.maxHeight = `${currentH}px`;
                    this.videoPanel.style.transition = 'none';
                }

                e.preventDefault();

                const rem = getRemSize();
                const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
                const newHeight = Math.max(0, Math.min(fullHeight, startHeight + dy));

                this.videoPanel.style.minHeight = `${newHeight}px`;
                this.videoPanel.style.maxHeight = `${newHeight}px`;
                applyVisuals(newHeight, rem);
            });

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && dragStarted),
            )
            .subscribe(() => {
                dragActive = false;

                const rem = getRemSize();
                const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
                const currentHeight = this.videoPanel.offsetHeight;
                const midPoint = fullHeight / 2;
                const targetHeight = currentHeight < midPoint ? 0 : fullHeight;
                const willMinimize = targetHeight === 0;

                // Snap visuals to target
                applyVisuals(targetHeight, rem);

                // Animate snap to target height
                this.videoPanel.style.transition = 'min-height 0.15s ease-out, max-height 0.15s ease-out';
                void this.videoPanel.offsetHeight;
                this.videoPanel.style.minHeight = `${targetHeight}px`;
                this.videoPanel.style.maxHeight = `${targetHeight}px`;

                setTimeout(() => {
                    cleanupAll();
                    if (willMinimize)
                        this.videoPanel.classList.add('minimized');
                    else
                        this.videoPanel.classList.remove('minimized');
                    vibrate();
                }, 160);
            });

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && !dragStarted),
            )
            .subscribe(() => { dragActive = false; });

        fromEvent<TouchEvent>(document, 'touchcancel')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive),
            )
            .subscribe(() => {
                dragActive = false;
                if (dragStarted)
                    cleanupAll();
            });
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
        this.teardownIsland();
        this.returnDragHandle();
        this.collapse();
        this.disposed$.next();
        this.disposed$.complete();
    }

    public toggleExpand(): void {
        if (!this.videoPanel.classList.contains('expanded')) {
            this.videoPanel.classList.remove('minimized');
            this.videoPanel.classList.add('expanded');
            this.setDragHandleVisible(false);
            document.body.appendChild(this.videoPanel);
            // Freeze narrow/wide state so rotating the device while fullscreen
            // doesn't reflow the hidden app layout underneath (e.g. left panel appearing).
            ScreenSize.freeze();
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
        this.setDragHandleVisible(true);
        // Resume ScreenSize updates; re-sync body classes to the current orientation.
        ScreenSize.unfreeze();
        void this.blazorRef.invokeMethodAsync('OnCollapsed');
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded'))
            this.collapse();
    }

    public startClosing() {
        if (this.closing)
            return;

        this.closing = true;
        this.setDragHandleVisible(false);
        this.videoPanel.classList.remove('first-time-open');
        this.videoPanel.classList.add('closing');

        const content = this.videoPanel.querySelector('.video-panel-content')!;
        let handled = false;
        const complete = () => {
            if (handled || !this.closing)
                return;

            handled = true;
            this.closing = false;
            this.closeTimer = 0;
            this.closeContent = null;
            this.closeComplete = null;
            content.removeEventListener('animationend', complete);
            void this.blazorRef.invokeMethodAsync('CloseVideoPanel');
        };

        this.closeContent = content;
        this.closeComplete = complete;
        content.addEventListener('animationend', complete);
        this.closeTimer = window.setTimeout(complete, 500);
    }

    public cancelClosing() {
        if (!this.closing && !this.videoPanel.classList.contains('closing'))
            return;

        this.closing = false;
        if (this.closeTimer !== 0) {
            window.clearTimeout(this.closeTimer);
            this.closeTimer = 0;
        }
        if (this.closeContent && this.closeComplete)
            this.closeContent.removeEventListener('animationend', this.closeComplete);
        this.closeContent = null;
        this.closeComplete = null;
        this.videoPanel.classList.remove('closing');
        this.setDragHandleVisible(!this.videoPanel.classList.contains('expanded'));
    }

    // endregion
}
