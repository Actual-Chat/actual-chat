const cache = new Map<string, string>();
const pending = new Map<string, Promise<string>>();

const BITMAP_THRESHOLD = 128;

export function getCachedUrl(cacheKey: string): string | undefined {
    return cache.get(cacheKey);
}

export function cacheAvatar(cacheKey: string, svgString: string, pixelSize: number): void {
    if (cache.has(cacheKey) || pending.has(cacheKey))
        return;

    const promise = pixelSize <= BITMAP_THRESHOLD
        ? renderToBitmap(svgString, pixelSize)
        : renderToSvgBlob(svgString);

    pending.set(cacheKey, promise);
    void promise.then(url => {
        cache.set(cacheKey, url);
        pending.delete(cacheKey);
        notifyListeners(cacheKey, url);
    });
}

// Listener support: components register to be notified when their cache entry is ready
type CacheListener = (url: string) => void;
const listeners = new Map<string, Set<CacheListener>>();

export function onCached(cacheKey: string, listener: CacheListener): () => void {
    let set = listeners.get(cacheKey);
    if (!set) {
        set = new Set();
        listeners.set(cacheKey, set);
    }
    set.add(listener);
    return () => {
        set.delete(listener);
        if (set.size === 0)
            listeners.delete(cacheKey);
    };
}

function notifyListeners(cacheKey: string, url: string): void {
    const set = listeners.get(cacheKey);
    if (!set)
        return;

    listeners.delete(cacheKey);
    for (const listener of set)
        listener(url);
}

function renderToSvgBlob(svgString: string): Promise<string> {
    const blob = new Blob([svgString], { type: 'image/svg+xml' });
    return Promise.resolve(URL.createObjectURL(blob));
}

function renderToBitmap(svgString: string, pixelSize: number): Promise<string> {
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
                if (pngBlob)
                    resolve(URL.createObjectURL(pngBlob));
                else
                    reject(new Error('canvas.toBlob failed'));
            }, 'image/png');
        };
        img.onerror = () => {
            URL.revokeObjectURL(svgUrl);
            reject(new Error('SVG image load failed'));
        };
        img.src = svgUrl;
    });
}
