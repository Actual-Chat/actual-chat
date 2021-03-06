export function isPromise<T, S>(obj: PromiseLike<T> | S): obj is PromiseLike<T> {
    // @ts-ignore
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj["then"] === 'function';
}
