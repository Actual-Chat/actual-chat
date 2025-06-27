import { fromEvent, Subject, takeUntil } from 'rxjs';
import { setTimeout } from 'timerQueue';

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
        const isPlayDisabled = this.playableText.classList.contains('play-disabled');
        if (isPlayDisabled)
            return;

        const childNodes = this.playableText.childNodes;
        const selection = getSelection();
        if (!selection || !childNodes)
            return;

        if (childNodes.length > 2) {
            if (selection.rangeCount) {
                const targetedNode = selection.focusNode;
                if (!targetedNode)
                    return;

                let targetNodeParent = targetedNode.parentNode;
                if (!targetNodeParent.classList.contains('playable-word'))
                    targetNodeParent = targetNodeParent.closest('.playable-word');
                if (!targetNodeParent)
                    return;

                let wordIndex = Array.prototype.indexOf.call(this.playableText.childNodes, targetNodeParent);
                let word = this.words[wordIndex];
                if (word) {
                    this.highlightClickedWord(selection, word, true);
                }
            }
        } else if (childNodes.length < 2) {
            if (selection.rangeCount) {
                let word = this.words.find(w =>
                    w.textRange.start <= selection.focusOffset && w.textRange.end >= selection.focusOffset);
                if (word) {
                    this.highlightClickedWord(selection, word, false);
                }
            }
        } else {
            const textNode = Array.from(childNodes).find(n => n.nodeType === Node.TEXT_NODE);
            const spanNode = Array.from(childNodes).find(n =>
                n.nodeType === Node.ELEMENT_NODE &&
                (n as Element).classList.contains('last-word')
            );

            const focusNode = selection.focusNode;
            if (focusNode === textNode) {
                const word = this.words.find(w =>
                    w.textRange.start <= selection.focusOffset &&
                    w.textRange.end >= selection.focusOffset
                );
                if (word) {
                    this.highlightClickedWord(selection, word, false);
                }
            } else if (focusNode === spanNode || (focusNode?.parentNode === spanNode)) {
                const word = this.words[this.words.length - 1];
                if (word) {
                    this.highlightClickedWord(selection, word, true);
                }
            }
        }
    }

    private highlightClickedWord(selection: Selection, word: Word, isOneWord: boolean) {
        if (!this.playableText)
            return;

        const textNode = isOneWord ? selection.focusNode : this.playableText.firstChild;
        if (!textNode || textNode.nodeType !== Node.TEXT_NODE)
            return;

        const range = document.createRange();
        if (!isOneWord) {
            range.setStart(textNode, word.textRange.start);
            range.setEnd(textNode, word.textRange.end);
        } else {
            range.setStart(textNode, 0);
            range.setEnd(textNode, textNode.textContent.length - 1);
        }

        const rect = range.getBoundingClientRect();
        const floatSpan = document.createElement("span");
        floatSpan.textContent = word.value;
        floatSpan.className = "selected-word-float";

        Object.assign(floatSpan.style, {
            position: "absolute",
            top: `${rect.top + window.scrollY + 4}px`,
            left: `${rect.left + window.scrollX}px`,
            width: `${rect.width}px`,
            height: `${rect.height - 8}px`,
        });

        document.body.appendChild(floatSpan);

        setTimeout(() => {
            floatSpan.remove();
            void this.blazorRef.invokeMethodAsync("OnMarkupClick", word.textRange);
        }, 400);
    }
}

