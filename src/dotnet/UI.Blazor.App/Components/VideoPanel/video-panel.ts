import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { CompactLayout } from 'compact-layout';

const MIN_SCALE = 1;
const MAX_SCALE_MOBILE = 4;
const MAX_SCALE_DESKTOP = 2;
const WHEEL_ZOOM_STEP = 0.002;
const TAP_MOVE_THRESHOLD = 225; // 15px squared
const TAP_MAX_DURATION = 500;
const DOUBLE_TAP_INTERVAL = 300;
const ZOOM_TRANSITION_MS = 250;

export class VideoPanel {
    private static readonly bodyClass = 'has-video-panel';
    private blazorRef: DotNet.DotNetObject;
    private readonly videoPanel: HTMLElement;
    private parentElement: HTMLElement | null = null;
    private parentNextSibling: ChildNode | null = null;
    private homeMarker: Comment | null = null;
    private disposed$: Subject<void> = new Subject<void>();

    // ScreenCast zoom/pan state
    private zoomScale = 1;
    private panX = 0;
    private panY = 0;
    private dragging = false;
    private lastTouchX = 0;
    private lastTouchY = 0;
    private lastMouseX = 0;
    private lastMouseY = 0;
    private mouseDragging = false;
    private lastMouseDragEndTime = Number.NEGATIVE_INFINITY;
    private pinching = false;
    private pinchInitialDist = 0;
    private pinchInitialScale = 0;
    private pinchContentX = 0;
    private pinchContentY = 0;
    private lastPinchEndTime = Number.NEGATIVE_INFINITY;
    // Unified tap / double-tap state (tracked inside touch handlers, not separate listeners)
    private tapTouchId = -1;
    private tapStartX = 0;
    private tapStartY = 0;
    private tapStartTime = 0;
    private tapMoved = false;
    private singleTapTimer = 0;
    private lastTouchActionTime = Number.NEGATIVE_INFINITY; // suppress synthetic click
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
    private panelMode: 'inline' | 'island' = 'inline';
    private compactReasons = new Set<string>();
    private forcedCollapseActive = false;
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
        // Guards the body:has(.video-panel...) rules. WebKit evaluates a compound left to right,
        // so an absent class here short-circuits before :has() runs - and :has() otherwise rescans
        // the whole body subtree on every DOM mutation just to re-prove the panel isn't there,
        // which measured 16-18% of WebContent's main thread during a call on an iPhone 13 Pro.
        document.body.classList.add(VideoPanel.bodyClass);

        this.parentElement = this.videoPanel.parentElement;
        this.parentNextSibling = this.videoPanel.nextSibling;
        if (this.parentElement) {
            this.homeMarker = document.createComment('video-panel-home');
            this.parentElement.insertBefore(this.homeMarker, this.videoPanel);
        }
        const needToShowElements = this.videoPanel.querySelectorAll('.show-with-delay');
        setTimeout(() => {
            needToShowElements.forEach(element => element.classList.add('show'));
            this.videoPanel.classList.remove('first-time-open');
        }, 1000);

        this.initGestures();
        this.setupHomeGuard();

