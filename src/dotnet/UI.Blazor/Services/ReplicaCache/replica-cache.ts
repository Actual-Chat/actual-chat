import { get, set, del, clear } from 'idb-keyval';

export class ReplicaCache {
    public static init(): void {
    }

    public static get(key: string): Promise<string | null> {
        return get(key) ?? null;
    }

    public static set(key: string, value: string): Promise<void> {
        if (value == null)
            return del(key)
        else
            return set(key, value);
    }

    public static clear(): Promise<void> {
        return clear();
    }
}

ReplicaCache.init();
