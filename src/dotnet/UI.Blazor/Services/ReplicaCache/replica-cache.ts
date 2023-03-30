import { get, set } from 'idb-keyval';

export class ReplicaCache {
    public static init() {
    }

    public static set(key: string, value: string): Promise<void>
    {
        return set(key, value);
    }

    public static get(key: string): Promise<string | undefined>
    {
        return get(key);
    }
}

ReplicaCache.init();
