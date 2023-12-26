import { Subject, } from 'rxjs';

import { Log } from 'logging';
import { setTimeout } from 'timerQueue';

const { debugLog } = Log.get('SearchPanel');

enum Side {
    Left,
    Right,
}

export class SearchPanel {
    private readonly disposed$ = new Subject<void>();
    private panel: HTMLElement;
    private blazorRef: DotNet.DotNetObject;
    private readonly side: Side;

    static create(panel: HTMLElement, blazorRef: DotNet.DotNetObject, side: Side): SearchPanel {
        return new SearchPanel(panel, blazorRef, side);
    }

    constructor(
        panel: HTMLElement,
        blazorRef: DotNet.DotNetObject,
        side: Side,
    ) {
        this.panel = panel;
        this.blazorRef = blazorRef;
        this.side = side;

        let sideCls = this.side == Side.Right ? "right" : "left";
        this.panel.classList.add(sideCls);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private async smoothClosePanel() : Promise<void> {
        this.panel.classList.add('close');
        setTimeout(() => {
            this.blazorRef.invokeMethodAsync("ClosePanel");
        }, 200);
    }
}
