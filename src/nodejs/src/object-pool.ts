import Denque from 'denque';
import { isPromise } from 'is-promise';

/** Object that can be reused after reset() call (for example for object pooling). */
export interface Resettable {
    /** Resets the state of the object. */
    reset(): void | PromiseLike<void>;
}

export function isResettable<T>(obj: T | Resettable): obj is Resettable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['reset'] === 'function';
}

/** Usage: new ObjectPool<Foo, [number, number]>((arg1, arg2) => new Foo(arg1, arg2)); can be async. */
export class ObjectPool<T, AllocArgs extends any[] = any[]>
{
    private pool = new Denque<T>();
    private factory: (...args: AllocArgs) => T | PromiseLike<T>;

    constructor(factory: (...args: AllocArgs) => T | PromiseLike<T>) {
        this.factory = factory;
    }

    /** Creates an object (with support of async initialization) or returns it from the pool */
    public async get(...args: AllocArgs): Promise<T> {
        const item = this.pool.pop();
        if (item === undefined) {
            const created = this.factory(...args);
            return isPromise(created) ? await created : created;
        }
        return item;
    }

    /** Calls reset() (and await it) on a Resettable object and returns it to the pool */
    public async release(obj: T): Promise<void> {
        if (isResettable(obj)) {
            const result = obj.reset();
            if (isPromise(result))
                await result;
        }
        this.pool.push(obj);
    }
}
