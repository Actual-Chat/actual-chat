import { get, set } from 'idb-keyval';

export class ReplicaCache {
    public static init() {
    }

    public static set(key: string, value: any): Promise<void>
    {
        return set(key, value);
    }

    public static get<T = any>(key: string): Promise<T | undefined>
    {
        return get(key);
    }
}

ReplicaCache.init();
