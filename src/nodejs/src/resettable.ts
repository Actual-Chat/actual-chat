/* eslint-disable @typescript-eslint/no-unnecessary-type-parameters */
export interface Resettable {
    reset(): void | PromiseLike<void>;
}

export function isResettable<T>(obj: T | Resettable): obj is Resettable {
    // @ts-expect-error because of the `obj.reset` type
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj.reset === 'function';
}
