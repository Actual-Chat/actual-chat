import { Subject } from 'rxjs';
import { debounce } from 'promises';
import { setTimeout } from 'timerQueue';

export class ChatEntryMessageInternalView {
    private blazorRef: DotNet.DotNetObject;
    private readonly messageMarkup: HTMLElement;
    private readonly playableText: HTMLElement | null;
    private readonly plainText: HTMLElement[] | null;
    private retainedText: HTMLElement | null;
    private changesText: HTMLElement | null;
    private markupHeight: number;
    private mutationObserver: MutationObserver;
    private retainedMutationObserver: MutationObserver;
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

        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');
        this.plainText = [...this.messageMarkup.querySelectorAll('.plain-text-markup')].map(el => el as HTMLElement);

        if (this.playableText || this.plainText) {
            this.markupHeight = this.messageMarkup.offsetHeight;
            this.messageMarkup.style.height = this.markupHeight + 'px';
            this.mutationObserver = new MutationObserver(this.updateMarkupSize);
            this.mutationObserver.observe(this.messageMarkup, this.observerOptions);

            this.resizeObserver = new ResizeObserver(this.updateHeightOnWidthChange)
            this.resizeObserver.observe(this.messageMarkup);

            this.retainedMutationObserver = new MutationObserver((mutations) => {
                mutations.forEach(mutation => {
                    if (mutation.type === 'characterData' && mutation.target.nodeType === Node.TEXT_NODE) {
                        this.changeSizeForPlainText();
                    }
                });
            });
        }
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

        if (this.retainedMutationObserver) {
            this.retainedMutationObserver.disconnect();
        }

        this.retainedText = null;
        this.changesText = null;
    }

    private updateHeightOnWidthChange: ResizeObserverCallback = () => {
        if (this.playableText) {
            const markupHeight = this.messageMarkup.offsetHeight;
            const playableHeight = this.playableText.offsetHeight;
            if (playableHeight != markupHeight) {
                this.changeSizeDebounced(playableHeight);
            }
        }
        if (this.plainText || this.retainedText || this.changesText) {
            this.changeSizeForPlainText();
        }
    };

    private updateMarkupSize: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = mutation.target.parentElement as HTMLElement;
                if (element.classList.contains('playable-text-markup')) {
                    this.changeSizeDebounced(element.offsetHeight);
                } else if (element.classList.contains('plain-text-markup')) {
                    this.changeSizeForPlainText();
                }
            }
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (node instanceof HTMLElement && node.classList.contains('retained')) {
                        if (this.retainedText)
                            this.retainedMutationObserver.disconnect();

                        this.retainedText = node as HTMLElement;
                        this.retainedMutationObserver.observe(this.retainedText, this.observerOptions);
                    }
                    if (node instanceof HTMLElement && node.classList.contains('changes')) {
                        this.changesText = node as HTMLElement;
                        this.changeSizeForPlainText(false);
                    }
                    if (node instanceof HTMLElement && node.classList.contains('change-item')) {
                        if (!this.changesText) {
                            this.changesText = node.closest('.changes');
                        }
                        this.changeSizeForPlainText(false);
                    }
                });

                mutation.removedNodes.forEach((node) => {
                    if (node instanceof HTMLElement && node.classList.contains('retained')) {
                        this.retainedMutationObserver.disconnect();
                        this.retainedText = null;
                    }
                    if (node instanceof HTMLElement && node.classList.contains('changes')) {
                        this.changesText = null;
                    }
                });
            }
        });
    }

    private changeSizeDebounced = debounce((height: number) => this.changeSize(height), 200);

    private changeSize(height: number) {
        if (this.isResizing || this.markupHeight === height)
            return;

        this.isResizing = true;

        this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);
        this.messageMarkup.addEventListener('transitionend', this.onTransitionEndBound);

        this.messageMarkup.style.height = height + 'px';
        this.markupHeight = height;

        setTimeout(() => {
            this.isResizing = false;
        }, 500);
    }

    private changeSizeForPlainText(withDebounce: boolean = true) {
        const range = document.createRange();
        range.selectNodeContents(this.messageMarkup);
        const height = Math.ceil(range.getBoundingClientRect().height);
        const minDifference = 1;
        if (Math.abs(height - this.markupHeight) > minDifference) {
            withDebounce ? this.changeSizeDebounced(height) : this.changeSize(height);
        }
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
