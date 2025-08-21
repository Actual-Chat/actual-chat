import { Subject } from 'rxjs';
import { debounce, ResettableFunc } from 'promises';
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

        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');

        this.markupHeight = this.messageMarkup.offsetHeight;
        this.messageMarkup.style.height = this.markupHeight + 'px';
        this.mutationObserver = new MutationObserver(this.updateMarkupSize);
        this.mutationObserver.observe(this.messageMarkup, this.observerOptions);

        this.resizeObserver = new ResizeObserver(this.updateHeightOnWidthChange);
        this.resizeObserver.observe(this.messageMarkup);

        this.slowDebouncedChangeSize = debounce(async (height: number) => {
            const actualHeight = this.getActualHeight();
            if (actualHeight <= height) {
                this.changeSize(height);
            }
        }, 2000);
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

    private updateHeightOnWidthChange: ResizeObserverCallback = () => this.changeSizeForText();

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
                    if (node instanceof HTMLElement
                        && ['change-item', 'changed-item', 'changes', 'retained', 'plain-text-markup'].some(cls => node.classList.contains(cls))) {
                        if (this.messageMarkup.classList.contains('empty')) {
                            this.messageMarkup.classList.remove('empty');
                        } else {
                            this.changeSizeForText(true);
                        }
                    }
                });
                mutation.removedNodes.forEach((node) => {
                    if (node instanceof HTMLElement
                        && ['change-item', 'changed-item', 'changes', 'retained', 'plain-text-markup'].some(cls => node.classList.contains(cls))) {
                        this.changeSizeForText();
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
        this.fastDebouncedChangeSize(this.playableText?.offsetHeight);

        setTimeout(() => {
            this.mutationObserver.observe(this.messageMarkup, this.observerOptions);
            this.resizeObserver.observe(this.messageMarkup);
        }, 500);
    }

    private fastDebouncedChangeSize = debounce((height: number) => this.changeSize(height), 150);

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

        this.messageMarkup.addEventListener('transitionend', this.onTransitionEndBound);
    }

    private changeSizeForText(slow: boolean = false) {
        const actualHeight = this.getActualHeight();
        if (Math.abs(actualHeight - this.markupHeight) < this.getRemInPixels())
            return;

        if (actualHeight > this.markupHeight) {
            this.slowDebouncedChangeSize.reset();
            this.fastDebouncedChangeSize(actualHeight);
        } else {
            slow ? this.slowDebouncedChangeSize(actualHeight) : this.fastDebouncedChangeSize(actualHeight);
        }
    }

    private getActualHeight(): number {
        const sendingStatus = this.messageMarkup.querySelector('.chat-message-sending-status') as HTMLElement | null;
        let style = sendingStatus ? getComputedStyle(sendingStatus) : new CSSStyleDeclaration();
        if (sendingStatus && style.position === 'absolute') {
            sendingStatus.style.display = 'none';
        }

        const range = document.createRange();
        range.selectNodeContents(this.messageMarkup);
        const height = Math.ceil(range.getBoundingClientRect().height);

        if (sendingStatus && style.position === 'absolute') {
            sendingStatus.style.display = '';
        }

        return height;
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
