export interface Disposable {
    dispose(): void;
}

export function isDisposable<T>(obj: T | Disposable): obj is Disposable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['dispose'] === 'function';
}
