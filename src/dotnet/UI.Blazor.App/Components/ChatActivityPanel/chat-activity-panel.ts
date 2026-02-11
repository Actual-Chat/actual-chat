// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated */
import { fromEvent, merge, Subject } from 'rxjs';
import { filter, first, switchMap, takeUntil, tap, throttleTime } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { Vector2D } from 'math';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { Disposables } from 'disposable';

// Sizes in rem units
const HEADER_COLLAPSED_HEIGHT_REM = 3.5;
const HEADER_EXPANDED_HEIGHT_REM = 7.5;
const HEADER_HEIGHT_RANGE_REM = HEADER_EXPANDED_HEIGHT_REM - HEADER_COLLAPSED_HEIGHT_REM; // 4rem
const PIN_BADGE_THRESHOLD_REM = 3.75; // extra rem after full expand to show pin badge
const LONG_PRESS_DELAY = 400; // ms to hold before unpin mode activates

// Get current font size and convert rem to pixels
function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function remToPx(rem: number): number {
    return rem * getRemSize();
}

export class ChatActivityPanel {
    private readonly activityPanel: HTMLElement;
    private chatView: HTMLElement;
    private header: HTMLElement;
    private panelWrapper: HTMLElement | null;
    private bottomSheet: HTMLElement | null;
    private pinBadge: HTMLElement | null;
    private unpinBadge: HTMLElement | null;
    private disposed$: Subject<void> = new Subject<void>();

    private state: 'expanded' | 'collapsed' = 'expanded';
    private isPinned = false;
    private lockUntil = 0;
    private isMoving = false;

    static create(activityPanel: HTMLElement): ChatActivityPanel {
        return new ChatActivityPanel(activityPanel);
    }

    constructor(activityPanel: HTMLElement) {
        this.activityPanel = activityPanel;
        this.chatView = document.querySelector('.chat-view')!;
        this.header = this.activityPanel.closest('.layout-header')!;
        this.panelWrapper = this.activityPanel.closest('.header-activity-panel-wrapper');
        this.bottomSheet = document.querySelector('.bottom-sheet');
        this.pinBadge = this.panelWrapper?.querySelector('.pin-badge') ?? null;
        this.unpinBadge = this.panelWrapper?.querySelector('.unpin-badge') ?? null;

        if (!this.chatView || !this.header) {
            return;
        }

        this.lockUntil = Date.now() + 3000;

        // Auto-collapse on scroll
        fromEvent(this.chatView, 'scroll').pipe(
            throttleTime(200),
            filter(() => {
                if (this.isMoving)
                    this.isMoving = false;
                return true;
            }),
            filter(() => !this.isLocked() && !this.isPinned),
            takeUntil(this.disposed$)
        ).subscribe(() => this.collapse());

        // Pointer events: swipe down to expand header
        merge(
            fromEvent<PointerEvent>(this.header, 'pointerdown'),
            fromEvent<PointerEvent>(this.activityPanel, 'pointerdown'),
            fromEvent<PointerEvent>(this.chatView, 'pointerdown')
        ).pipe(
            filter(e => e.target !== this.bottomSheet && !this.bottomSheet?.contains(e.target as Node)),
            filter(e => !this.isPinned || !this.activityPanel.contains(e.target as Node)),
            tap(startEvent => {
                this.isMoving = true;
                startEvent.preventDefault();
            }),
            switchMap(startEvent => {
                const startY = startEvent.clientY;
                const startX = startEvent.clientX;

                return fromEvent<PointerEvent>(document, 'pointermove').pipe(
                    filter(moveEvent => {
                        const dy = moveEvent.clientY - startY;
                        const dx = Math.abs(moveEvent.clientX - startX);
                        return dy > 6 && dy > dx;
                    }),
                    first(),
                    takeUntil(
                        fromEvent(document, 'pointerup').pipe(
                            tap(() => {
                                this.isMoving = false;
                            })
                        )
                    )
                );
            }),
            takeUntil(this.disposed$)
        ).subscribe(() => this.manualExpand());

        // Bottom sheet drag for pinning
        if (this.bottomSheet) {
            DocumentEvents.capturedPassive.touchStart$.pipe(
                filter(() => !ScreenSize.isWide()),
                filter(() => !this.isPinned),
                filter(e => this.bottomSheet?.contains(e.target as Node) ?? false),
                takeUntil(this.disposed$)
            ).subscribe(e => {
                Gestures.addActive(new BottomSheetPinGesture(this, e));
            });
        }

        // Activity panel long press for unpinning
        DocumentEvents.capturedPassive.touchStart$.pipe(
            filter(() => !ScreenSize.isWide()),
            filter(() => this.isPinned),
            filter(e => this.activityPanel.contains(e.target as Node)),
            takeUntil(this.disposed$)
        ).subscribe(e => {
            Gestures.addActive(new ActivityPanelUnpinGesture(this, e));
        });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.header.classList.remove('expanded', 'collapsed', 'pinned');
        this.activityPanel.classList.remove('pressing');
        this.pinBadge?.classList.remove('show');
        this.unpinBadge?.classList.remove('show');
        this.disposed$.next();
        this.disposed$.complete();
    }

