import { get, set, del, setMany, delMany, clear } from 'idb-keyval';

export class IndexedDb {
    public static async get(key: string): Promise<any | null> {
        const value = await get(key);
        return value === undefined ? null : value;
    }

    public static set(key: string, value: any): Promise<void> {
        if (value == null)
            return del(key)
        else
            return set(key, value);
    }

    public static async setMany(keys: IDBValidKey[], values: any[]): Promise<void> {
        const removeSet = new Array<IDBValidKey>();
        const setSet = new Array<[IDBValidKey, any]>()
        for (let i = 0; i < keys.length; i++) {
            const key = keys[i];
            const value = values[i];
            if (value === undefined || value === null)
                removeSet.push(key);
            else
                setSet.push([key, value]);
        }
        await delMany(removeSet);
        await setMany(setSet);
    }

    public static clear(): Promise<void> {
        return clear();
    }
}
