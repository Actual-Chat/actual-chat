// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated */
import { fromEvent, merge, Subject } from 'rxjs';
import { filter, first, switchMap, takeUntil, tap, throttleTime } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { Disposables } from 'disposable';

// Sizes in rem units
const HEADER_COLLAPSED_HEIGHT_REM = 3.5;
const HEADER_EXPANDED_HEIGHT_REM = 7.5;
const HEADER_HEIGHT_RANGE_REM = HEADER_EXPANDED_HEIGHT_REM - HEADER_COLLAPSED_HEIGHT_REM; // 4rem
const PIN_BADGE_THRESHOLD_REM = 3.75; // extra rem after full expand to show pin badge

// Get current font size and convert rem to pixels
function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function remToPx(rem: number): number {
    return rem * getRemSize();
}

function getSafeAreaTop(): number {
    const value = getComputedStyle(document.documentElement).getPropertyValue('--safe-area-top');
    return parseFloat(value) || 0;
}

export class ChatActivityPanel {
    private readonly activityPanel: HTMLElement;
    private chatView: HTMLElement;
    private header: HTMLElement;
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
        if (!(this.activityPanel instanceof HTMLElement))
            return;

        this.chatView = document.querySelector('.chat-view')!;
        this.header = this.activityPanel.closest('.layout-header')!;
        this.bottomSheet = document.querySelector('.bottom-sheet');
        this.pinBadge = this.bottomSheet?.querySelector('.pin-badge') ?? null;
        this.unpinBadge = this.bottomSheet?.querySelector('.unpin-badge') ?? null;

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

        // Bottom sheet drag for pin/unpin toggle
        if (this.bottomSheet) {
            DocumentEvents.capturedPassive.touchStart$.pipe(
                filter(() => !ScreenSize.isWide()),
                filter(e => this.bottomSheet?.contains(e.target as Node) ?? false),
                takeUntil(this.disposed$)
            ).subscribe(e => {
                Gestures.addActive(new BottomSheetTogglePinGesture(this, e));
            });
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.header?.classList.remove('expanded', 'collapsed', 'pinned');
        if (this.pinBadge) {
            this.pinBadge.style.transform = '';
            this.pinBadge.style.opacity = '';
            this.pinBadge.style.visibility = '';
        }
        if (this.unpinBadge) {
            this.unpinBadge.style.transform = '';
            this.unpinBadge.style.opacity = '';
            this.unpinBadge.style.visibility = '';
        }
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

// Gesture: Drag bottom-sheet down to toggle pin/unpin
class BottomSheetTogglePinGesture extends Gesture {
    private toggled = false;
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

        const bottomSheet = document.querySelector<HTMLElement>('.bottom-sheet')!;
        const header = panel['header'];
        const isPinned = panel['isPinned'];
        const badge = isPinned ? panel['unpinBadge'] : panel['pinBadge'];
        if (!bottomSheet || !header) {
            this.dispose();
            return;
        }

        tryPreventDefaultForEvent(touchStartEvent);

        // Calculate pixel values based on current font size + safe area
        const safeAreaTop = getSafeAreaTop();
        const headerCollapsedHeight = remToPx(HEADER_COLLAPSED_HEIGHT_REM) + safeAreaTop;
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
            if (badge) {
                badge.style.transform = '';
                badge.style.opacity = '';
                badge.style.visibility = '';
            }
            header.classList.remove('dragging');
            header.style.maxHeight = '';
        };

        const snapToState = () => {
            if (this.currentProgress > 0.5)
                panel.expand();
            else
                panel.collapse();
        };

        this.addDisposables(
            Disposables.fromAction(cleanup),
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => {
                if (!this.toggled) {
                    snapToState();
                    panel.setLockUntil(Date.now() + 5000);
                }
                this.dispose();
            }),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(() => {
                if (!this.toggled && this.badgeVisible) {
                    this.toggled = true;
                    cleanup();
                    if (isPinned)
                        panel.unpin();
                    else
                        panel.pin();
                } else if (!this.toggled) {
                    snapToState();
                    panel.setLockUntil(Date.now() + 5000);
                }
                this.dispose();
            }),
            DocumentEvents.active.touchMove$.subscribe(e => {
                if (this.toggled) return;

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
                this.currentProgress = Math.max(0, Math.min(1, dy / headerHeightRange));
                const currentHeight = headerCollapsedHeight + (headerHeightRange * this.currentProgress);

                // Calculate extra drag beyond full expand for badge
                const extraDrag = Math.max(0, dy - headerHeightRange);
                const sheetOffset = Math.min(extraDrag, pinBadgeThreshold);

                header.style.maxHeight = `${currentHeight}px`;
                bottomSheet.style.transform = `translateX(-50%) translateY(${sheetOffset}px)`;

                // Progressive badge scaling based on extra drag distance
                const badgeProgress = Math.min(1, extraDrag / pinBadgeThreshold);
                this.badgeVisible = badgeProgress >= 0.95;
                if (badge) {
                    badge.style.transform = `translateX(-50%) scale(${badgeProgress})`;
                    badge.style.opacity = '1';
                    badge.style.visibility = badgeProgress > 0.01 ? 'visible' : 'hidden';
                }
            })
        );
    }
}

// Helper: Get touch coordinates
function getCoords(event?: TouchEvent): { x: number; y: number } | null {
    const touches = event?.changedTouches ?? event?.touches;
    if (!touches?.length) return null;

    const touch = touches[0];
    return { x: touch.pageX, y: touch.pageY };
}
