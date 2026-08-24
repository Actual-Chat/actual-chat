import { getLogs } from 'logging';

const { debugLog } = getLogs('LocalizationUI');

const storage = window.localStorage as Storage | null;
const storageKey = 'ui.language';
const urlParameter = 'ui-language';

export interface UILanguageInfo {
    urlOverride: string | null;
    selected: string | null;
    clientLanguages: string[];
}

// The selection lives here rather than in LocalSettings because it must be readable
// before Blazor starts - and because LocalSettings is wiped whenever the session changes.
// Which of clientLanguages the app actually renders in is decided in .NET: the catalog
// list (Languages.AllUI) is there, and duplicating it here would be a second source of truth.
export class LocalizationUI {
    public static urlOverride: string | null;
    public static selected: string | null;
    public static languageInfo?: UILanguageInfo;

    public static init(): void {
        this.urlOverride = new URLSearchParams(location.search).get(urlParameter);
        this.selected = load();
        this.languageInfo = createInfo();
    }

    public static set(selected: string | null): void {
        if (this.selected === selected)
            return;

        debugLog?.log('set:', selected);
        this.selected = selected;
        save(selected);
        this.languageInfo = createInfo();
    }

    // Called with the effective language - i.e. the detected one when nothing is selected.
    public static setDocumentLanguage(language: string): void {
        document.documentElement.lang = language;
    }
}

function createInfo(): UILanguageInfo {
    return {
        urlOverride: LocalizationUI.urlOverride,
        selected: LocalizationUI.selected,
        clientLanguages: [...navigator.languages],
    }
}

function load(): string | null {
    if (!storage)
        return null;

    const selected = storage.getItem(storageKey) ?? null;
    debugLog?.log('load:', selected);
    return selected;
}

function save(selected: string | null): void {
    if (!storage)
        return;

    if (selected)
        storage.setItem(storageKey, selected);
    else
        storage.removeItem(storageKey);
}

LocalizationUI.init();
