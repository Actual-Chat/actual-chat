import { fromEvent, Subject, takeUntil } from 'rxjs';
import { setTimeout } from 'timerQueue';

const HIGHLIGHT_CLICKED_WORD = true;

interface NumberRange {
    start: number;
    end: number;
}

interface Word {
    value: string;
    textRange: NumberRange;
    timeRange: NumberRange;
}

export class PlayableTextMarkupView {
    private blazorRef: DotNet.DotNetObject;
    private readonly element: HTMLElement;
    private readonly words: Word[] = [];
    private disposed$: Subject<void> = new Subject<void>();

    static create(blazorRef: DotNet.DotNetObject, element: HTMLElement, words: Word[]): PlayableTextMarkupView {
        return new PlayableTextMarkupView(blazorRef, element, words);
    }

    constructor(blazorRef: DotNet.DotNetObject, element: HTMLElement, words: Word[]) {
        this.blazorRef = blazorRef;
        this.element = element;
        this.words = words.map(w => ({
            value: w.value,
            textRange: { start: w.textRange.start, end: w.textRange.end },
            timeRange: { start: w.timeRange.start, end: w.timeRange.end },
        }));

        fromEvent(this.element, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: Event) => this.onClick(e));
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onClick = (e: Event) => {
        if (this.element.classList.contains('play-disabled'))
            return;
        if (this.endsTextSelection())
            return;

        const target = e.target as HTMLElement;
        const word = this.findWord(target);
        this.onWordClick(word);
    }

    // Selecting text with the mouse ends with a click, which must not start the replay.
    // mousedown collapses any earlier selection, so a live range here belongs to this very click.
    private endsTextSelection(): boolean {
        const selection = getSelection();
        if (!selection || selection.isCollapsed || selection.rangeCount === 0)
            return false;

        return selection.getRangeAt(0).intersectsNode(this.element);
    }

    private onWordClick(word: Word | null) {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (HIGHLIGHT_CLICKED_WORD && word)
            this.highlightWord(word);

        const textRange: NumberRange = word?.textRange ?? { start: 0, end: 0 };
        void this.blazorRef.invokeMethodAsync('OnMarkupClick', textRange);
    }

    private findWord(target: HTMLElement): Word | null {
        // When words are rendered as individual .playable-word spans
        const wordSpan = target.closest('.playable-word')
            ?? (target.classList.contains('playable-word') ? target : null);
        if (wordSpan) {
            const wordSpans = this.element.querySelectorAll('.playable-word');
            const index = Array.prototype.indexOf.call(wordSpans, wordSpan) as number;
            if (index >= 0 && index < this.words.length)
                return this.words[index];
        }

        // Fallback: use selection offset for plain text nodes
        const selection = getSelection();
        if (selection?.rangeCount && selection.focusNode?.nodeType === Node.TEXT_NODE) {
            const offset = selection.focusOffset;
            return this.words.find(w => w.textRange.start <= offset && w.textRange.end >= offset) ?? null;
        }

        return null;
    }

    private highlightWord(word: Word) {
        const wordSpans = this.element.querySelectorAll('.playable-word');
        const index = this.words.indexOf(word);
        const node = index >= 0 ? wordSpans[index] : null;
        const rect = node?.getBoundingClientRect();
        if (!rect)
            return;

        const floatSpan = document.createElement('span');
        floatSpan.textContent = word.value;
        floatSpan.className = 'selected-word-float';
        Object.assign(floatSpan.style, {
            position: 'absolute',
            top: `${rect.top + window.scrollY + 4}px`,
            left: `${rect.left + window.scrollX}px`,
            width: `${rect.width}px`,
            height: `${rect.height - 8}px`,
        });
        document.body.appendChild(floatSpan);
        setTimeout(() => floatSpan.remove(), 400);
    }
}
