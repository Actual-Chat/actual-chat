import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { DeviceInfo } from 'device-info';
import { CompactLayout } from 'compact-layout';

const MIN_SCALE = 1;
const MAX_SCALE_MOBILE = 4;
const MAX_SCALE_DESKTOP = 2;
const WHEEL_ZOOM_STEP = 0.002;
const TAP_MOVE_THRESHOLD = 225; // 15px squared
const TAP_MAX_DURATION = 500;
const DOUBLE_TAP_INTERVAL = 300;
const ZOOM_TRANSITION_MS = 250;
// Name-badge flow around the footer buttons (single-row layouts). GAP is the clearance kept
// between a badge and the buttons; MIN is the narrowest a badge is truncated to beside them
// before it gives up and lifts above instead. The badge's own inset from the tile edge is read
// live from the rendered caption (it's `bottom-1 left-1` in css), so it needs no constant here.
const BADGE_GAP = 8;
const BADGE_MIN_REM = 8;

type Mode = 'inline' | 'expanded' | 'island' | 'hidden';

// State this script owns lives in attributes: Blazor rewrites `class` on every render.
const BarsHiddenAttribute = 'data-bars-hidden';
// See nomad-slot.ts
const NomadHoldAttribute = 'data-nomad-hold';

/**
 * The script half of CallScreen. The component decides the mode and renders it as a class; this
 * follows it: it positions and drags the island, and handles the gestures of the full-screen video.
 * Where the screen is in the DOM is RenderIntoNomadSlot's.
 */
export class CallScreen {
    private blazorRef: DotNet.DotNetObject;
    private readonly root: HTMLElement;
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
    private mode: Mode = 'inline';
    private isScreenSizeFrozen = false;
    private compactReasons = new Set<string>();
    private forcedCollapseActive = false;

    // Name-badge placement (full-screen video only): a rAF-coalesced relayout fed by a ResizeObserver
    // (stage resize) and a MutationObserver (tiles join/leave, name edits, bars-hidden and
    // layout-equal toggles). movedBadges holds the labels carrying inline styles this run,
    // so they can be reset on teardown or when the layout stops needing them.
    private badgeResizeObserver: ResizeObserver | null = null;
    private badgeMutationObserver: MutationObserver | null = null;
    private badgeRaf = 0;
    private movedBadges = new Set<HTMLElement>();

    static create(root: HTMLElement, blazorRef: DotNet.DotNetObject): CallScreen {
        return new CallScreen(root, blazorRef);
    }

    constructor(root: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.root = root;

        this.initGestures();

        // A modal over the screen takes Escape first, in the capture phase, and marks the event as handled
        fromEvent<KeyboardEvent>(document, 'keydown')
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape' && !e.defaultPrevented)
            )
            .subscribe(() => this.onEscPress());
        // The chat's editor takes the focus as the screen expands, and claims Escape to cancel what it
        // holds. With nothing to cancel the key is the screen's, and only the capture phase gets ahead of it.
        fromEvent<KeyboardEvent>(document, 'keydown', { capture: true })
            .pipe(
                takeUntil(this.disposed$),
                filter(e => e.key === 'Escape' && !e.defaultPrevented && this.isIdleEditor(e.target))
            )
            .subscribe(e => {
                if (!this.onEscPress())
                    return;

                e.preventDefault();
                e.stopPropagation();
            });

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

