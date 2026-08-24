import { fromEvent, Subject } from 'rxjs';
import { filter, takeUntil, throttleTime } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { CompactLayout } from 'compact-layout';
import { Disposables } from 'disposable';

// Wrapper height (the only animated element for collapse/expand)
const WRAPPER_HEIGHT_REM = 3.5; // h-14
const WRAPPER_MAX_HEIGHT_REM = 7; // max stretch when dragging past expanded
const DETENT_REM = 2; // dead zone at expanded position before stretch begins
const PIN_BADGE_THRESHOLD_REM = 2.5; // extra drag beyond detent to trigger pin

function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function remToPx(rem: number): number {
    return rem * getRemSize();
}

function vibrate(): void {
    if ('vibrate' in navigator)
        navigator.vibrate(10);
}

export class ChatActivityPanel {
    private readonly activityPanel: HTMLElement;
    private chatView!: HTMLElement;
    private header!: HTMLElement;
    private wrapper!: HTMLElement;
    private pinBadge: HTMLElement | null;
    private disposed$: Subject<void> = new Subject<void>();

    private state: 'expanded' | 'collapsed' = 'expanded';
    private isPinned = false;
    private lockUntil = 0;
    private isMoving = false;
    private compactReasons = new Set<string>();
    private forcedCollapseActive = false;

    static create(activityPanel: HTMLElement): ChatActivityPanel {
        return new ChatActivityPanel(activityPanel);
    }

