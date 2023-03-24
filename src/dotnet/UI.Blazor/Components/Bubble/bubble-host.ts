import {
    merge,
    Subject,
    takeUntil,
} from 'rxjs';
import {
    computePosition,
    flip,
    Middleware,
    offset,
    Placement,
    ReferenceElement,
    shift,
    VirtualElement,
} from '@floating-ui/dom';
import { Disposable } from 'disposable';
import { DocumentEvents, stopEvent } from 'event-handling';
import { getOrInheritData } from 'dom-helpers';
import { delayAsync } from 'promises';
import { nextTick } from 'timeout';
import { Vector2D } from 'math';
import Escapist from '../../Services/Escapist/escapist';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { VibrationUI } from '../../Services/VibrationUI/vibration-ui';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'BubbleHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

enum BubbleTrigger {
    None = 0,
    Primary = 1,
    Secondary = 2,
}

enum ArrowSide {
    Top,
    Right,
    Bottom,
    Left,
}

interface Bubble {
    id: string;
    bubbleRef: string;
    triggerElement: HTMLElement;
    placement: Placement;
    position: Vector2D | null;
    historyStepId: string | null;
    bubbleElement: HTMLElement | null;
}

export class BubbleHost implements Disposable {
    private readonly hoverBubbleDelayMs = 50;
    private readonly bubbleOffset = 12;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private arrowRef: HTMLElement;
    private arrowSide: ArrowSide;
    private bubble: Bubble | null;

