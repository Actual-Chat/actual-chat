import { Subject, takeUntil } from 'rxjs';
import { Log } from 'logging';
import { Disposable } from 'disposable';
import { DocumentEvents, preventDefaultForEvent } from 'event-handling';
import { getOrInheritData } from 'dom-helpers';

const { debugLog } = Log.get('SelectionHost');

export class SelectionHost implements Disposable {
    private readonly disposed$ = new Subject<void>();
    private readonly selection: Set<string> = new Set<string>();

    public static create(
        blazorRef: DotNet.DotNetObject,
        chatEntryId: string): SelectionHost {
        return new SelectionHost(blazorRef, chatEntryId);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly chatEntryId: string,
    ) {
        debugLog?.log('constructor');
        this.selection.add(chatEntryId);

        DocumentEvents.capturedActive.click$
            .pipe(takeUntil(this.disposed$))
            .subscribe(async (x: MouseEvent) => {
                await this.onClick(x);
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private async onClick(event: MouseEvent): Promise<void> {
        debugLog?.log('onClick, event:', event);

        if (!(event.target instanceof Element))
            return;

        let [triggerElement, chatEntryId] = getOrInheritData(event.target, 'chatEntryId');
        if (!triggerElement || !chatEntryId)
            return;

        const transcriptionInProgress = triggerElement.getElementsByClassName('chat-message-transcript').length > 0;
        if (transcriptionInProgress)
            return;

        preventDefaultForEvent(event);

        if (this.selection.has(chatEntryId)) {
            debugLog?.log('onUnselect, chatEntryId:', chatEntryId);

            if (triggerElement.classList.contains('replied-message')) {
                triggerElement.classList.remove('replied-message');
            }

            this.selection.delete(chatEntryId);
            await this.blazorRef.invokeMethodAsync('OnUnselect', chatEntryId);
            return;
        }

        debugLog?.log('onSelect, chatEntryId:', chatEntryId);

        this.selection.add(chatEntryId);
        if (!triggerElement.classList.contains('replied-message'))
            triggerElement.classList.remove('replied-message');
        await this.blazorRef.invokeMethodAsync('OnSelect', chatEntryId);
    }
}