    public manualExpand() {
        this.isMoving = false;
        if (this.state === 'expanded')
            return;

        this.expand();
        this.lockUntil = Date.now() + 5000;
    }

    public isLocked(): boolean {
        return Date.now() < this.lockUntil;
    }

    public collapse() {
        if (this.state === 'collapsed')
            return;

        this.state = 'collapsed';
        this.header.classList.remove('expanded');
        this.header.classList.add('collapsed');
    }

    public expand() {
        if (this.state === 'expanded')
            return;

        this.state = 'expanded';
        this.header.classList.remove('collapsed');
        this.header.classList.add('expanded');
    }

    public pin() {
        this.isPinned = true;
        this.expand();
        this.header.classList.add('pinned');
    }

    public unpin() {
        this.isPinned = false;
        this.header.classList.remove('pinned');
        this.collapse();
    }

    public getState() { return this.state; }
    public setLockUntil(value: number) { this.lockUntil = value; }
}

// Gesture: Drag bottom-sheet down to pin
class BottomSheetPinGesture extends Gesture {
    private pinned = false;
    private badgeVisible = false;
    private dragStarted = false;
    private currentProgress = 0; // 0 = collapsed, 1 = expanded

    constructor(
        private panel: ChatActivityPanel,
        private touchStartEvent: TouchEvent
    ) {
        super();
        const origin = getCoords(touchStartEvent);
        if (!origin) {
            this.dispose();
            return;
        }

        const bottomSheet = document.querySelector('.bottom-sheet') as HTMLElement;
        const header = panel['header'] as HTMLElement;
        const pinBadge = panel['pinBadge'] as HTMLElement | null;
        if (!bottomSheet || !header) {
            this.dispose();
            return;
        }

        tryPreventDefaultForEvent(touchStartEvent);

        // Calculate pixel values based on current font size
        const headerCollapsedHeight = remToPx(HEADER_COLLAPSED_HEIGHT_REM);
        const headerHeightRange = remToPx(HEADER_HEIGHT_RANGE_REM);
        const pinBadgeThreshold = remToPx(PIN_BADGE_THRESHOLD_REM);

        // Start from current state
        this.currentProgress = panel.getState() === 'collapsed' ? 0 : 1;

        // Visual feedback that bottom-sheet is touched (but don't change header yet)
        bottomSheet.classList.add('active');

        const startDragging = () => {
            if (this.dragStarted) return;
            this.dragStarted = true;

            // Set initial height BEFORE removing classes to prevent jump
            const initialHeight = headerCollapsedHeight + (headerHeightRange * this.currentProgress);
            header.style.maxHeight = `${initialHeight}px`;

            bottomSheet.classList.remove('active');
            bottomSheet.classList.add('dragging');
            header.classList.add('dragging');
            header.classList.remove('collapsed', 'expanded');
        };

        const cleanup = () => {
            bottomSheet.classList.remove('active');
            bottomSheet.classList.remove('dragging');
            bottomSheet.style.transform = '';
            pinBadge?.classList.remove('show');
            header.classList.remove('dragging');
            header.style.maxHeight = '';
        };

        const snapToState = () => {
            // Snap based on progress - if more than halfway, expand
            if (this.currentProgress > 0.5) {
                panel.expand();
            } else {
                panel.collapse();
            }
        };

        this.addDisposables(
            Disposables.fromAction(cleanup),
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => {
                if (!this.pinned) {
                    snapToState();
                    panel.setLockUntil(Date.now() + 5000);
                }
                this.dispose();
            }),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(() => {
                // Pin on release if badge was visible
                if (!this.pinned && this.badgeVisible) {
                    this.pinned = true;
                    cleanup();
                    panel.pin();
                } else if (!this.pinned) {
                    snapToState();
                    panel.setLockUntil(Date.now() + 5000);
                }
                this.dispose();
            }),
            DocumentEvents.active.touchMove$.subscribe(e => {
                if (this.pinned) return;

                const coords = getCoords(e);
                if (!coords) return;

                const dy = coords.y - origin.y;

                // Only start dragging when user moves finger down
                if (dy > 5) {
                    startDragging();
                    tryPreventDefaultForEvent(e);
                }

                if (!this.dragStarted) return;

                tryPreventDefaultForEvent(e);

                // Calculate progress: 0 = collapsed, 1 = fully expanded
                // Clamp between 0 and 1
                this.currentProgress = Math.max(0, Math.min(1, dy / headerHeightRange));
                const currentHeight = headerCollapsedHeight + (headerHeightRange * this.currentProgress);

                // Calculate extra drag beyond full expand for pinning
                const extraDrag = Math.max(0, dy - headerHeightRange);
                const sheetOffset = Math.min(extraDrag, pinBadgeThreshold);

                // Set header height directly (no RAF for immediate response)
                header.style.maxHeight = `${currentHeight}px`;
                // Move sheet down for extra drag
                bottomSheet.style.transform = `translateX(-50%) translateY(${sheetOffset}px)`;

                // Show badge when fully expanded + extra drag past threshold
                this.badgeVisible = extraDrag > pinBadgeThreshold;
                if (this.badgeVisible) {
                    pinBadge?.classList.add('show');
                } else {
                    pinBadge?.classList.remove('show');
                }
            })
        );
    }
}

