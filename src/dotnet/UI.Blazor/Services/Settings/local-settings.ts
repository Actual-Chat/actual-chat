import { Log } from 'logging';
import { BrowserInit } from '../BrowserInit/browser-init';

const { debugLog, warnLog } = Log.get('LocalSettings');

export class LocalSettings {
    private static _instance: LocalSettings;

    public static getInstance() : LocalSettings {
        if (!this._instance) {
            this._instance = new LocalSettings();
            this._instance.init()
        }
        return this._instance;
    }

    public getMany(keys: string[]): Array<string> {
        const result = new Array<string>();
        debugLog?.log(`getMany(${result.length} keys):`);
        for (const key of keys) {
            let value = localStorage.getItem(key);
            result.push(value);
            debugLog?.log(` · '${key}' -> '${value}'`);
        }
        return result;
    }

    public setMany(updates: Record<string, string>): void {
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

    private init(): void {
        const tagKey = ".App.sessionHash";
        // @ts-ignore
        const tagValue = BrowserInit.sessionHash;
        const oldTagValue = localStorage.getItem(tagKey);
        if (oldTagValue !== tagValue) {
            warnLog?.log(`init: local storage is cleared! ('${oldTagValue}' != '${tagValue}')`);
            localStorage.clear();
            localStorage.setItem(tagKey, tagValue);
        }
    }
}
