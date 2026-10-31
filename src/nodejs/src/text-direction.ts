/** Mirrors the page for testing right-to-left layouts without a right-to-left language: `?dir=rtl` in the
 *  URL (`?dir=ltr` to undo), `debugUI.setTextDirection`, or the Ctrl+Shift+L, R chord. Remembered in
 *  localStorage, so the next load comes up mirrored before Blazor starts (splash and skeletons included). */
export class TextDirection {
    private static readonly StorageKey = 'debug.textDirection';

    public static get current(): TextDirectionKind {
        return document.documentElement.dir === 'rtl' ? 'rtl' : 'ltr';
    }

    /** Applies the URL's `dir` parameter, or else whatever an earlier `set` left in localStorage. */
    public static restore(): void {
        const fromUrl = new URLSearchParams(location.search).get('dir');
        if (fromUrl === 'rtl' || fromUrl === 'ltr') {
            this.set(fromUrl);
            return;
        }

        this.apply(this.getPersisted());
    }

    public static set(direction: TextDirectionKind): void {
        try {
            if (direction === 'rtl')
                localStorage.setItem(this.StorageKey, direction);
            else
                localStorage.removeItem(this.StorageKey);
        } catch {
            // Storage is unavailable in some embedded contexts; the direction still applies for this load.
        }
        this.apply(direction);
    }

    public static toggle(): TextDirectionKind {
        const direction = this.current === 'rtl' ? 'ltr' : 'rtl';
        this.set(direction);
        return direction;
    }

    // Private methods

    private static apply(direction: TextDirectionKind): void {
        if (direction === 'rtl')
            document.documentElement.setAttribute('dir', 'rtl');
        else
            document.documentElement.removeAttribute('dir');
    }

    private static getPersisted(): TextDirectionKind {
        try {
            return localStorage.getItem(this.StorageKey) === 'rtl' ? 'rtl' : 'ltr';
        } catch {
            return 'ltr';
        }
    }
}

export type TextDirectionKind = 'ltr' | 'rtl';
