import { fromEvent, Subject, takeUntil } from 'rxjs';

export class MentionList {
    private readonly mentionList: HTMLElement;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private mentionListObserver: MutationObserver;

    static create(mentionList: HTMLElement): MentionList {
        return new MentionList(mentionList);
    }

    constructor(mentionList: HTMLElement) {
        this.mentionList = mentionList;
        this.mentionListObserver = new MutationObserver(this.scrollToCurrentItem);
        this.mentionListObserver.observe(this.mentionList, {
            attributes: true,
            childList: true,
            subtree: true,
        });
        fromEvent(this.mentionList, 'scroll')
            .pipe(takeUntil(this.disposed$),
        ).subscribe(() => this.mentionList.classList.add('expanded'));
    }

    private scrollToCurrentItem = (mutations, observer) => {
        for (const mutation of mutations) {
            if (mutation.type === 'attributes' && mutation.target.classList.contains('selected')) {
                const item = mutation.target as HTMLElement;
                const rect = item.getBoundingClientRect();
                const top = rect.top;
                const bottom = rect.bottom;
                const listRect = this.mentionList.getBoundingClientRect();
                const listTop = listRect.top;
                const listBottom = listRect.bottom;
                if (top < listTop) {
                    item.scrollIntoView({behavior: 'smooth', block: 'start'});
                } else if (bottom > listBottom) {
                    item.scrollIntoView({behavior: 'smooth', block: 'end'});
                }
            }
        }
    };

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.mentionListObserver.disconnect();
    }
}
