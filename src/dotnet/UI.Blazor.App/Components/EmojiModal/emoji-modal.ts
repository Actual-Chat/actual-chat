export class EmojiModal {
    private observer: IntersectionObserver | null = null;
    private recentScroll: RecentScroll | null = null;
    private gridForLoadEvents: HTMLElement | null = null;

    private readonly onMediaLoadOrError = (e: Event) => {
        const target = e.target;
        if (!(target instanceof HTMLImageElement))
            return;
        const item = target.closest<HTMLElement>('.c-gif-item');
        item?.classList.add('loaded');
    };

    static create(sentinels: HTMLElement[], blazorRef: DotNet.DotNetObject): EmojiModal {
        return new EmojiModal(sentinels, blazorRef);
    }

    constructor(
        sentinels: HTMLElement[],
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        const scrollContainer = sentinels[0]?.closest('.c-gif-scroll');
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
        for (const sentinel of sentinels)
            this.observer.observe(sentinel);

        // Mark .c-gif-item as .loaded once its <img> finishes (or errors).
        // 'load' / 'error' don't bubble — listen in capture phase on the grid.
        const grid = scrollContainer.querySelector<HTMLElement>('.c-gif-grid');
        if (grid) {
            grid.addEventListener('load', this.onMediaLoadOrError, { capture: true });
            grid.addEventListener('error', this.onMediaLoadOrError, { capture: true });
            this.gridForLoadEvents = grid;
        }

        const recentContainer = scrollContainer.querySelector<HTMLElement>('.c-gif-recent');
        if (recentContainer)
            this.recentScroll = new RecentScroll(recentContainer);
    }

    public dispose() {
        this.observer?.disconnect();
        this.observer = null;
        if (this.gridForLoadEvents) {
            this.gridForLoadEvents.removeEventListener('load', this.onMediaLoadOrError, { capture: true });
            this.gridForLoadEvents.removeEventListener('error', this.onMediaLoadOrError, { capture: true });
            this.gridForLoadEvents = null;
        }
        this.recentScroll?.dispose();
        this.recentScroll = null;
    }
}

class RecentScroll {
    private readonly list: HTMLElement;
    private readonly btnLeft: HTMLButtonElement;
    private readonly btnRight: HTMLButtonElement;

    private readonly onWheel = (e: WheelEvent) => {
        if (e.deltaY === 0)
            return;
        e.preventDefault();
        // Translate vertical wheel into horizontal scroll
        this.list.scrollBy({ left: e.deltaY, behavior: 'smooth' });
    };

    private readonly onScroll = () => this.updateArrows();

    private readonly wrap: HTMLElement;

    constructor(private readonly container: HTMLElement) {
        this.list = container.querySelector<HTMLElement>('.c-gif-recent-list')!;

        // Wrap list + arrows in a relative container for arrow positioning
        this.wrap = document.createElement('div');
        this.wrap.className = 'c-gif-recent-wrap';
        this.list.replaceWith(this.wrap);
        this.wrap.appendChild(this.list);

        this.btnLeft = this.createArrow('left');
        this.btnRight = this.createArrow('right');
        this.wrap.appendChild(this.btnLeft);
        this.wrap.appendChild(this.btnRight);

        this.list.addEventListener('wheel', this.onWheel, { passive: false });
        this.list.addEventListener('scroll', this.onScroll, { passive: true });
        this.updateArrows();
    }

    private createArrow(dir: 'left' | 'right'): HTMLButtonElement {
        const btn = document.createElement('button');
        btn.className = `c-gif-recent-arrow ${dir}`;
        btn.innerHTML = dir === 'left' ? '‹' : '›';
        btn.addEventListener('click', () => {
            const step = this.list.clientWidth * 0.6;
            this.list.scrollBy({ left: dir === 'left' ? -step : step, behavior: 'smooth' });
        });
        return btn;
    }

    private updateArrows() {
        const { scrollLeft, scrollWidth, clientWidth } = this.list;
        this.btnLeft.classList.toggle('hidden', scrollLeft <= 0);
        this.btnRight.classList.toggle('hidden', scrollLeft + clientWidth >= scrollWidth - 1);
    }

    public dispose() {
        this.list.removeEventListener('wheel', this.onWheel);
        this.list.removeEventListener('scroll', this.onScroll);
        this.btnLeft.remove();
        this.btnRight.remove();
    }
}
