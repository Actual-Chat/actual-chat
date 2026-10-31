// The right-to-left languages that have a UI catalog
const RightToLeftLanguages = new Set(['ar', 'he']);

/** The page's writing direction. It follows the UI language (`setForLanguage`), unless a direction is
 *  forced for testing: `?dir=rtl` / `?dir=ltr` in the URL, `debugUI.setTextDirection`, or the
 *  Ctrl+Shift+L, R chord. A forced right-to-left direction is remembered in localStorage, so the next
 *  load comes up mirrored before Blazor starts (splash and skeletons included). */
export class TextDirection {
    private static readonly StorageKey = 'debug.textDirection';
    private static _forced: TextDirectionKind | null = null;
    private static _isUrlRead = false;

    public static get current(): TextDirectionKind {
        return document.documentElement.dir === 'rtl' ? 'rtl' : 'ltr';
    }

    /** Applies a forced direction - the URL's `dir` parameter or a persisted `set` - if there is one. */
    public static restore(): void {
        const forced = this.getForced();
        if (forced)
            this.apply(forced);
    }

    /** Follows the language's direction, unless a direction is forced. */
    public static setForLanguage(language: string | null | undefined): void {
        if (this.getForced())
            return;

        this.apply(this.isRightToLeft(language) ? 'rtl' : 'ltr');
    }

    public static toggle(): TextDirectionKind {
        const direction = this.current === 'rtl' ? 'ltr' : 'rtl';
        // Back at the language's own direction: drop the override rather than persist it
        if (direction === (this.isRightToLeft(document.documentElement.lang) ? 'rtl' : 'ltr'))
            this.clear(direction);
        else
            this.set(direction);
        return direction;
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
        this._forced = direction;
        this.apply(direction);
    }

    /** Swaps a floating placement's left and right sides on a right-to-left page, so a menu that opens
     *  to the end of its trigger stays there; Floating UI mirrors the -start/-end alignments itself. */
    public static mirrorSide<T extends string>(placement: T): T {
        if (this.current !== 'rtl')
            return placement;

        return placement.replace(/^(left|right)/, side => side === 'left' ? 'right' : 'left') as T;
    }

    public static isRightToLeft(language: string | null | undefined): boolean {
        const primary = (language ?? '').split(/[-_]/)[0].toLowerCase();
        return RightToLeftLanguages.has(primary);
    }

    // Private methods

    private static clear(direction: TextDirectionKind): void {
        try {
            localStorage.removeItem(this.StorageKey);
        } catch {
            // Storage is unavailable; nothing was persisted
        }
        this._forced = null;
        this.apply(direction);
    }

    private static getForced(): TextDirectionKind | null {
        if (this._forced)
            return this._forced;

        // Read once: a toggle back to the language's direction must not be undone by the URL
        if (!this._isUrlRead) {
            this._isUrlRead = true;
            const fromUrl = new URLSearchParams(location.search).get('dir');
            if (fromUrl === 'rtl' || fromUrl === 'ltr') {
                this.set(fromUrl);
                return fromUrl;
            }
        }

        try {
            if (localStorage.getItem(this.StorageKey) === 'rtl')
                return this._forced = 'rtl';
        } catch {
            // Storage is unavailable; nothing was forced
        }
        return null;
    }

    private static apply(direction: TextDirectionKind): void {
        if (direction === 'rtl')
            document.documentElement.setAttribute('dir', 'rtl');
        else
            document.documentElement.removeAttribute('dir');
    }
}

export type TextDirectionKind = 'ltr' | 'rtl';
