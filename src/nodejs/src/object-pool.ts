import Denque from 'denque';

/** Object that can be reused after reset() call (for example for object pooling). */
export interface Resettable {
    /** Resets the state of the object. */
    reset(): void | PromiseLike<void>;
}

export function isResettable<T>(obj: T | Resettable): obj is Resettable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['reset'] === 'function';
}

/** Usage: new ObjectPool<Foo>() => new Foo()); can be async. */
export class ObjectPool<T>
{
    private readonly pool = new Denque<T>();
    private readonly factory: () => T | PromiseLike<T>;

    constructor(factory: () => T | PromiseLike<T>) {
        this.factory = factory;
    }

    /** Creates an object (with support of async initialization) or returns it from the pool */
    public async get(): Promise<T> {
        let item = this.pool.pop();
        if (item === undefined)
            item = await this.factory();
        return item;
    }

    /** Calls reset() (and await it) on a Resettable object and returns it to the pool */
    public async release(obj: T): Promise<void> {
        if (isResettable(obj))
            await obj.reset();
        this.pool.push(obj);
    }
}
