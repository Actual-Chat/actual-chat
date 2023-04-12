import { computePosition, flip, Middleware, offset, Placement, shift } from '@floating-ui/dom';
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
        const position = triggerElement.dataset['bubblePosition'] as Placement;
        const bubbleElement = document.getElementById(id);

        if (bubbleElement.style.display != 'block')
            bubbleElement.style.display = 'block'

        const middleware: Middleware[] = [];
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
        const { x, y } = await computePosition(
            triggerElement,
            bubbleElement,
            {
                placement: position,
                middleware: middleware,
            });
        Object.assign(bubbleElement.style, {
            left: `${x}px`,
            top: `${y}px`,
        });
    }
}
