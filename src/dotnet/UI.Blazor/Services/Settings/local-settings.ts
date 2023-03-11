import { Log } from 'logging';

const { debugLog, warnLog } = Log.get('LocalSettings');

export class LocalSettings {
    private static _isInitialized: boolean = false;

    public static init(): void {
        if (LocalSettings._isInitialized)
            return
        const tagKey = ".App.sessionHash";
        // @ts-ignore
        const tagValue: string = window.App.sessionHash;
        if (tagValue != localStorage.getItem(tagKey)) {
            warnLog?.log(`init: local storage is cleared!`);
            localStorage.clear();
            localStorage.setItem(tagKey, tagValue);
        }
    }

    public static getMany(keys: string[]): Array<string> {
        LocalSettings.init();
        const result = new Array<string>();
        debugLog?.log(`getMany(${result.length} keys):`);
        for (const key of keys) {
            let value = localStorage.getItem(key);
            result.push(value);
            debugLog?.log(` · '${key}' -> '${value}'`);
        }
        return result;
    }

    public static setMany(updates: Record<string, string>): void {
        LocalSettings.init();
        debugLog?.log(`setMany(${Object.keys(updates).length} keys):`);
        for (const [key, value] of Object.entries(updates)) {
            if (value == null) {
                localStorage.removeItem(key);
                debugLog?.log(` - '${key}'`);
            }
            else {
                localStorage.setItem(key, value);
                debugLog?.log(` * '${key}' <- '${value}'`);
            }
        }
    }
}
