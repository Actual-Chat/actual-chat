import { Log } from 'logging';

const { debugLog } = Log.get('LanguageUI');

export class LanguageUI {
    public static getLanguages() {
        const languages = navigator.languages;
        debugLog?.log(`getLanguages:`, languages)
        return languages;
    }
}
