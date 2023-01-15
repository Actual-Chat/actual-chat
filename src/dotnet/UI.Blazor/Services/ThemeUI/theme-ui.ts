import { Log, LogLevel } from 'logging';

const LogScope = 'ThemeUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

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
