import { Subject } from 'rxjs';
import { debounce, ResettableFunc } from 'promises';

const importantClasses = new Set([
    'change-item',
    'changed-item',
    'changes',
    'retained',
]);

export class ChatEntryMessageInternalView {
    private blazorRef: DotNet.DotNetObject;
    private readonly messageMarkup: HTMLElement;
    private playableText: HTMLElement | null;
    private markupHeight: number = 0;
    private scrollbarDelta = 0;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
    private observerOptions = {
        childList: true,
        subtree: true,
        characterData: true,
        characterDataOldValue: true,
    };
    private isResizing: boolean = false;
    private skipNext: boolean = false;
    private slowDebouncedChangeSize: ResettableFunc<(height: number) => Promise<void>>;
    private disposed$: Subject<void> = new Subject<void>();

    static create(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement): ChatEntryMessageInternalView {
        return new ChatEntryMessageInternalView(blazorRef, messageMarkup);
    }

    constructor(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement) {
        this.blazorRef = blazorRef;
        this.messageMarkup = messageMarkup;
        if (!this.messageMarkup)
            return;

        let content = this.messageMarkup.textContent;
        if (!content && !this.messageMarkup.classList.contains('streaming'))
            this.messageMarkup.classList.add('empty');

        const isNotStreaming = this.messageMarkup.classList.contains('not-streaming');
        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');
        if (isNotStreaming && !this.playableText)
            return;

        this.scrollbarDelta = this.getRemInPixels();

        this.markupHeight = this.messageMarkup.offsetHeight;
        this.messageMarkup.style.height = this.markupHeight + 'px';
        this.mutationObserver = new MutationObserver(this.updateMarkupSize);
        this.mutationObserver.observe(this.messageMarkup, this.observerOptions);

        this.resizeObserver = new ResizeObserver(this.updateHeightOnWidthChange);
        this.resizeObserver.observe(this.messageMarkup);

        this.slowDebouncedChangeSize = debounce(async (height: number) => {
            const actualHeight = this.getActualHeight();
            if (actualHeight > height)
                return;

            this.changeSize(height);
        }, 2000);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        this.mutationObserver?.disconnect();
        this.resizeObserver?.disconnect();
    }

    private getRemInPixels(): number {
        return parseFloat(getComputedStyle(document.documentElement).fontSize);
    }

    private updateHeightOnWidthChange: ResizeObserverCallback = () => {
        if (this.skipNext) {
            this.skipNext = false;
            return;
        }
        this.changeSizeForText();
    };

    private updateMarkupSize: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            const targetEl = mutation.target instanceof HTMLElement
                ? mutation.target
                : mutation.target.parentElement;

            if (!targetEl)
                return;

            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = mutation.target.parentElement as HTMLElement;
                if ([...element.classList].some(cls => importantClasses.has(cls)) || element.classList.contains('playable-text-markup')) {
                    this.changeSizeForText(true);
                } else {
                    this.changeSizeForText();
                }
            }
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement))
                        return;

                    if (node.classList.contains('playable-text-markup')) {
                        this.playableText = node as HTMLElement;
                        this.onTranscriptionFinalizedResize();
                    }
                    if (node.classList.contains('plain-text-markup')) {
                        if (this.messageMarkup.classList.contains('empty')) {
                            this.messageMarkup.classList.remove('empty');
                        } else {
                            this.changeSizeForText(true);
                        }
                    }
                });
                mutation.removedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement))
                        return;

                    if ([...node.classList].some(cls => importantClasses.has(cls))) {
                        this.changeSizeForText(true);
                    } else if (node.classList.contains('plain-text-markup')) {
                        this.changeSizeForText();
                    }
                });
            }
        });
    }

    private onTranscriptionFinalizedResize() {
        this.mutationObserver.disconnect();
        this.resizeObserver.disconnect();

        this.isResizing = false;
        const finalHeight = this.playableText?.offsetHeight ?? this.getActualHeight();
        this.fastDebouncedChangeSize(finalHeight);

        this.skipNext = true;
        this.mutationObserver.observe(this.messageMarkup, this.observerOptions);
        this.resizeObserver.observe(this.messageMarkup);
    }

    private fastDebouncedChangeSize = debounce((height: number) => {
        this.slowDebouncedChangeSize.reset();
        this.changeSize(height)
    }, 150);

    private changeSize(height: number) {
        if (this.isResizing || this.markupHeight === height)
            return;

        const actualHeight = this.getActualHeight();
        height = Math.max(height, actualHeight);

        this.isResizing = true;

        this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);
        this.messageMarkup.addEventListener('transitionend', this.onTransitionEndBound);

        this.messageMarkup.style.height = height + 'px';
        this.markupHeight = height;
    }

    private changeSizeForText(slow: boolean = false) {
        const actualHeight = this.getActualHeight();
        if (Math.abs(actualHeight - this.markupHeight) <= this.getRemInPixels())
            return;

        const debounceFunc = actualHeight > this.markupHeight || !slow
            ? this.fastDebouncedChangeSize
            : this.slowDebouncedChangeSize;

        debounceFunc(actualHeight);
    }

    private getActualHeight(): number {
        const range = document.createRange();
        range.selectNodeContents(this.messageMarkup);
        return Math.ceil(range.getBoundingClientRect().height);
    }

    private onTransitionEndBound = (e: TransitionEvent) => this.onTransitionEnd(e);

    private onTransitionEnd(e: TransitionEvent) {
        if (e.propertyName === 'height') {
            this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);
            this.markupHeight = this.messageMarkup.offsetHeight;
            this.isResizing = false;
        }
    }
}
