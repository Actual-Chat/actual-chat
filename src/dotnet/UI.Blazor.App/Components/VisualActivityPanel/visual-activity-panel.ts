import { fromEvent, Subject, takeUntil, filter } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { CompactLayout } from 'compact-layout';

const INLINE_FULL_HEIGHT_REM = 12; // body.narrow .video-panel min/max-h-48, .map-panel h-48
const FADE_START_REM = 3; // content starts fading below this height

function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function vibrate(): void {
    if ('vibrate' in navigator)
        navigator.vibrate(10);
}

// Owns the panel's drag handle: swipe up minimizes the active inline panel
// (call or map) to zero height, swipe down restores it; on release the height
// snaps to the nearest of the two. The video panel's other modes
// (expanded/island/pill) and the map's own pan gestures stay with the children.
export class VisualActivityPanel {
    private readonly root: HTMLElement;
    private readonly disposed$ = new Subject<void>();

    static create(root: HTMLElement): VisualActivityPanel {
        return new VisualActivityPanel(root);
    }

    constructor(root: HTMLElement) {
        this.root = root;
        this.initInlineDrag();
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    // Private methods

    private initInlineDrag(): void {
        // Resolved per drag: Blazor re-creates the handle on panel-mode changes
        const getHandle = () => this.root.querySelector<HTMLElement>('.c-drag-handle');

        let panel: HTMLElement | null = null;
        let content: HTMLElement | null = null;
        let startX = 0;
        let startY = 0;
        let startHeight = 0;
        let dragActive = false;
        let dragStarted = false;
        let rejected = false;

        const applyVisuals = (height: number, rem: number) => {
            if (!content)
                return;

            const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
            const progress = height / fullHeight; // 1=full, 0=collapsed

            // Blur increases as panel shrinks
            const blur = (1 - progress) * 16;
            content.style.filter = blur > 0.5 ? `blur(${blur}px)` : '';

            // Fade out content in the last stretch
            const fadeStart = FADE_START_REM * rem;
            if (height < fadeStart) {
                const t = 1 - height / fadeStart; // 0→1
                content.style.opacity = `${1 - t}`;
            } else {
                content.style.opacity = '';
            }
        };

        const cleanupAll = () => {
            if (content) {
                content.style.filter = '';
                content.style.opacity = '';
            }
            content = null;
            getHandle()?.classList.remove('dragging');
            if (panel) {
                panel.style.minHeight = '';
                panel.style.maxHeight = '';
                panel.style.transition = '';
            }
            panel = null;
        };

        fromEvent<TouchEvent>(this.root, 'touchstart', { passive: true } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => !ScreenSize.isWide()),
                filter(e => e.touches.length === 1),
            )
            .subscribe(e => {
                const target = this.resolveDragTarget(e);
                if (!target)
                    return;

                // Keyboard up + drag → close keyboard in parallel with the drag.
                // The viewport meta sets `interactive-widget=resizes-content`, so the
                // layout viewport grows as the keyboard hides; the panel expands into
                // that growing area without pushing the editor offscreen.
                if (CompactLayout.reasons.has('keyboard')) {
                    const active = document.activeElement as HTMLElement | null;
                    const isEditable = active
                        && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.isContentEditable);
                    if (isEditable)
                        active.blur();
                }

                panel = target;
                startX = e.touches[0].clientX;
                startY = e.touches[0].clientY;
                startHeight = panel.offsetHeight;
                dragActive = true;
                dragStarted = false;
                rejected = false;
            });

        fromEvent<TouchEvent>(document, 'touchmove', { passive: false } as AddEventListenerOptions)
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && !rejected),
            )
            .subscribe(e => {
                if (e.touches.length !== 1 || !panel)
                    return;

                const dy = e.touches[0].clientY - startY;
                const dx = e.touches[0].clientX - startX;
                if (!dragStarted) {
                    const absDy = Math.abs(dy);
                    const absDx = Math.abs(dx);
                    if (absDy < 8 && absDx < 8)
                        return;

                    if (absDx > absDy) {
                        rejected = true;
                        return;
                    }

                    dragStarted = true;
                    panel.classList.remove('minimized');
                    getHandle()?.classList.add('dragging');
                    content = this.getContent(panel);
                    const currentH = panel.offsetHeight;
                    panel.style.minHeight = `${currentH}px`;
                    panel.style.maxHeight = `${currentH}px`;
                    panel.style.transition = 'none';
                }

                e.preventDefault();

                const rem = getRemSize();
                const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
                const newHeight = Math.max(0, Math.min(fullHeight, startHeight + dy));
                panel.style.minHeight = `${newHeight}px`;
                panel.style.maxHeight = `${newHeight}px`;
                applyVisuals(newHeight, rem);
            });

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && dragStarted),
            )
            .subscribe(() => {
                dragActive = false;
                if (!panel)
                    return;

                const rem = getRemSize();
                const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
                const currentHeight = panel.offsetHeight;
                const midPoint = fullHeight / 2;
                const targetHeight = currentHeight < midPoint ? 0 : fullHeight;
                const willMinimize = targetHeight === 0;

                applyVisuals(targetHeight, rem);
                panel.style.transition = 'min-height 0.15s ease-out, max-height 0.15s ease-out';
                void panel.offsetHeight;
                panel.style.minHeight = `${targetHeight}px`;
                panel.style.maxHeight = `${targetHeight}px`;

                setTimeout(() => {
                    const snappedPanel = panel;
                    cleanupAll();
                    if (!snappedPanel)
                        return;

                    if (willMinimize)
                        snappedPanel.classList.add('minimized');
                    else
                        snappedPanel.classList.remove('minimized');
                    vibrate();
                }, 160);
            });

        fromEvent<TouchEvent>(document, 'touchend')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive && !dragStarted),
            )
            .subscribe(() => {
                dragActive = false;
                panel = null;
            });

        fromEvent<TouchEvent>(document, 'touchcancel')
            .pipe(
                takeUntil(this.disposed$),
                filter(() => dragActive),
            )
            .subscribe(() => {
                dragActive = false;
                if (dragStarted)
                    cleanupAll();
                else
                    panel = null;
            });
    }

    // Resolves which panel a touch may drag-resize: the handle drags the
    // active inline panel; the video panel body works too (except buttons);
    // touches on the map pan the map, so they never start a drag.
    private resolveDragTarget(e: TouchEvent): HTMLElement | null {
        const target = e.target as HTMLElement;
        if (target.closest('.c-drag-handle'))
            return this.getInlinePanel();

        const video = target.closest<HTMLElement>('.video-panel');
        if (video && !target.closest('button, .btn-h') && this.isInline(video))
            return video;

        return null;
    }

    private getInlinePanel(): HTMLElement | null {
        const video = this.root.querySelector<HTMLElement>(':scope > .video-panel');
        if (video)
            return this.isInline(video) ? video : null;

        return this.root.querySelector<HTMLElement>('.map-panel');
    }

    private isInline(videoPanel: HTMLElement): boolean {
        return !videoPanel.classList.contains('expanded')
            && !videoPanel.classList.contains('collapsed')
            && !videoPanel.classList.contains('panel-hidden');
    }

    private getContent(panel: HTMLElement): HTMLElement | null {
        return panel.classList.contains('video-panel')
            ? panel.querySelector<HTMLElement>('.c-container')
            : panel;
    }
}
