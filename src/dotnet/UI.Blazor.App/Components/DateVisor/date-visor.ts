import { debounce } from 'actuallab-core';

export class DateVisor {
    private readonly abortController = new AbortController();

    public static create(dateVisor: HTMLElement): DateVisor {
        return new DateVisor(dateVisor);
    }

    constructor(private readonly dateVisor: HTMLElement) {
        document.addEventListener('scroll', this.onScroll, {
            capture: true,
            passive: true,
            signal: this.abortController.signal,
        });
    }

    public dispose(): void {
        this.abortController.abort();
        this.hideDebounced.reset();
        this.dateVisor.removeAttribute('data-scrolling');
    }

    // Private methods

    private onScroll = (event: Event): void => {
        const scroller = event.target;
        if (!(scroller instanceof HTMLElement) || !scroller.classList.contains('chat-view')
            || scroller.dataset.identity !== this.dateVisor.dataset.chatId)
            return;

        this.hideDebounced();
        queueMicrotask(() => {
            if (this.abortController.signal.aborted)
                return;

            this.dateVisor.toggleAttribute('data-scrolling', !scroller.hasAttribute('data-sticky-end'));
        });
    };

    private readonly hideDebounced = debounce(() => this.dateVisor.removeAttribute('data-scrolling'), 800);
}
