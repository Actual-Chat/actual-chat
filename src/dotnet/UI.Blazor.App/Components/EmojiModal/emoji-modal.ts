export class EmojiModal {
    private observer: IntersectionObserver | null = null;

    static create(sentinel: HTMLElement, blazorRef: DotNet.DotNetObject): EmojiModal {
        return new EmojiModal(sentinel, blazorRef);
    }

    constructor(
        sentinel: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        const scrollContainer = sentinel.closest('.c-gif-scroll');
        if (!scrollContainer)
            return;
        this.observer = new IntersectionObserver(
            entries => {
                for (const entry of entries) {
                    if (entry.isIntersecting)
                        void this.blazorRef.invokeMethodAsync('OnSentinelVisible');
                }
            },
            { root: scrollContainer, rootMargin: '0px 0px 200px 0px' },
        );
        this.observer.observe(sentinel);
    }

    public dispose() {
        this.observer?.disconnect();
        this.observer = null;
    }
}
