import { Subject } from 'rxjs';

export class ChatEntryMessageInternalView {
    private blazorRef: DotNet.DotNetObject;
    private readonly messageMarkup: HTMLElement;
    private readonly playableText: HTMLElement | null;
    private readonly plainText: HTMLElement[] | null;
    private markupHeight: number;
    private mutationObserver: MutationObserver;
    private resizeObserver: ResizeObserver;
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
            this.messageMarkup.style.minHeight = Math.floor(this.markupHeight) + 'px';
            this.messageMarkup.style.maxHeight = Math.floor(this.markupHeight) + 'px';
            const observerOptions = {
                childList: true,
                subtree: true,
                characterData: true,
                characterDataOldValue: true,
            };
            this.mutationObserver = new MutationObserver(this.updateMarkupSize);

            if (this.playableText) {
                this.mutationObserver.observe(this.playableText, observerOptions);
            } else if (this.plainText) {
                this.mutationObserver.observe(this.messageMarkup, observerOptions);
            }
            this.resizeObserver = new ResizeObserver(this.updateHeightOnWidthChange)
            this.resizeObserver.observe(this.messageMarkup);
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private updateHeightOnWidthChange: ResizeObserverCallback = () => {
        if (this.playableText) {
            const markupHeight = this.messageMarkup.offsetHeight;
            const playableHeight = this.playableText.offsetHeight;
            if (playableHeight != markupHeight) {
                this.changeSize(playableHeight);
            }
        }
        if (this.plainText) {
            this.changeSizeForPlainText();
        }
    };

    private updateMarkupSize: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = mutation.target.parentElement as HTMLElement;
                if (element.classList.contains('playable-text-markup')) {
                    this.changeSize(element.offsetHeight);
                } else if (element.classList.contains('plain-text-markup')) {
                    this.changeSizeForPlainText();
                }
            }
        });
    }

    private changeSize(height: number) {
        const isNewValueGreater = this.markupHeight < height;
        this.messageMarkup.addEventListener('transitionend', (e: TransitionEvent) => this.onTransitionEnd(e, isNewValueGreater));
        if (this.markupHeight < height) {
            this.messageMarkup.style.minHeight = height + 'px';
            this.messageMarkup.style.maxHeight = height + 'px';
        } else {
            this.messageMarkup.style.minHeight = height + 'px';
        }
    }

    private changeSizeForPlainText() {
        const markupHeight = this.messageMarkup.offsetHeight;
        const range = document.createRange();
        range.selectNodeContents(this.messageMarkup);
        const height = Math.floor(range.getBoundingClientRect().height);
        if (height != markupHeight) {
            this.changeSize(height);
        }
    }

    private onTransitionEnd(e: TransitionEvent, isNewValueGreater: boolean) {
        if (e.propertyName === 'min-height' || e.propertyName === 'max-height') {
            if (!isNewValueGreater) {
                this.messageMarkup.style.maxHeight = this.messageMarkup.offsetHeight + 'px';
            }
            this.messageMarkup.removeEventListener('transitionend', (e) => this.onTransitionEnd(e, isNewValueGreater));
            this.markupHeight = this.messageMarkup.offsetHeight;
        }
    }
}
