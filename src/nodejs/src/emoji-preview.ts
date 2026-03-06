import { DocumentEvents } from 'event-handling';
import { Timeout } from 'timeout';
import { Log } from 'logging';
import { fromEvent } from 'rxjs';
import { fastRaf } from 'fast-raf';
import { Tune, TuneUI } from '../../dotnet/UI.Blazor/Services/TuneUI/tune-ui';

const { debugLog } = Log.get('EmojiPreview');

const LONG_PRESS_DELAY_MS = 300;
const CONTAINER_SIZE_REM = 12;
const TOUCH_OFFSET_REM = 5;
const TOUCH_MOVE_TOLERANCE_PX = 10;

export class EmojiPreview {
    private static overlay: HTMLElement | null = null;
    private static currentTarget: HTMLElement | null = null;
    private static longPressTimeout: Timeout | null = null;
    private static isTouch = false;
    private static touchOnEmoji = false;
    private static longPressTriggered = false;
    private static touchStartX = 0;
    private static touchStartY = 0;

    public static init(): void {
        debugLog?.log('EmojiPreview.init');

        // Desktop: hover using pointerover/pointerout (they bubble unlike pointerenter/pointerleave)
        DocumentEvents.passive.pointerOver$.subscribe((e: PointerEvent) => {
            if (e.pointerType === 'touch')
                return;
            this.tryShowPreview(e.target as HTMLElement);
        });

        // Use fromEvent for pointerout since it's not in DocumentEventSet
        fromEvent<PointerEvent>(document, 'pointerout', { passive: true }).subscribe((e: PointerEvent) => {
            if (e.pointerType === 'touch')
                return;

            const target = e.target as HTMLElement;
            const relatedTarget = e.relatedTarget as HTMLElement | null;

            // Check if we're leaving an emoji element
            const emojiEl = this.findEmojiElement(target);
            if (!emojiEl)
                return;

            // Check if we're moving to a child of the same emoji element
            if (relatedTarget && emojiEl.contains(relatedTarget))
                return;

            // Check if relatedTarget is also an emoji (we're just moving between emojis)
            const newEmojiEl = relatedTarget ? this.findEmojiElement(relatedTarget) : null;
            if (newEmojiEl === emojiEl)
                return;

            this.hidePreview();
        });

        // Hide preview on any click (menu might close)
        DocumentEvents.passive.click$.subscribe(() => {
            this.hidePreview();
        });

        // Intercept click and contextmenu to prevent menu from closing during long press
        const interceptEvent = (e: Event) => {
            // Block contextmenu while touch is active on emoji (even before timer fires)
            // Block click only after long press actually triggered
            const shouldBlock = e.type === 'contextmenu'
                ? (this.longPressTriggered || (this.isTouch && this.touchOnEmoji))
                : this.longPressTriggered;
            if (shouldBlock) {
                e.stopPropagation();
                e.preventDefault();
                if (e.type === 'click')
                    this.longPressTriggered = false;
            }
        };
        fromEvent(document, 'contextmenu', { capture: true }).subscribe(interceptEvent);
        fromEvent(document, 'click', { capture: true }).subscribe(interceptEvent);

        // Touch: long press
        DocumentEvents.passive.pointerDown$.subscribe((e: PointerEvent) => {
            if (e.pointerType !== 'touch')
                return;

            this.isTouch = true;
            const target = e.target as HTMLElement;
            const emojiEl = this.findEmojiElement(target);
            if (!emojiEl) {
                this.touchOnEmoji = false;
                return;
            }

            this.touchOnEmoji = true;
            this.touchStartX = e.clientX;
            this.touchStartY = e.clientY;
            this.longPressTimeout?.dispose();
            this.longPressTimeout = new Timeout(LONG_PRESS_DELAY_MS, () => {
                this.longPressTriggered = true;
                TuneUI.play(Tune.ClickButton);
                this.showPreview(emojiEl, true);
            });
        });

        DocumentEvents.passive.pointerUp$.subscribe(() => {
            if (this.isTouch) {
                this.longPressTimeout?.dispose();
                this.longPressTimeout = null;
                this.hidePreview();
                this.isTouch = false;
                this.touchOnEmoji = false;
                // Safety reset: browser may not fire click after long press,
                // leaving longPressTriggered=true and eating the next real tap
                if (this.longPressTriggered)
                    setTimeout(() => { this.longPressTriggered = false; }, 300);
            }
        });

        DocumentEvents.passive.pointerCancel$.subscribe(() => {
            if (this.isTouch) {
                this.longPressTimeout?.dispose();
                this.longPressTimeout = null;
                this.hidePreview();
                this.isTouch = false;
                this.touchOnEmoji = false;
            }
        });

        DocumentEvents.passive.pointerMove$.subscribe((e: PointerEvent) => {
            if (this.isTouch && this.longPressTimeout) {
                const dx = e.clientX - this.touchStartX;
                const dy = e.clientY - this.touchStartY;
                if (dx * dx + dy * dy > TOUCH_MOVE_TOLERANCE_PX * TOUCH_MOVE_TOLERANCE_PX) {
                    this.longPressTimeout.dispose();
                    this.longPressTimeout = null;
                }
            }
        });

        // Watch for reaction animation triggers
        this.initReactionAnimations();

        // Prefetch emoji SVGs after critical resources are loaded
        this.prefetchEmojis();
    }

