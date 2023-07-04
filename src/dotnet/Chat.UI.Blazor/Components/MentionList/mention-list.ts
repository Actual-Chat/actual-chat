export class MentionList {
    private blazorRef: DotNet.DotNetObject;
    private readonly mentionList: HTMLElement;
    private mentionListObserver: MutationObserver;

    static create(mentionList: HTMLElement, blazorRef: DotNet.DotNetObject): MentionList {
        return new MentionList(mentionList, blazorRef);
    }

    constructor(mentionList: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.mentionList = mentionList;
        this.blazorRef = blazorRef;
        this.mentionListObserver = new MutationObserver(this.scrollToCurrentItem);
        this.mentionListObserver.observe(this.mentionList, {
            attributes: true,
            childList: true,
            subtree: true,
        })
    }

    private scrollToCurrentItem = (mutationsList, observer) => {
        for (const mutation of mutationsList) {
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
        this.mentionListObserver.disconnect();
    }
}

