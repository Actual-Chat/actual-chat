import { Subject } from 'rxjs';
import {
    computePosition,
    flip,
    Middleware,
    offset,
    Placement,
    shift,
} from '@floating-ui/dom';
import { Disposable } from 'disposable';
import { delayAsync } from 'promises';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { VibrationUI } from '../../Services/VibrationUI/vibration-ui';
import { Log } from 'logging';

const { logScope, debugLog } = Log.get('BubbleHost');

interface Bubble {
    id: string;
    bubbleRef: string;
    triggerElement: HTMLElement;
    placement: Placement;
    historyStepId: string | null;
    bubbleElement: HTMLElement | null;
}

export class BubbleHost implements Disposable {
    private readonly hoverBubbleDelayMs = 50;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private bubble: Bubble | null;

    public static create(blazorRef: DotNet.DotNetObject): BubbleHost {
        return new BubbleHost(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        debugLog?.log('constructor');
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public get isDesktopMode(): boolean {
        return ScreenSize.isWide();
    }

    public showOrPosition(
        bubbleRef: string,
        triggerElement: HTMLElement | string,
        placement?: Placement | null,
    ): void {
        let bubble = this.create(bubbleRef, triggerElement, placement);
        if (this.isShown(bubble))
            void this.position(this.bubble, bubble);
        else
            this.show(bubble);
    }

    public hideById(id: string): void {
        const bubble = this.bubble;
        if (!bubble || bubble.id !== id) {
            debugLog?.log('hideById: no bubble with id:', id)
            return;
        }

        this.hide();
    }

    public async positionById(id: string): Promise<void> {
        const bubble = this.bubble;
        if (!bubble || bubble.id !== id) {
            debugLog?.log('positionById: no bubble with id:', id)
            return;
        }

        if (!bubble.bubbleElement) {
            // This is the very first render of hover bubble
            await delayAsync(this.hoverBubbleDelayMs);
        }

        bubble.bubbleElement = document.getElementById(bubble.id);
        void this.position(bubble);
    }

    // Private methods

    private create(
        bubbleRef: string,
        triggerElement: HTMLElement | SVGElement | string,
        placement: Placement | null,
    ): Bubble {
        if (!(triggerElement instanceof HTMLElement)) {
            const triggerElementId = triggerElement as string;
            triggerElement = document.getElementById(triggerElementId);
        }
        placement = placement ?? getPlacementFromAttributes(triggerElement);
        return {
            id: nextId(),
            bubbleRef: bubbleRef,
            triggerElement: triggerElement,
            placement: placement,
            historyStepId: null,
            bubbleElement: null,
        };
    }

    private isShown(bubble: Bubble) {
        let m = this.bubble;
        return m
            && m.bubbleRef === bubble.bubbleRef
            && m.triggerElement === bubble.triggerElement;
    }

    private show(bubble: Bubble): void {
        debugLog?.log('show:', bubble)
        if (!bubble)
            throw new Error(`${logScope}.show: bubble == null.`);

        this.bubble = bubble;
        this.blazorRef.invokeMethodAsync('OnShowRequest', bubble.id, bubble.bubbleRef);
        if (ScreenSize.isNarrow())
            VibrationUI.vibrate();
    }

    private hide(options?: {
        id?: string,
    }): void {
        debugLog?.log('hide, options:', options);
        const bubble = this.bubble;
        if (!bubble)
            return;

        if (options) {
            if (options.id !== undefined && bubble.id !== options.id)
                return;
        }

        this.bubble = null;
        // Hide (un-render) it
        this.blazorRef.invokeMethodAsync('OnHideRequest', bubble.id);
    }

    private async position(bubble: Bubble, updatedBubble?: Bubble): Promise<void> {
        if (!bubble)
            throw new Error(`${logScope}.position: bubble == null.`);

        if (updatedBubble) {
            bubble.bubbleElement = updatedBubble.bubbleElement ?? bubble.bubbleElement;
            bubble.placement = updatedBubble.placement ?? bubble.placement;
        }

        let bubbleElement = bubble.bubbleElement;
        if (!bubbleElement)
            return;

        debugLog?.log(`position: bubble:`, bubble);
        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block'

        const middleware: Middleware[] = [];
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
        const { x, y } = await computePosition(
            bubble.triggerElement,
            bubbleElement,
            {
                placement: bubble.placement ?? 'top',
                middleware: middleware,
            });
        Object.assign(bubbleElement.style, {
            left: `${x}px`,
            top: `${y}px`,
        });
    }
}

// Helpers

let _nextId = 1;
// Bubble Ids are used as HTML element Ids, so they need to have unique prefix
let nextId = () => 'bubble:' + (_nextId++).toString();

function getPlacementFromAttributes(triggerElement: HTMLElement): Placement | null {
    const placement = triggerElement.dataset['bubblePlacement'];
    return placement?.length > 0 ? placement as Placement : null;
}
