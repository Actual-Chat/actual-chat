import { fromEvent, Observable, takeUntil, filter } from 'rxjs';

export const INLINE_FULL_HEIGHT_REM = 12; // body.narrow min-h-48 max-h-48, .map-panel h-48
const FADE_START_REM = 3; // content starts fading below this height

export function getRemSize(): number {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

export function vibrate(): void {
    if ('vibrate' in navigator)
        navigator.vibrate(10);
}

export interface InlineDragSource {
    element: HTMLElement;
    accepts?: (e: TouchEvent) => boolean;
}

export interface InlineDragOptions {
    // Resized by the gesture; gets the `minimized` class when snapped shut
    panel: HTMLElement;
    handle: HTMLElement | null;
    dragSources: InlineDragSource[];
    // Blurred and faded as the panel shrinks; may be the panel itself
    getContent: () => HTMLElement | null;
    canStart: () => boolean;
    onDragStart?: (e: TouchEvent) => void;
    disposed$: Observable<void>;
}

// Swipe up minimizes the panel to zero height, swipe down restores it to
// INLINE_FULL_HEIGHT_REM; on release the height snaps to the nearest of the two.
export function attachInlineDrag(options: InlineDragOptions): void {
    const { panel, handle, disposed$ } = options;

    let startX = 0;
    let startY = 0;
    let startHeight = 0;
    let dragActive = false;
    let dragStarted = false;
    let rejected = false;
    let content: HTMLElement | null = null;

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
        handle?.classList.remove('dragging');
        panel.style.minHeight = '';
        panel.style.maxHeight = '';
        panel.style.transition = '';
    };

    const startDragFrom = (e: TouchEvent) => {
        options.onDragStart?.(e);
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        startHeight = panel.offsetHeight;
        dragActive = true;
        dragStarted = false;
        rejected = false;
    };

    for (const source of options.dragSources) {
        fromEvent<TouchEvent>(source.element, 'touchstart', { passive: true } as AddEventListenerOptions)
            .pipe(
                takeUntil(disposed$),
                filter(() => options.canStart()),
                filter(e => e.touches.length === 1),
                filter(e => source.accepts?.(e) ?? true),
            )
            .subscribe(startDragFrom);
    }

    fromEvent<TouchEvent>(document, 'touchmove', { passive: false } as AddEventListenerOptions)
        .pipe(
            takeUntil(disposed$),
            filter(() => dragActive && !rejected),
        )
        .subscribe(e => {
            if (e.touches.length !== 1)
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
                handle?.classList.add('dragging');
                content = options.getContent();
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
            takeUntil(disposed$),
            filter(() => dragActive && dragStarted),
        )
        .subscribe(() => {
            dragActive = false;

            const rem = getRemSize();
            const fullHeight = INLINE_FULL_HEIGHT_REM * rem;
            const currentHeight = panel.offsetHeight;
            const midPoint = fullHeight / 2;
            const targetHeight = currentHeight < midPoint ? 0 : fullHeight;
            const willMinimize = targetHeight === 0;

            // Snap visuals to target
            applyVisuals(targetHeight, rem);

            // Animate snap to target height
            panel.style.transition = 'min-height 0.15s ease-out, max-height 0.15s ease-out';
            void panel.offsetHeight;
            panel.style.minHeight = `${targetHeight}px`;
            panel.style.maxHeight = `${targetHeight}px`;

            setTimeout(() => {
                cleanupAll();
                if (willMinimize)
                    panel.classList.add('minimized');
                else
                    panel.classList.remove('minimized');
                vibrate();
            }, 160);
        });

    fromEvent<TouchEvent>(document, 'touchend')
        .pipe(
            takeUntil(disposed$),
            filter(() => dragActive && !dragStarted),
        )
        .subscribe(() => { dragActive = false; });

    fromEvent<TouchEvent>(document, 'touchcancel')
        .pipe(
            takeUntil(disposed$),
            filter(() => dragActive),
        )
        .subscribe(() => {
            dragActive = false;
            if (dragStarted)
                cleanupAll();
        });
}
