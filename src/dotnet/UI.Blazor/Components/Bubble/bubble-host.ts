import { arrow, computePosition, flip, offset, Placement, shift, autoUpdate } from '@floating-ui/dom';
import { Subject, debounceTime, startWith, takeUntil } from 'rxjs';
import { ScreenOrientation } from 'orientation';
import { getSafeAreaPadding } from 'safe-area';
import { getLogs } from 'logging';

const bubbleViewportGap = 5;

interface BubbleModel {
    bubbleRef: string;
    triggerElement: HTMLElement;
    bubbleElement?: HTMLElement;
    isInViewport: boolean;
    isTopElement: boolean;
    isRead: boolean;
    // Set when the show is requested, not when the bubble element appears - see show()
    isShown: boolean;
    priority: number;
    index?: number;
    total?: number;
}

const { debugLog, warnLog } = getLogs('BubbleHost');

export class BubbleHost {
    private readonly mutationObserver: MutationObserver;
    private readonly disposed$: Subject<void> = new Subject<void>();

    private bubbleHost: HTMLElement;

    private _bubbles: BubbleModel[] = [];
    private _clearAutoUpdate?: () => void;
    private _suppressAll = false;

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
            document.getElementById('app')!,
            {
                subtree: true,
                childList: true,
                attributes: true,
                attributeFilter: [
                    'data-side-nav',
                    'data-settings-panel',
                ],
            });

        this.bubbleHost = document.querySelector('.ac-bubble-host')!;
        this.applyOrientationClass();
        ScreenOrientation.change$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.applyOrientationClass());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        this.mutationObserver.disconnect();
    }

    private applyOrientationClass(): void {
        const wantLandscape = !ScreenOrientation.isPortrait;
        const hasLandscape = this.bubbleHost.classList.contains('landscape');
        if (wantLandscape && !hasLandscape)
            this.bubbleHost.classList.add('landscape');
        else if (!wantLandscape && hasLandscape)
            this.bubbleHost.classList.remove('landscape');
    }

    public skipBubbles(): string[] {
        debugLog?.log(`skipBubbles`);

        // Set suppress flag to prevent any future bubbles from showing
        this._suppressAll = true;

        this.clearAutoUpdate();

        const unreadBubbles = this._bubbles.filter(x => !x.isRead);
        const shownBubble = unreadBubbles.find(x => x.isShown);
        if (shownBubble) {
            shownBubble.isShown = false;
            if (shownBubble.bubbleElement)
                shownBubble.bubbleElement.style.display = 'none';
        }

        const bubblesToSkip = unreadBubbles
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            .filter(() => true); // Skip all of them rather than just the current "batch"
            // .filter(x => x.isTopElement && x.isInViewport); // Skip just the current "batch"
        bubblesToSkip.forEach(x => x.isRead = true);

        return bubblesToSkip.map(x => x.bubbleRef);
    }

    public readBubble(bubbleRef: string): void {
        debugLog?.log(`readBubble:`, bubbleRef);

        const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
        if (bubble) {
            bubble.isRead = true;
            bubble.isShown = false;
            bubble.bubbleElement = undefined;
        }

        this.clearAutoUpdate();
        this.showNextBubble();
    }

    // readBubbles here are bubbleRefs of bubbles read by the user, which shouldn't be shown again
    public resetBubbles(readBubbles: string[] = []): void {
        debugLog?.log(`resetBubbles, readBubbles:`, readBubbles);

        // Clear suppress flag to allow bubbles to show again
        this._suppressAll = false;

        while (this.readBubbles.length)
            this.readBubbles.pop();

        this.readBubbles.push(...readBubbles);

        this.clearAutoUpdate();
        // isShown is cleared too: every caller of this method drops the rendered bubble on the Blazor side
        this._bubbles.forEach(x => {
            x.isRead = this.readBubbles.includes(x.bubbleRef);
            x.isShown = false;
            x.bubbleElement = undefined;
            x.index = undefined;
            x.total = undefined;
        });
        this.updateBubbles();
        this.showNextBubble();
    }

    public showBubble(id: string, bubbleRef: string): void {
        debugLog?.log(`showBubble:`, id, bubbleRef);

        const bubble = this._bubbles.find(x => x.bubbleRef === bubbleRef);
        if (!bubble) {
            warnLog?.log(`Bubble not found:`, bubbleRef);
            return;
        }
        bubble.isShown = true;

        const triggerElement = bubble.triggerElement;
        const position = triggerElement.dataset.bubblePlacement as Placement;
        const bubbleElement = document.getElementById(id);
        if (!bubbleElement) {
            warnLog?.log(`Bubble element not found:`, id);
            bubble.isShown = false;
            return;
        }

        bubble.bubbleElement = bubbleElement;

        const arrowElement = document.getElementsByClassName('ac-bubble-arrow')[0] as HTMLElement;

        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block';

        this.clearAutoUpdate();
        this._clearAutoUpdate = autoUpdate(
            triggerElement,
            bubbleElement,
            () => {
                void this.updatePosition(triggerElement, bubbleElement, arrowElement, position);
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
                    shift({ padding: getSafeAreaPadding(bubbleViewportGap) }),
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

        if (!middlewareData.arrow)
            return;

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
            // @ts-expect-error property name
            [staticSide]: '-4px',
        });
    }

    private updateBubbles(): void {
        debugLog?.log(`updateBubbles`);

        const elements = this.getBubbleElements();
        elements.forEach(el => {
            const bubbleRef = el.dataset.bubble;
            if (!bubbleRef) {
                warnLog?.log(`Bubble element without data-bubble:`, el);
                return;
            }
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
                priority: Number(el.dataset.bubblePriority),
            };
            this._bubbles.push(newBubble);
        });

        this._bubbles.forEach(bubble => {
            bubble.isInViewport = this.isElementInViewport(bubble.triggerElement);
            bubble.isTopElement = this.isTopElement(bubble.triggerElement)
                || (bubble.isTopElement && this.topElementIsBubble(bubble.triggerElement));
            if (bubble.isShown && (!bubble.isTopElement || !bubble.isInViewport)) {
                bubble.isShown = false;
                if (bubble.bubbleElement)
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

        if (this._suppressAll)
            return;

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

        // Marked here rather than in showBubble(): the round trip can outlast the DOM
        // debounce above, and re-requesting the same bubble meanwhile makes it blink
        bubble.isShown = true;
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
        return element === trigger;
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
        return bubble != undefined;
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
