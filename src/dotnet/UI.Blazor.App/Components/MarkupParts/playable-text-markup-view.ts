import { fromEvent, Subject, takeUntil } from 'rxjs';
import { setTimeout, clearTimeout } from 'timerQueue';

const HOVER_DWELL_MS = 700;
const FLASH_BLINK_MS = 450;

interface NumberRange {
    start: number;
    end: number;
}

interface Word {
    value: string;
    textRange: NumberRange;
    timeRange: NumberRange;
}

interface Caret {
    node: Node;
    offset: number;
}

export class PlayableTextMarkupView {
    private blazorRef: DotNet.DotNetObject;
    private readonly element: HTMLElement;
    private readonly words: Word[] = [];
    private disposed$: Subject<void> = new Subject<void>();
    private readonly authorColorN: number;
    private hoverTimer: number | null = null;
    private lastHoverIndex = -1;
    private hoverEl: HTMLElement | null = null;
    private hoverRect: DOMRect | null = null;

    static create(blazorRef: DotNet.DotNetObject, element: HTMLElement, words: Word[]): PlayableTextMarkupView {
        return new PlayableTextMarkupView(blazorRef, element, words);
    }

    constructor(blazorRef: DotNet.DotNetObject, element: HTMLElement, words: Word[]) {
        this.blazorRef = blazorRef;
        this.element = element;
        this.authorColorN = this.readAuthorColorN(element);
        this.words = words.map(w => ({
            value: w.value,
            textRange: { start: w.textRange.start, end: w.textRange.end },
            timeRange: { start: w.timeRange.start, end: w.timeRange.end },
        }));

        fromEvent(this.element, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: Event) => this.onClick(e));
        fromEvent(this.element, 'pointermove')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: Event) => this.onPointerMove(e as PointerEvent));
        fromEvent(this.element, 'pointerleave')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.resetHover());
        fromEvent(window, 'scroll', { capture: true, passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.resetHover());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.resetHover();
        this.disposed$.next();
        this.disposed$.complete();
    }

    private onClick = (e: Event) => {
        if (this.element.classList.contains('play-disabled'))
            return;
        if (this.endsTextSelection())
            return;

        // A coach-marked word carries its own hint menu inside this view: the tap opens the hint, not the
        // replay. A data-menu on an ancestor (the message's own context menu) must not block word clicks.
        const menuEl = (e.target as HTMLElement).closest('[data-menu]');
        if (menuEl && this.element.contains(menuEl))
            return;

        this.resetHover();
        const me = e as MouseEvent;
        const index = this.wordIndexAtPoint(me.clientX, me.clientY);
        this.onWordClick(index);
    }

    // Selecting text with the mouse ends with a click, which must not start the replay.
    // mousedown collapses any earlier selection, so a live range here belongs to this very click.
    private endsTextSelection(): boolean {
        const selection = getSelection();
        if (!selection || selection.isCollapsed || selection.rangeCount === 0)
            return false;

        return selection.getRangeAt(0).intersectsNode(this.element);
    }

    private onWordClick(index: number) {
        if (index >= 0)
            this.flashWord(index, this.playingColor(), 'playable-word-flash');

        const textRange: NumberRange = index >= 0 ? this.words[index].textRange : { start: 0, end: 0 };
        void this.blazorRef.invokeMethodAsync('OnMarkupClick', textRange);
    }

    private onPointerMove = (e: PointerEvent) => {
        if (e.pointerType !== 'mouse' || !document.body.classList.contains('hoverable'))
            return;
        if (this.element.classList.contains('play-disabled'))
            return;

        const x = e.clientX, y = e.clientY;
        // Once the hint is showing it tracks the cursor to the next word at once; until then it waits
        // out the dwell, so a cursor merely passing over the text doesn't light words up.
        if (this.hoverEl != null) {
            this.updateHover(x, y);
            return;
        }
        if (this.hoverTimer != null)
            clearTimeout(this.hoverTimer);
        this.hoverTimer = setTimeout(() => this.updateHover(x, y), HOVER_DWELL_MS);
    }

    private updateHover(x: number, y: number) {
        this.hoverTimer = null;
        // While the cursor stays over the already-lit word, skip the caret lookup (a layout flush) that
        // every pointermove would otherwise trigger.
        if (this.hoverEl != null && this.hoverRect != null
            && x >= this.hoverRect.left && x <= this.hoverRect.right
            && y >= this.hoverRect.top && y <= this.hoverRect.bottom)
            return;

        const index = this.wordIndexAtPoint(x, y);
        if (index < 0) {
            this.resetHover();
            return;
        }
        if (index === this.lastHoverIndex && this.hoverEl != null)
            return;

        const rect = this.wordRect(index);
        if (!rect) {
            this.resetHover();
            return;
        }

        this.lastHoverIndex = index;
        this.hoverRect = rect;
        if (this.hoverEl == null) {
            this.hoverEl = this.makeOverlay(rect, 'playable-word-hover');
            this.hoverEl.style.background = this.hoverColor();
            document.body.appendChild(this.hoverEl);
        } else
            this.positionOverlay(this.hoverEl, rect);
    }

    private resetHover() {
        if (this.hoverTimer != null) {
            clearTimeout(this.hoverTimer);
            this.hoverTimer = null;
        }
        if (this.hoverEl != null) {
            this.hoverEl.remove();
            this.hoverEl = null;
        }
        this.hoverRect = null;
        this.lastHoverIndex = -1;
    }

    private flashWord(index: number, color: string, className: string) {
        const rect = this.wordRect(index);
        if (!rect)
            return;

        const el = this.makeOverlay(rect, className);
        el.style.background = color;
        document.body.appendChild(el);
        setTimeout(() => el.remove(), FLASH_BLINK_MS + 50);
    }

    private hoverColor(): string {
        return `color-mix(in srgb, var(--author-color-${this.authorColorN}) 32%, transparent)`;
    }

    private playingColor(): string {
        return `color-mix(in srgb, var(--author-color-${this.authorColorN}) 40%, transparent)`;
    }

    private readAuthorColorN(element: HTMLElement): number {
        const match = /playable-text-color-(\d+)/.exec(element.className);
        return match ? Number(match[1]) : 1;
    }

    private wordIndexAtPoint(x: number, y: number): number {
        const caret = this.caretAt(x, y);
        if (!caret || caret.node.nodeType !== Node.TEXT_NODE)
            return -1;

        const offset = this.globalOffset(caret.node, caret.offset);
        return this.wordIndexAt(offset);
    }

    private caretAt(x: number, y: number): Caret | null {
        const doc = document as unknown as {
            caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null;
            caretRangeFromPoint?: (x: number, y: number) => Range | null;
        };
        if (doc.caretPositionFromPoint) {
            const p = doc.caretPositionFromPoint(x, y);
            return p && this.element.contains(p.offsetNode) ? { node: p.offsetNode, offset: p.offset } : null;
        }
        if (doc.caretRangeFromPoint) {
            const r = doc.caretRangeFromPoint(x, y);
            return r && this.element.contains(r.startContainer) ? { node: r.startContainer, offset: r.startOffset } : null;
        }
        return null;
    }

    // A run span holds a slice of the text starting at its data-c0; a caret offset inside that node is
    // relative to the slice, so the global text offset is data-c0 + offset. Plain text (no active
    // playback) has no run span, and the caret offset is already global.
    private globalOffset(node: Node, offset: number): number {
        const el = node.nodeType === Node.TEXT_NODE ? node.parentElement : node as HTMLElement;
        const runSpan = el?.closest('[data-c0]') as HTMLElement | null;
        const c0 = runSpan ? Number(runSpan.dataset.c0) : 0;
        return c0 + offset;
    }

    private wordIndexAt(offset: number): number {
        for (let i = 0; i < this.words.length; i++) {
            const range = this.words[i].textRange;
            if (offset >= range.start && offset < range.end)
                return i;
        }
        const last = this.words.length - 1;
        return last >= 0 && offset >= this.words[last].textRange.start ? last : -1;
    }

    private wordRect(index: number): DOMRect | null {
        const word = this.words[index];
        const runSpan = this.runSpanForOffset(word.textRange.start);
        const c0 = runSpan ? Number(runSpan.dataset.c0) : 0;
        const textNode = this.firstText(runSpan ?? this.element);
        if (!textNode)
            return null;

        const length = textNode.textContent.length;
        const localStart = word.textRange.start - c0;
        if (localStart < 0 || localStart >= length)
            return null;

        const visibleLength = word.value.replace(/[\s\u200B]+$/, '').length;
        const localEnd = Math.min(localStart + visibleLength, length);
        const range = document.createRange();
        range.setStart(textNode, localStart);
        range.setEnd(textNode, Math.max(localStart, localEnd));
        return range.getBoundingClientRect();
    }

    private runSpanForOffset(offset: number): HTMLElement | null {
        const spans = this.element.querySelectorAll<HTMLElement>('[data-c0]');
        for (const span of Array.from(spans)) {
            const c0 = Number(span.dataset.c0);
            const length = span.textContent.length;
            if (offset >= c0 && offset < c0 + length)
                return span;
        }
        return null;
    }

    private firstText(node: Node): Text | null {
        if (node.nodeType === Node.TEXT_NODE)
            return node as Text;
        for (const child of Array.from(node.childNodes)) {
            const text = this.firstText(child);
            if (text)
                return text;
        }
        return null;
    }

    private makeOverlay(rect: DOMRect, className: string): HTMLElement {
        const el = document.createElement('span');
        el.className = className;
        el.style.position = 'absolute';
        this.positionOverlay(el, rect);
        return el;
    }

    private positionOverlay(el: HTMLElement, rect: DOMRect) {
        Object.assign(el.style, {
            top: `${rect.top + window.scrollY - 2}px`,
            left: `${rect.left + window.scrollX - 5}px`,
            width: `${rect.width + 10}px`,
            height: `${rect.height + 4}px`,
        });
    }
}
