// Fades in a message's playable text once it arrives. The height of a streaming message is left
// alone: InfiniteList animates the whole item from what its content wants, and a second transition of
// the same property nested inside it would only be something for that one to chase.
export class ChatEntryMessageInternalView {
    private readonly messageMarkup: HTMLElement;
    private playableTextObserver: MutationObserver | null = null;

    static create(messageMarkup: HTMLElement): ChatEntryMessageInternalView {
        return new ChatEntryMessageInternalView(messageMarkup);
    }

    constructor(messageMarkup: HTMLElement) {
        this.messageMarkup = messageMarkup;
        if (messageMarkup.querySelector('.playable-text-markup') != null)
            return;

        this.playableTextObserver = new MutationObserver(this.onSmoothShowPlayableText);
        this.playableTextObserver.observe(this.messageMarkup, { childList: true, subtree: true });
    }

    public dispose(): void {
        this.playableTextObserver?.disconnect();
        this.playableTextObserver = null;
    }

    // Private methods

    private onSmoothShowPlayableText: MutationCallback = mutations => {
        for (const mutation of mutations) {
            if (mutation.type !== 'childList')
                continue;

            for (const node of mutation.addedNodes)
                if (node instanceof HTMLElement && node.classList.contains('playable-text-markup'))
                    node.classList.add('smooth-show');
        }
    };
}
