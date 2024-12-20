import { fromEvent, Subject, takeUntil } from 'rxjs';

class NumberRange {
    start: number;
    end: number;

    constructor(
        start: number,
        end: number) {
        this.start = start;
        this.end = end;
    }
}

class Word {
    value: string;
    textRange: NumberRange;
    timeRange: NumberRange;

    constructor(
        value: string,
        textRange: NumberRange,
        timeRange: NumberRange) {
        this.value = value;
        this.textRange = textRange;
        this.timeRange = timeRange;
    }
}

export class PlayableTextMarkupView {
    private blazorRef: DotNet.DotNetObject;
    private readonly playableText: HTMLElement;
    private words: Word[] = [];
    private disposed$: Subject<void> = new Subject<void>();

    static create(blazorRef: DotNet.DotNetObject, playableText: HTMLElement, words: Word[]): PlayableTextMarkupView {
        return new PlayableTextMarkupView(blazorRef, playableText, words);
    }

    constructor(blazorRef: DotNet.DotNetObject,
                playableText: HTMLElement,
                words: Word[]) {
        this.blazorRef = blazorRef;
        this.playableText = playableText;

        words.forEach(word => {
            let wordValue = word.value;
            let wordTextRange = new NumberRange(word.textRange.start, word.textRange.end);
            let wordTimeRange = new NumberRange(word.timeRange.start, word.timeRange.end);
            let wordItem = new Word(wordValue, wordTextRange, wordTimeRange);
            this.words.push(wordItem);
        });

        if (this.playableText == null)
            return;

        fromEvent(this.playableText, 'pointerup')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onClickHandler(event));
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onClickHandler = (e: PointerEvent) => {
        let childNodes = (e.target as Node).childNodes;
        if (!childNodes)
            return;

        let clicked: Word = this.findClickedWord(childNodes, e.clientX, e.clientY);
        if (clicked)
            void this.blazorRef.invokeMethodAsync("OnMarkupClick", clicked.textRange);
    }

    private findClickedWord(childNodes: NodeListOf<Node>, x: number, y: number) {
        const parentNode = this.playableText as Node;
        for (let i = 0; i < this.words.length; i++) {
            const range = document.createRange();
            let currentNode = childNodes[i];
            if (currentNode.nodeName !== '#text')
                return null;

            range.setStart(parentNode, 0);
            range.setEnd(parentNode, childNodes.length);
            let rects = range.getClientRects();
            let clickedRectIndex = isClickInRects(rects);
            if (clickedRectIndex != null)
                return this.words[clickedRectIndex];
        }

        function isClickInRects(rects: DOMRectList) {
            for (let i = 0; i < rects.length; i++) {
                let r = rects[i]
                if (r.left < x && r.right > x && r.top < y && r.bottom > y)
                    return i;
            }
            return null;
        }
        return null;
    }
}

