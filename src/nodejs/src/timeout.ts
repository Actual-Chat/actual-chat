// nextTick & nextTickAsync are quite similar to polyfill of
// [setImmediate](https://developer.mozilla.org/en-US/docs/Web/API/Window/setImmediate),
// which we don't use because it relies on setTimeout, which is throttled in background tabs.
import { Disposable } from 'disposable';
import { setTimeout, clearTimeout } from 'timerQueue';

let nextTickImpl: (callback: () => unknown) => void = null;

if (globalThis['MessageChannel']) {
    const nextTickCallbacks = new Array<() => unknown>();
    const nextTickChannel = new MessageChannel();
    nextTickChannel.port1.onmessage = () => {
        const callback = nextTickCallbacks.shift();
        callback();
    };

    nextTickImpl = (callback: () => unknown) => {
        nextTickCallbacks.push(callback);
        nextTickChannel.port2.postMessage(null);
    }
}
else {
    // MessageChannel is unavailable in AudioWorklets, so we use setTimeout-based version here,
    // which implies ~ 8-9ms delay in average.
    nextTickImpl = (callback: () => unknown) => setTimeout(callback, 0);
}

export const nextTick = nextTickImpl;
export const nextTickAsync = () => new Promise<void>(resolve => nextTick(resolve));

// Timeout: a nicer wrapper around setTimeout

export class Timeout implements Disposable {
    protected handle: number | null = null;

    static start(isPrecise: boolean, timeoutMs: number, callback: () => unknown): Timeout {
        return isPrecise
            ? new PreciseTimeout(timeoutMs, callback)
            : new Timeout(timeoutMs, callback);
    }

    static startRegular(timeoutMs: number, callback: () => unknown): Timeout {
        return new Timeout(timeoutMs, callback);
    }

    static startPrecise(timeoutMs: number, callback: () => unknown): PreciseTimeout {
        return new PreciseTimeout(timeoutMs, callback);
    }

    constructor(timeoutMs?: number, callback?: () => unknown, handle?: number) {
        if (handle) {
            this.handle = handle;
            return;
        }

        this.handle = setTimeout(callback, timeoutMs) as unknown as number;
        return;
    }

    public dispose(): void {
        this.clear();
    }

    public clear(): void {
        if (this.handle) {
            clearTimeout(this.handle)
            this.handle = null;
        }
    }
}

// Precise timeout (5-16ms?) based on requestAnimationFrame

const disablePreciseTimeout = false;

export class PreciseTimeout extends Timeout {
    constructor(timeoutMs: number, callback: () => unknown,) {
        if (disablePreciseTimeout) {
            super(timeoutMs, callback);
            return;
        }

        // Precise timeout handling
        const endsAt = Date.now() + timeoutMs;
        const loop = () => {
            if (Date.now() >= endsAt)
                callback();
            else
                this.handle = requestAnimationFrame(loop);
        };
        super(undefined, undefined, requestAnimationFrame(loop));
    }

    public clear(): void {
        if (disablePreciseTimeout)
            return super.clear();

        if (this.handle) {
            cancelAnimationFrame(this.handle);
            this.handle = null;
        }
    }
}
