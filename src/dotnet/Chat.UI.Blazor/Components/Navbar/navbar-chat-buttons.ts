import { Subject } from 'rxjs';
import Sortable, { SortableEvent } from 'sortablejs';

export class NavbarChatButtons {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly list: HTMLElement;
    private readonly sortable: Sortable;

    static create(list: HTMLElement, blazorRef: DotNet.DotNetObject): NavbarChatButtons {
        return new NavbarChatButtons(list, blazorRef);
    }

    constructor(list: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.list = list;
        this.blazorRef = blazorRef;
        this.sortable = Sortable.create(
            this.list,
            {
                animation: 150,
                delay: 100,
                delayOnTouchOnly: true,
                onUpdate: (_: SortableEvent) => {
                    const chats = Array.from(this.list.children).map((x: HTMLElement) => x.dataset['chatId']);
                    void this.blazorRef.invokeMethodAsync('OnOrderChanged', chats);
                }
            });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.sortable.destroy();
    }
}
