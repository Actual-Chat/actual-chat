import { fromEvent, Subject, takeUntil } from 'rxjs';

export class MentionList {
    private readonly mentionList: HTMLElement;
    private readonly header: HTMLElement | null;
    private readonly editor: HTMLElement | null;
    private readonly resizeObserver: ResizeObserver;
    private readonly onViewportResize = () => this.scheduleMeasure();
    private readonly disposed$: Subject<void> = new Subject<void>();
    private measureScheduled = false;
    private scrollScheduled = false;

    static create(mentionList: HTMLElement): MentionList {
        return new MentionList(mentionList);
    }

    constructor(mentionList: HTMLElement) {
        this.mentionList = mentionList;
        this.editor = mentionList.closest<HTMLElement>('.chat-message-editor');
        const layout = mentionList.closest('.list-view-layout') ?? document;
        this.header = layout.querySelector<HTMLElement>('.layout-header');

        fromEvent(this.mentionList, 'scroll')
            .pipe(takeUntil(this.disposed$),
            ).subscribe(() => this.mentionList.classList.add('expanded'));

        this.resizeObserver = new ResizeObserver(() => this.scheduleMeasure());
        if (this.header)
            this.resizeObserver.observe(this.header);
        if (this.editor)
            this.resizeObserver.observe(this.editor);
        window.visualViewport?.addEventListener('resize', this.onViewportResize);
        this.scheduleMeasure();
    }

    private scheduleMeasure() {
        if (this.measureScheduled)
            return;
        this.measureScheduled = true;
        requestAnimationFrame(() => {
            this.measureScheduled = false;
            this.measureAvailable();
        });
    }

    private measureAvailable() {
        if (!this.editor)
            return;
        const editorTop = this.editor.getBoundingClientRect().top;
        const headerBottom = this.header?.getBoundingClientRect().bottom ?? 0;
        const listTop = this.mentionList.getBoundingClientRect().top;
        const chips = this.mentionList.parentElement?.querySelector<HTMLElement>('.mention-list-chips');
        const chipsRect = chips?.getBoundingClientRect();
        // Chips pill is anchored above the list via `-top-5`; subtract how far it overhangs.
        const chipsOverhang = chipsRect && chipsRect.height > 0
            ? Math.max(0, listTop - chipsRect.top)
            : 20;
        const available = Math.max(0, editorTop - headerBottom - 16 - chipsOverhang);
        this.mentionList.style.setProperty('--mention-list-max-h', `${available}px`);
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

        this.resizeObserver.disconnect();
        window.visualViewport?.removeEventListener('resize', this.onViewportResize);
        this.disposed$.next();
        this.disposed$.complete();
    }
}
