import { DocumentEvents } from 'event-handling';
import { Timeout } from 'timeout';
import { Log } from 'logging';
import { fromEvent } from 'rxjs';

const { debugLog } = Log.get('EmojiPreview');

const LONG_PRESS_DELAY_MS = 300;
const CONTAINER_SIZE_REM = 12;

export class EmojiPreview {
    private static overlay: HTMLElement | null = null;
    private static currentTarget: HTMLElement | null = null;
    private static longPressTimeout: Timeout | null = null;
    private static isTouch = false;

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

        // Touch: long press
        DocumentEvents.passive.pointerDown$.subscribe((e: PointerEvent) => {
            if (e.pointerType !== 'touch')
                return;

            this.isTouch = true;
            const target = e.target as HTMLElement;
            const emojiEl = this.findEmojiElement(target);
            if (!emojiEl)
                return;

            this.longPressTimeout?.dispose();
            this.longPressTimeout = new Timeout(LONG_PRESS_DELAY_MS, () => {
                this.showPreview(emojiEl);
            });
        });

        DocumentEvents.passive.pointerUp$.subscribe(() => {
            if (this.isTouch) {
                this.longPressTimeout?.dispose();
                this.longPressTimeout = null;
                this.hidePreview();
                this.isTouch = false;
            }
        });

        DocumentEvents.passive.pointerCancel$.subscribe(() => {
            if (this.isTouch) {
                this.longPressTimeout?.dispose();
                this.longPressTimeout = null;
                this.hidePreview();
                this.isTouch = false;
            }
        });

        DocumentEvents.passive.pointerMove$.subscribe(() => {
            if (this.isTouch && this.longPressTimeout) {
                this.longPressTimeout.dispose();
                this.longPressTimeout = null;
            }
        });
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
            if (el.dataset?.emojiPreview) {
                return el;
            }
            el = el.parentElement;
        }
        return null;
    }

    private static showPreview(emojiEl: HTMLElement): void {
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

        // Position so the preview is centered on the emoji
        let left = centerX - containerSizePx / 2;
        let top = centerY - containerSizePx / 2;

        // Keep within viewport
        const margin = 8;
        left = Math.max(margin, Math.min(left, window.innerWidth - containerSizePx - margin));
        top = Math.max(margin, Math.min(top, window.innerHeight - containerSizePx - margin));

        overlay.style.left = `${left}px`;
        overlay.style.top = `${top}px`;

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
