// Fills duration badges for *old* videos in the right-panel Media grid — the ones
// whose duration wasn't persisted server-side (VisualMediaItem.DurationMs == 0).
// Reuses VirtualList's own visibility signal (no extra IntersectionObserver): the
// Blazor side forwards the set of visible row keys, and a row's metadata is only
// fetched once it has dwelled in view past DwellMs, so fast scrolling costs nothing.
const DwellMs = 1000;

export class VisualMediaDuration {
    private readonly container: HTMLElement;
    private readonly cache = new Map<string, string>();
    private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
    private isDisposed = false;

    static create(container: HTMLElement): VisualMediaDuration {
        return new VisualMediaDuration(container);
    }

    constructor(container: HTMLElement) {
        this.container = container;
    }

    public setVisibleKeys(keys: string[]) {
        if (this.isDisposed)
            return;
        const next = new Set(keys);
        for (const key of [...this.timers.keys()])
            if (!next.has(key)) {
                clearTimeout(this.timers.get(key));
                this.timers.delete(key);
            }
        for (const key of next) {
            this.fillCached(key);
            if (this.timers.has(key) || !this.hasPending(key))
                continue;
            this.timers.set(key, setTimeout(() => {
                this.timers.delete(key);
                this.resolveRow(key);
            }, DwellMs));
        }
    }

    public dispose() {
        if (this.isDisposed)
            return;
        this.isDisposed = true;
        for (const timer of this.timers.values())
            clearTimeout(timer);
        this.timers.clear();
    }

    private pendingTiles(key: string): HTMLElement[] {
        const row = this.container.querySelector<HTMLElement>(`li.item[data-key="${CSS.escape(key)}"]`);
        if (!row)
            return [];
        return [...row.querySelectorAll<HTMLElement>('.c-duration[data-duration-url]:not([data-resolved])')];
    }

    private hasPending(key: string): boolean {
        return this.pendingTiles(key).length > 0;
    }

    private fillCached(key: string) {
        for (const tile of this.pendingTiles(key)) {
            const cached = this.cache.get(tile.dataset.mediaId ?? '');
            if (cached !== undefined)
                this.write(tile, cached);
        }
    }

    private resolveRow(key: string) {
        for (const tile of this.pendingTiles(key))
            this.resolveTile(tile);
    }

    private resolveTile(tile: HTMLElement) {
        const mediaId = tile.dataset.mediaId ?? '';
        const url = tile.dataset.durationUrl;
        if (!url)
            return;
        const cached = this.cache.get(mediaId);
        if (cached !== undefined) {
            this.write(tile, cached);
            return;
        }
        const video = document.createElement('video');
        video.preload = 'metadata';
        video.muted = true;
        video.onloadedmetadata = () => {
            const text = formatDuration(video.duration);
            video.removeAttribute('src');
            video.load();
            if (!text)
                return;
            this.cache.set(mediaId, text);
            this.write(tile, text);
        };
        video.onerror = () => {
            video.removeAttribute('src');
        };
        video.src = url;
    }

    private write(tile: HTMLElement, text: string) {
        tile.textContent = text;
        tile.setAttribute('data-resolved', '');
    }
}

function formatDuration(seconds: number): string {
    if (!Number.isFinite(seconds) || seconds <= 0)
        return '';
    const total = Math.round(seconds);
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    const pad = (n: number) => n.toString().padStart(2, '0');
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}
