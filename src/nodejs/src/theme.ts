import { BrowserInfo } from "../../dotnet/UI.Blazor/Services/BrowserInfo/browser-info";
import { Log } from 'logging';

const { debugLog } = Log.get('Theme');

const StorageKey = 'ui.theme'
const AvailableThemes = ['light', 'dark'];
const Storage = globalThis?.localStorage;
const IsEnabled = document?.body != null && Storage != null;

export interface ThemeInfo {
    theme: string | null;
    defaultTheme: string;
    currentTheme: string;
    colors: string;
}

export class Theme {
    public static theme : string | null = null;
    public static defaultTheme = '';
    public static currentTheme = '';
    public static info: ThemeInfo = { theme: '', defaultTheme: '', currentTheme: '', colors: '' };

    public static init(): void {
        this.theme = load();
        this.defaultTheme = detectDefaultTheme();
        this.apply(false);
        const defaultThemeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
        defaultThemeMediaQuery.addListener(_ => {
            Theme.defaultTheme = detectDefaultTheme();
            this.apply();
        });
    }

    public static set(theme: string | null) : void {
        if (!AvailableThemes.find(x => x === theme))
            theme = null;

        if (this.theme === theme)
            return;

        this.theme = theme;
        save(theme);
        this.apply();
    }

    public static apply(mustNotify = true) : ThemeInfo {
        this.currentTheme = this.theme ?? this.defaultTheme;
        if (this.currentTheme === this.info.currentTheme && this.defaultTheme === this.info.defaultTheme)
            return;

        if (!IsEnabled)
            return;

        const classList = document.body.classList;
        const oldClass = `theme-${this.info.currentTheme}`;
        const newClass = `theme-${this.currentTheme}`;
        classList.remove(oldClass);
        classList.add(newClass);

        this.info = createThemeInfo();
        if (mustNotify)
            BrowserInfo.onThemeChanged(this.info);
    }

}

function createThemeInfo() : ThemeInfo {
    return {
        theme: Theme.theme,
        defaultTheme: Theme.defaultTheme,
        currentTheme: Theme.currentTheme,
        colors: getColors(),
    }
}

function detectDefaultTheme() {
    if (!IsEnabled)
        return 'light';

    const defaultThemeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    return defaultThemeMediaQuery.matches ? 'dark' : 'light';
}

function getColors() : string {
    if (!IsEnabled)
        return '';

    const style = getComputedStyle(document.body);
    const headerColor = style.getPropertyValue('--background-01');
    const postPanelColor = style.getPropertyValue('--post-panel');
    return normalizeColor(headerColor) + ";" + normalizeColor(postPanelColor);
}

function normalizeColor(hexColor : string) : string {
    if (hexColor && hexColor.length === 4)
        return '#' + hexColor[1] + hexColor[1] + hexColor[2] + hexColor[2] + hexColor[3] + hexColor[3];
    return hexColor;
}

function load() : string | null {
    if (!IsEnabled)
        return;

    const theme = Storage.getItem(StorageKey) ?? null;
    debugLog?.log('load:', theme);
    return theme;
}

function save(theme: string | null) : void {
    if (!IsEnabled)
        return;

    debugLog?.log('save:', theme);
    if (theme)
        Storage.setItem(StorageKey, theme);
    else
        Storage.removeItem(StorageKey);
}

Theme.init();
