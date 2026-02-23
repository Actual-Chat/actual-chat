type CacheListener = (url: string) => void;

const BITMAP_THRESHOLD = 128;

export class SvgCache {
    private static readonly cache = new Map<string, string>();
    private static readonly pending = new Map<string, Promise<string>>();
    private static readonly listeners = new Map<string, Set<CacheListener>>();

    static get(cacheKey: string): string | undefined {
        return this.cache.get(cacheKey);
    }

    static add(cacheKey: string, svgString: string, pixelSize: number): void {
        if (this.cache.has(cacheKey) || this.pending.has(cacheKey)) return;

        const promise =
            pixelSize <= BITMAP_THRESHOLD
                ? SvgCache.renderToBitmap(svgString, pixelSize)
                : SvgCache.renderToSvgBlob(svgString);

        this.pending.set(cacheKey, promise);
        void promise.then(url => {
            this.cache.set(cacheKey, url);
            this.pending.delete(cacheKey);
            SvgCache.notifyListeners(cacheKey, url);
        });
    }

    static clear(): void {
        for (const url of this.cache.values()) URL.revokeObjectURL(url);
        this.cache.clear();
        this.pending.clear();
        this.listeners.clear();
    }

    static onCached(cacheKey: string, listener: CacheListener): () => void {
        let set = this.listeners.get(cacheKey);
        if (!set) {
            set = new Set();
            this.listeners.set(cacheKey, set);
        }
        set.add(listener);
        return () => {
            set.delete(listener);
            if (set.size === 0) this.listeners.delete(cacheKey);
        };
    }

    // Private methods

    private static notifyListeners(cacheKey: string, url: string): void {
        const set = this.listeners.get(cacheKey);
        if (!set) return;

        this.listeners.delete(cacheKey);
        for (const listener of set) listener(url);
    }

    private static renderToSvgBlob(svgString: string): Promise<string> {
        const blob = new Blob([svgString], { type: 'image/svg+xml' });
        return Promise.resolve(URL.createObjectURL(blob));
    }

    private static renderToBitmap(svgString: string, pixelSize: number): Promise<string> {
        return new Promise<string>((resolve, reject) => {
            const img = new Image();
            const blob = new Blob([svgString], { type: 'image/svg+xml' });
            const svgUrl = URL.createObjectURL(blob);
            img.onload = () => {
                URL.revokeObjectURL(svgUrl);
                const canvas = document.createElement('canvas');
                canvas.width = pixelSize;
                canvas.height = pixelSize;
                const ctx = canvas.getContext('2d')!;
                ctx.drawImage(img, 0, 0, pixelSize, pixelSize);
                canvas.toBlob(pngBlob => {
                    if (pngBlob) resolve(URL.createObjectURL(pngBlob));
                    else reject(new Error('canvas.toBlob failed'));
                }, 'image/png');
            };
            img.onerror = () => {
                URL.revokeObjectURL(svgUrl);
                reject(new Error('SVG image load failed'));
            };
            img.src = svgUrl;
        });
    }
}
