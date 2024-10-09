import { Subscription } from 'rxjs';

export interface Disposable {
    dispose(): void;
}

export interface AsyncDisposable {
    disposeAsync(): Promise<void>;
}

export class Disposables {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    public static readonly None: Disposable = { dispose() { } };

    public static fromAction(dispose: () => void): Disposable {
        return { dispose };
    }

    public static fromSubscription(subscription: Subscription): Disposable {
        return {
            dispose() {
                subscription.unsubscribe();
            }
        }
    }

    public static empty(): Disposable {
        return Disposables.None;
    }
}

export function isDisposable<T>(obj: T | Disposable): obj is Disposable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['dispose'] === 'function';
}

export function isAsyncDisposable<T>(obj: T | AsyncDisposable): obj is AsyncDisposable {
    return !!obj && (typeof obj === 'object' || typeof obj === 'function') && typeof obj['disposeAsync'] === 'function';
}

export class DisposableBag implements Disposable {
    private disposables = new Array<Disposable | Subscription>();

    public get isDisposed() { return this.disposables === null; }

    public addDisposables(...disposables: Array<Disposable | Subscription>) {
        if (this.isDisposed)
            throw new ObjectDisposedError();

        this.disposables.push(...disposables);
    }

    public dispose() {
        if (this.disposables === null)
            return;

        const disposables = this.disposables;
        this.disposables = null;
        for (const disposable of disposables) {
            if (disposable instanceof Subscription)
                disposable.unsubscribe();
            else
                disposable?.dispose();
        }
    }
}

export class ObjectDisposedError extends Error {
    constructor(message?: string) {
        super(message ?? 'The object is already disposed.');
    }
}