        // Escape key handler
        fromEvent<KeyboardEvent>(document, 'keydown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape')
            )
            .subscribe(() => this.onEscPress());

        // Fold to island whenever any layout source requests compact mode
        // (on-screen keyboard, landscape mobile, etc.), restore when all sources release.
        fromEvent<CustomEvent<{ reason: string }>>(document, 'chat-layout:request-compact')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.addCompactReason(e.detail.reason));
        fromEvent<CustomEvent<{ reason: string }>>(document, 'chat-layout:release-compact')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.removeCompactReason(e.detail.reason));
        // Bootstrap from any reasons already active (e.g. app opened in landscape mobile).
        for (const reason of CompactLayout.reasons)
            this.compactReasons.add(reason);
        this.syncForcedCollapseToBlazor();
    }

    private syncForcedCollapseToBlazor(): void {
        const wantCompact = this.compactReasons.size > 0;
        if (wantCompact) {
            // Re-force whenever Blazor has cleared the collapsed state but compact is still wanted
            // (e.g. switching the panel mode to Expanded replaces Collapsed).
            // Skip when minimized: user explicitly swiped the panel down to 0 height, the
            // compact-mode requirement is already satisfied — don't reveal the panel as an
            // island just because the keyboard opened.
            if (!this.isExpanded() && !this.isCollapsed() && !this.isMinimized()) {
                this.forcedCollapseActive = true;
                void this.blazorRef.invokeMethodAsync('OnForceIsland', true);
            }
        }
        else if (this.forcedCollapseActive) {
            this.forcedCollapseActive = false;
            void this.blazorRef.invokeMethodAsync('OnForceIsland', false);
        }
    }

    private addCompactReason = (reason: string): void => {
        if (this.compactReasons.has(reason))
            return;

        this.compactReasons.add(reason);
        this.syncForcedCollapseToBlazor();
    }

    private removeCompactReason = (reason: string): void => {
        if (!this.compactReasons.has(reason))
            return;

        this.compactReasons.delete(reason);
        this.syncForcedCollapseToBlazor();
    }

    // Safety net: if the panel ever ends up under <body> without any of the
    // state classes that move it there (expanded / collapsed), pull it back
    // to its original Razor-rendered home. Catches races where Blazor's
    // class-attribute rewrite drops a JS-added class but the JS teardown
    // that would have reparented it never fires.
    //
    // Why a full teardown (it's idempotent): if we did partial cleanup here
    // and just set panelMode='inline', the subsequent updatePanelMode() call
    // from Blazor would short-circuit (panelMode already matches), leaving
    // the stale island ResizeObserver alive — and the next size change would
    // re-fire positionIslandDefault() and pin `top/right` back on the inline
    // panel.
    private setupHomeGuard(): void {
        const observer = new MutationObserver(() => {
            if (this.videoPanel.parentElement !== document.body)
                return;

            const cl = this.videoPanel.classList;
            if (cl.contains('expanded') || cl.contains('collapsed'))
                return;

            this.teardownIsland();
            this.panelMode = 'inline';
        });
        observer.observe(this.videoPanel, { attributes: true, attributeFilter: ['class'] });
        this.disposed$.subscribe(() => observer.disconnect());
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

    private isMinimized(): boolean {
        return this.videoPanel.classList.contains('minimized');
    }

    private get maxScale(): number {
        return document.body.classList.contains('narrow') ? MAX_SCALE_MOBILE : MAX_SCALE_DESKTOP;
    }

    private getScreenCastContainer(): HTMLElement | null {
        return this.videoPanel.querySelector<HTMLElement>('.remote-video-container.item-focused.screencast');
    }

    // Returns the visible render surface — canvas when canvas backend is active,
    // video element when MSTG backend is active (canvas is display:none in that case).
    private getScreenCastSurface(): HTMLElement | null {
        const container = this.getScreenCastContainer();
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

    // Toolbar toggle fires only on the focused (big) tile. Small tiles are
    // reserved for pin-on-tap (handled in Blazor), so a tap there must not
    // toggle the header/footer.
    private isOnFocusedTile(target: HTMLElement): boolean {
        return target.closest('.remote-video-container.item-focused') != null
            && !target.closest('.video-panel-toolbar')
            && !target.closest('.video-panel-chat');
    }

    private isOnScreenCast(target: HTMLElement): boolean {
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

    private getContentRect(
        container: HTMLElement,
    ): { offsetX: number; offsetY: number; width: number; height: number } {
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

                    if (performance.now() - this.lastTouchActionTime < 1000)
                        return false;

                    if (performance.now() - this.lastMouseDragEndTime < 300)
                        return false;

                    return this.isOnFocusedTile(e.target as HTMLElement);
                })
            )
            .subscribe(() => this.videoPanel.classList.toggle('toolbar-hidden'));

        // ── Desktop: wheel zoom ──
        fromEvent<WheelEvent>(this.videoPanel, 'wheel', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(e => this.isExpanded() && this.isOnScreenCast(e.target as HTMLElement))
            )
            .subscribe(e => this.onWheel(e));

        // ── Desktop: mouse drag ──
        fromEvent<PointerEvent>(this.videoPanel, 'pointerdown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.pointerType === 'mouse' && this.isExpanded()
                    && this.isOnScreenCast(e.target as HTMLElement) && e.button === 0 && this.zoomScale > 1)
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
                this.lastMouseDragEndTime = performance.now();
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
        const onVideo = this.isOnFocusedTile(target);
        const onScreenCast = this.isOnScreenCast(target);

        // Track screencast touches for move/end filtering
        if (onScreenCast)
            for (const t of Array.from(e.changedTouches))
                this.activeTouchIds.add(t.identifier);

        // ── Pinch (2 fingers on screencast) ──
        if (onScreenCast && e.touches.length === 2) {
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
            const container = this.getScreenCastContainer();
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
        if (onScreenCast && e.touches.length === 1) {
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
            this.tapStartTime = performance.now();
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

            const container = this.getScreenCastContainer();
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
            const container = this.getScreenCastContainer();
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
            this.lastPinchEndTime = performance.now();
        }
        if (this.dragging && e.touches.length === 0)
            this.dragging = false;

        // ── Tap detection (all fingers lifted, same touch that started on video) ──
        if (e.touches.length === 0 && e.changedTouches.length === 1
            && e.changedTouches[0].identifier === this.tapTouchId) {
            this.tapTouchId = -1;
            const elapsed = performance.now() - this.tapStartTime;
            if (!this.tapMoved && elapsed < TAP_MAX_DURATION && performance.now() - this.lastPinchEndTime > 500)
                this.handleTap(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
        }
    }

    // endregion

    // region: Tap / double-tap

    private handleTap(screenX: number, screenY: number): void {
        this.lastTouchActionTime = performance.now();

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
        const container = this.getScreenCastContainer();
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
        const container = this.getScreenCastContainer();
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
        const container = this.getScreenCastContainer();
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
        const container = this.getScreenCastContainer();
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
        const surface = this.getScreenCastSurface();
        const container = this.getScreenCastContainer();
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

    // Called from Blazor when collapsed/hidden state changes.
    // Owns the transition between inline and island (collapsed) — island
    // reparents to <body> for fixed positioning + drag. The hidden state needs
    // no JS: `.panel-hidden` hides the panel in place, and the visible remnant
    // is the separate ActivityPill component.
    public updatePanelMode(): void {
        this.videoPanel.classList.remove('minimized');
        const isCollapsed = this.videoPanel.classList.contains('collapsed');
        const newMode: 'inline' | 'island' = isCollapsed ? 'island' : 'inline';
        if (newMode === this.panelMode)
            return;

        if (this.panelMode === 'island')
            this.teardownIsland();
        if (newMode === 'island')
            this.setupIsland();
        this.panelMode = newMode;
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

        // Watch header/subheader/banners and island aspect changes to reposition.
        const subheader = document.querySelector('.layout-subheader');
        const headerContent = document.querySelector('.layout-header > .c-content');
        if (!this.islandResizeObserver) {
            this.islandResizeObserver = new ResizeObserver(() => {
                if (!this.islandDragged)
                    this.positionIslandDefault();
                else
                    this.clampIslandToViewport();
            });
            if (subheader)
                this.islandResizeObserver.observe(subheader);
            if (headerContent)
                this.islandResizeObserver.observe(headerContent);
            this.islandResizeObserver.observe(this.videoPanel);
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
        this.videoPanel.classList.remove('portrait-video');
        this.videoPanel.style.removeProperty('--video-panel-island-aspect');
        this.restoreToParent();
    }

    // Place the island top-right. Narrow: just below the main header title row
    // (ignoring activity panel + subheader so the island stays close to the top and
    // away from the editor), with safe-area-right respected. Wide: below subheader
    // (or header), small right gap.
    private positionIslandDefault(): void {
        let top: number;
        let right: string;
        if (ScreenSize.isNarrow()) {
            // Main header title row only — activity panel sits below it inside
            // .layout-header and we intentionally overlap it (see #island-overlap).
            const headerContent = document.querySelector('.layout-header > .c-content');
            if (headerContent) {
                top = headerContent.getBoundingClientRect().bottom + 8;
            } else {
                top = 64;
            }
            right = 'calc(var(--safe-area-right) + 0.5rem)';
        } else {
            const subheader = document.querySelector('.layout-subheader');
            if (subheader && subheader.getBoundingClientRect().height > 0) {
                top = subheader.getBoundingClientRect().bottom + 8;
            } else {
                const header = document.querySelector('.layout-header');
                top = header ? header.getBoundingClientRect().bottom + 8 : 64;
            }
            right = '0.5rem';
        }
        this.videoPanel.style.top = `${top}px`;
        this.videoPanel.style.right = right;
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

    // region: Panel expand/collapse

    public dispose() {
        if (this.disposed$.closed)
            return;

        document.body.classList.remove(VideoPanel.bodyClass);

        // Hide before any DOM reshuffling (collapse/reparent) so callers that
        // dispose without playing a close animation don't see the panel briefly
        // snap to its inline location before unmount.
        this.videoPanel.style.visibility = 'hidden';
        if (this.singleTapTimer) {
            clearTimeout(this.singleTapTimer);
            this.singleTapTimer = 0;
        }
        this.teardownIsland();
        this.collapse();
        this.homeMarker?.parentNode?.removeChild(this.homeMarker);
        this.homeMarker = null;
        this.disposed$.next();
        this.disposed$.complete();
    }

    public toggleExpand(): void {
        if (this.videoPanel.parentElement !== document.body)
            this.expand();
        else
            this.collapse();
    }

    public expand(): void {
        if (this.videoPanel.parentElement === document.body)
            return;

        // Reparent BEFORE adding 'expanded' — otherwise position:fixed would
        // resolve against the original (possibly transformed) ancestor for a frame.
        document.body.appendChild(this.videoPanel);
        this.videoPanel.classList.remove('minimized');
        this.videoPanel.classList.add('expanded');
        // Freeze narrow/wide state so rotating the device while fullscreen
        // doesn't reflow the hidden app layout underneath (e.g. left panel appearing).
        ScreenSize.freeze();
        void this.blazorRef.invokeMethodAsync('OnExpanded');
    }

    public collapse() {
        if (this.videoPanel.parentElement !== document.body)
            return;

        this.resetZoom();
        // If compact reasons still demand island mode, stay attached to body — no point
        // restoring to the inline parent only to setupIsland() will reparent right back.
        // Going via inline causes a visible "video lands in chat header" flash.
        const willReForceIsland = this.compactReasons.size > 0;
        if (!willReForceIsland)
            this.restoreToParent();
        this.videoPanel.classList.remove('expanded', 'toolbar-hidden');
        ScreenSize.unfreeze();
        void this.blazorRef.invokeMethodAsync('OnCollapsed');
        if (willReForceIsland)
            this.videoPanel.classList.add('collapsed');
        // While fullscreen, panelMode stayed at whatever it was before expand() ran.
        // Reset so updatePanelMode runs the real transition from the current class state.
        this.panelMode = 'inline';
        this.updatePanelMode();
        if (willReForceIsland) {
            this.forcedCollapseActive = true;
            void this.blazorRef.invokeMethodAsync('OnForceIsland', true);
        }
    }

    private restoreToParent(): void {
        const parent = this.parentElement;
        if (!parent)
            return;
        if (this.homeMarker?.parentNode === parent) {
            if (this.homeMarker.nextSibling !== this.videoPanel)
                parent.insertBefore(this.videoPanel, this.homeMarker.nextSibling);
            return;
        }
        if (this.parentNextSibling?.parentNode === parent)
            parent.insertBefore(this.videoPanel, this.parentNextSibling);
        else
            parent.appendChild(this.videoPanel);
    }

    private onEscPress() {
        if (this.videoPanel.classList.contains('expanded'))
            this.collapse();
    }

    public startClosing() {
        if (this.closing)
            return;

        this.closing = true;
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
            // Hide immediately so Blazor re-render can't flash the panel
            // (re-render overwrites class attr, dropping JS-added "closing" → fill lost)
            this.videoPanel.style.visibility = 'hidden';
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
    }

    // endregion
}
