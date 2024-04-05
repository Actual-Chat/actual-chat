import { isResettable } from 'resettable';

export class ObjectPool<T>
{
    private readonly pool = new Array<T>();
    private readonly factory: () => T;

    constructor(factory: () => T) {
        this.factory = factory;
    }

    public expandTo(count: number): ObjectPool<T> {
        while (this.pool.length < count)
            this.pool.push(this.factory());
        return this;
    }

    public get(): T {
        let item = this.pool.pop();
        if (item === undefined)
            item = this.factory();
        return item;
    }

    public release(obj: T): void {
        if (!obj)
            return;

        if (isResettable(obj)) {
            void (async () => {
                await obj.reset();
                this.pool.push(obj);
            })();
        }
        else
            this.pool.push(obj);
    }
}

export class AsyncObjectPool<T>
{
    private readonly pool = new Array<T>();
    private readonly factory: () => T | PromiseLike<T>;

    constructor(factory: () => T | PromiseLike<T>) {
        this.factory = factory;
    }

    public async expandTo(count: number): Promise<AsyncObjectPool<T>> {
        while (this.pool.length < count)
            this.pool.push(await this.factory());
        return this;
    }

    public async get(): Promise<T> {
        let item = this.pool.pop();
        if (item === undefined)
            item = await this.factory();
        return item;
    }

    public async release(obj: T): Promise<void> {
        if (!obj)
            return;

        if (isResettable(obj))
            await obj.reset();
        this.pool.push(obj);
    }
}
