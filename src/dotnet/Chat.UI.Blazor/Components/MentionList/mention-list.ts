const LogScope: string = 'MentionList';

export class MentionList {
    private blazorRef: DotNet.DotNetObject;
    private readonly manager: HTMLElement;
    private readonly managerObserver : MutationObserver;
    private mentionListObserver : MutationObserver;
    private listTop: number;
    private listBottom: number;

    static create(manager: HTMLElement, blazorRef: DotNet.DotNetObject): MentionList {
        return new MentionList(manager, blazorRef);
    }

    constructor(manager: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.manager = manager;
        this.blazorRef = blazorRef;
        this.managerObserver = new MutationObserver(this.getMentionList);
        this.managerObserver.observe(this.manager, {
            attributes: true,
            childList: true,
            subtree: true,
        });
    }

    private getMentionList = (mutationsList, observer) => {
        for(const mutation of mutationsList) {
            for(const added_node of mutation.addedNodes) {
                if (added_node.nodeName == 'DIV') {
                    const element = added_node as HTMLElement;
                    if(element.classList.contains('mention-list')) {
                        const items = element.querySelectorAll('.mention-item');
                        if (items.length > 6) {
                            const rect = element.getBoundingClientRect();
                            this.listTop = rect.top;
                            this.listBottom = rect.bottom;
                            this.mentionListObserver = new MutationObserver(this.scrollAtCurrentItem);
                            this.mentionListObserver.observe(element, {
                                attributes: true,
                                childList: true,
                                subtree: true,
                            })
                        }
                    }
                }
            }
        }
    };

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
}