    constructor(activityPanel: HTMLElement) {
        this.activityPanel = activityPanel;
        if (!(this.activityPanel instanceof HTMLElement))
            return;

        this.chatView = document.querySelector('.chat-view')!;
        this.header = this.activityPanel.closest('.layout-header')!;
        this.wrapper = this.activityPanel.closest('.header-activity-panel-wrapper')!;
        this.pinBadge = this.activityPanel.querySelector('.c-pin-badge');

        if (!this.chatView || !this.header || !this.wrapper) // eslint-disable-line @typescript-eslint/no-unnecessary-condition
            return;

        this.lockUntil = Date.now() + 3000;

        // Auto-collapse on scroll
        fromEvent(this.chatView, 'scroll').pipe(
            throttleTime(200),
            filter(() => {
                if (this.isMoving)
                    this.isMoving = false;
                return true;
            }),
            filter(() => !this.isNotParticipating()),
            filter(() => !this.isLocked() && !this.isPinned),
            takeUntil(this.disposed$)
        ).subscribe(() => this.collapse());

        // Continuous drag gesture for expand/pin/unpin.
        // Why header-touch: .chat-activity-panel has `pointer-events: none` (to keep
        // the panel's background click from toggling the right panel), so touches in
        // the panel area land on .layout-header — we accept those here and let
        // PanelDragGesture decide whether the gesture does anything based on
        // isPinned/state/dy threshold.
        // Disabled entirely when not-participating (panel is just a static banner).
        DocumentEvents.capturedPassive.touchStart$.pipe(
            filter(() => !ScreenSize.isWide()),
            filter(() => !this.isNotParticipating()),
            filter(e => {
                const target = e.target as Node;
                return this.activityPanel.contains(target) || this.header.contains(target);
            }),
            takeUntil(this.disposed$)
        ).subscribe(e => {
            this.isMoving = true;
            Gestures.addActive(new PanelDragGesture(this, e));
        });

        // Watch for not-participating class: when it appears, force expand
        // (e.g. user disconnects from call while header is collapsed)
        const observer = new MutationObserver(() => {
            if (this.isNotParticipating() && this.state === 'collapsed')
                this.expand();
        });
        observer.observe(this.activityPanel, { attributes: true, attributeFilter: ['class'] });
        this.disposed$.subscribe(() => observer.disconnect());

        // Fold whenever any layout source requests compact mode (keyboard, landscape mobile, …),
        // restore when all sources release — and only if we initiated the collapse.
        fromEvent<CustomEvent<{ reason: string }>>(document, 'chat-layout:request-compact')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.addCompactReason(e.detail.reason));
        fromEvent<CustomEvent<{ reason: string }>>(document, 'chat-layout:release-compact')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.removeCompactReason(e.detail.reason));
        // Bootstrap from any reasons already active.
        for (const reason of CompactLayout.reasons)
            this.compactReasons.add(reason);
        if (this.compactReasons.size > 0 && this.state !== 'collapsed' && !this.isPinned && !this.isNotParticipating()) {
            this.forcedCollapseActive = true;
            this.collapse();
        }
    }

    private addCompactReason = (reason: string): void => {
        if (this.compactReasons.has(reason))
            return;
        this.compactReasons.add(reason);
        if (this.state !== 'collapsed' && !this.isPinned && !this.isNotParticipating()) {
            this.forcedCollapseActive = true;
            this.collapse();
        }
    }

    private removeCompactReason = (reason: string): void => {
        if (!this.compactReasons.has(reason))
            return;
        this.compactReasons.delete(reason);
        if (this.compactReasons.size !== 0)
            return;
        if (this.forcedCollapseActive) {
            this.forcedCollapseActive = false;
            this.expand();
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        // Blazor calls dispose() even when init bailed out above on a missing header
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        this.header?.classList.remove('expanded', 'collapsed', 'pinned');
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

    public isNotParticipating(): boolean {
        return this.activityPanel.classList.contains('not-participating');
    }

    public collapse() {
        if (this.state === 'collapsed')
            return;
        if (this.isNotParticipating())
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

// Gesture: Drag activity panel down to expand/pin, drag up to unpin.
//
// State machine:
//   COLLAPSED → drag down → wrapper height follows finger → EXPANDED
//   EXPANDED  → continue drag down → rubber-band → badge appears → vibro
//            → release with badge visible → PIN
//   PINNED    → drag up → wrapper height shrinks → release near 0 → UNPIN + vibro
class PanelDragGesture extends Gesture {
    private toggled = false;
    private badgeVisible = false;
    private detentVibroFired = false;
    private bottomVibroFired = false;
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

        const header = panel['header'];
        const wrapper = panel['wrapper'];
        const isPinned = panel['isPinned'];
        const badge = isPinned ? null : panel['pinBadge'];
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!header || !wrapper) {
            this.dispose();
            return;
        }

        tryPreventDefaultForEvent(touchStartEvent);
        const gestureStartTime = Date.now();

        const wrapperHeightRange = remToPx(WRAPPER_HEIGHT_REM);
        const wrapperMaxHeight = remToPx(WRAPPER_MAX_HEIGHT_REM);
        const detentZone = remToPx(DETENT_REM);
        const pinBadgeThreshold = remToPx(PIN_BADGE_THRESHOLD_REM);
        const activityPanel = panel['activityPanel'];
        const startProgress = panel.getState() === 'collapsed' ? 0 : 1;
        this.currentProgress = startProgress;

        const startDragging = () => {
            if (this.dragStarted) return;
            this.dragStarted = true;

            const initialHeight = wrapperHeightRange * this.currentProgress;
            wrapper.style.height = `${initialHeight}px`;
            wrapper.style.transition = 'none';
            header.classList.add('dragging');
            header.classList.remove('collapsed', 'expanded');
        };

        const cleanup = () => {
            if (badge) {
                badge.style.transform = '';
                badge.style.opacity = '';
                badge.style.visibility = '';
            }
            header.classList.remove('dragging');
            wrapper.style.height = '';
            wrapper.style.transition = '';
            activityPanel.style.bottom = '';
            activityPanel.style.top = '';
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
                const elapsed = Date.now() - gestureStartTime;
                panel['isMoving'] = false;

                if (!this.toggled && this.badgeVisible) {
                    // Dragged past pin threshold → pin / unpin
                    this.toggled = true;
                    // Clear badge & panel positioning immediately
                    if (badge) {
                        badge.style.transform = '';
                        badge.style.opacity = '';
                        badge.style.visibility = '';
                    }
                    activityPanel.style.bottom = '';
                    activityPanel.style.top = '';
                    // Fast snap-back from stretched height to normal
                    header.classList.remove('dragging');
                    wrapper.style.transition = 'height 0.15s ease-out';
                    wrapper.style.height = `${wrapperHeightRange}px`;
                    // After snap-back completes, clean up and apply final state
                    setTimeout(() => {
                        wrapper.style.height = '';
                        wrapper.style.transition = '';
                        if (isPinned) {
                            panel.unpin();
                        } else {
                            panel.pin();
                        }
                        vibrate();
                    }, 160);
                } else if (!this.toggled) {
                    if (isPinned && this.currentProgress < 0.3) {
                        // Dragged up far enough → unpin
                        this.toggled = true;
                        cleanup();
                        panel.unpin();
                        vibrate();
                    } else if (!isPinned && elapsed < 300 && this.dragStarted) {
                        // Quick swipe down → snap expand
                        cleanup();
                        panel.manualExpand();
                    } else {
                        snapToState();
                        panel.setLockUntil(Date.now() + 5000);
                    }
                }
                this.dispose();
            }),
            DocumentEvents.active.touchMove$.subscribe(e => {
                if (this.toggled) return;

                const coords = getCoords(e);
                if (!coords) return;

                const dy = coords.y - origin.y;

                if (isPinned) {
                    // Pinned: drag UP to unpin
                    if (dy < -5)
                        startDragging();
                    if (!this.dragStarted) return;
                    tryPreventDefaultForEvent(e);

                    const effectiveDy = startProgress * wrapperHeightRange + dy;
                    this.currentProgress = Math.max(0, Math.min(1, effectiveDy / wrapperHeightRange));
                    wrapper.style.height = `${wrapperHeightRange * this.currentProgress}px`;
                } else {
                    // Not pinned: drag DOWN to expand, then pin
                    if (dy > 5)
                        startDragging();
                    if (!this.dragStarted) return;
                    tryPreventDefaultForEvent(e);

                    const effectiveDy = startProgress * wrapperHeightRange + dy;
                    this.currentProgress = Math.max(0, Math.min(1, effectiveDy / wrapperHeightRange));

                    // Vibrate when panel reaches expanded position
                    if (this.currentProgress >= 1 && !this.detentVibroFired) {
                        this.detentVibroFired = true;
                        vibrate();
                    }
                    if (this.currentProgress < 0.9)
                        this.detentVibroFired = false;

                    // Beyond expanded: detent zone, then stretch downward
                    const rawExtra = Math.max(0, effectiveDy - wrapperHeightRange);
                    // Dead zone: first detentZone px of extra drag → panel stays put
                    const effectiveExtra = Math.max(0, rawExtra - detentZone);
                    const stretchHeight = Math.min(effectiveExtra, wrapperMaxHeight - wrapperHeightRange);
                    const currentHeight = wrapperHeightRange * this.currentProgress + stretchHeight;
                    wrapper.style.height = `${currentHeight}px`;

                    // During stretch: center panel vertically so content
                    // drifts down by half the extra height
                    if (stretchHeight > 0) {
                        activityPanel.style.bottom = 'auto';
                        activityPanel.style.top = `${stretchHeight / 2}px`;
                    } else {
                        activityPanel.style.bottom = '';
                        activityPanel.style.top = '';
                    }

                    const badgeProgress = Math.min(1, effectiveExtra / pinBadgeThreshold);
                    this.badgeVisible = badgeProgress >= 0.95;

                    // Vibrate when panel hits maximum stretch (bottom limit)
                    const maxStretch = wrapperMaxHeight - wrapperHeightRange;
                    if (stretchHeight >= maxStretch && !this.bottomVibroFired) {
                        this.bottomVibroFired = true;
                        vibrate();
                    }
                    if (stretchHeight < maxStretch * 0.9)
                        this.bottomVibroFired = false;

                    if (badge) {
                        badge.style.transform = `scale(${badgeProgress})`;
                        badge.style.opacity = `${badgeProgress}`;
                        badge.style.visibility = badgeProgress > 0.01 ? 'visible' : 'hidden';
                    }
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
