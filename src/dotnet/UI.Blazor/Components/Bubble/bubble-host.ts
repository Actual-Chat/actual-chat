import { arrow, computePosition, flip, offset, Placement, shift } from '@floating-ui/dom';
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
}

const { debugLog } = Log.get('BubbleHost');

export class BubbleHost {
    private readonly _bubbles: BubbleModel[] = [];
    private readonly mutationObserver: MutationObserver;
    private readonly skipped: Subject<void> = new Subject<void>();

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
                if (mutation.addedNodes.length || mutation.removedNodes.length) {
                    domChanged$.next(undefined);
                }
            });
        });
        domChanged$
            .pipe(
                startWith(undefined),
                debounceTime(1000),
                takeUntil(this.skipped),
            )
            .subscribe(() => {
                this.updateBubbles();
                this.showNextBubble();
            });
        this.mutationObserver.observe(document.getElementById('app'), { subtree: true, childList: true });
    }

    public async skipBubbles(): Promise<void> {
        debugLog?.log(`skipBubbles`);

        this.mutationObserver.disconnect();
        this.skipped.next(undefined);
        this.skipped.complete();
    }

    public async readBubble(bubbleRef: string): Promise<void> {
        debugLog?.log(`readBubble:`, bubbleRef);

        const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
        bubble.isRead = true;

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
                if (bubble.isRead)
                    return;

                bubble.isInViewport = this.isElementInViewport(el);
                bubble.isTopElement = this.isTopElement(el);

                return;
            }

            const newBubble: BubbleModel = {
                bubbleRef: bubbleRef,
                triggerElement: el,
                isInViewport: this.isElementInViewport(el),
                isTopElement: this.isTopElement(el),
                isRead: false,
                isShown: false,
            };
            this._bubbles.push(newBubble);
        });
    }

    private showNextBubble(): void {
        debugLog?.log(`showNextBubble`);

        const notReadBubbles = this._bubbles.filter(x => !x.isRead);
        const shownBubble = notReadBubbles.find(x => x.isShown);

        if (shownBubble) {
            if (!shownBubble.isInViewport || !shownBubble.isTopElement) {
                shownBubble.isShown = false;
                shownBubble.bubbleElement.style.display = 'none';
            }

            return;
        }

        const bubbleToShow = notReadBubbles.find(x => x.isInViewport && x.isTopElement);
        if (!bubbleToShow)
            return;

        this.show(bubbleToShow);
    }

    private show(bubble: BubbleModel): void {
        debugLog?.log(`show:`, bubble.bubbleRef);

        void this.blazorRef.invokeMethodAsync('OnShow', bubble.bubbleRef);
    }

    private isElementInViewport(element: HTMLElement): boolean {
        const rect = element.getBoundingClientRect();
        const viewHeight = Math.max(document.documentElement.clientHeight, window.innerHeight);
        return rect.bottom >= 0 && rect.top - viewHeight < 0;
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

    private getBubbleElements(): HTMLElement[] {
        const elements = [...document.querySelectorAll('[data-bubble]')];
        return elements as HTMLElement[];
    }
}
