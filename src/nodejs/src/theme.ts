import { getLogs } from 'logging';
import { EventHandlerSet } from 'event-handling';

const { debugLog } = getLogs('Theme');

const storage = window.localStorage as Storage | null;
const storageKey = 'ui.theme'
const availableThemes = ['light', 'dark', 'ash'];

export interface ThemeInfo {
    theme: string | null;
    defaultTheme: string;
    currentTheme: string;
    colors: string;
}

export class Theme {
    public static theme : string | null;
    public static defaultTheme = '';
    public static currentTheme = '';
    public static info?: ThemeInfo;
    public static changed: EventHandlerSet<ThemeInfo> = new EventHandlerSet<ThemeInfo>();

    public static init(): void {
        this.theme = load();
        this.defaultTheme = detectDefaultTheme();
        this.apply(false);
        const defaultThemeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
        // TODO(AY): review eslint suppressions
        // eslint-disable-next-line @typescript-eslint/no-deprecated
        defaultThemeMediaQuery.addListener(_ => {
            Theme.defaultTheme = detectDefaultTheme();
            Theme.apply();
        });
    }

    public static set(theme: string | null): void {
        if (!availableThemes.find(x => x === theme))
            theme = null;

        if (this.theme === theme)
            return;

        debugLog?.log('setTheme:', theme);
        this.theme = theme;
        save(theme);
        this.apply();
    }

    private static apply(mustNotify = true): void {
        this.currentTheme = this.theme ?? this.defaultTheme;
        if (this.currentTheme === this.info?.currentTheme && this.defaultTheme === this.info.defaultTheme)
            return;

        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (document?.body) {
            const classList = document.body.classList;
            const oldClass = `theme-${this.info?.currentTheme ?? ''}`;
            const newClass = `theme-${this.currentTheme}`;
            classList.remove(oldClass);
            classList.add(newClass);
            // The root canvas (<html>) shows through the iOS keyboard's rounded-corner notches, where the
            // body background would otherwise propagate. Paint it with --post-panel so those corners match
            // the input bar and keyboard. --post-panel is defined on body, so it's read and set here.
            // TODO(AndreyY): check if it's possible to do this declaratively
            const postPanel = getComputedStyle(document.body).getPropertyValue('--post-panel');
            document.documentElement.style.backgroundColor = normalizeColor(postPanel);
        }

        this.info = createThemeInfo();
        if (mustNotify)
            this.changed.triggerSilently(this.info);
    }
}

function createThemeInfo(): ThemeInfo {
    return {
        theme: Theme.theme,
        defaultTheme: Theme.defaultTheme,
        currentTheme: Theme.currentTheme,
        colors: getColors(),
    }
}

function detectDefaultTheme() {
    if (!storage)
        return 'light';

    const defaultThemeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    return defaultThemeMediaQuery.matches ? 'dark' : 'light';
}

function getColors(): string {
    if (!storage)
        return '';

    const style = getComputedStyle(document.body);
    const headerColor = style.getPropertyValue('--background-01');
    const postPanelColor = style.getPropertyValue('--post-panel');
    return normalizeColor(headerColor) + ';' + normalizeColor(postPanelColor);
}

function normalizeColor(hexColor: string): string {
    if (hexColor.length === 4)
        return '#' + hexColor[1] + hexColor[1] + hexColor[2] + hexColor[2] + hexColor[3] + hexColor[3];
    return hexColor;
}

function load(): string | null {
    if (!storage)
        return null;

    const theme = storage.getItem(storageKey) ?? null;
    debugLog?.log('load:', theme);
    return theme;
}

function save(theme: string | null): void {
    if (!storage)
        return;

    debugLog?.log('save:', theme);
    if (theme)
        storage.setItem(storageKey, theme);
    else
        storage.removeItem(storageKey);
}

Theme.init();
