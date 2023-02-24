export interface Disposable {
    dispose(): void;
}

export interface AsyncDisposable {
    disposeAsync(): Promise<void>;
}

export function isDisposable<T>(obj: T | Disposable): obj is Disposable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['dispose'] === 'function';
}

export function isAsyncDisposable<T>(obj: T | AsyncDisposable): obj is AsyncDisposable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['disposeAsync'] === 'function';
}

export class ObjectDisposedError extends Error {
    constructor(message?: string) {
        super(message ?? 'The object is already disposed.');
    }
}
