const LogScope = 'LanguageUI';
const debug = true;

export class LanguageUI {
    public static getLanguages() {
        const languages = navigator.languages;
        if (debug)
            console.debug(`${LogScope}.getLanguages:`, languages)
        return languages;
    }
}
