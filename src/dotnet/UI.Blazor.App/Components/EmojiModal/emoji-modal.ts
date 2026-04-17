export class EmojiModal {
    private observer: IntersectionObserver | null = null;

    static create(sentinel: HTMLElement, blazorRef: DotNet.DotNetObject): EmojiModal | null {
        if (!(sentinel instanceof HTMLElement))
            return null;

        const scrollContainer = sentinel.closest('.tab');
        if (!scrollContainer)
            return null;

        return new EmojiModal(sentinel, scrollContainer, blazorRef);
    }

    constructor(
        sentinel: HTMLElement,
        scrollContainer: Element,
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
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