        // The component renders the mode as a class. Following it from an observer rather than from
        // a call after the render moves the root within the task that changed the class - before a
        // paint could show a full-screen root still sized by the chat header it was inline in.
        const classObserver = new MutationObserver(() => this.update());
        classObserver.observe(this.root, { attributes: true, attributeFilter: ['class'] });
        this.disposed$.subscribe(() => classObserver.disconnect());
        this.update();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        if (this.singleTapTimer) {
            clearTimeout(this.singleTapTimer);
            this.singleTapTimer = 0;
        }
        this.teardownBadgeLayout();
        this.teardownIsland();
        this.root.parentElement?.removeAttribute(NomadHoldAttribute);
        this.setScreenSizeFrozen(false);
        this.disposed$.next();
        this.disposed$.complete();
    }

    // Private methods

    private update(): void {
        const mode = this.getMode();
        if (mode !== this.mode) {
            if (this.mode === 'island')
                this.teardownIsland();
            if (this.mode === 'expanded') {
                this.resetZoom();
                this.root.removeAttribute(BarsHiddenAttribute);
            }
            this.root.classList.remove('minimized');
            this.mode = mode;
            if (mode === 'island')
                this.setupIsland();
        }
        this.syncNomadHold();
        const hasVideo = this.root.classList.contains('has-video');
        if (!hasVideo)
            this.root.removeAttribute(BarsHiddenAttribute);
        if (mode === 'expanded' && hasVideo)
            this.setupBadgeLayout();
        else
            this.teardownBadgeLayout();
        // Freeze narrow/wide state on mobile only: there a rotation would otherwise reflow the hidden app
        // layout underneath (e.g. the left panel appearing in landscape). On desktop the user resizes
        // deliberately and the full-screen video must honor it live - it drops the side chat and its
        // controls once the viewport falls below Large.
        this.setScreenSizeFrozen(mode === 'expanded' && hasVideo && DeviceInfo.isMobile);
        this.syncForcedCollapseToBlazor();
    }

    // ── Name-badge placement ──
    // Multi-row grids are handled by css (the bottom row's badge moves to the tile top). This only
    // runs the single-row case and the sidebar's big focused tile. Every badge stays at its base
    // bottom-left (0.25rem from the edges); only a badge the central footer controls actually cover
    // is touched — truncated to the room on their left, or pushed flush against their right edge and
    // grown rightwards, or (only when neither side has room) lifted just above them. Right-side
    // obstacles — the chat toggle on desktop, the small-tile column in the sidebar — clamp how far
    // right a bottom badge may reach so it never slides under them.
    private setupBadgeLayout(): void {
        const stage = this.root.querySelector<HTMLElement>('.video-stage');
        if (!stage || this.badgeResizeObserver)
            return;

        this.badgeResizeObserver = new ResizeObserver(() => this.scheduleBadgeLayout());
        this.badgeResizeObserver.observe(stage);
        this.badgeMutationObserver = new MutationObserver(() => this.scheduleBadgeLayout());
        this.badgeMutationObserver.observe(stage, { childList: true, subtree: true, characterData: true });
        this.badgeMutationObserver.observe(this.root, {
            attributes: true,
            attributeFilter: ['class', BarsHiddenAttribute],
        });
        void document.fonts.ready.then(() => this.scheduleBadgeLayout());
        this.scheduleBadgeLayout();
    }

    private teardownBadgeLayout(): void {
        this.badgeResizeObserver?.disconnect();
        this.badgeResizeObserver = null;
        this.badgeMutationObserver?.disconnect();
        this.badgeMutationObserver = null;
        if (this.badgeRaf) {
            cancelAnimationFrame(this.badgeRaf);
            this.badgeRaf = 0;
        }
        this.movedBadges.forEach(label => this.resetBadge(label));
        this.movedBadges.clear();
    }

    private scheduleBadgeLayout(): void {
        if (this.badgeRaf)
            return;

        this.badgeRaf = requestAnimationFrame(() => {
            this.badgeRaf = 0;
            this.layoutBadges();
        });
    }

    private resetBadge(label: HTMLElement): void {
        label.style.maxWidth = '';
        label.style.transform = '';
    }

    // Union of the central footer control cluster — not the full-width bar. Excludes the invisible
    // slot holder and the chat toggle (it sits at the right edge and is handled as a separate
    // right-side obstacle, not something badges flow around). Null when the bars are tap-hidden.
    private footerButtonsRect(): DOMRect | null {
        const footer = this.root.querySelector<HTMLElement>('.call-screen-footer');
        if (!footer || this.root.hasAttribute(BarsHiddenAttribute))
            return null;

        // The sides stretch to the bar's edges, so it's their controls that count, not the sides
        return this.unionRect(Array.from(footer.querySelectorAll(
            ':scope > :not(.c-side):not(.btn-chat), :scope > .c-side > :not(.btn-incut)')));
    }

    private unionRect(elements: Element[]): DOMRect | null {
        let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
        for (const el of elements) {
            const r = el.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0)
                continue;

            left = Math.min(left, r.left);
            top = Math.min(top, r.top);
            right = Math.max(right, r.right);
            bottom = Math.max(bottom, r.bottom);
        }
        return left === Infinity ? null : new DOMRect(left, top, right - left, bottom - top);
    }

    private layoutBadges(): void {
        if (this.disposed$.closed)
            return;

        const stage = this.root.querySelector<HTMLElement>('.video-stage');
        if (!stage)
            return;

        const isEqual = this.root.classList.contains('layout-equal');
        const tiles = isEqual
            ? Array.from(stage.querySelectorAll<HTMLElement>(
                '.c-grid > .video-track-player.item-focused, .c-grid > .video-track-player.item-x'))
            : Array.from(stage.querySelectorAll<HTMLElement>('.video-track-player.item-focused'));
        const items = tiles
            .map(tile => ({ tile, label: tile.querySelector<HTMLElement>('.video-participant-label') }))
            .filter((it): it is { tile: HTMLElement; label: HTMLElement } => it.label != null);

        // Reset first: clears any prior run so the css (multi-row) or the base bottom-left take over.
        for (const it of items)
            this.resetBadge(it.label);
        this.movedBadges.forEach(label => {
            if (!items.some(it => it.label === label))
                this.resetBadge(label);
        });
        this.movedBadges = new Set<HTMLElement>();
        if (!this.isExpanded() || items.length === 0)
            return;

        // Several rows → css owns it (the bottom-row badge sits at the tile top). Leave them reset.
        if (isEqual && new Set(items.map(it => Math.round(it.tile.getBoundingClientRect().top))).size > 1)
            return;

        // Bars hidden → nothing to flow around; badges stay bottom-left.
        const buttons = this.footerButtonsRect();
        if (!buttons)
            return;

        // The central control cluster badges flow around, grown by GAP on the sides and top (the
        // bottom edge is the screen edge, so it isn't padded).
        const box = {
            l: buttons.left - BADGE_GAP,
            r: buttons.right + BADGE_GAP,
            t: buttons.top - BADGE_GAP,
            b: buttons.bottom,
        };

        // Right-side obstacles a bottom badge must never slide under: the chat toggle (desktop) and,
        // in the sidebar, the small-tile column. They clamp each tile's usable right edge.
        const rightObstacles: DOMRect[] = [];
        const chatButton = this.root.querySelector<HTMLElement>('.call-screen-footer .btn-chat');
        if (chatButton) {
            const r = chatButton.getBoundingClientRect();
            if (r.width > 0 && r.height > 0)
                rightObstacles.push(r);
        }
        if (!isEqual) {
            const column = this.unionRect(
                Array.from(stage.querySelectorAll<HTMLElement>('.video-track-player.item-x')));
            if (column)
                rightObstacles.push(column);
        }
        const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
        const min = BADGE_MIN_REM * rem;

        // Measure each badge at a generous width (left/bottom-anchored, so left/bottom stay put) and
        // work out its tile's usable right edge after the right-side obstacles.
        const measured = items.map(it => {
            const tileRect = it.tile.getBoundingClientRect();
            it.label.style.maxWidth = `${tileRect.width}px`;
            const rect = it.label.getBoundingClientRect();
            const inset = rect.left - tileRect.left;
            let usableRight = tileRect.right - inset;
            for (const o of rightObstacles) {
                if (o.left < tileRect.right && o.right > tileRect.left && o.left > rect.left)
                    usableRight = Math.min(usableRight, o.left - BADGE_GAP);
            }
            return {
                ...it,
                tileRect,
                inset,
                usableRight,
                anchorLeft: rect.left,
                anchorBottom: rect.bottom,
                h: rect.height,
                naturalW: rect.width,
            };
        });
        for (const it of measured) {
            let dx = 0, dy = 0, maxW = Math.max(0, it.usableRight - it.anchorLeft);
            const hit = it.anchorLeft < box.r && it.anchorLeft + it.naturalW > box.l
                && it.anchorBottom > box.t && it.anchorBottom - it.h < box.b;
            if (hit) {
                const leftRoom = box.l - it.anchorLeft;
                const rightRoom = it.usableRight - box.r;
                if (leftRoom >= min)
                    maxW = leftRoom; // stays bottom-left, truncated before the buttons
                else if (rightRoom >= Math.min(it.naturalW, min)) {
                    maxW = rightRoom; // flush against the right of the buttons, grows rightwards
                    dx = box.r - it.anchorLeft;
                }
                else {
                    dy = box.t - it.anchorBottom; // no room beside the buttons: lift above them
                    maxW = it.tileRect.width - 2 * it.inset;
                }
            }
            it.label.style.maxWidth = `${Math.max(0, maxW)}px`;
            if (dx !== 0 || dy !== 0)
                it.label.style.transform = `translate(${dx}px, ${dy}px)`;
            this.movedBadges.add(it.label);
        }
    }

    private getMode(): Mode {
        const cl = this.root.classList;
        if (cl.contains('expanded'))
            return 'expanded';
        if (cl.contains('collapsed'))
            return 'island';

        return cl.contains('panel-hidden') ? 'hidden' : 'inline';
    }

    private syncNomadHold(): void {
        // While compact mode is about to turn the inline screen into the island, it skips the chat header's
        // slot: landing there for a frame reads as a flash in the header.
        const isForcingIsland = this.compactReasons.size > 0 && !this.isMinimized();
        this.root.parentElement?.toggleAttribute(NomadHoldAttribute, this.mode === 'inline' && isForcingIsland);
    }

    private setScreenSizeFrozen(mustFreeze: boolean): void {
        if (mustFreeze === this.isScreenSizeFrozen)
            return;

        this.isScreenSizeFrozen = mustFreeze;
        if (mustFreeze)
            ScreenSize.freeze();
        else
            ScreenSize.unfreeze();
    }

    private toggleBars(): void {
        this.root.toggleAttribute(BarsHiddenAttribute);
    }

    private onEscPress(): boolean {
        // The menu host closes its menu on the same key, but hears it on window, after this handler
        if (!this.isExpanded() || document.querySelector('.ac-menu-host[data-has-menu]'))
            return false;

        void this.blazorRef.invokeMethodAsync('OnEscape');
        return true;
    }

    private isIdleEditor(target: EventTarget | null): boolean {
        const editor = target instanceof Element ? target.closest('.chat-message-editor') : null;
        return editor != null
            && !editor.hasAttribute('data-has-related-entry')
            && !editor.querySelector('[data-has-editor-content], .attachment-list-wrapper');
    }

    private syncForcedCollapseToBlazor(): void {
        const wantCompact = this.compactReasons.size > 0;
        if (wantCompact) {
            // Re-force whenever Blazor has cleared the collapsed state but compact is still wanted
            // (e.g. switching the panel mode to Expanded replaces Collapsed).
            // Skip when minimized: user explicitly swiped the panel down to 0 height, the
            // compact-mode requirement is already satisfied — don't reveal the panel as an
            // island just because the keyboard opened.
            if (this.mode === 'inline' && !this.isMinimized()) {
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
        this.syncNomadHold();
        this.syncForcedCollapseToBlazor();
    }

    private removeCompactReason = (reason: string): void => {
        if (!this.compactReasons.has(reason))
            return;

        this.compactReasons.delete(reason);
        this.syncNomadHold();
        this.syncForcedCollapseToBlazor();
    }

    // region: Helpers

    private isExpanded(): boolean {
        return this.root.classList.contains('expanded');
    }

    private isMinimized(): boolean {
        return this.root.classList.contains('minimized');
    }

    private get maxScale(): number {
        return document.body.classList.contains('narrow') ? MAX_SCALE_MOBILE : MAX_SCALE_DESKTOP;
    }

    private getScreenCastContainer(): HTMLElement | null {
        return this.root.querySelector<HTMLElement>('.remote-video-container.item-focused.screencast');
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
            && !target.closest('.call-screen-toolbar')
            && !target.closest('.call-screen-chat');
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
        fromEvent<MouseEvent>(this.root, 'click')
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
            .subscribe(() => this.toggleBars());

        // ── Desktop: wheel zoom ──
        fromEvent<WheelEvent>(this.root, 'wheel', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(e => this.isExpanded() && this.isOnScreenCast(e.target as HTMLElement))
            )
            .subscribe(e => this.onWheel(e));

        // ── Desktop: mouse drag ──
        fromEvent<PointerEvent>(this.root, 'pointerdown')
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
        fromEvent<TouchEvent>(this.root, 'touchstart', { passive: false } as AddEventListenerOptions)
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
        this.toggleBars();
    }

    private onDoubleTap(screenX: number, screenY: number): void {
        const container = this.getScreenCastContainer();
        if (!container) {
            // Non-screencast video — toggle the bars
            this.toggleBars();
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

    private setupIsland(): void {
        this.teardownIsland(); // clean up any previous island state
        this.islandDragged = false;
        this.islandTeardown$ = new Subject<void>();
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
            this.islandResizeObserver.observe(this.root);
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
        this.root.style.top = '';
        this.root.style.left = '';
        this.root.style.right = '';
        this.root.style.cursor = '';
        this.root.removeAttribute('data-portrait-video');
        this.root.style.removeProperty('--call-screen-island-aspect');
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
        this.root.style.top = `${top}px`;
        this.root.style.right = right;
        this.root.style.left = '';
    }

    private initIslandDrag(): void {
        const teardown$ = this.islandTeardown$!;
        // Pointer events for unified mouse+touch drag.
        fromEvent<PointerEvent>(this.root, 'pointerdown')
            .pipe(
                takeUntil(teardown$),
                filter(() => this.root.classList.contains('collapsed')),
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
        const rect = this.root.getBoundingClientRect();
        this.islandOrigLeft = rect.left;
        this.islandOrigTop = rect.top;
        // Switch to left-based positioning immediately so right doesn't fight.
        this.root.style.left = `${rect.left}px`;
        this.root.style.right = 'auto';
        this.root.setPointerCapture(e.pointerId);
        this.root.style.cursor = 'grabbing';
    }

    private onIslandPointerMove(e: PointerEvent): void {
        e.preventDefault();
        const dx = e.clientX - this.islandStartX;
        const dy = e.clientY - this.islandStartY;
        // Clamp to viewport while dragging.
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const w = this.root.offsetWidth;
        const h = this.root.offsetHeight;
        const newLeft = Math.max(0, Math.min(vw - w, this.islandOrigLeft + dx));
        const newTop = Math.max(0, Math.min(vh - h, this.islandOrigTop + dy));
        this.root.style.left = `${newLeft}px`;
        this.root.style.top = `${newTop}px`;
        if (Math.abs(dx) > 4 || Math.abs(dy) > 4)
            this.islandDragged = true;
    }

    private onIslandPointerUp(): void {
        this.islandDragging = false;
        this.root.style.cursor = '';
    }

    private clampIslandToViewport(): void {
        if (!this.root.classList.contains('collapsed'))
            return;

        const rect = this.root.getBoundingClientRect();
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
            this.root.style.left = `${left}px`;
            this.root.style.top = `${top}px`;
            this.root.style.right = 'auto';
        }
    }

    // endregion
}
