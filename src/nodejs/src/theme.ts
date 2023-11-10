import { BrowserInfo } from "../../dotnet/UI.Blazor/Services/BrowserInfo/browser-info";
import { Log } from 'logging';

const { debugLog } = Log.get('Theme');

const StorageKey = 'ui.theme'
const AvailableThemes = ['light', 'dark'];

export class Theme {
    public static theme : string | null = null;
    public static defaultTheme : string;
    public static appliedTheme = '';

    public static init(): void {
        this.defaultTheme = detectDefaultTheme();
        this.set(load());
        const defaultThemeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
        defaultThemeMediaQuery.addListener(_ => {
            const defaultTheme = detectDefaultTheme();
            if (Theme.defaultTheme !== defaultTheme) {
                Theme.defaultTheme = defaultTheme;
                BrowserInfo.onDefaultThemeChanged(defaultTheme);
            }
        });
    }

    public static set(theme: string | null) : string {
        const classList = document?.body?.classList;
        if (!classList)
            return;

        if (!AvailableThemes.find(x => x === theme))
            theme = null;

        if (this.theme !== theme) {
            save(theme);
            this.theme = theme;
        }

        const appliedTheme = theme ?? this.defaultTheme;
        if (this.appliedTheme !== appliedTheme) {
            const oldClass = `theme-${this.appliedTheme}`;
            const newClass = `theme-${appliedTheme}`;
            classList.remove(oldClass);
            classList.add(newClass);
            this.appliedTheme = appliedTheme;
        }
        return this.getColors();
    }

    public static getColors() : string {
        const style = getComputedStyle(document.body);
        const headerColor = style.getPropertyValue('--background-01');
        const postPanelColor = style.getPropertyValue('--post-panel');
        return normalizeColor(headerColor) + ";" + normalizeColor(postPanelColor);
    }
}

function detectDefaultTheme() {
    const defaultThemeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    return defaultThemeMediaQuery.matches ? 'dark' : 'light';
}

function normalizeColor(hexColor : string) : string {
    if (hexColor && hexColor.length === 4)
        return '#' + hexColor[1] + hexColor[1] + hexColor[2] + hexColor[2] + hexColor[3] + hexColor[3];
    return hexColor;
}

function load() : string | null {
    const storage = globalThis?.localStorage;
    if (!storage)
        return null;

    return storage.getItem(StorageKey);
}

function save(theme: string | null) : void {
    const storage = globalThis?.localStorage;
    if (!storage)
        return null;

    return storage.setItem(StorageKey, theme);
}

Theme.init();
