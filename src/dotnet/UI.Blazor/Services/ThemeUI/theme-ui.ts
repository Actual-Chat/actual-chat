const LogScope = 'ThemeUI';
const debug = true;

export class ThemeUI {
    public static applyTheme(theme: string) {
        if (debug)
            console.debug(`${LogScope}.applyTheme(${theme})`)
        const bodyClassList = document.body.classList;
        if (theme === "Light")
            bodyClassList.remove('theme-dark')
        if (theme === "Dark")
            bodyClassList.add('theme-dark')
    }
}
