const LogScope: string = 'LocalSettings';
const DebugMode: boolean = true;

export class LocalSettings {
    private static _isInitialized: boolean = false;

    public static initialize() {
        if (LocalSettings._isInitialized)
            return
        const tagKey = ".App.sessionHash";
        // @ts-ignore
        const tagValue: string = window.App.sessionHash;
        if (tagValue != localStorage.getItem(tagKey)) {
            if (DebugMode)
                console.info(`${LogScope}: local storage is cleared!`);
            localStorage.clear();
            localStorage.setItem(tagKey, tagValue);
        }
    }

    public static getMany(keys: string[]) {
        LocalSettings.initialize();
        const result = new Array<string>();
        if (DebugMode)
            console.debug(`${LogScope}: getMany(${result.length} keys):`);
        for (const key of keys) {
            let value = localStorage.getItem(key);
            result.push(value);
            if (DebugMode)
                console.debug(`${LogScope}: · '${key}' -> '${value}'`);
        }
        return result;
    }

    public static setMany(updates: Record<string, string>) {
        LocalSettings.initialize();
        if (DebugMode)
            console.debug(`${LogScope}: setMany(${Object.keys(updates).length} keys):`);
        for (const [key, value] of Object.entries(updates)) {
            if (value == null) {
                localStorage.removeItem(key);
                if (DebugMode)
                    console.debug(`${LogScope}: - '${key}'`);
            }
            else {
                localStorage.setItem(key, value);
                if (DebugMode)
                    console.debug(`${LogScope}: * '${key}' <- '${value}'`);
            }
        }
    }
}
