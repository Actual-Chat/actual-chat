export interface Resettable {
    reset(): void | PromiseLike<void>;
}

export function isResettable<T>(obj: T | Resettable): obj is Resettable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['reset'] === 'function';
}
