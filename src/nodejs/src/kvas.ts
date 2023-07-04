import { UseStore, createStore, get, set, del, getMany, setMany, delMany, clear } from 'idb-keyval';
import { Log } from 'logging';

const { debugLog } = Log.get('Kvas');

export class Kvas {
    private readonly store: UseStore;
    private readonly debugLog: Log;

    constructor(
        public readonly name: string,
        mustLog = false,
    ) {
        this.store = createStore(name, 'kvas');
        this.debugLog = mustLog ? debugLog : null;
        this.debugLog?.log(`${this.name}: .ctor()`)
    }

    public async get(key: string): Promise<unknown> {
        const value = await get(key, this.store) as unknown;
        return this.processGet(key, value);
    }

    public set(key: string, value: unknown): Promise<void> {
        value = this.processSet(key, value);
        if (value === null)
            return del(key, this.store);
        else
            return set(key, value, this.store);
    }

    public remove(key: string): Promise<void> {
        return this.set(key, null);
    }

    public clear(): Promise<void> {
        this.debugLog?.log(`${this.name}: clear()`);
        return clear(this.store);
    }

    public async getMany(keys: string[]): Promise<unknown[]> {
        const values = await getMany(keys, this.store);
        this.debugLog?.log(`${this.name}: batch read of ${keys.length} item(s):`);
        for (let i = 0; i < keys.length; i++)
            values[i] = this.processGet(keys[i], values[i]);
        return values as unknown[];
    }

    public async setMany(keys: string[], values: unknown[]): Promise<void> {
        const removeSet = new Array<string>();
        const setSet = new Array<[string, unknown]>()
        this.debugLog?.log(`${this.name}: batch write of ${keys.length} item(s):`);
        for (let i = 0; i < keys.length; i++) {
            const key = keys[i];
            const value = this.processSet(key, values[i]);
            if (value === null)
                removeSet.push(key);
            else
                setSet.push([key, value]);
        }
        await delMany(removeSet, this.store);
        await setMany(setSet, this.store);
    }

    // Private methods

    private processGet(key: string, value: unknown): unknown {
        if (value === undefined)
            value = null;
        this.debugLog?.log(`${this.name}: [ ]`, key, '=', value);
        return value;
    }

    private processSet(key: string, value: unknown): unknown {
        if (value === undefined || value === null) {
            this.debugLog?.log(`${this.name}: [-]`, key);
            value = null;
        }
        else
            this.debugLog?.log(`${this.name}: [+]`, key, '=', value);
        return value;
    }
}
