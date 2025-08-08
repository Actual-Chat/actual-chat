import { Subject } from 'rxjs';
import { debounce } from 'promises';
import { setTimeout } from 'timerQueue';

export class ChatEntryMessageInternalView {
    private blazorRef: DotNet.DotNetObject;
    private readonly messageMarkup: HTMLElement;
    private playableText: HTMLElement | null;
    private markupHeight: number;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
    private observerOptions = {
        childList: true,
        subtree: true,
        characterData: true,
        characterDataOldValue: true,
    };
    private isResizing: boolean = false;
    private disposed$: Subject<void> = new Subject<void>();

    static create(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement): ChatEntryMessageInternalView {
        return new ChatEntryMessageInternalView(blazorRef, messageMarkup);
    }

    constructor(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement) {
        this.blazorRef = blazorRef;
        this.messageMarkup = messageMarkup;
        if (!this.messageMarkup)
            return;

        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');

        this.markupHeight = this.messageMarkup.offsetHeight;
        this.messageMarkup.style.height = this.markupHeight + 'px';
        this.mutationObserver = new MutationObserver(this.updateMarkupSize);
        this.mutationObserver.observe(this.messageMarkup, this.observerOptions);

        this.resizeObserver = new ResizeObserver(this.updateHeightOnWidthChange);
        this.resizeObserver.observe(this.messageMarkup);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        if (this.mutationObserver) {
            this.mutationObserver.disconnect();
        }

        if (this.resizeObserver) {
            this.resizeObserver.disconnect();
        }
    }

    private getRemInPixels(): number {
        return parseFloat(getComputedStyle(document.documentElement).fontSize);
    }

    private updateHeightOnWidthChange: ResizeObserverCallback = () => {
        this.changeSizeForText();
    };

    private updateMarkupSize: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = mutation.target.parentElement as HTMLElement;
                if (['change-item', 'changed-item', 'changes', 'retained'].some(cls => element.classList.contains(cls))) {
                    this.changeSizeForText(true);
                } else {
                    this.changeSizeForText();
                }
            }
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (node instanceof HTMLElement && node.classList.contains('playable-text-markup')) {
                        this.playableText = node as HTMLElement;
                        this.onTranscriptionFinalizedResize();
                    }
                });
            }
        });
    }

    private onTranscriptionFinalizedResize() {
        if (this.mutationObserver) {
            this.mutationObserver.disconnect();
        }
        if (this.resizeObserver) {
            this.resizeObserver.disconnect();
        }
        this.isResizing = false;
        this.changeSizeFastDebounced(this.playableText?.offsetHeight);

        setTimeout(() => {
            this.mutationObserver.observe(this.messageMarkup, this.observerOptions);
            this.resizeObserver.observe(this.messageMarkup);
        }, 500);
    }

    private changeSizeFastDebounced = debounce((height: number) => this.changeSize(height), 150);
    private changeSizeSlowDebounced = debounce((height: number) => this.changeSize(height), 2000);

    private changeSize(height: number) {
        if (this.isResizing || this.markupHeight === height)
            return;

        const actualHeight = this.getActualHeight();
        if (height < actualHeight) {
            height = actualHeight;
        }

        this.isResizing = true;

        this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);
        this.messageMarkup.addEventListener('transitionend', this.onTransitionEndBound);

        this.messageMarkup.style.height = height + 'px';
        this.markupHeight = height;

        setTimeout(() => {
            this.isResizing = false;
        }, 300);
    }

    private changeSizeForText(slow: boolean = false) {
        const actualHeight = this.getActualHeight();
        const oldHeight = this.markupHeight;
        const minDelta = this.getRemInPixels();
        if (Math.abs(actualHeight - this.markupHeight) > minDelta) {
            if (actualHeight > oldHeight) {
                this.changeSizeFastDebounced(actualHeight);
            } else {
                slow ? this.changeSizeSlowDebounced(actualHeight) : this.changeSizeFastDebounced(actualHeight);
            }
        }
    }

    private getActualHeight() : number {
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
