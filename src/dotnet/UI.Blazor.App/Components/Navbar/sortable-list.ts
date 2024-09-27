import { Subject } from 'rxjs';
import Sortable, { SortableEvent } from 'sortablejs';
import { DeviceInfo } from 'device-info';
import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';

export class SortableList {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly list: HTMLElement;
    private readonly sortable: Sortable;

    static create(list: HTMLElement, blazorRef: DotNet.DotNetObject, dataIdAttr : string): SortableList {
        return new SortableList(list, blazorRef, dataIdAttr);
    }

    constructor(list: HTMLElement, blazorRef: DotNet.DotNetObject, dataIdAttr : string) {
        this.list = list;
        this.blazorRef = blazorRef;

        const options: Sortable.Options = {
                dataIdAttr: dataIdAttr,
                animation: 150,
                onUpdate: (_: SortableEvent) => {
                    const places = this.sortable.toArray();
                    void this.blazorRef.invokeMethodAsync('OnOrderChanged', places);
                },
            };
        if (DeviceInfo.isTouchCapable) {
            options.delay = 500;
            options.dragClass = 'sortable-target';
            options.chosenClass = 'sortable-target';
            options.delayOnTouchOnly = true;
            options.onChoose = (_: SortableEvent) => {
                TuneUI.play(Tune.DragStart);
            };
        }
        else {
            options.handle = '.c-dots';
        }
        // Don't use HTML5 DnD on Windows clients.
        if (BrowserInfo.hostKind == 'MauiApp' && BrowserInfo.appKind == 'Windows')
            options.forceFallback = true;
        this.sortable = Sortable.create(this.list, options);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.sortable.destroy();
    }
}
