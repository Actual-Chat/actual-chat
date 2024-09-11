import { Subject } from 'rxjs';
import Sortable, { SortableEvent } from 'sortablejs';
import { DeviceInfo } from 'device-info';
import { Tune, TuneUI } from '../../../UI.Blazor/Services/TuneUI/tune-ui';

export class NavbarPlaceButtons {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly list: HTMLElement;
    private readonly sortable: Sortable;

    static create(list: HTMLElement, blazorRef: DotNet.DotNetObject): NavbarPlaceButtons {
        return new NavbarPlaceButtons(list, blazorRef);
    }

    constructor(list: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.list = list;
        this.blazorRef = blazorRef;

        const options: Sortable.Options = DeviceInfo.isTouchCapable
            ? {
                dataIdAttr: 'data-place-id',
                animation: 150,
                delay: 500,
                dragClass: 'sortable-target',
                chosenClass: 'sortable-target',
                delayOnTouchOnly: true,
                onUpdate: (_: SortableEvent) => {
                    const places = this.sortable.toArray();
                    void this.blazorRef.invokeMethodAsync('OnOrderChanged', places);
                },
                onChoose: (_: SortableEvent) => {
                    TuneUI.play(Tune.DragStart);
                },
            }
            : {
                dataIdAttr: 'data-place-id',
                animation: 150,
                handle: '.c-dots',
                onUpdate: (_: SortableEvent) => {
                    const places = this.sortable.toArray();
                    void this.blazorRef.invokeMethodAsync('OnOrderChanged', places);
                },
            };
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
