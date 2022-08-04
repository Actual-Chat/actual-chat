interface QueuedTask {
    whenCompleted: Promise<void>;
    queuedAt: number;
}

export function onceAtATime<T extends (...args: unknown[]) => Promise<void>>(func: (...args: Parameters<T>) => ReturnType<T>, maxWait = 500)
    : (...args: Parameters<T>) => Promise<void> {
    let lastTask: QueuedTask | null = null;
    let context: unknown;

    return async function(...onceArgs: Parameters<T>): Promise<void> {
        context = this;
        const time = Date.now();
        if (lastTask) {
            const waitDuration = time - lastTask.queuedAt;
            if (waitDuration >= maxWait) {
                lastTask = null;
            }
            else {
                await lastTask.whenCompleted;
                return;
            }
        }

        const newTask = (async () => {
            try {
                await func.apply(context, onceArgs);
            }
            catch (e) {
                console.error('onceAtATime: call has failed', e);
            }
            finally {
                lastTask = null;
            }
        })();
        lastTask = {
            whenCompleted: newTask,
            queuedAt: time,
        };

        await newTask;
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
