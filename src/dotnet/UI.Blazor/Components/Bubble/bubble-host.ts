import {
    Placement,
    computePosition,
    flip,
    shift,
    offset,
    arrow,
} from '@floating-ui/dom';
import { Log } from 'logging';

interface BubbleInfo {
    bubbleRef: string;
    order: number;
}

const { debugLog } = Log.get('BubbleHost');

export class BubbleHost {
    public static create(blazorRef: DotNet.DotNetObject): BubbleHost {
        return new BubbleHost(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        debugLog?.log('constructor');
    }

    public getBubblesByGroup(group: string): BubbleInfo[] {
        debugLog?.log(`getBubblesByGroup:`, group);

        const elements = [...document.querySelectorAll(`[data-bubble-group="${group}"]`)];
        return elements
            .map((el: HTMLElement) => {
                const bubbleInfo: BubbleInfo = {
                    bubbleRef: el.dataset['bubble'],
                    order: Number(el.dataset['bubbleOrder']),
                };
                return bubbleInfo;
            });
    }

    public async showBubble(id: string, bubbleRef: string): Promise<void> {
        debugLog?.log(`showBubble:`, id, bubbleRef);

        const triggerElement: HTMLElement = document.querySelector(`[data-bubble="${bubbleRef}"]`);
        const position = triggerElement.dataset['bubblePlacement'] as Placement;
        const bubbleElement = document.getElementById(id);
        const arrowElement = document.getElementsByClassName('ac-bubble-arrow')[0] as HTMLElement;

        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block'

        const { x, y, placement, middlewareData } = await computePosition(
            triggerElement,
            bubbleElement,
            {
                placement: position,
                middleware: [
                    offset(6),
                    flip(),
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
            [staticSide]: '-5px',
        });
    }
}
