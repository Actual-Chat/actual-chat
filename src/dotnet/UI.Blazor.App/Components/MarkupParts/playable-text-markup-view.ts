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

        fromEvent(this.playableText, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: Event) => this.onClickHandler(event));
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onClickHandler = (e: Event) => {
        if (this.playableText.childNodes.length > 1) {
            let selection = getSelection();
            if (selection.rangeCount) {
                const targetedNode = selection.focusNode;
                const targetNodeParent = targetedNode.parentNode;
                let wordIndex = Array.prototype.indexOf.call(this.playableText.childNodes, targetNodeParent);
                let word = this.words[wordIndex];
                void this.blazorRef.invokeMethodAsync("OnMarkupClick", word.textRange);
            }
        } else {
            let selection = getSelection();
            if (selection.rangeCount) {
                let word = this.words.find(w =>
                    w.textRange.start <= selection.focusOffset && w.textRange.end >= selection.focusOffset);
                void this.blazorRef.invokeMethodAsync("OnMarkupClick", word.textRange);
            }
        }
    }
}

