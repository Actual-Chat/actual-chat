import { arrow, computePosition, flip, offset, Placement, shift, autoUpdate } from '@floating-ui/dom';
import { Subject, debounceTime, startWith, takeUntil } from 'rxjs';
import { Log } from 'logging';

interface BubbleModel {
    bubbleRef: string;
    triggerElement: HTMLElement;
    bubbleElement?: HTMLElement;
    isInViewport: boolean;
    isTopElement: boolean;
    isRead: boolean;
    isShown: boolean;
    priority: number;
    index?: number;
    total?: number;
}

const { debugLog } = Log.get('BubbleHost');

export class BubbleHost {
    private readonly mutationObserver: MutationObserver;
    private readonly disposed$: Subject<void> = new Subject<void>();

    private _bubbles: BubbleModel[] = [];
    private _clearAutoUpdate: () => void;

    public static create(blazorRef: DotNet.DotNetObject, readBubbles: string[]): BubbleHost {
        return new BubbleHost(blazorRef, readBubbles);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly readBubbles: string[]) {
        debugLog?.log('constructor');

        const domChanged$ = new Subject();
        this.mutationObserver = new MutationObserver((mutations) => {
            mutations.forEach(mutation => {
                if (mutation.addedNodes.length
                    || mutation.removedNodes.length
                    || mutation.type === 'attributes') {
                    domChanged$.next(undefined);
                }
            });
        });
        domChanged$
            .pipe(
                startWith(undefined),
                debounceTime(500),
                takeUntil(this.disposed$),
            )
            .subscribe(() => {
                this.updateBubbles();
                this.showNextBubble();
            });
        this.mutationObserver.observe(
            document.getElementById('app'),
            {
                subtree: true,
                childList: true,
                attributes: true,
                attributeFilter: [
                    'data-side-nav',
                    'data-settings-panel',
                ],
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        this.mutationObserver.disconnect();
    }

    public async skipBubbles(): Promise<string[]> {
        debugLog?.log(`skipBubbles`);

        this.clearAutoUpdate();

        const notReadBubbles = this._bubbles.filter(x => !x.isRead);
        const shownBubble = notReadBubbles.find(x => x.isShown);
        if (shownBubble) {
            shownBubble.isShown = false;
            shownBubble.bubbleElement.style.display = 'none';
        }

        const bubblesToSkip = notReadBubbles
            .filter(x => x.isTopElement && x.isInViewport);
        bubblesToSkip.forEach(x => x.isRead = true);

        return bubblesToSkip.map(x => x.bubbleRef);
    }

    public async readBubble(bubbleRef: string): Promise<void> {
        debugLog?.log(`readBubble:`, bubbleRef);

        const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
        bubble.isRead = true;
        this.clearAutoUpdate();
        this.showNextBubble();
    }

    public async restartBubbles(): Promise<void> {
        debugLog?.log(`restartBubbles`);

        while (this.readBubbles.length)
            this.readBubbles.pop();

        this._bubbles.forEach(x => x.isRead = false);
        this.updateBubbles();
        this.showNextBubble();
    }

    public async showBubble(id: string, bubbleRef: string): Promise<void> {
        debugLog?.log(`showBubble:`, id, bubbleRef);

        const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
        bubble.isShown = true;

        const triggerElement = bubble.triggerElement;
        const position = triggerElement.dataset['bubblePlacement'] as Placement;
        const bubbleElement = document.getElementById(id);
        bubble.bubbleElement = bubbleElement;

        const arrowElement = document.getElementsByClassName('ac-bubble-arrow')[0] as HTMLElement;

        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block';

        this.clearAutoUpdate();
        this._clearAutoUpdate = autoUpdate(
            triggerElement,
            bubbleElement,
            async () => {
                await this.updatePosition(triggerElement, bubbleElement, arrowElement, position);
            }, {
                animationFrame: true,
            });
    }

    private async updatePosition(
        triggerElement: HTMLElement,
        bubbleElement: HTMLElement,
        arrowElement: HTMLElement,
        position: Placement): Promise<void> {
        const { x, y, placement, middlewareData } = await computePosition(
            triggerElement,
            bubbleElement,
            {
                placement: position,
                middleware: [
                    offset(6),
                    flip({ fallbackAxisSideDirection: 'end' }),
                    shift({ padding: 5 }),
                    arrow({ element: arrowElement }),
                ],
            });

        const rect = triggerElement.getBoundingClientRect();
        if (!this.isElementInDOM(rect)) {
            bubbleElement.style.display = 'none';
        }

        Object.assign(bubbleElement.style, {
            left: `${x}px`,
            top: `${y}px`,
        });

        const { x: arrowX, y: arrowY } = middlewareData.arrow;

        const staticSide = {
            top: 'bottom',
            right: 'left',
            bottom: 'top',
            left: 'right',
        }[placement.split('-')[0]];

        Object.assign(arrowElement.style, {
            left: arrowX != null ? `${arrowX}px` : '',
            top: arrowY != null ? `${arrowY}px` : '',
            right: '',
            bottom: '',
            [staticSide]: '-4px',
        });
    }

    private updateBubbles(): void {
        debugLog?.log(`updateBubbles`);

        const elements = this.getBubbleElements();
        elements.forEach(el => {
            const bubbleRef = el.dataset['bubble'];
            if (this.readBubbles.includes(bubbleRef)) {
                return;
            }

            const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
            if (bubble) {
                bubble.triggerElement = el;

                return;
            }

            const newBubble: BubbleModel = {
                bubbleRef: bubbleRef,
                triggerElement: el,
                isInViewport: false,
                isTopElement: false,
                isRead: false,
                isShown: false,
                priority: Number(el.dataset['bubblePriority']),
            };
            this._bubbles.push(newBubble);
        });

        this._bubbles.forEach(bubble => {
            bubble.isInViewport = this.isElementInViewport(bubble.triggerElement);
            bubble.isTopElement = this.isTopElement(bubble.triggerElement)
                || (bubble.isTopElement && this.topElementIsBubble(bubble.triggerElement));
            if (bubble.isShown && (!bubble.isTopElement || !bubble.isInViewport)) {
                bubble.isShown = false;
                bubble.bubbleElement.style.display = 'none';
            }
        });

        this._bubbles = this._bubbles.sort((a, b) => a.priority - b.priority);

        const bubblesToShowWithoutIndex = this._bubbles
            .filter(x => !x.isRead)
            .filter(x => x.isTopElement && x.isInViewport)
            .filter(x => x.total === undefined && x.index === undefined);
        bubblesToShowWithoutIndex.forEach((bubble, index) => {
            bubble.index = index + 1;
            bubble.total = bubblesToShowWithoutIndex.length;
        });
    }

    private showNextBubble(): void {
        debugLog?.log(`showNextBubble`);

        const notReadBubbles = this._bubbles.filter(x => !x.isRead);
        const bubbleToShow = notReadBubbles.find(x => x.isInViewport && x.isTopElement);
        if (!bubbleToShow)
            return;

        const shownBubble = notReadBubbles.find(x => x.isShown);
        if (shownBubble === bubbleToShow)
            return;

        const isLastVisible = notReadBubbles
            .filter(x => x !== bubbleToShow)
            .filter(x => x.isInViewport && x.isTopElement)
            .length === 0;
        this.show(bubbleToShow, isLastVisible);
    }

    private show(bubble: BubbleModel, isLastVisible: boolean): void {
        debugLog?.log(`show:`, bubble.bubbleRef);

        void this.blazorRef.invokeMethodAsync(
            'OnShow',
            bubble.bubbleRef,
            isLastVisible,
            bubble.index,
            bubble.total);
    }

    private isElementInViewport(element: HTMLElement): boolean {
        const rect = element.getBoundingClientRect();
        if (!this.isElementInDOM(rect))
            return false;

        const viewHeight = Math.max(document.documentElement.clientHeight, window.innerHeight);
        return rect.bottom >= 0 && rect.top - viewHeight < 0;
    }

    private isElementInDOM(rect: DOMRect): boolean {
        return rect.bottom !== 0 || rect.top !== 0;
    }

    private isTopElement(element: HTMLElement): boolean {
        const rect = element.getBoundingClientRect();
        const topElement = document.elementFromPoint(rect.left, rect.top);
        if (element.isSameNode(topElement))
            return true;

        if (topElement == null)
            return false;

        if (topElement.contains(element))
            return true;

        const trigger = topElement.closest('[data-bubble]');
        if (element === trigger)
            return true;

        return false;
    }

    private topElementIsBubble(element: HTMLElement): boolean {
        const rect = element.getBoundingClientRect();
        const topElement = document.elementFromPoint(rect.left, rect.top);
        if (!topElement)
            return false;

        const isBubble = topElement.classList.contains('ac-bubble');
        if (isBubble)
            return true;

        const bubble = topElement.closest('.ac-bubble');
        if (bubble != undefined)
            return true;

        return false;
    }

    private getBubbleElements(): HTMLElement[] {
        const elements = [...document.querySelectorAll('[data-bubble]')];
        return elements as HTMLElement[];
    }

    private clearAutoUpdate(): void {
        if (!this._clearAutoUpdate)
            return;

        this._clearAutoUpdate();
        this._clearAutoUpdate = undefined;
    }
}
