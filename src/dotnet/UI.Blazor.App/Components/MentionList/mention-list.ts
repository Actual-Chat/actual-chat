/* eslint-disable @typescript-eslint/no-unused-vars, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
import { fromEvent, Subject, takeUntil } from 'rxjs';

export class MentionList {
    private readonly mentionList: HTMLElement;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private scrollScheduled = false;

    static create(mentionList: HTMLElement): MentionList {
        return new MentionList(mentionList);
    }

    constructor(mentionList: HTMLElement) {
        this.mentionList = mentionList;
        fromEvent(this.mentionList, 'scroll')
            .pipe(takeUntil(this.disposed$),
            ).subscribe(() => this.mentionList.classList.add('expanded'));
    }

    // Called from Blazor whenever the selected mention changes.
    public scrollSelectedIntoView() {
        if (this.scrollScheduled)
            return;
        this.scrollScheduled = true;
        requestAnimationFrame(() => {
            this.scrollScheduled = false;
            const item = this.mentionList.querySelector<HTMLElement>('.mention-list-item.selected');
            if (!item)
                return;
            const rect = item.getBoundingClientRect();
            const listRect = this.mentionList.getBoundingClientRect();
            if (rect.top < listRect.top)
                item.scrollIntoView({ behavior: 'smooth', block: 'start' });
            else if (rect.bottom > listRect.bottom)
                item.scrollIntoView({ behavior: 'smooth', block: 'end' });
        });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
