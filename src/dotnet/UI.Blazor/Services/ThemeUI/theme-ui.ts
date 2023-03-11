import { Log } from 'logging';

const { debugLog } = Log.get('ThemeUI');

export class ThemeUI {
    public static applyTheme(theme: string) {
        debugLog?.log(`applyTheme, theme:`, theme)
        const bodyClassList = document.body.classList;
        if (theme === "Light")
            bodyClassList.remove('theme-dark')
        if (theme === "Dark")
            bodyClassList.add('theme-dark')
    }
}