// Gesture: Long press on activity panel then drag up to unpin
class ActivityPanelUnpinGesture extends Gesture {
    private unpinModeActive = false;
    private unpinned = false;
    private longPressTimeout: ReturnType<typeof setTimeout> | null = null;
    private dragOrigin: Vector2D | null = null;
    private currentProgress = 1; // 1 = expanded, 0 = collapsed
    private cleanupCalled = false;

    constructor(
        private panel: ChatActivityPanel,
        private touchStartEvent: TouchEvent
    ) {
        super();
        const origin = getCoords(touchStartEvent);
        if (!origin) {
            this.dispose();
            return;
        }

        const activityPanel = panel['activityPanel'];
        const header = panel['header'] as HTMLElement;
        const panelWrapper = panel['panelWrapper'] as HTMLElement | null;
        const unpinBadge = panel['unpinBadge'];

        // Calculate pixel values based on current font size
        const headerHeightRange = remToPx(HEADER_HEIGHT_RANGE_REM);

        // Safety: clear any leftover state from previous gesture
        unpinBadge?.classList.remove('show');
        document.querySelector('.unpin-badge')?.classList.remove('show');
        panelWrapper?.classList.remove('dragging');
        panelWrapper?.style.removeProperty('transform');
        header.classList.remove('dragging');

        tryPreventDefaultForEvent(touchStartEvent);
        activityPanel.classList.add('pressing');

        const cleanup = () => {
            if (this.cleanupCalled) return;
            this.cleanupCalled = true;

            activityPanel.classList.remove('pressing');
            activityPanel.classList.remove('unpin-active');
            // Hide badge - use both reference and direct query
            unpinBadge?.classList.remove('show');
            document.querySelector('.unpin-badge')?.classList.remove('show');
            panelWrapper?.classList.remove('dragging');
            panelWrapper?.style.removeProperty('transform');
            header.classList.remove('dragging');
            if (this.longPressTimeout) {
                clearTimeout(this.longPressTimeout);
                this.longPressTimeout = null;
            }
        };

        // Track last known finger position
        let lastCoords = origin;
        // Max translateY to keep panel on screen (calculated when unpin mode activates)
        let maxTranslateY = headerHeightRange;

        // Start long press timer - badge shows immediately after long press
        this.longPressTimeout = setTimeout(() => {
            this.unpinModeActive = true;
            // Capture current finger position as drag origin (not the original touch point)
            this.dragOrigin = lastCoords;
            // Calculate max translateY so panel doesn't go above screen top
            if (panelWrapper) {
                const rect = panelWrapper.getBoundingClientRect();
                maxTranslateY = rect.top;
            }
            activityPanel.classList.add('unpin-active');
            unpinBadge?.classList.add('show');
            panelWrapper?.classList.add('dragging');
            header.classList.add('dragging');
        }, LONG_PRESS_DELAY);

        const finishGesture = () => {
            if (this.isDisposed) return;
            // Always cleanup first to ensure badge is hidden
            cleanup();
            // Unpin if panel moved past 50% of the range
            if (!this.unpinned && this.unpinModeActive && this.currentProgress < 0.5) {
                this.unpinned = true;
                panel.unpin();
            } else if (!this.unpinned) {
                panel.expand();
            }
            this.dispose();
        };

        this.addDisposables(
            Disposables.fromAction(cleanup),
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => {
                cleanup();
                if (this.currentProgress > 0.5) {
                    panel.expand();
                } else {
                    panel.collapse();
                }
                this.dispose();
            }),
            // Fallback: pointerup is more reliable than touchEnd on some devices
            DocumentEvents.capturedPassive.pointerUp$.subscribe(finishGesture),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(finishGesture),
            DocumentEvents.active.touchMove$.subscribe(e => {
                if (this.unpinned) return;

                const coords = getCoords(e);
                if (!coords) return;

                // Track finger position for accurate dragOrigin when long press activates
                lastCoords = coords;

                if (!this.unpinModeActive || !this.dragOrigin) return;

                tryPreventDefaultForEvent(e);

                const dy = this.dragOrigin.y - coords.y; // positive when dragging up

                // Move panel up - it will overlay header content above it
                // Limit so panel doesn't go above screen top
                const translateY = Math.max(0, Math.min(dy, maxTranslateY));
                if (panelWrapper) {
                    panelWrapper.style.transform = `translateY(-${translateY}px)`;
                }

                // Calculate progress for snap decision (1 = expanded, 0 = collapsed)
                this.currentProgress = Math.max(0, Math.min(1, 1 - dy / headerHeightRange));
                // Badge stays visible until finger is released
            })
        );
    }
}

// Helper: Get touch coordinates
function getCoords(event?: TouchEvent): Vector2D | null {
    const touches = event?.changedTouches ?? event?.touches;
    if (!touches?.length) return null;

    const touch = touches[0];
    return new Vector2D(touch.pageX, touch.pageY);
}
