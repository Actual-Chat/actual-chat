export interface Debounced<T extends (...args: unknown[]) => unknown> {
    (...args: Parameters<T>): void;
    now(...args: Parameters<T>): void;
    cancel(): void;
}

export function debounce<T extends (...args: unknown[]) => unknown>(func: (...args: Parameters<T>) => ReturnType<T>, wait = 300, immediately = false): Debounced<T> {
    let context: unknown;
    let waitTimeout: number | null = null;
    let previousTimestamp = 0;
    let args: unknown[] = [];

    const later = function() {
        const passed = Date.now() - previousTimestamp;
        if (wait > passed) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            waitTimeout = setTimeout(later, wait - passed);
        } else {
            waitTimeout = null;
            if (!immediately)
                func.apply(context, args);
            // This check is needed because `func` can recursively invoke `debounced`.
            if (!waitTimeout)
                args = context = null;
        }
    };

    const debounced: Debounced<T> = function(...dArgs: Parameters<T>): void {
        context = this;
        args = dArgs;
        previousTimestamp = Date.now();
        if (!waitTimeout) {
            clearTimeout(waitTimeout);
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            waitTimeout = setTimeout(later, wait);
            if (immediately)
                func.apply(context, args);
        }
    };
    debounced.cancel = function() {
        clearTimeout(waitTimeout);
        waitTimeout = args = context = null;
    };
    debounced.now = function(...dArgs: Parameters<T>): void {
        clearTimeout(waitTimeout);
        waitTimeout = null;
        context = this;
        args = dArgs;
        func.apply(context, args);
    }

    return debounced;
}
