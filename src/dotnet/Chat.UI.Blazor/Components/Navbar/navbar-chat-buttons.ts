import { Subject } from 'rxjs';

export class NavbarChatButtons {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly chats: HTMLElement;

    static create(places: HTMLElement, blazorRef: DotNet.DotNetObject): NavbarChatButtons {
        return new NavbarChatButtons(places, blazorRef);
    }

    constructor(chats: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.chats = chats;
        this.blazorRef = blazorRef;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
