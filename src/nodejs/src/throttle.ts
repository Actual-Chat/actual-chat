export interface Throttled<T extends (...args: unknown[]) => unknown> {
    (...args: Parameters<T>): void;
    cancel(): void;
}

export function throttle<T extends (...args: unknown[]) => unknown>(func: (...args: Parameters<T>) => ReturnType<T>, wait = 300, immediately = true): Throttled<T> {
    let context: unknown;
    let waitTimeout: number | null = null;
    let previousTimestamp = 0;
    let args: Parameters<T>;

    const later = function() {
        const passed = Date.now() - previousTimestamp;
        if (wait > passed) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            waitTimeout = setTimeout(later, wait - passed);
        } else {
            waitTimeout = null;
            if (!immediately) {
                previousTimestamp = Date.now();
                func.apply(context, args);
            }
            // This check is needed because `func` can recursively invoke `throttle`.
            if (!waitTimeout)
                args = context = null;
        }
    };

    const throttled: Throttled<T> = function( ...dArgs: Parameters<T>): void {
        context = this;
        args = dArgs;
        if (!waitTimeout) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            waitTimeout = setTimeout(later, wait);
            if (immediately) {
                previousTimestamp = Date.now();
                func.apply(context, args);
            }
        }
    };

    throttled.cancel = function() {
        clearTimeout(waitTimeout);
        waitTimeout = args = context = null;
    };

    return throttled;
}
