export function onceAtATime<T extends (...args: unknown[]) => Promise<void>>(func: (...args: Parameters<T>) => ReturnType<T>)
    : (...args: Parameters<T>) => Promise<void> {
    const queue = new Array<Promise<void>>();
    let context: unknown;

    return async function(...onceArgs: Parameters<T>): Promise<void> {
        context = this;
        const lastTask = queue.length > 0 ? queue[queue.length - 1] : null;
        if (queue.length > 0) {
            await lastTask;
            return;
        }
        const newTask = (async () => {
            try {
                if (lastTask != null)
                    await lastTask.then(v => v, reason => console.error('onceAtATime: rejected promise', reason));
                await func.apply(context, onceArgs);
            } finally {
                void queue.shift();
            }
        })();
        queue.push(newTask);
    };
}

export function serialize<T extends (...args: unknown[]) => Promise<unknown>>(func: (...args: Parameters<T>) => ReturnType<T>): (...sArgs: Parameters<T>) => ReturnType<T> {
    // This works as our promise queue
    let last: Promise<unknown> = Promise.resolve();
    let context: unknown;

    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    return function (...sArgs: Parameters<T>): ReturnType<T> {
        context = this;
        // Catch is necessary here — otherwise a rejection in a promise will
        // break the serializer forever
        last = last
            .catch(reason => console.error('serialize: rejected promise', reason))
            // eslint-disable-next-line @typescript-eslint/no-unsafe-return
            .then(() => func.apply(context,...sArgs));
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        return last;
    }
}
