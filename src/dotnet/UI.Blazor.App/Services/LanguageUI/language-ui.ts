import { getLogs } from 'logging';

const { debugLog } = getLogs('LanguageUI');

export class LanguageUI {
    public static getLanguages() {
        const languages = navigator.languages;
        debugLog?.log(`getLanguages:`, languages)
        return languages;
    }
}
