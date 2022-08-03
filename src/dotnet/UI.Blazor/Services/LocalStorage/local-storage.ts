const LogScope: string = 'LocalStorage';
const DebugMode: boolean = true;

export class LocalStorage {
    public static getMany(keys: string[]) {
        const result = new Array<string>();
        if (DebugMode)
            console.debug(`${LogScope}: getMany(${result.length} keys):`);
        for (const key of keys) {
            let value = localStorage.getItem(key);
            result.push(value);
            if (DebugMode)
                console.debug(`${LogScope}: - "${key}" -> "${value}"`);
        }
        return result;
    }

    public static setMany(updates: Record<string, string>) {
        if (DebugMode)
            console.debug(`${LogScope}: setMany(${updates.length} keys):`);
        for (const [key, value] of Object.entries(updates)) {
            if (value == null) {
                localStorage.removeItem(key);
                if (DebugMode)
                    console.debug(`${LogScope}: * "${key}" <- "${value}"`);
            }
            else {
                localStorage.setItem(key, value);
                if (DebugMode)
                    console.debug(`${LogScope}: - "${key}"`);
            }
        }
    }
}