    private static prefetchEmojis(): void {
        const meta = document.querySelector<HTMLMetaElement>('meta[name="emoji-svg-names"]');
        if (!meta?.content)
            return;

        const svgNames = meta.content.split(',');
        const basePath = '/dist/images/emoji/';

        const ignoreFetchError = (_: unknown) => { /* expected */ };
        const prefetchBatch = (urls: string[]): Promise<unknown[]> =>
            Promise.all(urls.map(url => fetch(url, { priority: 'low' } as RequestInit).catch(ignoreFetchError)));

        // After page load: prefetch static, then animated
        window.addEventListener('load', () => {
            const staticUrls = svgNames.map(n => `${basePath}${n}.svg`);
            const animatedUrls = svgNames.map(n => `${basePath}${n}-animated.svg`);
            void prefetchBatch(staticUrls).then(() => prefetchBatch(animatedUrls));
        }, { once: true });
    }

    private static initReactionAnimations(): void {
        const observer = new MutationObserver((mutations) => {
            for (const mutation of mutations) {
                if (mutation.type === 'childList') {
                    for (const node of mutation.addedNodes) {
                        if (node instanceof HTMLElement)
                            this.processAnimateElements(node);
                    }
                }
                if (mutation.type === 'attributes'
                    && mutation.attributeName === 'data-emoji-animate'
                    && mutation.target instanceof HTMLElement
                    && mutation.target.dataset.emojiAnimate)
                    this.animateReaction(mutation.target);
            }
        });
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['data-emoji-animate'],
        });
    }

    private static processAnimateElements(root: HTMLElement): void {
        if (root.dataset.emojiAnimate)
            this.animateReaction(root);
        root.querySelectorAll<HTMLElement>('[data-emoji-animate]')
            .forEach(el => this.animateReaction(el));
    }

    private static animateReaction(el: HTMLElement): void {
        const svgName = el.dataset.emojiAnimate;
        if (!svgName)
            return;

        el.removeAttribute('data-emoji-animate');

        // Double-RAF: first frame lets layout settle (new reaction row, message height change),
        // second frame lets scroll adjustments complete (chat auto-scroll).
        // This ensures getBoundingClientRect returns the final viewport position.
        let centerX = 0, centerY = 0, containerSizePx = 0;
        let clampedLeft = 0, clampedTop = 0;
        let offsetX = 0, offsetY = 0;

        requestAnimationFrame(() => fastRaf({
            read: () => {
                const remToPx = parseFloat(getComputedStyle(document.documentElement).fontSize);
                containerSizePx = CONTAINER_SIZE_REM * remToPx;

                const rect = el.getBoundingClientRect();
                centerX = rect.left + rect.width / 2;
                centerY = rect.top + rect.height / 2;

                const left = centerX - containerSizePx / 2;
                let top = centerY - containerSizePx / 2;

                // Clamp above the message editor so animation doesn't appear behind it
                const editor = document.querySelector('.chat-message-editor');
                const maxBottom = editor
                    ? editor.getBoundingClientRect().top
                    : window.innerHeight;
                top = Math.min(top, maxBottom - containerSizePx);

                const margin = 8;
                clampedLeft = Math.max(margin, Math.min(left, window.innerWidth - containerSizePx - margin));
                clampedTop = Math.max(margin, Math.min(top, window.innerHeight - containerSizePx - margin));
                offsetX = left - clampedLeft;
                offsetY = top - clampedTop;
            },
            write: () => {
                const overlay = document.createElement('div');
                overlay.className = 'emoji-reaction-animate';

                const img = document.createElement('img');
                img.src = `/dist/images/emoji/${svgName}-animated.svg`;
                img.alt = '';
                img.className = 'emoji-preview-image';
                overlay.appendChild(img);

                overlay.style.left = `${clampedLeft}px`;
                overlay.style.top = `${clampedTop}px`;
                overlay.style.transformOrigin = `${centerX - clampedLeft}px ${centerY - clampedTop}px`;

                if (offsetX !== 0 || offsetY !== 0) {
                    img.style.transform = `translate(${offsetX}px, ${offsetY}px)`;
                    overlay.style.setProperty('--offset-x', `${offsetX}px`);
                    overlay.style.setProperty('--offset-y', `${offsetY}px`);
                }

                document.body.appendChild(overlay);
                overlay.addEventListener('animationend', () => overlay.remove());

                debugLog?.log('animateReaction:', svgName);
            },
        }));
    }

    private static tryShowPreview(target: HTMLElement): void {
        const emojiEl = this.findEmojiElement(target);
        if (emojiEl) {
            this.showPreview(emojiEl);
        }
    }

    private static findEmojiElement(target: HTMLElement | null): HTMLElement | null {
        // Look for an element with data-emoji-preview attribute
        let el: HTMLElement | null = target;
        while (el) {
            if (el.dataset.emojiPreview) {
                return el;
            }
            el = el.parentElement;
        }
        return null;
    }

    private static showPreview(emojiEl: HTMLElement, isTouch = false): void {
        const svgName = emojiEl.dataset.emojiPreview;
        if (!svgName)
            return;

        // Don't re-show for same element
        if (this.currentTarget === emojiEl)
            return;

        this.hidePreview();
        this.currentTarget = emojiEl;

        // Get rem to px conversion
        const remToPx = parseFloat(getComputedStyle(document.documentElement).fontSize);
        const containerSizePx = CONTAINER_SIZE_REM * remToPx;

        // Create overlay
        const overlay = document.createElement('div');
        overlay.className = 'emoji-preview-overlay';

        const img = document.createElement('img');
        img.src = `/dist/images/emoji/${svgName}-animated.svg`;
        img.alt = '';
        img.className = 'emoji-preview-image';
        overlay.appendChild(img);

        // Position overlay centered above the emoji
        const rect = emojiEl.getBoundingClientRect();
        const centerX = rect.left + rect.width / 2;
        const centerY = rect.top + rect.height / 2;
        const touchOffsetPx = isTouch ? TOUCH_OFFSET_REM * remToPx : 0;

        // Position so the preview is centered on the emoji (shifted up for touch)
        const left = centerX - containerSizePx / 2;
        const top = centerY - containerSizePx / 2 - touchOffsetPx;

        // Keep within viewport, track how much we had to shift
        const margin = 8;
        const clampedLeft = Math.max(margin, Math.min(left, window.innerWidth - containerSizePx - margin));
        const clampedTop = Math.max(margin, Math.min(top, window.innerHeight - containerSizePx - margin));

        overlay.style.left = `${clampedLeft}px`;
        overlay.style.top = `${clampedTop}px`;

        // Compensate image and background position so they stay aligned with the emoji
        const offsetX = left - clampedLeft;
        const offsetY = top - clampedTop;
        if (offsetX !== 0 || offsetY !== 0) {
            img.style.transform = `translate(${offsetX}px, ${offsetY}px)`;
            overlay.style.setProperty('--offset-x', `${offsetX}px`);
            overlay.style.setProperty('--offset-y', `${offsetY}px`);
        }

        document.body.appendChild(overlay);
        this.overlay = overlay;

        // Trigger animation
        requestAnimationFrame(() => {
            overlay.classList.add('emoji-preview-visible');
        });

        debugLog?.log('showPreview:', svgName);
    }

    private static hidePreview(): void {
        if (this.overlay) {
            this.overlay.remove();
            this.overlay = null;
            debugLog?.log('hidePreview');
        }
        this.currentTarget = null;
    }
}
