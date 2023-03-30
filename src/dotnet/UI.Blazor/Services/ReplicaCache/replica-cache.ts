import { get, set, clear } from 'idb-keyval';

export class ReplicaCache {
    public static init() {
    }

    public static set(key: string, value: string): Promise<void> {
        return set(key, value);
    }

    public static get(key: string): Promise<string | undefined> {
        return get(key);
    }

    public static clear(): Promise<void> {
        return clear();
    }
}

ReplicaCache.init();
