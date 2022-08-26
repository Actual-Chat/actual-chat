const LogScope: string = 'MentionList';

export class MentionList {
    private blazorRef: DotNet.DotNetObject;
    private readonly mentionList: HTMLElement;
    private mentionListObserver : MutationObserver;
    private listTop: number;
    private listBottom: number;

    static create(mentionList: HTMLElement, blazorRef: DotNet.DotNetObject): MentionList {
        return new MentionList(mentionList, blazorRef);
    }

    constructor(mentionList: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.mentionList = mentionList;
        this.blazorRef = blazorRef;
        this.mentionListObserver = new MutationObserver(this.scrollAtCurrentItem);
        this.mentionListObserver.observe(this.mentionList, {
            attributes: true,
            childList: true,
            subtree: true,
        })
        const rect = this.mentionList.getBoundingClientRect();
        this.listTop = rect.top;
        this.listBottom = rect.bottom;
    }

    private scrollAtCurrentItem = (mutationsList, observer) => {
        for(const mutation of mutationsList) {
            if (mutation.type === 'attributes' && mutation.target.classList.contains('bg-mention-list-hover')) {
                const item = mutation.target as HTMLElement;
                const rect = item.getBoundingClientRect();
                const top = rect.top;
                const bottom = rect.bottom;
                if (top < this.listTop) {
                    item.scrollIntoView({behavior: 'smooth', block: 'start'});
                } else if (bottom > this.listBottom) {
                    item.scrollIntoView({behavior: 'smooth', block: 'end'});
                }
            }
        }
    };

    public dispose() {
        this.mentionListObserver.disconnect();
    }
}