    public static create(blazorRef: DotNet.DotNetObject): BubbleHost {
        return new BubbleHost(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        debugLog?.log('constructor');
        merge(
            DocumentEvents.active.click$,
            DocumentEvents.active.contextmenu$,
        )
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onClick(event));

        Escapist.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => {
                if (this.bubble != null) {
                    stopEvent(event);
                    this.hide();
                }
            });
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
        position?: Vector2D | null,
    ): void {
        let bubble = this.create(bubbleRef, triggerElement, placement, position);
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
            // This is the very first render of hover menu
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
        position: Vector2D | null,
    ): Bubble {
        if (!(triggerElement instanceof HTMLElement)) {
            const triggerElementId = triggerElement as string;
            triggerElement = document.getElementById(triggerElementId);
        }
        const dataset = triggerElement.dataset['bubble'];
        const nextElementClass = String(dataset).split('|')[1]
        const nextElement = document.getElementById(nextElementClass) as HTMLElement;
        placement = placement ?? getPlacementFromAttributes(triggerElement);
        return {
            id: nextId(),
            bubbleRef: bubbleRef,
            triggerElement: nextElement,
            placement: placement,
            position: position,
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
            throw new Error(`${LogScope}.show: bubble == null.`);

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
            throw new Error(`${LogScope}.position: bubble == null.`);

        if (updatedBubble) {
            bubble.bubbleElement = updatedBubble.bubbleElement ?? bubble.bubbleElement;
            bubble.placement = updatedBubble.placement ?? bubble.placement;
            bubble.position = updatedBubble.position ?? bubble.position;
        }

        let bubbleElement = bubble.bubbleElement;
        if (!bubbleElement)
            return;

        debugLog?.log(`position: bubble:`, bubble);
        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block'

        let referenceElement: ReferenceElement;
        const middleware: Middleware[] = [];
        const position = bubble.position;
        if (position && !isButtonTrigger(bubble.triggerElement)) {
            // Pointer relative positioning
            referenceElement = {
                getBoundingClientRect() {
                    return {
                        width: 0,
                        height: 0,
                        x: position.x,
                        y: position.y,
                        top: position.y,
                        left: position.x,
                        right: position.x,
                        bottom: position.y,
                    };
                },
            } as VirtualElement;
            middleware.push(flip());
            middleware.push(shift({ padding: 5 }));
        } else {
            // Trigger element relative positioning
            referenceElement = bubble.triggerElement;
            middleware.push(offset(this.bubbleOffset));
            middleware.push(flip());
            middleware.push(shift({ padding: 5 }));
        }
        const { x, y } = await computePosition(
            referenceElement,
            bubbleElement,
            {
                placement: bubble.placement ?? 'top',
                middleware: middleware,
            });
        Object.assign(bubbleElement.style, {
            left: `${x}px`,
            top: `${y}px`,
        });
        this.arrowRef = document.getElementsByClassName('ac-bubble-arrow')[0] as HTMLElement;
        this.getArrowSide(referenceElement, bubbleElement);
        this.arrowPosition(referenceElement, bubbleElement);
        console.log('arrowSide: ', this.arrowSide);
    }

    private arrowPosition(reference, bubble) {
        const refRect = reference.getBoundingClientRect();
        const bubbleRect = bubble.getBoundingClientRect();
        this.getArrowSide(refRect, bubbleRect);
        let x: number;
        let y: number;
        if (this.arrowSide == ArrowSide.Left) {
            x = bubbleRect.left - 4;
        } else if (this.arrowSide == ArrowSide.Right) {
            x = bubbleRect.right - 4;
        } else if (this.arrowSide == ArrowSide.Top) {
            y = bubbleRect.top - 4;
        } else {
            y = bubbleRect.bottom - 4;
        }

        if (this.arrowSide == ArrowSide.Left || this.arrowSide == ArrowSide.Right) {
            y = (refRect.bottom + refRect.top) / 2 - 4;
        } else {
            x = (refRect.left + refRect.right) / 2 - 4;
        }

        Object.assign(this.arrowRef.style, {
            left: `${x}px`,
            top: `${y}px`,
        });
    }

    private getArrowSide(refRect, bubbleRect) {
        if (Math.round(bubbleRect.top - refRect.bottom) == this.bubbleOffset)
            this.arrowSide = ArrowSide.Top;
        else if (Math.round(refRect.top - bubbleRect.bottom) == this.bubbleOffset)
            this.arrowSide = ArrowSide.Bottom;
        else if (Math.round(refRect.left - bubbleRect.right) == this.bubbleOffset)
            this.arrowSide = ArrowSide.Right;
        else this.arrowSide = ArrowSide.Left;
    }

    // Event handlers

    private onClick(event: Event): void {
        let trigger = BubbleTrigger.None
        if (event.type == 'click')
            trigger = BubbleTrigger.Primary;
        if (event.type == 'contextmenu')
            trigger = BubbleTrigger.Secondary;
        debugLog?.log('onClick, event:', event, ', trigger:', trigger);

        let isDesktopMode = this.isDesktopMode;

        // Ignore clicks which definitely aren't "ours"
        if (trigger == BubbleTrigger.None)
            return;
        if (!(event.target instanceof Element))
            return;

        let [triggerElement, bubbleRef] = getOrInheritData(event.target, 'bubble');
        if (triggerElement && bubbleRef) {
            const bubbleTrigger = BubbleTrigger[triggerElement.dataset['bubbleTrigger'] ?? 'Secondary'];
            if (trigger !== bubbleTrigger) {
                const altBubbleTrigger = bubbleTrigger == BubbleTrigger.Primary ? BubbleTrigger.Secondary : BubbleTrigger.None;
                if (!isDesktopMode || trigger != altBubbleTrigger)
                    bubbleRef = null;
            }
        }

        if (!bubbleRef) {
            // We couldn't find any bubble to activate on click
            const isClickInsideBubble = event.target.closest('.ac-bubble') != null;
            if (isClickInsideBubble) {
                // The bubble will process the action, but we can schedule bubble hiding here
                nextTick(() => this.hide({ id: this.bubble.id }));
                return;
            }

            this.hide();
            return stopEvent(event);
        }

        const position = isDesktopMode && event instanceof PointerEvent
                         ? new Vector2D(event.clientX, event.clientY)
                         : null;
        const bubble = this.create(bubbleRef, triggerElement, null, position);
        if (this.isShown(bubble)) {
            // Is it the second click on the same button that triggered the menu?
            if (triggerElement.nodeName == 'BUTTON')
                this.hide();
            else
                void this.position(this.bubble, bubble)
        }
        else
            this.show(bubble);

        stopEvent(event);
    }
}

// Helpers

let _nextId = 1;
// Menu Ids are used as HTML element Ids, so they need to have unique prefix
let nextId = () => 'bubble:' + (_nextId++).toString();

function getPlacementFromAttributes(triggerElement: HTMLElement): Placement | null {
    const placement = triggerElement.dataset['bubblePlacement'];
    return placement?.length > 0 ? placement as Placement : null;
}

function isButtonTrigger(triggerElement: HTMLElement | null): boolean {
    if (!triggerElement)
        return false;

    if (!(triggerElement.closest('button') instanceof HTMLElement))
        return false;

    // Buttons inside bubbles aren't counted as triggers
    return triggerElement.closest('.ac-bubble') == null;
}
