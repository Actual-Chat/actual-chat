// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import { Subject } from 'rxjs';
import { debounce, ResettableFunc } from 'actuallab-core';
import { fastRaf } from 'fast-raf';

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
    private markupHeight = 0;
    private lastWidth: number | null = null;
    private isHeightAuto = true;
    private setHeightAutoAfterTransition = false;
    private classObserver: MutationObserver;
    private messageHeightObserver: MutationObserver;
    private messageWidthObserver: ResizeObserver;
    private createPlayableTextObserver: MutationObserver;
    private observerOptions = {
        childList: true,
        subtree: true,
        characterData: true,
        characterDataOldValue: true,
    };
    private isResizing = false;
    private skipNext = false;
    private slowDebouncedChangeSize: ResettableFunc<[height: number]>;
    private disposed$: Subject<void> = new Subject<void>();

    static create(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement): ChatEntryMessageInternalView {
        return new ChatEntryMessageInternalView(blazorRef, messageMarkup);
    }

    constructor(blazorRef: DotNet.DotNetObject, messageMarkup: HTMLElement) {
        this.blazorRef = blazorRef;
        this.messageMarkup = messageMarkup;
        if (!this.messageMarkup)
            return;

        const isStreaming = this.messageMarkup.classList.contains('streaming');

        this.classObserver = new MutationObserver(this.stateHandler);
        this.classObserver.observe(this.messageMarkup, {
            attributes: true,
            attributeFilter: ['class'],
            attributeOldValue: true,
        });

        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');
        this.slowDebouncedChangeSize = debounce(async (height: number) => {
            const actualHeight = this.getActualHeight();
            if (actualHeight > height)
                return;

            this.changeSize(height);
        }, 1500);

        if (isStreaming) {
            fastRaf({
                read: () => {
                    this.markupHeight = this.messageMarkup.offsetHeight;
                },
                write: () => {
                    this.messageMarkup.style.height = `${this.markupHeight}px`;
                    this.isHeightAuto = false;
                    this.observerHandler(true);
                },
            });
        }
        if (!this.playableText && isStreaming) {
            this.createPlayableTextObserver = new MutationObserver(this.smoothShowPlayableText);
            this.createPlayableTextObserver.observe(this.messageMarkup, {
                childList: true,
                subtree: true,
            });
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        this.messageHeightObserver?.disconnect();
        this.messageWidthObserver?.disconnect();
        this.classObserver?.disconnect();
        this.createPlayableTextObserver?.disconnect();
    }

    private getRemInPixels(): number {
        return parseFloat(getComputedStyle(document.documentElement).fontSize);
    }

    private observerHandler(observe = true) {
        if (observe) {
            this.messageHeightObserver = new MutationObserver(this.updateMarkupSize);
            this.messageHeightObserver.observe(this.messageMarkup, this.observerOptions);
            this.messageWidthObserver = new ResizeObserver(this.updateHeightOnWidthChange);
            this.messageWidthObserver.observe(this.messageMarkup);
            return;
        }
        this.messageHeightObserver?.disconnect();
        this.messageWidthObserver?.disconnect();
    }

    private updateHeightOnWidthChange: ResizeObserverCallback = (entries) => {
        if (this.skipNext || this.isHeightAuto) {
            this.skipNext = false;
            return;
        }
        for (const entry of entries) {
            const width = entry.contentRect.width;
            if (this.lastWidth === width)
                return;

            this.lastWidth = width;
            this.changeSizeForText();
        }
    };

    private stateHandler: MutationCallback = (mutations) => {
        for (const mutation of mutations) {
            if (mutation.type === 'attributes' && mutation.attributeName === 'class') {
                const target = mutation.target as HTMLElement;
                const oldClassList = (mutation.oldValue ?? '').split(/\s+/);
                const newClassList = target.className.split(/\s+/);
                const added = newClassList.filter(c => !oldClassList.includes(c));
                if (added.includes('not-streaming')) {
                    this.onTranscriptionFinalizedResize();
                    this.messageMarkup.style.height = 'auto';
                    this.isHeightAuto = true;
                    return;
                } else if (added.includes('from-streaming')) {
                    this.onTranscriptionFinalizedResize();
                    this.messageMarkup.style.height = 'auto';
                    this.isHeightAuto = true;
                    return;
                } else if (added.includes('streaming')) {
                    this.observerHandler(true);
                }
            }
        }
    };

    private updateMarkupSize: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            const targetEl = mutation.target instanceof HTMLElement
                ? mutation.target
                : mutation.target.parentElement;

            if (!targetEl)
                return;

            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement))
                        return;

                    if ([...node.classList].some(cls => importantClasses.has(cls))) {
                        this.changeSizeForText();
                    }
                });
                mutation.removedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement))
                        return;

                    if ([...node.classList].some(cls => importantClasses.has(cls))) {
                        this.changeSizeForText(true);
                    }
                });
            }

            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = (mutation.target.parentElement?.closest(
                    '.change-item, .changed-item, .changes, .retained'
                )) as HTMLElement | null;

                if (element) {
                    this.changeSizeForText(true);
                } else {
                    this.changeSizeForText();
                }
            }
        });
    }

    private smoothShowPlayableText: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            const targetEl = mutation.target instanceof HTMLElement
                ? mutation.target
                : mutation.target.parentElement;

            if (!targetEl)
                return;

            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (!(node instanceof HTMLElement))
                        return;

                    if (node.classList.contains('playable-text-markup')) {
                        node.classList.add('smooth-show');
                    }
                });
            }

            if (mutation.type === 'characterData' &&
                mutation.target.nodeType === Node.TEXT_NODE) {
                const element = (mutation.target.parentElement?.closest(
                    '.change-item, .changed-item, .changes, .retained'
                )) as HTMLElement | null;

                if (element) {
                    this.changeSizeForText(true);
                } else {
                    this.changeSizeForText();
                }
            }
        });
    }

    private onTranscriptionFinalizedResize() {
        this.observerHandler(false);
        this.fastDebouncedChangeSize.reset();
        this.slowDebouncedChangeSize.reset();

        this.isResizing = false;
        this.messageMarkup.style.height = 'auto';
        this.isHeightAuto = true;
        this.skipNext = true;
    }

    private fastDebouncedChangeSize = debounce((height: number) => {
        this.slowDebouncedChangeSize.reset();
        this.changeSize(height)
    }, 50);

    private changeSize(height: number, setHeightAuto = false) {
        if (this.isResizing && height <= this.markupHeight)
            return;

        if (this.markupHeight === height)
            return;

        const actualHeight = this.getActualHeight();
        height = Math.max(height, actualHeight);
        this.isResizing = true;

        this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);
        this.setHeightAutoAfterTransition = setHeightAuto;
        this.messageMarkup.addEventListener('transitionend', this.onTransitionEndBound, { once: true });
        this.messageMarkup.style.height = `${height}px`;
        this.markupHeight = height;
    }

    private changeSizeForText(slow = false) {
        fastRaf({
            read: () => requestAnimationFrame(() => {
                const actualHeight = this.getActualHeight();
                const markupHeight = this.markupHeight;
                const remInPx = this.getRemInPixels();

                const needToResize = Math.abs(actualHeight - markupHeight) > remInPx;
                if (!needToResize)
                    return;

                const debounceFunc =
                    actualHeight > markupHeight || !slow
                        ? this.fastDebouncedChangeSize
                        : this.slowDebouncedChangeSize;

                debounceFunc(actualHeight);
            })
        });
    }

    private getActualHeight(): number {
        const range = document.createRange();
        range.selectNodeContents(this.messageMarkup);
        return Math.ceil(range.getBoundingClientRect().height);
    }

    private onTransitionEndBound = (e: TransitionEvent) => this.onTransitionEnd(e);
    private onTransitionEnd(e: TransitionEvent) {
        if (e.propertyName !== 'height')
            return;

        this.messageMarkup.removeEventListener('transitionend', this.onTransitionEndBound);

        if (this.setHeightAutoAfterTransition) {
            this.messageMarkup.style.height = '';
            this.setHeightAutoAfterTransition = false;
        }

        this.markupHeight = this.messageMarkup.offsetHeight;
        this.isResizing = false;
    }
}
