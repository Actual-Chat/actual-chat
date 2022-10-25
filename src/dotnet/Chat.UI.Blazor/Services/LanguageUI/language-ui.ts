import { Log, LogLevel } from '../../../../nodejs/src/logging';

const LogScope = 'LanguageUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class LanguageUI {
    public static getLanguages() {
        const languages = navigator.languages;
        debugLog?.log(`getLanguages:`, languages)
        return languages;
    }
}
